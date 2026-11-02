using Mirror;
using UnityEngine;
using FPSGame.Hazards;

namespace FPSGame.Environment
{
    /// <summary>
    /// Unarmed enemy car: keeps ahead of its target (higher world Z), lines up with it on X and drops
    /// obstacles behind itself at a fixed interval. It never leaves a box around its start position (the
    /// initial point), however far the target goes; with no target it drives back to that point.
    /// Moved by <c>NavMeshAgent.Move</c> each frame instead of a path: the target moves all the time and the
    /// car follows it, not a destination.
    /// </summary>
    public class ObstacleLayerCarBehaviour : EnemyCarBehaviour
    {
        [Header("Range around the initial point (the car's world position when the server starts)")]
        [Tooltip("How far the car may go from the initial point toward +Z (the direction of travel).")]
        [SerializeField, Min(0f)] private float maxZForward = 40f;
        [Tooltip("How far the car may go from the initial point toward -Z.")]
        [SerializeField, Min(0f)] private float maxZBackward = 20f;
        [Tooltip("How far the car may go from the initial point toward +X.")]
        [SerializeField, Min(0f)] private float maxXRight = 8f;
        [Tooltip("How far the car may go from the initial point toward -X.")]
        [SerializeField, Min(0f)] private float maxXLeft = 8f;
        [Tooltip("Speed (m/s) of the drive back to the initial point while there is no target.")]
        [SerializeField, Min(0f)] private float returnSpeed = 6f;

        [Header("Following along Z (world Z, measured from the target; positive = ahead of it)")]
        [Tooltip("Gap the car tries to hold, as far as the range allows.")]
        [SerializeField] private float preferredZOffset = 30f;
        [Tooltip("Extra speed (m/s) on top of the target's own speed that the car may use to close or open the gap.")]
        [SerializeField, Min(0f)] private float zCorrectionSpeed = 8f;
        [Tooltip("Gap error (m) → speed correction (m/s). Higher = snappier.")]
        [SerializeField, Min(0f)] private float zGain = 0.4f;
        [Tooltip("The gap isn't corrected while it is within this distance (m) of the wanted one, so the car " +
                 "doesn't mirror every small move of the target.")]
        [SerializeField, Min(0f)] private float zDeadZone = 4f;
        [Tooltip("Max change of the car's own Z speed (m/s²). Lower = lazier: the car lags behind the target's " +
                 "speeding up and braking instead of copying it at once.")]
        [SerializeField, Min(0.1f)] private float zAcceleration = 4f;
        [Tooltip("How fast the measured target speed follows its real value. Lower = more delay and smoother, higher = less lag, more jitter.")]
        [SerializeField, Min(0.1f)] private float speedSmoothing = 2f;

        [Header("Alignment (world X)")]
        [Tooltip("Max sideways speed (m/s) while lining up with the target.")]
        [SerializeField, Min(0f)] private float lateralSpeed = 8f;
        [SerializeField, Min(0f)] private float lateralAcceleration = 16f;
        [Tooltip("X error (m) → sideways speed (m/s). Higher = tighter lock, lower = lazier follow.")]
        [SerializeField, Min(0f)] private float lateralGain = 2f;

