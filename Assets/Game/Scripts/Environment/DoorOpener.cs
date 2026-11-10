using FPSGame.Enemies;
using FPSGame.Player;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    public class DoorOpener : NetworkBehaviour
    {
        [SerializeField] private GameObject door;

        [Header("Auto close")]
        [Tooltip("Seconds the door stays open before closing itself. Opening it again while open restarts the timer. 0 = stays open until something closes it.")]
        [SerializeField, Min(0f)] private float autoCloseDelay = 0f;

        [Tooltip("If a player stands in the doorway when the timer runs out, try again after this many seconds.")]
        [SerializeField, Min(0.1f)] private float blockedRetryDelay = 1f;

        [Tooltip("Optional box describing the doorway (may be a disabled collider). Empty = bounds of the door's own colliders.")]
        [SerializeField] private BoxCollider doorwayArea;

        [Tooltip("Extra clearance around the doorway box, in metres.")]
        [SerializeField, Min(0f)] private float doorwayPadding = 0.2f;

        [Space]
        [Tooltip("Server only. Fires whenever the door closes (timer, CloseDoor, ToggleDoor) — e.g. wire to InteractObjective.ResetObjective so it can be opened again.")]
        [SerializeField] private UnityEvent onClosed;

        [SyncVar(hook = nameof(OnOpenedStateChange))]
        private bool isOpened;

        // Server only. A deadline, not a countdown — same pattern as bleedout.
        private double closeAt;

        private bool doorwayResolved;
        private Transform doorwayFrame;
        private Bounds doorwayLocalBounds;

        public bool IsOpened => isOpened;

        [Server]
        public void OpenDoor()
        {
            SetOpened(true);
        }

        [Server]
        public void CloseDoor()
        {
            SetOpened(false);
        }

        [Server]
        public void ToggleDoor()
        {
            SetOpened(!isOpened);
        }

        [Server]
        private void SetOpened(bool opened)
        {
            // Opening an already open door only restarts the auto-close timer.
            if (opened)
                closeAt = NetworkTime.time + autoCloseDelay;

            if (isOpened == opened)
                return;

            isOpened = opened;

            if (!opened)
                onClosed?.Invoke();
        }

        [ServerCallback]
        private void Update()
        {
            if (!isOpened || autoCloseDelay <= 0f || NetworkTime.time < closeAt)
                return;

            if (IsDoorwayOccupied())
            {
                closeAt = NetworkTime.time + blockedRetryDelay;
                return;
            }

            CloseDoor();
        }

        private void OnOpenedStateChange(bool oldValue, bool newValue)
        {
            ApplyOpenedState(newValue);
        }

        public override void OnStartClient()
        {
            ApplyOpenedState(isOpened);
        }

        private void ApplyOpenedState(bool opened)
        {
            if (door != null)
                door.SetActive(!opened);
        }

        // ── Doorway check ─────────────────────────────────────────────────

        // Geometry instead of a physics overlap: on the server other players' copies have
        // their CharacterController disabled, so no query would see them (as in FlameHazard).
        [Server]
        private bool IsDoorwayOccupied()
        {
            if (!TryGetDoorway(out Transform frame, out Bounds local))
                return false;

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

                if (SphereTouchesBox(center, radius, frame, local)
                    || SphereTouchesBox(center + Vector3.up * reach, radius, frame, local)
                    || SphereTouchesBox(center - Vector3.up * reach, radius, frame, local))
                    return true;
            }

            return false;
        }

        private static bool SphereTouchesBox(Vector3 point, float radius, Transform frame, Bounds local)
        {
            Vector3 closestLocal = local.ClosestPoint(frame.InverseTransformPoint(point));
            Vector3 closest = frame.TransformPoint(closestLocal);
            return (closest - point).sqrMagnitude <= radius * radius;
        }

        // Resolved lazily: ProBuilder assigns the collider mesh at runtime, after Awake.
        private bool TryGetDoorway(out Transform frame, out Bounds local)
        {
            if (!doorwayResolved)
            {
                doorwayResolved = true;
                ResolveDoorway();
            }

            frame = doorwayFrame;
            local = doorwayLocalBounds;
            return frame != null;
        }

        private void ResolveDoorway()
        {
            if (doorwayArea != null)
            {
                doorwayFrame = doorwayArea.transform;
                doorwayLocalBounds = new Bounds(doorwayArea.center, doorwayArea.size);
                return;
            }

            if (door == null)
                return;

            // Door colliders are disabled while the door is open — read their shapes, not their bounds.
            Transform frame = door.transform;
            bool any = false;
            Bounds result = default;

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
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? min.x : max.x,
                        (i & 2) == 0 ? min.y : max.y,
                        (i & 4) == 0 ? min.z : max.z);
                    Vector3 inFrame = frame.InverseTransformPoint(col.transform.TransformPoint(corner));

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

            if (any)
            {
                doorwayFrame = frame;
                doorwayLocalBounds = result;
            }
            else
            {
                Debug.LogWarning($"[DoorOpener] {name}: no Box/Mesh collider on the door and no doorwayArea — auto close won't check the doorway.", this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (doorwayArea == null)
                return;

            Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
            Gizmos.matrix = doorwayArea.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(doorwayArea.center, doorwayArea.size);
        }
    }
}
