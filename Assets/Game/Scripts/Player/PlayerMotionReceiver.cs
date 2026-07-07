using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Local player's consumer of the MotionDirector signal: nudges the CharacterController
    // sideways so the ride disturbs the player's balance and can push them toward an edge.
    //
    // Uses PlayerController.AddExternalVelocity (a per-frame Move offset) — NOT AddForce,
    // because a CharacterController can't be pushed by forces.
    //
    // The push only applies while the player stands on a MotionPlatform. That state is read
    // straight from PlayerController.OnMotionPlatform, which is resolved inside the ground
    // check the controller already runs every frame — so this adds no physics query of its own.
    // Runs only for the local owner — purely local, no replication.
    public class PlayerMotionReceiver : NetworkBehaviour
    {
        [Header("Push strength")]
        [Tooltip("Lateral velocity (m/s) per lateral signal unit added to the controller.")]
        [SerializeField] private float pushScale = 3f;

        [Header("Carry modifier")]
        [Tooltip("Extra push multiplier while carrying loot — heavier = the ride hits harder.")]
        [SerializeField] private float carryMultiplier = 1.8f;

        [Header("Platform detection")]
        [Tooltip("Keep applying the push briefly after leaving the platform (e.g. mid-jump).")]
        [SerializeField] private float stayGrace = 0.25f;

        [Header("Debug")]
        [Tooltip("Log platform state and push values to the console. Turn off once wired.")]
        [SerializeField] private bool debugLog = true;

        private PlayerController controller;
        private PlayerCarry carry;
        private float onPlatformTimer;

        // Debug bookkeeping.
        private bool loggedNoDirector;
        private bool lastOnPlatform;
        private float debugAccum;

        private bool loggedNotOwned;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            carry = GetComponent<PlayerCarry>();
            Debug.Log($"[PlayerMotionReceiver] Awake on '{name}', controller={(controller != null)}", this);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Debug.Log("[PlayerMotionReceiver] OnStartLocalPlayer — this is the owned local player.", this);
        }

        private void Update()
        {
            if (!isOwned)
            {
                if (debugLog && !loggedNotOwned)
                {
                    Debug.Log($"[PlayerMotionReceiver] Update running but !isOwned on '{name}'.", this);
                    loggedNotOwned = true;
                }
                return;
            }

            if (controller == null)
            {
                if (debugLog && !loggedNoDirector)
                {
                    Debug.LogWarning("[PlayerMotionReceiver] No PlayerController on this object — " +
                        "the component must sit on the same GameObject as PlayerController.", this);
                    loggedNoDirector = true;
                }
                return;
            }

            MotionDirector director = MotionDirector.Instance;
            if (director == null)
            {
                if (debugLog && !loggedNoDirector)
                {
                    Debug.LogWarning("[PlayerMotionReceiver] MotionDirector.Instance is null — " +
                        "no MotionDirector spawned in the scene?", this);
                    loggedNoDirector = true;
                }
                return;
            }
            loggedNoDirector = false;

            // Periodic diagnostic: dump what's actually under the player's feet.
            if (debugLog)
            {
                debugAccum += Time.deltaTime;
                if (debugAccum >= 0.5f)
                {
                    debugAccum = 0f;
                    DumpFeet();
                }
            }

            // Log platform enter/exit transitions.
            bool onPlatform = controller.OnMotionPlatform;
            if (debugLog && onPlatform != lastOnPlatform)
            {
                Debug.Log($"[PlayerMotionReceiver] OnMotionPlatform -> {onPlatform}", this);
                lastOnPlatform = onPlatform;
            }

            // Grace window so a jump on the platform doesn't cut the push mid-swerve.
            if (onPlatform)
                onPlatformTimer = stayGrace;
            else
                onPlatformTimer -= Time.deltaTime;

            if (onPlatformTimer <= 0f)
                return;

            float scale = pushScale;
            if (carry != null && carry.IsCarrying)
                scale *= carryMultiplier;

            Vector3 push = director.LateralAxis * (director.LateralSignal * scale);
            controller.AddExternalVelocity(push);
        }

        // Debug-only: sweep the player's feet across ALL layers and report every collider,
        // its layer, and whether it carries a MotionPlatform. Reveals a missing marker,
        // a trigger floor, or a layer that's outside the player's Ground Mask.
        private void DumpFeet()
        {
            Vector3 origin = transform.position + Vector3.down * 0.1f;
            Collider[] hits = Physics.OverlapSphere(origin, 0.4f, ~0, QueryTriggerInteraction.Collide);

            if (hits.Length == 0)
            {
                Debug.Log($"[PlayerMotionReceiver] feet sweep: nothing under player " +
                    $"(OnMotionPlatform={controller.OnMotionPlatform})", this);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[PlayerMotionReceiver] feet sweep (OnMotionPlatform={controller.OnMotionPlatform}):");
            foreach (var h in hits)
            {
                bool hasMarker = h.GetComponentInParent<FPSGame.Environment.MotionPlatform>() != null;
                sb.Append($"\n  - '{h.name}' layer='{LayerMask.LayerToName(h.gameObject.layer)}'({h.gameObject.layer}) " +
                    $"isTrigger={h.isTrigger} MotionPlatform={hasMarker}");
            }
            Debug.Log(sb.ToString(), this);
        }
    }
}
