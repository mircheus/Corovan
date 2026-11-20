using System.Collections.Generic;
using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Scene marker: a safe spot a player respawns at when no teammate's car has a free passenger
    /// seat (see <see cref="PlayerHealth.ServerRespawn"/>). The nearest enabled marker to where the
    /// player fell wins, so they can be spread along the level and switched on/off as it progresses.
    /// No marker in the scene = the NetworkManager's spawn points.
    /// <para>The player is placed at the pivot (feet) facing its forward (yaw only).</para>
    /// </summary>
    public class PlayerRespawnPoint : MonoBehaviour
    {
        private static readonly List<PlayerRespawnPoint> all = new List<PlayerRespawnPoint>();
        public static IReadOnlyList<PlayerRespawnPoint> All => all;

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        /// <summary>The enabled marker closest to a world point, or null when there is none.</summary>
        public static PlayerRespawnPoint FindNearest(Vector3 from)
        {
            PlayerRespawnPoint best = null;
            float bestSqr = float.MaxValue;
            foreach (PlayerRespawnPoint point in all)
            {
                float sqr = (point.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = point;
                }
            }
            return best;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 1.2f);
        }
    }
}
