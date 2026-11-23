using System.Collections;
using UnityEngine;
using Mirror;
using FPSGame.Combat;
using FPSGame.Player;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// The car's forward machine gun: single shots, a hitscan ray from firePoint along its forward,
    /// with an angular spread cone plus a random offset of the ray's start in the firePoint's local
    /// X/Y plane. The driver fires it with LMB while nothing is in hand (PlayerInputHandler).
    ///
    /// Like the player's weapons, the hit is client-authoritative: the driver's client picks what the
    /// ray hit, the server checks the shot (driver alive, ammo, rate, distance) and applies its own
    /// damage value through the driver's PlayerHitReporter (friendly-fire rules included).
    /// Ammo is the car's, shared by whoever drives it, and synced from the server.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarMachineGun : NetworkBehaviour
    {
        [Header("Shot")]
        [Tooltip("Ray start; the gun fires along its forward (blue axis). Keep it on the logical car, not under the " +
                 "rotating visual root: shots go straight ahead of the car body whatever the visual yaw.")]
        [SerializeField] private Transform firePoint;
        [Tooltip("Cosmetic muzzle on the gun model (under the visual root): tracers and the muzzle flash start here, " +
                 "so they leave the barrel the player sees. Empty = the fire point.")]
        [SerializeField] private Transform visualMuzzle;
        [Tooltip("Experiment \"aim with the wheel\": on = shots follow the visual nose (CarBodyVisual yaw) and the gun " +
                 "model turns with the body; off = shots go straight ahead, the gun model keeps pointing there.")]
        [SerializeField] private bool gunFollowsBodyYaw;
        [SerializeField] private int damage = 15;
        [SerializeField] private float range = 120f;
        [Tooltip("Seconds between two shots.")]
        [SerializeField] private float fireInterval = 0.15f;
        [Tooltip("Half-angle (deg) of the random spread cone around the fire point's forward.")]
        [SerializeField] private float spreadAngle = 1.5f;
        [Tooltip("Random offset of the ray start along the fire point's local X / Y, ± these values (m).")]
        [SerializeField] private Vector2 originSpread = new Vector2(0.15f, 0.05f);
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Velocity (m/s) a killing shot gives a ragdoll.")]
        [SerializeField] private float hitPushSpeed = 6f;

        [Header("Ammo")]
        [SerializeField] private int maxAmmo = 200;

        [Header("Recoil")]
        [Tooltip("Gun model kicked back along its own -Z on every shot. Keep the fire point off it, " +
                 "so the ray doesn't move with the kick.")]
        [SerializeField] private Transform recoilTarget;
        [Tooltip("How far back the model goes, in its parent's local units.")]
        [SerializeField] private float recoilDistance = 0.08f;
        [Tooltip("Seconds to reach full kick.")]
        [SerializeField] private float recoilKickTime = 0.03f;
        [Tooltip("Seconds to ease back to rest.")]
        [SerializeField] private float recoilReturnTime = 0.12f;

        [Header("Effects")]
        [Tooltip("Spawned at the fire point on every shot, parented to it.")]
        [SerializeField] private GameObject muzzleFlashPrefab;
        [Tooltip("Effect already on the car (e.g. a child of the fire point), restarted on every shot.")]
        [SerializeField] private ParticleSystem shotEffect;
        [Tooltip("Spawn a one-shot AudioSource with fireClip on every shot. Off for now.")]
        [SerializeField] private bool playFireSound;
        [SerializeField] private AudioClip fireClip;
        [Range(0f, 1f)] [SerializeField] private float fireVolume = 1f;
        [Tooltip("Spawned at the hit point, facing along the surface normal.")]
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private float impactLifetime = 5f;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.12f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.5f, 0.9f);

        [SyncVar(hook = nameof(OnAmmoChanged))]
        private int ammo;

        private CarController car;
        private Material tracerMaterial;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        // Owner: shots fired that the synced ammo doesn't reflect yet. Cleared after a quiet second,
        // so a shot the server turned down can't leave the counter low for good.
        private int pendingShots;
        private float lastLocalShotTime = float.NegativeInfinity;
        private float lastServerShotTime = float.NegativeInfinity;

        private Vector3 recoilRestPosition;
        private float recoilTime = -1f; // seconds into the current kick, -1 = at rest

        public int MaxAmmo => maxAmmo;
        /// <summary>Server ammo minus the owner's shots still on their way to the server.</summary>
        public int DisplayAmmo => Mathf.Max(0, ammo - pendingShots);

        private Transform FirePoint => firePoint != null ? firePoint : transform;
        private Transform EffectPoint => visualMuzzle != null ? visualMuzzle : FirePoint;

        private CarBodyVisual bodyVisual;

        /// <summary>C4 experiment switch: shots (and the gun model) follow the visual nose.</summary>
        public bool GunFollowsBodyYaw
        {
            get => gunFollowsBodyYaw;
            set => gunFollowsBodyYaw = value;
        }

        private void Awake()
        {
            car = GetComponent<CarController>();
            bodyVisual = GetComponent<CarBodyVisual>();
            if (recoilTarget != null)
                recoilRestPosition = recoilTarget.localPosition;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ammo = maxAmmo;
        }

        private void OnDestroy()
        {
            if (tracerMaterial != null)
                Destroy(tracerMaterial);
        }

        private void Update()
        {
            if (pendingShots > 0 && (!isOwned || Time.time - lastLocalShotTime > 1f))
                pendingShots = 0;

            UpdateRecoil();
        }

        // Fast linear kick back, then an ease-out return. A new shot restarts from the current
        // offset's phase, so rapid fire never snaps the model.
        private void UpdateRecoil()
        {
            if (recoilTarget == null || recoilTime < 0f)
                return;

            recoilTime += Time.deltaTime;
            float amount;
            if (recoilTime < recoilKickTime)
            {
                amount = recoilTime / recoilKickTime;
            }
            else
            {
                float t = recoilReturnTime > 0f ? (recoilTime - recoilKickTime) / recoilReturnTime : 1f;
                if (t >= 1f)
                {
                    recoilTime = -1f;
                    t = 1f;
                }
                amount = (1f - t) * (1f - t);
            }

            recoilTarget.localPosition = recoilRestPosition
                + recoilTarget.localRotation * (Vector3.back * (recoilDistance * amount));
        }

        private void StartRecoil()
        {
            if (recoilTarget == null)
                return;

            // Mid-return: jump the clock to the point of the kick that matches the current offset.
            float current = 0f;
            if (recoilTime >= recoilKickTime && recoilReturnTime > 0f)
            {
                float t = (recoilTime - recoilKickTime) / recoilReturnTime;
                current = (1f - t) * (1f - t);
            }
            else if (recoilTime >= 0f && recoilKickTime > 0f)
            {
                current = recoilTime / recoilKickTime;
            }
            recoilTime = current * recoilKickTime;
        }

        /// <summary>Server: refill, capped at maxAmmo (an ammo crate, a pickup, a trigger).</summary>
        [Server]
        public void ServerAddAmmo(int amount)
        {
            if (amount > 0)
                ammo = Mathf.Min(maxAmmo, ammo + amount);
        }

        private void OnAmmoChanged(int oldValue, int newValue)
        {
            if (newValue < oldValue)
                pendingShots = Mathf.Max(0, pendingShots - (oldValue - newValue));
        }

        // ---------- owner: fire ----------

        /// <summary>
        /// Owner (the driver holds the car's authority): fire one shot if the gun is ready.
        /// Returns false on cooldown, out of ammo, or when this client doesn't drive the car.
        /// </summary>
        public bool TryFire(PlayerHitReporter shooter)
        {
            if (!isOwned || shooter == null || !car.HasDriver || !car.VehicleIsAlive)
                return false;
            if (DisplayAmmo <= 0 || Time.time - lastLocalShotTime < fireInterval)
                return false;

            lastLocalShotTime = Time.time;

            Transform fp = FirePoint;
            Vector2 offset = new Vector2(Random.Range(-originSpread.x, originSpread.x),
                                         Random.Range(-originSpread.y, originSpread.y));
            Vector3 origin = Origin(offset);
            Vector2 cone = Random.insideUnitCircle * spreadAngle;
            Vector3 dir = fp.rotation * Quaternion.Euler(-cone.y, cone.x, 0f) * Vector3.forward;

            // "Aim with the wheel": turn the shot around the car's pivot by the visual yaw.
            if (gunFollowsBodyYaw && bodyVisual != null && bodyVisual.EffectEnabled)
            {
                Quaternion yaw = Quaternion.AngleAxis(bodyVisual.VisualYaw, transform.up);
                origin = transform.position + yaw * (origin - transform.position);
                dir = yaw * dir;
            }

            bool hit = TryRaycast(origin, dir, shooter.transform, out RaycastHit info);
            Vector3 end = hit ? info.point : origin + dir * range;

            NetworkBehaviour target = null;
            if (hit && DamageRouter.FindReceiver(info.collider) is NetworkBehaviour receiver)
                target = receiver;

            PlayEffects(EffectOrigin(offset), end, hit ? info.normal : Vector3.zero, hit);

            // Before the command: on the host it runs right away and its ammo hook settles this.
            pendingShots++;
            CmdFire(offset, end, hit ? info.normal : Vector3.zero, hit, target, dir * hitPushSpeed);
            return true;
        }

        // Ray start for an offset in the fire point's X/Y plane (unscaled metres).
        private Vector3 Origin(Vector2 offset)
        {
            Transform fp = FirePoint;
            return fp.position + fp.right * offset.x + fp.up * offset.y;
        }

        // Cosmetic start of the tracer / flash: the same offset on the visual muzzle.
        private Vector3 EffectOrigin(Vector2 offset)
        {
            Transform ep = EffectPoint;
            return ep.position + ep.right * offset.x + ep.up * offset.y;
        }

        // Nearest hit that is neither this car nor the driver.
        private bool TryRaycast(Vector3 origin, Vector3 dir, Transform shooter, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, hitMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Transform t = hitBuffer[i].collider.transform;
                if (t.IsChildOf(transform) || t.IsChildOf(shooter))
                    continue;
                if (hitBuffer[i].distance < best)
                {
                    best = hitBuffer[i].distance;
                    nearest = hitBuffer[i];
                    found = true;
                }
            }
            return found;
        }

        [Command]
        private void CmdFire(Vector2 offset, Vector3 end, Vector3 normal, bool hit, NetworkBehaviour target, Vector3 force)
        {
            if (!car.HasDriver || !car.VehicleIsAlive || ammo <= 0)
                return;
            // Half the interval of slack: command arrival jitters with the network.
            if (Time.time - lastServerShotTime < fireInterval * 0.5f)
                return;

            PlayerHealth driver = car.GetCurrentDriverServer();
            if (driver == null || driver.IsIncapacitated)
                return;

            lastServerShotTime = Time.time;
            ammo--;

            // Damage is the server's value; the client only says what it hit. Pivots of big targets
            // can sit well away from the hit point, hence the slack on the range.
            if (target != null && target.gameObject != gameObject && target is IDamageReceiver receiver
                && Vector3.Distance(FirePoint.position, target.transform.position) <= range + 10f)
            {
                var reporter = driver.GetComponent<PlayerHitReporter>();
                if (reporter != null)
                    reporter.ServerApplyHit(receiver, damage, Vector3.ClampMagnitude(force, hitPushSpeed));
            }

            RpcShot(offset, end, normal, hit);
        }

        [ClientRpc(includeOwner = false, channel = Channels.Unreliable)]
        private void RpcShot(Vector2 offset, Vector3 end, Vector3 normal, bool hit)
        {
            PlayEffects(EffectOrigin(offset), end, normal, hit);
        }

        // ---------- effects (cosmetic, every peer) ----------

        private void PlayEffects(Vector3 origin, Vector3 end, Vector3 normal, bool hit)
        {
            Transform fp = EffectPoint;
            StartRecoil();

            if (muzzleFlashPrefab != null)
            {
                GameObject flash = Instantiate(muzzleFlashPrefab, origin, fp.rotation, fp);
                SetLayer(flash, 0); // FPSF effects sit on the viewmodel layer the main camera skips
                Destroy(flash, 2f);
            }

            if (shotEffect != null)
            {
                // Play() does nothing while the system still runs from the last shot: stop emitting
                // first (live particles stay), then restart so the bursts fire again.
                shotEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                shotEffect.Play(true);
            }

            if (playFireSound && fireClip != null)
                AudioSource.PlayClipAtPoint(fireClip, origin, fireVolume);

            StartCoroutine(Tracer(origin, end));

            if (hit && impactPrefab != null)
            {
                Quaternion rot = normal.sqrMagnitude > 0f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                Destroy(Instantiate(impactPrefab, end, rot), impactLifetime);
            }
        }

        private IEnumerator Tracer(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null)
                tracerMaterial = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.material = tracerMaterial;
            line.startColor = line.endColor = tracerColor;
            line.startWidth = line.endWidth = tracerWidth;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            yield return new WaitForSeconds(tracerDuration);
            Destroy(go);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        private void OnDrawGizmosSelected()
        {
            Transform fp = FirePoint;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(fp.position, fp.forward * Mathf.Min(range, 30f));
            Gizmos.matrix = fp.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(originSpread.x * 2f, originSpread.y * 2f, 0.01f));
        }
    }
}
