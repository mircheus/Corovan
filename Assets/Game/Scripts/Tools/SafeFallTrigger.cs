using UnityEngine;

namespace FPSGame.Tools
{
    [RequireComponent(typeof(Rigidbody))]
    public class SafeFallTrigger : MonoBehaviour
    {
        [SerializeField] FloorMeshModifier floor;
        [SerializeField] Collider safeBaseCollider;
        [SerializeField] bool autoFindFloor = true;
        [SerializeField] string floorTag = "Floor";

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (safeBaseCollider == null) safeBaseCollider = GetComponent<Collider>();
            if (floor == null && autoFindFloor)
            {
                var go = GameObject.FindGameObjectWithTag(floorTag);
                if (go != null) floor = go.GetComponent<FloorMeshModifier>();
            }
        }

        void OnEnable()
        {
            if (floor != null) floor.OnHoleCreated += HandleHoleCreated;
        }

        void OnDisable()
        {
            if (floor != null) floor.OnHoleCreated -= HandleHoleCreated;
        }

        void HandleHoleCreated(Vector3[] holeLocalCorners)
        {
            if (floor == null || holeLocalCorners == null || holeLocalCorners.Length != 4) return;

            // Safe base center in floor local space.
            Vector3 baseWorld;
            if (safeBaseCollider != null)
            {
                Bounds b = safeBaseCollider.bounds;
                baseWorld = new Vector3(b.center.x, b.min.y, b.center.z);
            }
            else
            {
                baseWorld = transform.position;
            }
            Vector3 baseLocal = floor.transform.InverseTransformPoint(baseWorld);

            if (PointInQuadXZ(baseLocal, holeLocalCorners))
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
            }
        }

        // Cross-product point-in-polygon test on the XZ plane. Assumes the quad is
        // ordered consistently (CCW per FloorMeshModifier guarantee). For a convex
        // polygon all cross products share the same sign when the point is inside.
        // For a concave quad the test still rejects clear outsiders but may classify
        // points inside the concave notch as inside; FloorMeshModifier already warns
        // on concave shapes.
        static bool PointInQuadXZ(Vector3 p, Vector3[] quad)
        {
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = quad[i];
                Vector3 b = quad[(i + 1) & 3];
                float cross = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
                if (cross > 1e-5f)
                {
                    if (sign < 0) return false;
                    sign = 1;
                }
                else if (cross < -1e-5f)
                {
                    if (sign > 0) return false;
                    sign = -1;
                }
            }
            return true;
        }
    }
}
