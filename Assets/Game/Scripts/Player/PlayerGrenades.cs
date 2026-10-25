using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Weapons;

namespace FPSGame.Player
{
    /// <summary>
    /// Turns the FPS Framework grenade throw into a server-spawned network object. The owner's
    /// Throwable still plays its cook/throw animation; at the release frame Throwable.ThrowOverride
    /// hands us the pose, velocity and remaining fuse, we spend one grenade (client-side ammo, like
    /// bullets) and the server spawns the networked grenade and runs its fuse and blast.
    /// </summary>
    public class PlayerGrenades : NetworkBehaviour
    {
        [Tooltip("Networked grenade (Explosive + NetworkExplosive). Must be in CustomNetworkManager's runtime spawn list.")]
        [SerializeField] private GameObject grenadePrefab;
        [Tooltip("FPSF ammo profile that counts grenades (Inventory ammo list).")]
        [SerializeField] private string ammoName = "Gernades";

        [Header("Server validation")]
        [SerializeField] private float maxSpawnDistance = 3f;
        [SerializeField] private float maxThrowSpeed = 40f;
        [SerializeField] private float maxFuse = 6f;

        private Inventory inventory;
        private PlayerHealth health;
        private Actor actor;

        public GameObject GrenadePrefab => grenadePrefab;
        public string AmmoName => ammoName;

        private void Awake()
        {
            inventory = GetComponentInChildren<Inventory>(true);
            health = GetComponent<PlayerHealth>();
            actor = GetComponent<Actor>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Throwable.ThrowOverride = OnLocalThrow;
        }

        public override void OnStopLocalPlayer()
        {
            base.OnStopLocalPlayer();
            if (Throwable.ThrowOverride == (System.Func<Throwable, Vector3, Quaternion, Vector3, float, bool>)OnLocalThrow)
                Throwable.ThrowOverride = null;
        }

        /// <summary>Grenades left (owner).</summary>
        public int Count
        {
            get
            {
                AmmoProfile profile = inventory != null ? inventory.FindAmmunition(ammoName) : null;
                return profile != null ? profile.amount : 0;
            }
        }

        private bool OnLocalThrow(Throwable throwable, Vector3 position, Quaternion rotation, Vector3 velocity, float fuse)
        {
            if (throwable == null || !throwable.transform.IsChildOf(transform))
                return false; // not ours (cannot happen for the owner's only FPSF, but stay safe)

            AmmoProfile profile = inventory != null ? inventory.FindAmmunition(ammoName) : null;
            if (profile == null || profile.amount <= 0)
                return true; // out of grenades: the throw animation plays, nothing leaves the hand

            profile.amount--;
            CmdThrow(position, rotation, velocity, fuse);
            return true;
        }

        [Command]
        private void CmdThrow(Vector3 position, Quaternion rotation, Vector3 velocity, float fuse)
        {
            if (grenadePrefab == null || health.IsIncapacitated)
                return;
            if (Vector3.Distance(position, transform.position) > maxSpawnDistance)
                return;

            velocity = Vector3.ClampMagnitude(velocity, maxThrowSpeed);
            fuse = Mathf.Clamp(fuse, 0f, maxFuse);

            GameObject grenade = Instantiate(grenadePrefab, position, rotation);
            grenade.GetComponent<NetworkExplosive>().ServerLaunch(velocity, fuse, actor);
            NetworkServer.Spawn(grenade);
        }
    }
}
