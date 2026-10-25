using System.Collections;
using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Combat;

namespace FPSGame.Enemies
{
    /// <summary>
    /// A humanoid target dummy built on the FPS Framework's Bot: health lives on the server, and on
    /// death every client flops the FPSF Ragdoll with the killing blow's push. The ragdoll is cosmetic
    /// and simulated per client — bodies may settle slightly differently on each screen.
    ///
    /// Hits on individual bones go through FPSF HealthGroups (head/limb multipliers) to the
    /// FPSFDamageable on this object, then to the server like any other target.
    /// </summary>
    [RequireComponent(typeof(Ragdoll))]
    public class RagdollEnemy : NetworkBehaviour, IDamageReceiver, IHitForceReceiver
    {
        [SerializeField] private int maxHealth = 100;

        [Header("After death")]
        [Tooltip("Seconds until the dummy stands back up. 0 or less = destroy it instead.")]
        [SerializeField] private float respawnDelay = 5f;
        [Tooltip("Seconds until a non-respawning dummy is removed.")]
        [SerializeField] private float destroyDelay = 4f;

        [Header("Ragdoll push")]
        [Tooltip("Multiplier on the killing hit's push (velocity change applied to every bone).")]
        [SerializeField] private float pushMultiplier = 1f;
        [Tooltip("Upper bound on the push, so a big impact force does not launch the body.")]
        [SerializeField] private float maxPushSpeed = 12f;

        [SyncVar] private int health;
        [SyncVar(hook = nameof(OnDeadChanged))] private bool isDead;

        private Ragdoll ragdoll;
        private Vector3 pendingHitForce;

        public int CurrentHealth => health;
        public int MaxHealth => maxHealth;
        public bool IsDead => isDead;

        /// <summary>Server only: the dummy just died / stood back up. Lets an AI on the same object stop and resume.</summary>
        public event System.Action ServerDied;
        public event System.Action ServerRespawned;

        private void Awake()
        {
            ragdoll = GetComponent<Ragdoll>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            health = maxHealth;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late join on an already dead dummy: just lie down, no push.
            if (isDead && !isServer)
                StartCoroutine(SetRagdollWhenReady(true, Vector3.zero));
        }

        [Server]
        public void ServerSetHitForce(Vector3 force)
        {
            pendingHitForce = force;
        }

        [Server]
        public void TakeDamage(int damage)
        {
            if (isDead || damage <= 0)
                return;

            health = Mathf.Max(0, health - damage);
            if (health > 0)
                return;

            isDead = true;
            ServerDied?.Invoke();
            RpcDie(Vector3.ClampMagnitude(pendingHitForce * pushMultiplier, maxPushSpeed));

            if (respawnDelay > 0f)
                StartCoroutine(ServerRespawnAfter(respawnDelay));
            else
                StartCoroutine(ServerDestroyAfter(destroyDelay));
        }

        [ClientRpc]
        private void RpcDie(Vector3 push)
        {
            StartCoroutine(SetRagdollWhenReady(true, push));
        }

        private IEnumerator ServerRespawnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            health = maxHealth;
            isDead = false; // the hook stands it back up on every client
            ServerRespawned?.Invoke();
        }

        private IEnumerator ServerDestroyAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            NetworkServer.Destroy(gameObject);
        }

        private void OnDeadChanged(bool wasDead, bool nowDead)
        {
            // Death goes through RpcDie, which carries the push; the hook only handles standing up.
            if (wasDead && !nowDead)
                StartCoroutine(SetRagdollWhenReady(false, Vector3.zero));
        }

        // FPSF's Ragdoll gathers its bones in Start; an RPC or hook can arrive before that.
        private IEnumerator SetRagdollWhenReady(bool limp, Vector3 push)
        {
            while (ragdoll.rigidbodies == null)
                yield return null;

            if (limp)
                ragdoll.Enable(push);
            else
                ragdoll.Disable(); // the Animator takes the bones back to its pose
        }
    }
}