        [Header("Obstacles")]
        [Tooltip("Networked prefab (NetworkIdentity on its root), registered in CustomNetworkManager.runtimeSpawnPrefabs. " +
                 "Spawned facing world -Z, so a flying obstacle travels back toward the target. " +
                 "Nothing is spawned while the target is ahead of the car (higher Z).")]
        [SerializeField] private GameObject obstaclePrefab;
        [Tooltip("Where obstacles appear (at the car's rear). Empty = Rear Offset behind the car's pivot.")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private float rearOffset = 4f;
        [SerializeField] private float spawnHeightOffset = 0f;
        [Tooltip("Flight speed of each spawned obstacle (m/s), along world -Z. Overrides the speed set on the obstacle prefab.")]
        [SerializeField, Min(0f)] private float obstacleSpeed = 15f;
        [Tooltip("Seconds between two obstacles.")]
        [SerializeField, Min(0.1f)] private float spawnInterval = 2f;
        [Tooltip("Each obstacle is shifted on X by a random value in ±this around the spawn position.")]
        [SerializeField, Min(0f)] private float spawnSpreadX = 4f;

        private Vector3 initialPosition;
        private bool initialCaptured;

        private float targetVelocityZ;
        private float lastTargetZ;
        private bool hasLastTargetZ;
        private bool speedSeeded;
        private float lateralVelocity;
        private float velocityZ; // the car's own Z speed; chases the desired one with limited acceleration
        private Vector3 velocity;
        private double nextSpawnTime;

        private float MinZ => initialPosition.z - maxZBackward;
        private float MaxZ => initialPosition.z + maxZForward;
        private float MinX => initialPosition.x - maxXLeft;
        private float MaxX => initialPosition.x + maxXRight;

        public override Vector3 ServerVelocity => velocity;

        public override void OnStartServer()
        {
            base.OnStartServer();
            // The point everything is measured from: wherever the car stands when the scene starts.
            initialPosition = transform.position;
            initialCaptured = true;
        }

        public override void ServerOnTargetChanged()
        {
            hasLastTargetZ = false;
            speedSeeded = false;
            nextSpawnTime = NetworkTime.time + spawnInterval;
        }

        // No target: drive back to the initial point (the car's speeds carry over, so it eases out of the chase).
        public override void ServerIdle()
        {
            hasLastTargetZ = false;
            speedSeeded = false;

            float dt = Time.deltaTime;
            if (dt <= 0f || !initialCaptured)
                return;

            Vector3 position = transform.position;
            Vector2 toInitial = new(initialPosition.x - position.x, initialPosition.z - position.z);
            float distance = toInitial.magnitude;

            Vector2 desired = Vector2.zero;
            if (distance > 0.05f)
                desired = toInitial / distance * Mathf.Min(returnSpeed, distance); // slows down over the last metres

            lateralVelocity = Mathf.MoveTowards(lateralVelocity, desired.x, lateralAcceleration * dt);
            velocityZ = Mathf.MoveTowards(velocityZ, desired.y, zAcceleration * dt);

            float stepX = lateralVelocity * dt;
            float stepZ = velocityZ * dt;
            // Never overshoot the point.
            if (Mathf.Abs(stepX) > Mathf.Abs(toInitial.x)) stepX = toInitial.x;
            if (Mathf.Abs(stepZ) > Mathf.Abs(toInitial.y)) stepZ = toInitial.y;

            Move(new Vector3(stepX, 0f, stepZ), dt);
        }

        public override void ServerTick()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !initialCaptured)
                return;

            Vector3 position = transform.position;
            Vector3 targetPosition = Car.TargetPosition;

            MeasureTargetSpeed(targetPosition.z, dt);
            Move(ComputeStep(position, targetPosition, dt), dt);

            TrySpawnObstacle(targetPosition);
        }

        private void Move(Vector3 step, float dt)
        {
            var agent = Car.Agent;
            if (!agent.isOnNavMesh)
                return;
            agent.Move(step);
            velocity = step / dt;
        }

        // The target is a networked car whose velocity we have no clean access to — differentiate its position instead.
        private void MeasureTargetSpeed(float targetZ, float dt)
        {
            if (hasLastTargetZ)
            {
                float raw = (targetZ - lastTargetZ) / dt;
                if (!speedSeeded)
                {
                    targetVelocityZ = raw; // no smoothing from a zero start
                    speedSeeded = true;
                }
                else
                {
                    targetVelocityZ = Mathf.Lerp(targetVelocityZ, raw, 1f - Mathf.Exp(-speedSmoothing * dt));
                }
            }
            else
            {
                targetVelocityZ = 0f;
            }
            lastTargetZ = targetZ;
            hasLastTargetZ = true;
        }

