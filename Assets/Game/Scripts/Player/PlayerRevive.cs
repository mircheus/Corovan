using System;
using Mirror;
using UnityEngine;

namespace FPSGame.Player
{
    // Sits on the player prefab and answers "can a teammate pick me up?". The hold itself is timed
    // by the reviving client (PlayerInputHandler), the same way hold-E interactables work, but every
    // step is re-validated here on the server: who is reviving, from how far, and for how long.
    //
    // While a revive is in progress the victim's bleedout countdown is frozen, so nobody dies half a
    // second before the hold that was going to save them completes.
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerRevive : NetworkBehaviour
    {
        [Header("Revive")]
        [Tooltip("Seconds a teammate must hold the interact key to get this player back on their feet.")]
        [SerializeField] private float reviveHoldDuration = 4f;
        [Tooltip("How close the reviving player has to stand.")]
        [SerializeField] private float reviveRange = 2.5f;

        // Slack on the server-side checks, absorbing latency between the reviver's client and here.
        private const float RangeTolerance = 1f;
        private const float HoldTolerance = 0.25f;

        // netId of the player currently reviving this one; 0 when nobody is.
        [SyncVar(hook = nameof(OnReviverChangedHook))]
        private uint reviverNetId;

        // NetworkTime.time when the current hold began. Lets the downed player's own HUD draw the
        // progress bar without the reviver having to stream it across.
        [SyncVar]
        private double reviveStartTime;

        private PlayerHealth health;

        public PlayerHealth Health => health;
        public float ReviveHoldDuration => reviveHoldDuration;
        public float ReviveRange => reviveRange;
        public bool IsBeingRevived => reviverNetId != 0;

        /// <summary>0–1 progress of the hold in flight. Zero when nobody is reviving.</summary>
        public float ReviveProgress
        {
            get
            {
                if (reviverNetId == 0 || reviveHoldDuration <= 0f)
                    return 0f;
                return Mathf.Clamp01((float)(NetworkTime.time - reviveStartTime) / reviveHoldDuration);
            }
        }

        /// <summary>Fires on clients when someone starts or stops reviving this player.</summary>
        public event Action<bool> OnBeingRevivedChanged;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
        }

        /// <summary>True when <paramref name="reviver"/> is a live, close-enough teammate and this player is down.</summary>
        public bool CanBeRevivedBy(GameObject reviver)
        {
            if (reviver == null || reviver == gameObject)
                return false;
            if (health == null || !health.IsDowned)
                return false;

            var reviverHealth = reviver.GetComponent<PlayerHealth>();
            if (reviverHealth == null || reviverHealth.IsIncapacitated)
                return false;

            float dist = Vector3.Distance(transform.position, reviver.transform.position);
            return dist <= reviveRange + RangeTolerance;
        }

        [Server]
        public void ServerBeginRevive(GameObject reviver)
        {
            // First one to reach the body owns the hold; a second player can't stack onto it.
            if (reviverNetId != 0 || !CanBeRevivedBy(reviver))
                return;

            var reviverIdentity = reviver.GetComponent<NetworkIdentity>();
            if (reviverIdentity == null)
                return;

            reviveStartTime = NetworkTime.time;
            reviverNetId = reviverIdentity.netId;
            health.ServerSetBleedoutPaused(true);
        }

        [Server]
        public void ServerCancelRevive(GameObject reviver)
        {
            if (reviverNetId == 0 || !IsCurrentReviver(reviver))
                return;

            ServerAbortRevive();
        }

        [Server]
        public void ServerCompleteRevive(GameObject reviver)
        {
            if (reviverNetId == 0 || !IsCurrentReviver(reviver))
                return;

            if (!CanBeRevivedBy(reviver))
            {
                ServerAbortRevive();
                return;
            }

            // The hold is timed client-side; don't take its word for how long it lasted.
            if (NetworkTime.time - reviveStartTime < reviveHoldDuration - HoldTolerance)
                return;

            reviverNetId = 0;
            health.ServerRevive();
        }

        // Watches the hold from the server's side, so a revive dies with its reviver: they got shot,
        // went down themselves, walked off, or dropped the connection entirely.
        [ServerCallback]
        private void Update()
        {
            if (reviverNetId == 0)
                return;

            if (health == null || !health.IsDowned)
            {
                ServerAbortRevive();
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(reviverNetId, out NetworkIdentity reviverIdentity) ||
                reviverIdentity == null ||
                !CanBeRevivedBy(reviverIdentity.gameObject))
            {
                ServerAbortRevive();
            }
        }

        [Server]
        private void ServerAbortRevive()
        {
            reviverNetId = 0;
            health.ServerSetBleedoutPaused(false);
        }

        [Server]
        private bool IsCurrentReviver(GameObject reviver)
        {
            if (reviver == null)
                return false;

            var identity = reviver.GetComponent<NetworkIdentity>();
            return identity != null && identity.netId == reviverNetId;
        }

        private void OnReviverChangedHook(uint oldReviver, uint newReviver)
        {
            OnBeingRevivedChanged?.Invoke(newReviver != 0);
        }
    }
}
