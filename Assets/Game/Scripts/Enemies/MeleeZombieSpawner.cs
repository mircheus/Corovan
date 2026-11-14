using Mirror;
using UnityEngine;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Spawns <see cref="MeleeZombie"/>s. The loop, activation modes, alive cap and director requests
    /// live in <see cref="EnemySpawner"/>.
    /// </summary>
    public class MeleeZombieSpawner : EnemySpawner
    {
        [Header("Zombie")]
        [SerializeField] private MeleeZombie zombiePrefab;

        protected override NetworkBehaviour Prefab => zombiePrefab;
        protected override bool IsEnemyDead(NetworkBehaviour enemy) => ((MeleeZombie)enemy).IsDead;
        protected override Color GizmoColor => new Color(0.4f, 1f, 0.3f, 1f);

        protected override void AggroIfIdle(NetworkBehaviour enemy)
        {
            var zombie = (MeleeZombie)enemy;
            if (zombie.Target != null)
                return;
            var target = FindAggroPlayer(zombie.transform.position);
            if (target != null)
                zombie.ServerSetTarget(target);
        }

        private void Reset()
        {
            spawnInterval = 2f;
            maxAlive = 6;
            spawnClearance = 1f;
        }
    }
}
