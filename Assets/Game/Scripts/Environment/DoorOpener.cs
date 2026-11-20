using FPSGame.Enemies;
using FPSGame.Player;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    // A sliding door: rises when opened, comes back down when closed.
    //
    // Sync: the server writes (target, amount at the start of the move, NetworkTime of that start);
    // every peer computes the pose itself from NetworkTime — nothing is sent per frame and late
    // joiners land on the right pose. The colliders stay on the whole time: a door that is almost
    // shut physically blocks anyone trying to slip under it.
    //
    // Safety: the door never comes down on anyone. A close doesn't start while the doorway is
    // occupied (retried later), and a door that is already closing goes back up as soon as
    // someone steps into the doorway, then tries again.
    public class DoorOpener : NetworkBehaviour
    {
        [SerializeField] private GameObject door;

        [Header("Movement")]
        [Tooltip("How far the door rises, along its parent's up axis (parent units — metres unless the parent is scaled). 0 = the door's own height, from its colliders.")]
        [SerializeField, Min(0f)] private float openHeight = 0f;

        [Tooltip("Door speed both ways, metres per second.")]
        [SerializeField, Min(0.01f)] private float moveSpeed = 2f;

        [Header("Auto close")]
        [Tooltip("Seconds the door stays fully open before closing itself. Opening it again while open restarts the timer. 0 = stays open until something closes it.")]
        [SerializeField, Min(0f)] private float autoCloseDelay = 0f;

        [Tooltip("If the doorway is occupied when the door should close (or the door had to go back up), try again after this many seconds.")]
        [SerializeField, Min(0.1f)] private float blockedRetryDelay = 1f;

        [Header("Doorway")]
        [Tooltip("Optional box describing the doorway (may be a disabled collider). Empty = bounds of the door's own colliders in the closed pose.")]
        [SerializeField] private BoxCollider doorwayArea;

        [Tooltip("Extra clearance around the doorway box, in metres.")]
        [SerializeField, Min(0f)] private float doorwayPadding = 0.2f;

        [Tooltip("Besides players (always checked), solid colliders on these layers in the doorway also stop the door: enemies, loot, cars. Nothing = players only.")]
        [SerializeField] private LayerMask obstructionLayers = 0;

        [Space]
        [Tooltip("Server only. Fires when the door has come fully down — e.g. wire to InteractObjective.ResetObjective so it can be opened again.")]
        [SerializeField] private UnityEvent onClosed;

        // Where the door is heading. The pose is derived from the three values together;
        // Mirror sends a behaviour's dirty SyncVars in one message, so they arrive together.
        [SyncVar] private bool isOpened;
        [SyncVar] private float moveFromAmount;   // 0 = shut, 1 = fully open, at moveStartTime
        [SyncVar] private double moveStartTime;

        // Server only.
        private double closeAt = double.PositiveInfinity;   // a pending close (deadline, as bleedout)
        private bool shut = true;                           // onClosed already fired for this close

        private Vector3 closedLocalPosition;
        private float appliedAmount = -1f;

        private bool shapeResolved;
        private float autoHeight;
        private Transform doorwayFrame;
        private Bounds doorwayLocalBounds;

        private static readonly Collider[] OverlapBuffer = new Collider[32];

        /// <summary>The door is open or opening.</summary>
        public bool IsOpened => isOpened;

        /// <summary>0 = shut, 1 = fully open.</summary>
        public float OpenAmount => AmountAt(NetworkTime.time);

        public bool IsFullyClosed => !isOpened && OpenAmount <= 0f;

        private void Awake()
        {
            if (door != null)
                closedLocalPosition = door.transform.localPosition;
        }

        // ── Commands (server) ─────────────────────────────────────────────

        [Server]
        public void OpenDoor()
        {
            if (!isOpened)
                BeginMove(true);

            // Re-opening an open door only restarts the timer; it runs from the moment the door is fully up.
            closeAt = autoCloseDelay > 0f ? FullyOpenTime() + autoCloseDelay : double.PositiveInfinity;
        }

        // Safe to call every frame (KeycardLock does): while the doorway is occupied it only
        // schedules a retry instead of starting a close that would bounce straight back up.
        [Server]
        public void CloseDoor()
        {
            if (!isOpened)
                return;

            if (IsDoorwayObstructed())
            {
                closeAt = NetworkTime.time + blockedRetryDelay;
                return;
            }

            BeginMove(false);
            closeAt = double.PositiveInfinity;
        }

        [Server]
        public void ToggleDoor()
        {
            if (isOpened)
                CloseDoor();
            else
                OpenDoor();
        }

        [Server]
        private void BeginMove(bool open)
        {
            double now = NetworkTime.time;
            moveFromAmount = AmountAt(now);   // before isOpened changes — AmountAt reads it
            moveStartTime = now;
            isOpened = open;
            shut = false;
        }

        // ── Tick ──────────────────────────────────────────────────────────

        private void Update()
        {
            if (isServer)
                ServerTick();

            ApplyPose();
        }

        [Server]
        private void ServerTick()
        {
            double now = NetworkTime.time;

            if (isOpened)
            {
                if (now >= closeAt)
                    CloseDoor();   // re-checks the doorway and reschedules itself if occupied
                return;
            }

            if (shut)
                return;

            if (AmountAt(now) <= 0f)
            {
                shut = true;
                onClosed?.Invoke();
                return;
            }

            // Someone stepped under a closing door: back up, try again once it is fully open.
            if (IsDoorwayObstructed())
            {
                BeginMove(true);
                closeAt = FullyOpenTime() + blockedRetryDelay;
            }
        }

        private void ApplyPose()
        {
            if (door == null)
                return;

            float amount = AmountAt(NetworkTime.time);
            if (amount == appliedAmount)
                return;

            appliedAmount = amount;
            door.transform.localPosition = amount > 0f
                ? closedLocalPosition + Vector3.up * (LiftHeight * amount)
                : closedLocalPosition;
        }

        private float AmountAt(double time)
        {
            // A door that never moved needs no geometry (ProBuilder colliders may not exist yet).
            if (!isOpened && moveFromAmount <= 0f)
                return 0f;

            // Clients run NetworkTime behind the server (buffer), so a fresh move may start "in the future".
            float travelled = (float)System.Math.Max(0.0, time - moveStartTime) * Rate;
            return isOpened
                ? Mathf.Min(1f, moveFromAmount + travelled)
                : Mathf.Max(0f, moveFromAmount - travelled);
        }

        private double FullyOpenTime()
        {
            double reachedAt = moveStartTime + (1f - moveFromAmount) / Rate;
            return System.Math.Max(NetworkTime.time, reachedAt);
        }

        // Fraction of the full travel per second.
        private float Rate => moveSpeed / Mathf.Max(0.01f, LiftHeight);

        private float LiftHeight
        {
            get
            {
                if (openHeight > 0f)
                    return openHeight;
                ResolveShape();
                return autoHeight;
            }
        }

        // ── Doorway check ─────────────────────────────────────────────────

        // Players by geometry instead of a physics overlap: on the server other players' copies have
        // their CharacterController disabled, so no query would see them (as in FlameHazard).
        [Server]
        private bool IsDoorwayObstructed()
        {
            ResolveShape();
            if (doorwayFrame == null)
                return false;

            // The box is taken in the closed pose — the door (and a doorwayArea under it) moves.
            Matrix4x4 toWorld = ClosedPoseMatrix(doorwayFrame);
            Matrix4x4 toLocal = toWorld.inverse;
            Bounds local = doorwayLocalBounds;

            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                // A downed player in the doorway still blocks it; the dead and drivers (body parked) don't.
                if (player.IsDead || player.IsDriving)
                    continue;
                if (!player.TryGetComponent(out CharacterController body))
                    continue;

                Vector3 center = body.transform.TransformPoint(body.center);
                float reach = Mathf.Max(0f, body.height * 0.5f - body.radius);
                float radius = body.radius + doorwayPadding;

                if (SphereTouchesBox(center, radius, toWorld, toLocal, local)
                    || SphereTouchesBox(center + Vector3.up * reach, radius, toWorld, toLocal, local)
                    || SphereTouchesBox(center - Vector3.up * reach, radius, toWorld, toLocal, local))
                    return true;
            }

            if (obstructionLayers.value != 0)
            {
                Vector3 center = toWorld.MultiplyPoint3x4(local.center);
                Vector3 scale = toWorld.lossyScale;
                Vector3 half = Vector3.Scale(local.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)))
                               + Vector3.one * doorwayPadding;

                int count = Physics.OverlapBoxNonAlloc(center, half, OverlapBuffer, toWorld.rotation,
                    obstructionLayers, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    Transform hit = OverlapBuffer[i].transform;
                    // The door assembly itself (door, frame, walls) never blocks.
                    if (hit.IsChildOf(transform) || (door != null && hit.IsChildOf(door.transform)))
                        continue;
                    return true;
                }
            }

            return false;
        }

        private static bool SphereTouchesBox(Vector3 point, float radius, Matrix4x4 toWorld, Matrix4x4 toLocal, Bounds local)
        {
            Vector3 closestLocal = local.ClosestPoint(toLocal.MultiplyPoint3x4(point));
            Vector3 closest = toWorld.MultiplyPoint3x4(closestLocal);
            return (closest - point).sqrMagnitude <= radius * radius;
        }

        // World matrix of t as it is when the door is shut.
        private Matrix4x4 ClosedPoseMatrix(Transform t)
        {
            if (door == null || !t.IsChildOf(door.transform))
                return t.localToWorldMatrix;

            Transform d = door.transform;
            Matrix4x4 parent = d.parent != null ? d.parent.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 closedDoor = parent * Matrix4x4.TRS(closedLocalPosition, d.localRotation, d.localScale);
            return closedDoor * d.worldToLocalMatrix * t.localToWorldMatrix;
        }

        // Resolved lazily: ProBuilder assigns the collider mesh at runtime, after Awake.
        private void ResolveShape()
        {
            if (shapeResolved)
                return;
            shapeResolved = true;

            bool hasDoorBounds = TryGetDoorBounds(out Bounds doorBounds);

            if (doorwayArea != null)
            {
                doorwayFrame = doorwayArea.transform;
                doorwayLocalBounds = new Bounds(doorwayArea.center, doorwayArea.size);
            }
            else if (hasDoorBounds)
            {
                doorwayFrame = door.transform;
                doorwayLocalBounds = doorBounds;
            }
            else
            {
                Debug.LogWarning($"[DoorOpener] {name}: no Box/Mesh collider on the door and no doorwayArea — the doorway isn't checked before closing.", this);
            }

            if (hasDoorBounds)
            {
                // Door height in its parent's space (that's where it moves).
                Transform d = door.transform;
                Matrix4x4 toParent = Matrix4x4.TRS(Vector3.zero, d.localRotation, d.localScale);
                float minY = float.MaxValue, maxY = float.MinValue;
                Vector3 min = doorBounds.min, max = doorBounds.max;
                for (int i = 0; i < 8; i++)
                {
                    float y = toParent.MultiplyPoint3x4(Corner(min, max, i)).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                autoHeight = maxY - minY;
            }

            if (openHeight <= 0f && autoHeight <= 0f)
            {
                autoHeight = 3f;
                Debug.LogWarning($"[DoorOpener] {name}: can't measure the door's height — set Open Height. Using {autoHeight} m.", this);
            }
        }

        // Union of the door's Box/Mesh collider shapes in the door's local space.
        // Shapes, not Collider.bounds — those are empty for a disabled collider.
        private bool TryGetDoorBounds(out Bounds result)
        {
            result = default;
            if (door == null)
                return false;

            Transform frame = door.transform;
            bool any = false;

            foreach (Collider col in door.GetComponentsInChildren<Collider>(true))
            {
                Bounds shape;
                if (col is BoxCollider box)
                    shape = new Bounds(box.center, box.size);
                else if (col is MeshCollider meshCol && meshCol.sharedMesh != null)
                    shape = meshCol.sharedMesh.bounds;
                else
                    continue;

                Vector3 min = shape.min, max = shape.max;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 inFrame = frame.InverseTransformPoint(col.transform.TransformPoint(Corner(min, max, i)));
                    if (!any)
                    {
                        result = new Bounds(inFrame, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(inFrame);
                    }
                }
            }

            return any;
        }

        private static Vector3 Corner(Vector3 min, Vector3 max, int i)
        {
            return new Vector3(
                (i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z);
        }

        private void OnDrawGizmosSelected()
        {
            if (doorwayArea != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
                Gizmos.matrix = doorwayArea.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(doorwayArea.center, doorwayArea.size);
                Gizmos.matrix = Matrix4x4.identity;
            }

            if (door != null && openHeight > 0f && !Application.isPlaying)
            {
                Transform d = door.transform;
                Vector3 lift = d.parent != null ? d.parent.TransformVector(Vector3.up * openHeight) : Vector3.up * openHeight;
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(d.position, d.position + lift);
                Gizmos.DrawWireSphere(d.position + lift, 0.1f);
            }
        }
    }
}
