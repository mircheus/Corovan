using UnityEngine;

namespace FPSGame.Environment
{
    // Marks a trigger volume as a deadly gap around the convoy — the space a swerve can throw a
    // player into. Falling in is a crash onto the road: it costs health, drops carried loot, blacks
    // out the screen with crashMessage, and after the respawn delay brings the player back in a
    // teammate's car or at a PlayerRespawnPoint (see PlayerFallHazard, PlayerHealth.ServerCrash).
    //
    // This is only an identifiable marker plus per-volume tuning: the actual detection and response
    // run on the falling player, because only the owning client runs the CharacterController that
    // generates trigger events. Put it on a trigger collider sitting under / beside the platform
    // edges, on a layer that collides with the Player layer.
    public class FallHazardVolume : MonoBehaviour
    {
        [Tooltip("Damage dealt when a player falls into this volume. A fall that would empty their health is a death.")]
        [SerializeField] private int fallDamage = 25;

        [Tooltip("If true, ignores fallDamage and kills the player outright (bottomless pit).")]
        [SerializeField] private bool instantKill = false;

        [Tooltip("Shown on the blacked-out screen of the player who fell in.")]
        [SerializeField] private string crashMessage = "Вы разбились на дороге";

        public int FallDamage => fallDamage;
        public bool InstantKill => instantKill;
        public string CrashMessage => crashMessage;

        private void Reset()
        {
            // Nudge the collider toward being a trigger so the marker works out of the box.
            Collider col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }
    }
}