        private Vector3 ComputeStep(Vector3 position, Vector3 targetPosition, float dt)
        {
            // Z: the wanted place is "preferred gap ahead of the target", cut down to the range. The speed we want is
            // the target's speed plus a correction toward it (none inside the dead zone); the car's own speed only
            // chases that with limited acceleration — that is the lag.
            float wantedZ = Mathf.Clamp(targetPosition.z + preferredZOffset, MinZ, MaxZ);
            float error = wantedZ - position.z;
            error = Mathf.Sign(error) * Mathf.Max(0f, Mathf.Abs(error) - zDeadZone);
            float correction = Mathf.Clamp(error * zGain, -zCorrectionSpeed, zCorrectionSpeed);
            velocityZ = Mathf.MoveTowards(velocityZ, targetVelocityZ + correction, zAcceleration * dt);

            // The range is a hard wall.
            float newZ = position.z + velocityZ * dt;
            float clampedZ = Mathf.Clamp(newZ, MinZ, MaxZ);
            if (clampedZ != newZ)
                velocityZ = (clampedZ - position.z) / dt; // held at the edge: no speed built up against it

            // X: line up with the target inside the range; the gap closes with limited speed and acceleration.
            float dx = Mathf.Clamp(targetPosition.x, MinX, MaxX) - position.x;
            float desiredLateral = Mathf.Clamp(dx * lateralGain, -lateralSpeed, lateralSpeed);
            lateralVelocity = Mathf.MoveTowards(lateralVelocity, desiredLateral, lateralAcceleration * dt);
            float stepX = lateralVelocity * dt;
            if (Mathf.Abs(stepX) > Mathf.Abs(dx))
                stepX = dx; // don't overshoot the lane
            float clampedX = Mathf.Clamp(position.x + stepX, MinX, MaxX);

            return new Vector3(clampedX - position.x, 0f, clampedZ - position.z);
        }

        private void TrySpawnObstacle(Vector3 targetPosition)
        {
            // A target ahead of the car is out of the obstacles' way (they fly toward -Z): hold fire, and wait a
            // full interval once it falls behind again.
            if (targetPosition.z > transform.position.z)
            {
                nextSpawnTime = NetworkTime.time + spawnInterval;
                return;
            }

            if (NetworkTime.time < nextSpawnTime)
                return;
            nextSpawnTime = NetworkTime.time + spawnInterval;

            if (obstaclePrefab == null)
            {
                Debug.LogError($"[EnemyCar] {name}: obstaclePrefab is not assigned!", this);
                return;
            }

            Vector3 origin = spawnPoint != null
                ? spawnPoint.position
                : transform.position + Vector3.back * rearOffset;
            origin.y += spawnHeightOffset;
            origin.x += Random.Range(-spawnSpreadX, spawnSpreadX);

            GameObject obstacle = Instantiate(obstaclePrefab, origin, Quaternion.LookRotation(Vector3.back));
            if (obstacle.TryGetComponent(out CarEjectionTrigger flight))
                flight.SetSpeed(obstacleSpeed);
            else
                Debug.LogWarning($"[EnemyCar] {name}: obstacle prefab has no CarEjectionTrigger, Obstacle Speed is ignored.", this);

            NetworkServer.Spawn(obstacle);
        }

        private void OnDrawGizmosSelected()
        {
            // The range box (around the real initial point while playing as server, else around where the car stands now).
            Vector3 center = initialCaptured ? initialPosition : transform.position;
            Vector3 size = new(maxXLeft + maxXRight, 0.2f, maxZBackward + maxZForward);
            Vector3 boxCenter = new(center.x + (maxXRight - maxXLeft) * 0.5f, center.y, center.z + (maxZForward - maxZBackward) * 0.5f);
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 1f);
            Gizmos.DrawWireCube(boxCenter, size);
            Gizmos.DrawSphere(center, 0.4f);

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Vector3 origin = spawnPoint != null ? spawnPoint.position : transform.position + Vector3.back * rearOffset;
            origin.y += spawnHeightOffset;
            Gizmos.DrawLine(origin + Vector3.left * spawnSpreadX, origin + Vector3.right * spawnSpreadX);
        }
    }
}
