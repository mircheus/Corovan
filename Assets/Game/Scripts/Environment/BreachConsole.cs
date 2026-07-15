using System;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Overheat/breach console on truck A. Holds only the heat/overheat state machine
    // (TЗ v0). Decoupled: it does NOT open the vault, pull aggro, restrict fire or draw
    // UI — it exposes state + events (see the public contract) that those systems
    // subscribe to. The key-card slot / interaction layer feeds it via SetCardInSlot.
    //
    // Server-authoritative: the whole tick (heat, warm-up/cooldown timers, transitions)
    // runs on the server. State and HeatNormalized replicate to clients via SyncVars;
    // clients use them for cosmetics only (indicators, light, shake).
    public class BreachConsole : NetworkBehaviour
    {
        [Header("Warm-up")]
        [Tooltip("Delay for Breaching -> Open.")]
        [SerializeField] private float warmupDuration = 2f;

        [Header("Heat")]
        [Tooltip("Heat rise per second, only in Open (~5 s window from cold).")]
        [SerializeField] private float heatRisePerSecond = 0.2f;
        [Tooltip("Heat fall per second, in every state but Open (cools faster than it heats).")]
        [SerializeField] private float heatFallPerSecond = 0.35f;
        [Tooltip("Heat level (0..1) that trips the overheat.")]
        [SerializeField] private float overheatThreshold = 1f;
        [Tooltip("Lock duration in Overheat before returning to Idle.")]
        [SerializeField] private float overheatCooldown = 4f;

        [SyncVar(hook = nameof(OnStateSync))]
        private ConsoleState state = ConsoleState.Idle;

        [SyncVar(hook = nameof(OnHeatSync))]
        private float heat;

        // --- Public contract (hooks for other systems) ---
        public ConsoleState State => state;
        public float HeatNormalized => heat;
        public bool IsVaultOpen => state == ConsoleState.Open;         // for the vault system
        public bool IsCardHeld => state == ConsoleState.Breaching ||    // for aggro / restricted fire
                                  state == ConsoleState.Open;

        public event Action<ConsoleState> OnStateChanged; // fired on every transition
        public event Action<float> OnHeatChanged;         // fired on every heat change (UI bar)

        // --- Server-only working state ---
        private bool cardInSlot;
        private float warmupTimer;
        private float overheatTimer;

        public override void OnStartServer()
        {
            base.OnStartServer();
            state = ConsoleState.Idle;
            heat = 0f;
            cardInSlot = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late joiners: prime cosmetics with the current replicated values. On the
            // host the server path already raised these, so skip to avoid a double fire.
            if (!isServer)
            {
                OnStateChanged?.Invoke(state);
                OnHeatChanged?.Invoke(heat);
            }
        }

        // Entry point, called by the interaction/network layer, authoritative on the server.
        // Insertion is ignored while overheated (slot blocked).
        [Server]
        public void SetCardInSlot(bool inserted)
        {
            if (state == ConsoleState.Overheat)
                return; // slot blocked
            cardInSlot = inserted;
        }

        private void Update()
        {
            if (!isServer)
                return;
            Tick(Time.deltaTime);
        }

        // Server tick.
        [Server]
        private void Tick(float dt)
        {
            switch (state)
            {
                case ConsoleState.Idle:
                    CoolDown(dt);
                    if (cardInSlot)
                        EnterBreaching();
                    break;

                case ConsoleState.Breaching:
                    CoolDown(dt);
                    if (!cardInSlot) { SetState(ConsoleState.Idle); break; }
                    warmupTimer -= dt;
                    if (warmupTimer <= 0f)
                        SetState(ConsoleState.Open);
                    break;

                case ConsoleState.Open:
                    if (!cardInSlot) { SetState(ConsoleState.Idle); break; }
                    SetHeat(heat + heatRisePerSecond * dt);
                    if (heat >= overheatThreshold)
                        EnterOverheat();
                    break;

                case ConsoleState.Overheat:
                    CoolDown(dt);
                    // v0: fixed timer. To switch to a heat-based exit later
                    // (heat < overheatResetThreshold), replace only this condition.
                    overheatTimer -= dt;
                    if (overheatTimer <= 0f)
                        SetState(ConsoleState.Idle);
                    break;
            }
        }

        // Debug/test entry: force the breach without a key-card, slot or operator (bypasses
        // BreachConsoleSlot entirely). Toggles the card state so a second call pulls it back
        // out. Blocked during overheat, same as the real slot. Server-only — call from a
        // host-side debug hotkey. Remove with the rest of the debug scaffolding before ship.
        [Server]
        public void DebugToggleBreach()
        {
            if (state == ConsoleState.Overheat)
                return; // slot blocked, mirror SetCardInSlot
            cardInSlot = !cardInSlot;
        }

        private void EnterBreaching()
        {
            warmupTimer = warmupDuration;
            SetState(ConsoleState.Breaching);
        }

        private void EnterOverheat()
        {
            cardInSlot = false; // forcibly eject the card from the slot
            overheatTimer = overheatCooldown;
            SetState(ConsoleState.Overheat);
        }

        private void CoolDown(float dt) => SetHeat(heat - heatFallPerSecond * dt);

        // Server-side setters: assign the SyncVar and raise the event locally (covers a
        // headless server and the host). The SyncVar hook raises it on pure clients.
        [Server]
        private void SetState(ConsoleState s)
        {
            if (s == state)
                return;
            state = s;
            OnStateChanged?.Invoke(s);
        }

        [Server]
        private void SetHeat(float v)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(v, heat))
                return;
            heat = v;
            OnHeatChanged?.Invoke(v);
        }

        private void OnStateSync(ConsoleState oldValue, ConsoleState newValue)
        {
            if (isServer)
                return; // host already raised it via the server setter
            OnStateChanged?.Invoke(newValue);
        }

        private void OnHeatSync(float oldValue, float newValue)
        {
            if (isServer)
                return;
            OnHeatChanged?.Invoke(newValue);
        }
    }
}
