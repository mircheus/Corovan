using System.Collections;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Vault on truck B, driven by a BreachConsole on truck A. Subscribes to the console's
    // state contract (§5): open while the console is Open, closed in every other state
    // (Idle / Breaching / Overheat). Reversible — opens and closes repeatedly with the
    // burst rhythm, unlike the one-shot ContainerDoor it's modelled on.
    //
    // Server-authoritative: the open/closed decision reads the server-side console State;
    // the door swing replicates through a SyncVar so every client — and late joiners —
    // sees the correct pose.
    public class BreachVault : NetworkBehaviour
    {
        [Header("Console")]
        [Tooltip("The BreachConsole whose State drives this vault.")]
        [SerializeField] private BreachConsole console;

        [Header("Door")]
        [Tooltip("Transform that swings. Its authored local rotation is the closed pose.")]
        [SerializeField] private Transform doorPivot;

        [Tooltip("Local euler offset added to the closed pose to reach the open pose.")]
        [SerializeField] private Vector3 openEulerOffset = new Vector3(0f, -110f, 0f);

        [Tooltip("Seconds the door takes to swing between closed and open.")]
        [SerializeField] private float swingDuration = 1f;

        [Tooltip("Easing: maps normalized time (0..1) to progress (0..1).")]
        [SerializeField] private AnimationCurve swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SyncVar(hook = nameof(OnOpenChanged))]
        private bool isOpen;

        private Quaternion closedRot, openRot;
        private Coroutine swingRoutine;
        private bool clientStarted;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            if (doorPivot != null)
            {
                closedRot = doorPivot.localRotation;
                openRot = closedRot * Quaternion.Euler(openEulerOffset);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (console != null)
            {
                console.OnStateChanged += OnConsoleStateChanged;
                // Sync to whatever the console already reports (e.g. host restart / late setup).
                isOpen = console.IsVaultOpen;
            }
            else
            {
                Debug.LogWarning($"[BreachVault] {name} has no BreachConsole assigned.", this);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (console != null)
                console.OnStateChanged -= OnConsoleStateChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientStarted = true;
            // Late joiners: snap to the current pose with no animation.
            ApplyRotation(isOpen ? openRot : closedRot);
        }

        // §5: open at Open, closed otherwise. IsVaultOpen already encodes that.
        [Server]
        private void OnConsoleStateChanged(ConsoleState state)
        {
            ServerSetOpen(console.IsVaultOpen);
        }

        [Server]
        private void ServerSetOpen(bool open)
        {
            if (isOpen == open)
                return;
            isOpen = open; // hook drives the swing on every client (host included)
        }

        private void OnOpenChanged(bool oldValue, bool newValue)
        {
            // Hook can fire during initial spawn (before OnStartClient) for late joiners —
            // snap in that case rather than animating from an unset pose.
            if (!clientStarted)
            {
                ApplyRotation(newValue ? openRot : closedRot);
                return;
            }

            if (swingRoutine != null)
                StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingTo(newValue ? openRot : closedRot));
        }

        private IEnumerator SwingTo(Quaternion target)
        {
            float t = 0f;
            Quaternion from = doorPivot != null ? doorPivot.localRotation : Quaternion.identity;

            while (t < swingDuration)
            {
                t += Time.deltaTime;
                float k = swingCurve.Evaluate(Mathf.Clamp01(t / swingDuration));
                if (doorPivot != null)
                    doorPivot.localRotation = Quaternion.Slerp(from, target, k);
                yield return null;
            }

            ApplyRotation(target);
            swingRoutine = null;
        }

        private void ApplyRotation(Quaternion rot)
        {
            if (doorPivot != null)
                doorPivot.localRotation = rot;
        }
    }
}
