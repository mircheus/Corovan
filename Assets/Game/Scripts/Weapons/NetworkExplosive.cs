using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Weapons
{
    /// <summary>
    /// Networks an FPS Framework Explosive (the thrown grenade). The server owns it: it simulates the
    /// throw, runs the fuse and the explosion — damage goes through FPSFDamageable to our receivers.
    /// Clients get a kinematic copy moved by NetworkTransform and play the explosion's effects
    /// (blast, crater, camera shake) when the server says it went off.
    /// </summary>
    [RequireComponent(typeof(Explosive))]
    public class NetworkExplosive : NetworkBehaviour
    {
        [Tooltip("How long after the blast the server removes the object (lets the effects RPC go first).")]
        [SerializeField] private float despawnDelay = 0.1f;

        private Explosive explosive;
        private Rigidbody body;

        private void Awake()
        {
            explosive = GetComponent<Explosive>();
            body = GetComponent<Rigidbody>();
            // The network object is removed by the server, never by FPSF's own Destroy.
            explosive.destroyOnExplode = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            explosive.onExploded += ServerOnExploded;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (isServer)
                return;

            // Clients never run the fuse or the blast themselves: one explosion, on the server.
            explosive.enabled = false;
            if (body != null)
                body.isKinematic = true;
        }

        /// <summary>Server: launch a freshly instantiated grenade before NetworkServer.Spawn.</summary>
        [Server]
        public void ServerLaunch(Vector3 velocity, float fuse, Actor thrower)
        {
            explosive.source = thrower;
            explosive.delay = fuse;
            explosive.exlopeAfterDelay = true; // Explosive.Start arms the fuse
            if (body != null)
                body.linearVelocity = velocity;
        }

        [Server]
        private void ServerOnExploded(Explosive _)
        {
            explosive.onExploded -= ServerOnExploded;
            RpcExploded(transform.position, transform.rotation);

            // Hide it right away; remove it once the RPC is on its way.
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            if (body != null)
                body.isKinematic = true;
            Invoke(nameof(ServerDespawn), despawnDelay);
        }

        [Server]
        private void ServerDespawn()
        {
            NetworkServer.Destroy(gameObject);
        }

        [ClientRpc]
        private void RpcExploded(Vector3 position, Quaternion rotation)
        {
            // The host already played everything inside Explosive.Explode.
            if (isServer)
                return;

            transform.SetPositionAndRotation(position, rotation);
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;

            explosive.AddEffects();
            ShakeLocalPlayer(position);
        }

        // The same falloff Explosive uses for its own camera shake.
        private void ShakeLocalPlayer(Vector3 position)
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
                return;
            var character = local.GetComponent<CharacterManager>();
            if (character == null || character.cameraManager == null)
                return;

            float scale = explosive.ignoreGlobalScale ? 1f : transform.lossyScale.magnitude;
            float distance = Vector3.Distance(position, local.transform.position);
            if (distance > explosive.effectRadius * scale)
                return;

            float shake = Mathf.InverseLerp(1f, 0f, distance / explosive.effectRadius * scale);
            character.cameraManager.ShakeCameras(explosive.cameraShake * shake);
        }
    }
}
