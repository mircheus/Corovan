using Mirror;
using UnityEngine;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Spawns <see cref="ShooterDroid"/>s. The loop, activation modes, alive cap and director requests
    /// live in <see cref="EnemySpawner"/>. Corpses are removed by RagdollEnemy itself
    /// (respawnDelay 0 → destroyDelay).
    /// </summary>
    public class ShooterDroidSpawner : EnemySpawner
    {
        [Header("Droid")]
        [SerializeField] private ShooterDroid droidPrefab;

        protected override NetworkBehaviour Prefab => droidPrefab;
        protected override bool IsEnemyDead(NetworkBehaviour enemy) => ((ShooterDroid)enemy).IsDead;

        protected override void AggroIfIdle(NetworkBehaviour enemy)
        {
            var droid = (ShooterDroid)enemy;
            if (droid.Target != null)
                return;
            var target = FindAggroPlayer(droid.transform.position);
            if (target != null)
                droid.ServerSetTarget(target);
        }
    }
}
