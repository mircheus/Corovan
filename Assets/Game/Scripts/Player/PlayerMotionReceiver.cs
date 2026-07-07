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

        private PlayerController controller;
        private PlayerCarry carry;
        private float onPlatformTimer;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            carry = GetComponent<PlayerCarry>();
        }

        private void Update()
        {
            if (!isOwned)
                return;

            MotionDirector director = MotionDirector.Instance;
            if (director == null || controller == null)
                return;

            // Grace window so a jump on the platform doesn't cut the push mid-swerve.
            if (controller.OnMotionPlatform)
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
    }
}
