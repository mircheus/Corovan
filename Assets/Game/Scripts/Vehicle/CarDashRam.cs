using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Combat;
using FPSGame.Config;
using FPSGame.Player;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Ram damage from the car's dash. While a dash is on (the owner's <see cref="CarController.DashVelocity"/> above
    /// <see cref="minDashSpeed"/>), the owner overlaps the car's solid box colliders — grown by <see cref="padding"/> and
    /// stretched along the dash by <see cref="extraReach"/> plus one physics step of travel — and reports every
    /// <see cref="IDamageReceiver"/> it touches, once per dash. The step of travel matters: the car's wall sweep stops it
    /// short of an enemy car and cancels the dash into it, so the ram must see the target the step before contact.
    /// Server: the driver is alive, a dash was reported lately, the target is near and not hit yet in this dash;
    /// the damage is the server's (<see cref="damage"/>, scaled by the speed left in the dash) and goes through the
    /// driver's <see cref="PlayerHitReporter"/> (hit marker on a kill, the target turns on the driver).
    /// Runs before CarController so it reads the dash before this step's wall sweep cancels it.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(CarController))]
    public class CarDashRam : NetworkBehaviour
    {
        [Header("Damage (server)")]
        [Tooltip("Damage of a ram at the dash's full speed. Overridden by the scene's BalanceConfig (playerCar.dashRamDamage).")]
        [SerializeField, Min(0)] private int damage = 100;
        [Tooltip("On: damage and knockback scale with the speed left in the dash at the moment of the hit (a dash bleeds " +
                 "off as it goes). Off: every ram deals the full amount.")]
        [SerializeField] private bool scaleWithDashSpeed = true;
        [Tooltip("Push (units/sec) handed to the target along the dash — the ragdoll's fling when the ram kills.")]
        [SerializeField, Min(0f)] private float hitPush = 15f;
        [Tooltip("Knock speed (units/sec) along the dash given to targets that can be shoved (IKnockbackReceiver: enemy " +
                 "cars). The target's own knockbackScale/decay/stun shape the slide. 0 = no knockback.")]
        [SerializeField, Min(0f)] private float knockbackSpeed = 12f;
        [Tooltip("Damage the rammer's own car takes per target hit. 0 = rams are free.")]
        [SerializeField, Min(0)] private int selfDamage = 0;

        [Header("Targets")]
        [Tooltip("Ram other players on foot (friendly fire, PlayerHitReporter's teammate multiplier applies). " +
                 "Players riding this car are never hit.")]
        [SerializeField] private bool hitPlayers = false;
        [Tooltip("Ram other player cars (PlayerHitReporter's vehicle multiplier applies).")]
        [SerializeField] private bool hitPlayerCars = false;
        [Tooltip("Layers the ram box looks at. Colliders without an IDamageReceiver above them are ignored anyway.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Ram box (owner)")]
        [Tooltip("Below this dash speed (units/sec) the dash no longer rams.")]
        [SerializeField, Min(0.1f)] private float minDashSpeed = 8f;
        [Tooltip("Metres the car's box is grown by on every side.")]
        [SerializeField, Min(0f)] private float padding = 0.15f;
        [Tooltip("Metres the box reaches ahead along the dash, on top of one physics step of travel.")]
        [SerializeField, Min(0f)] private float extraReach = 0.4f;
        [Tooltip("Metres the bottom of the box is raised, so the road under the car is never in it.")]
        [SerializeField, Min(0f)] private float groundClearance = 0.3f;
        [Tooltip("Camera shake on the driver for each target rammed.")]
        [SerializeField, Min(0f)] private float cameraShake = 0.35f;

        [Header("Validation (server)")]
        [Tooltip("Seconds past the dash's own duration a ram report is still accepted (network delay).")]
        [SerializeField, Min(0f)] private float dashWindowSlack = 0.5f;
        [Tooltip("A ram on a target whose pivot is farther than this from the car's pivot is rejected. " +
                 "Generous: the server sees the car where it was an interpolation delay ago.")]
        [SerializeField, Min(1f)] private float maxServerDistance = 15f;

        [SerializeField] private bool debugRam;

        private CarController car;
        private Rigidbody rb;
        private BoxCollider[] boxes;

        // Owner: targets hit in the current dash, and the speed it started with.
        private readonly HashSet<NetworkBehaviour> hitThisDash = new HashSet<NetworkBehaviour>();
        private float dashStartSpeed;

        // Server: same, keyed by the dash the driver reported last.
        private readonly HashSet<NetworkBehaviour> serverHitThisDash = new HashSet<NetworkBehaviour>();
        private float serverDashTime = float.NegativeInfinity;

        // Bigger than usual: a droid pack's ragdoll bones fill a small buffer before anything else gets in.
        private static readonly Collider[] overlapBuffer = new Collider[128];

        private void Awake()
        {
            car = GetComponent<CarController>();
            rb = GetComponent<Rigidbody>();
            var solid = new List<BoxCollider>();
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (!box.isTrigger)
                    solid.Add(box);
            }
            boxes = solid.ToArray();
            car.OnDashStarted += OnDashStarted;
        }

        private void OnDestroy()
        {
            if (car != null)
                car.OnDashStarted -= OnDashStarted;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (SceneBalance.Current != null)
                damage = SceneBalance.Current.playerCar.dashRamDamage;
        }

        private void OnDashStarted()
        {
            hitThisDash.Clear();
            dashStartSpeed = car.DashVelocity.magnitude;
        }

        // ---------- owner: find what the dash runs into ----------

        private void FixedUpdate()
        {
            if (!isOwned || !car.HasDriver || !car.VehicleIsAlive || dashStartSpeed <= 0f)
                return;

            Vector3 dash = car.DashVelocity;
            float speed = dash.magnitude;
            if (speed < minDashSpeed)
                return;

            Vector3 dir = dash / speed;
            float reach = extraReach + speed * Time.fixedDeltaTime;

            foreach (BoxCollider box in boxes)
            {
                if (box == null || !box.enabled || !box.gameObject.activeInHierarchy)
                    continue;

                Transform t = box.transform;
                Vector3 scale = t.lossyScale;
                Vector3 size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                float lift = Mathf.Clamp(groundClearance, 0f, Mathf.Max(0f, size.y - 0.05f));

                // The box stretched toward the dash: shifted half the reach along it, grown by the reach's
                // share on each local axis (an axis-aligned cover of the swept box — exact for a straight dash).
                Vector3 localDir = Quaternion.Inverse(t.rotation) * dir;
                Vector3 half = new Vector3(size.x, size.y - lift, size.z) * 0.5f
                             + Vector3.one * padding
                             + new Vector3(Mathf.Abs(localDir.x), Mathf.Abs(localDir.y), Mathf.Abs(localDir.z)) * (reach * 0.5f);
                Vector3 center = t.TransformPoint(box.center) + t.up * (lift * 0.5f) + dir * (reach * 0.5f);

                int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, t.rotation, hitMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    TryRam(overlapBuffer[i], speed, dir);
            }
        }

        private void TryRam(Collider hit, float speed, Vector3 dir)
        {
            if (hit.attachedRigidbody == rb || hit.transform.IsChildOf(transform))
                return;

            IDamageReceiver receiver = DamageRouter.FindReceiver(hit);
            if (!(receiver is NetworkBehaviour target) || receiver.IsDead || !Allowed(receiver, target))
                return;
            if (!hitThisDash.Add(target))
                return;

            if (debugRam)
                Debug.Log($"[RAM] {name} rams '{target.name}' via '{hit.name}' at {speed:F1}/{dashStartSpeed:F1}", hit);

            CmdRam(target, speed / dashStartSpeed, dir);
            PlayLocalFeedback();
        }

        private void PlayLocalFeedback()
        {
            UIManager.Instance?.Hitmarker?.Enable();

            NetworkIdentity local = NetworkClient.localPlayer;
            CharacterManager character = local != null ? local.GetComponent<CharacterManager>() : null;
            if (cameraShake > 0f && character != null && character.cameraManager != null)
                character.cameraManager.ShakeCameras(cameraShake);
        }

        // Same rules on both sides: no own car, no one riding it, players and player cars only when switched on.
        private bool Allowed(IDamageReceiver receiver, NetworkBehaviour target)
        {
            if (ReferenceEquals(target, car))
                return false;
            if (receiver is PlayerHealth)
                return hitPlayers && !RidesThisCar(target.netId);
            if (receiver is CarController)
                return hitPlayerCars;
            return true;
        }

        private bool RidesThisCar(uint playerNetId)
        {
            if (playerNetId == 0)
                return false;
            if (car.DriverNetId == playerNetId)
                return true;
            foreach (CarPassengerSeat seat in GetComponents<CarPassengerSeat>())
            {
                if (seat.PassengerNetId == playerNetId)
                    return true;
            }
            return false;
        }

        // ---------- server: check the report, apply the server's damage ----------

        [Command]
        private void CmdRam(NetworkBehaviour target, float speedFraction, Vector3 dir)
        {
            if (!car.HasDriver || !car.VehicleIsAlive)
                return;

            PlayerHealth driver = car.GetCurrentDriverServer();
            PlayerHitReporter reporter = driver != null ? driver.GetComponent<PlayerHitReporter>() : null;
            if (driver == null || driver.IsIncapacitated || reporter == null)
                return;

            // CmdReportDash travels on the same reliable channel ahead of this one, so the dash is already known.
            float sinceDash = Time.time - car.ServerLastDashTime;
            if (sinceDash < 0f || sinceDash > car.DashDuration + dashWindowSlack)
                return;

            if (target == null || target.gameObject == gameObject || !(target is IDamageReceiver receiver)
                || receiver.IsDead || !Allowed(receiver, target))
                return;
            if (Vector3.Distance(transform.position, target.transform.position) > maxServerDistance)
                return;

            if (serverDashTime != car.ServerLastDashTime)
            {
                serverDashTime = car.ServerLastDashTime;
                serverHitThisDash.Clear();
            }
            if (!serverHitThisDash.Add(target))
                return;

            float scale = scaleWithDashSpeed ? Mathf.Clamp01(speedFraction) : 1f;
            int dealt = Mathf.RoundToInt(damage * scale);
            Vector3 along = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
            Vector3 push = along * hitPush;

            // Before the damage: a killing ram hands the knock on to the wreck's launch.
            if (knockbackSpeed > 0f && target.TryGetComponent(out IKnockbackReceiver knockable))
                knockable.ServerKnockback(along * (knockbackSpeed * scale));

            bool applied = reporter.ServerApplyHit(receiver, dealt, push);
            if (applied && selfDamage > 0)
                car.TakeDamage(selfDamage);
        }
    }
}
