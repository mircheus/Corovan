using UnityEngine;

namespace FPSGame.Environment
{
    // Marks a trigger volume as a deadly gap around the convoy — the space a swerve can throw a
    // player into. Falling in costs health, drops carried loot, and snaps the player back to their
    // last safe spot on the platform (see PlayerFallHazard).
    //
    // This is only an identifiable marker plus per-volume tuning: the actual detection and response
    // run on the falling player, because only the owning client runs the CharacterController that
    // generates trigger events. Put it on a trigger collider sitting under / beside the platform
    // edges, on a layer that collides with the Player layer.
    public class FallHazardVolume : MonoBehaviour
    {
        [Tooltip("Damage dealt when a player falls into this volume.")]
        [SerializeField] private int fallDamage = 25;

        [Tooltip("If true, ignores fallDamage and kills the player outright (bottomless pit).")]
        [SerializeField] private bool instantKill = false;

        public int FallDamage => fallDamage;
        public bool InstantKill => instantKill;

        private void Reset()
        {
            // Nudge the collider toward being a trigger so the marker works out of the box.
            Collider col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }
    }
}
