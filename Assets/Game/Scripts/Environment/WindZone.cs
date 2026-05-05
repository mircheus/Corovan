using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    public enum WindAxis
    {
        PositiveX,
        NegativeX,
        PositiveZ,
        NegativeZ,
        PositiveY,
        NegativeY,
        Custom
    }

    [RequireComponent(typeof(Collider))]
    public class WindZone : MonoBehaviour
    {
        [Header("Wind Settings")]
        [SerializeField] private bool isActive = true;
        [SerializeField] private WindAxis direction = WindAxis.PositiveX;
        [SerializeField] private Vector3 customDirection = Vector3.right;
        [SerializeField] private float strength = 8f;

        [Header("Space")]
        [Tooltip("If true, direction is interpreted in this transform's local space (rotation affects wind direction).")]
        [SerializeField] private bool useLocalSpace = false;

        private void Reset()
        {
            Collider[] cols = GetComponents<Collider>();
            foreach (Collider c in cols)
                c.isTrigger = true;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!isActive)
                return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !player.isOwned)
                return;

            player.AddExternalVelocity(GetWindVelocity());
        }

        private Vector3 GetWindVelocity()
        {
            Vector3 dir;
            switch (direction)
            {
                case WindAxis.PositiveX: dir = Vector3.right; break;
                case WindAxis.NegativeX: dir = Vector3.left; break;
                case WindAxis.PositiveZ: dir = Vector3.forward; break;
                case WindAxis.NegativeZ: dir = Vector3.back; break;
                case WindAxis.PositiveY: dir = Vector3.up; break;
                case WindAxis.NegativeY: dir = Vector3.down; break;
                case WindAxis.Custom: dir = customDirection.sqrMagnitude > 0.0001f ? customDirection.normalized : Vector3.zero; break;
                default: dir = Vector3.zero; break;
            }

            if (useLocalSpace)
                dir = transform.TransformDirection(dir);

            return dir * strength;
        }

        public void SetActive(bool active) => isActive = active;
        public void SetStrength(float value) => strength = value;
        public void SetDirection(WindAxis axis) => direction = axis;

        private void OnDrawGizmos()
        {
            Gizmos.color = isActive ? new Color(0.4f, 0.8f, 1f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Vector3 origin = transform.position;
            Vector3 windDir = GetWindVelocity();
            if (windDir.sqrMagnitude < 0.0001f)
                return;
            Vector3 end = origin + windDir.normalized * Mathf.Max(1f, strength * 0.25f);
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawSphere(end, 0.15f);
        }
    }
}
