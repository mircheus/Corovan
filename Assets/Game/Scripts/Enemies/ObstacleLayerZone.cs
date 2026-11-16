using System.Collections.Generic;
using UnityEngine;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    /// <summary>
    /// Scene setup for <see cref="ObstacleLayerZoneCarBehaviour"/>: the attack point the car drops obstacles from,
    /// the safe point it falls back to, and the zone a player car has to be in for the attack.
    /// A plain MonoBehaviour (no NetworkIdentity) — only the server reads it. A spawned car (prefab, no scene
    /// references) picks the zone nearest to where it appeared; a car placed in the scene can be given one directly.
    /// </summary>
    public class ObstacleLayerZone : MonoBehaviour
    {
        [Tooltip("Where the car stands while it drops obstacles.")]
        [SerializeField] private Transform attackPoint;
        [Tooltip("Where the car waits while no player car is in the zone.")]
        [SerializeField] private Transform safePoint;
        [Tooltip("A player car inside this collider is attacked. Tested geometrically (Box/Sphere/Capsule/convex " +
                 "Mesh), so make it a trigger on a layer that touches nothing. Empty = the collider on this object.")]
        [SerializeField] private Collider zone;

        private static readonly List<ObstacleLayerZone> all = new();

        public Transform AttackPoint => attackPoint;
        public Transform SafePoint => safePoint;
        public bool IsValid => attackPoint != null && safePoint != null && zone != null;

        private void Awake()
        {
            if (zone == null)
                zone = GetComponent<Collider>();
        }

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        /// <summary>A driven, alive player car stands inside the zone.</summary>
        public bool HasPlayerCarInside()
        {
            if (zone == null || !zone.enabled)
                return false;
            foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (!car.VehicleIsAlive || !car.HasDriver)
                    continue;
                Vector3 p = car.transform.position;
                if ((zone.ClosestPoint(p) - p).sqrMagnitude <= 0.01f)
                    return true;
            }
            return false;
        }

        /// <summary>The valid zone whose attack point is nearest to <paramref name="position"/>, or null.</summary>
        public static ObstacleLayerZone FindNearest(Vector3 position)
        {
            ObstacleLayerZone best = null;
            float bestSqr = float.PositiveInfinity;
            foreach (ObstacleLayerZone z in all)
            {
                if (!z.IsValid)
                    continue;
                float sqr = (z.attackPoint.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = z;
                }
            }
            return best;
        }

        private void OnDrawGizmos()
        {
            if (attackPoint != null)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.2f, 1f);
                Gizmos.DrawSphere(attackPoint.position, 0.6f);
                Gizmos.DrawLine(attackPoint.position, attackPoint.position + attackPoint.forward * 3f);
            }
            if (safePoint != null)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.3f, 1f);
                Gizmos.DrawSphere(safePoint.position, 0.6f);
                Gizmos.DrawLine(safePoint.position, safePoint.position + safePoint.forward * 3f);
            }
            if (attackPoint != null && safePoint != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
                Gizmos.DrawLine(attackPoint.position, safePoint.position);
            }
        }
    }
}
