using FPSGame.Environment;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Server-side: lets a NavMeshAgent-driven enemy fall through holes cut into the floor
    /// at runtime (see FloorMeshModifier / DestroyableObject).
    ///
    /// The baked NavMesh knows nothing about runtime holes, so the agent keeps the enemy
    /// pinned to the baked surface — it "walks on air" over a gap. This component spherecasts
    /// straight down each server tick; when there's no floor under the feet it detaches the
    /// NavMeshAgent and applies gravity manually. NetworkTransformUnreliable syncs the fall
    /// to every client, so only the server needs to run it.
    ///
    /// The fallen enemy is despawned when it enters a FallHazardVolume trigger (the deadly
    /// gap under the platform). That contact needs a Rigidbody on the enemy — a static
    /// collider generates no trigger events (which is why KillVolume relies on the player's
    /// CharacterController). Add a *kinematic* Rigidbody so it never fights the NavMeshAgent.
    ///
    /// Prefab setup:
    ///   • Sits next to NavMeshAgent + ZombieEnemy (ZombieEnemy is optional — any
    ///     NavMeshAgent enemy works).
    ///   • Add a kinematic Rigidbody (isKinematic = true, useGravity = false).
    ///   • Set floorMask to the layer(s) of the destructible platform / floor colliders.
    ///   • The enemy's layer must collide with the FallHazardVolume's layer in the Physics matrix.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyFallThrough : NetworkBehaviour
    {
        [Header("Ground Check")]
        [Tooltip("Layers treated as solid floor. Must include the destructible platform's layer.")]
        [SerializeField] private LayerMask floorMask = ~0;
        [Tooltip("How far below the feet to look for floor before deciding it's a hole.")]
        [SerializeField] private float groundCheckDistance = 0.5f;
        [Tooltip("Lift the ray origin above the pivot so it starts inside the body.")]
        [SerializeField] private float groundCheckUpOffset = 0.3f;
        [Tooltip("Radius of the downward spherecast — avoids slipping through the thin cut band at hole edges.")]
        [SerializeField] private float groundCheckRadius = 0.25f;

        [Header("Fall")]
        [SerializeField] private float gravity = 25f;

        private NavMeshAgent agent;
        private ZombieEnemy zombie;
        private bool falling;
        private float fallVelocity;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            zombie = GetComponent<ZombieEnemy>();
        }

        private void Update()
        {
            if (!isServer)
                return;

            if (falling)
            {
                TickFall();
                return;
            }

            // Only a live, agent-driven enemy can fall.
            if (zombie != null && !zombie.IsAlive)
                return;
            if (!agent.enabled || !agent.isOnNavMesh)
                return;

            if (!HasFloorBelow())
                BeginFall();
        }

        private bool HasFloorBelow()
        {
            Vector3 origin = transform.position + Vector3.up * groundCheckUpOffset;
            float dist = groundCheckUpOffset + groundCheckDistance;
            return Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out _, dist,
                                      floorMask, QueryTriggerInteraction.Ignore);
        }

        private void BeginFall()
        {
            falling = true;
            fallVelocity = 0f;

            // Stop the agent pinning us to the baked NavMesh, then hand off to gravity.
            agent.enabled = false;

            // Let the AI stop steering / attacking (ZombieEnemy.Update also bails once the
            // agent is disabled, but this cancels pending attacks and clears the target).
            if (zombie != null)
                zombie.ServerBeginFall();
        }

        private void TickFall()
        {
            fallVelocity += gravity * Time.deltaTime;
            transform.position += Vector3.down * (fallVelocity * Time.deltaTime);
        }

        // The falling enemy is removed the moment it reaches the deadly gap under the platform.
        // Server-only, and only while falling — a grounded enemy near the pit edge won't vanish.
        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !falling)
                return;

            if (other.GetComponentInParent<FallHazardVolume>() != null)
                NetworkServer.Destroy(gameObject);
        }
    }
}
