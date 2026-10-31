using System;
using UnityEngine;

namespace FPSGame.Config
{
    /// <summary>
    /// Per-scene balance values, applied over prefab defaults by <see cref="SceneBalance"/>.
    /// Grow it a field at a time as systems start reading from it.
    /// </summary>
    [CreateAssetMenu(fileName = "Balance", menuName = "FPS Game/Balance Config")]
    public class BalanceConfig : ScriptableObject
    {
        public PlayerBalance player = new PlayerBalance();
        public ShooterDroidBalance shooterDroid = new ShooterDroidBalance();
        public EnemyCarBalance enemyCar = new EnemyCarBalance();
    }

    // All players share one section for now.
    [Serializable]
    public class PlayerBalance
    {
        [Min(1)] public int maxHealth = 100;

        [Header("Weapon damage")]
        [Tooltip("Damage of one trigger pull, as in the FPSF preset (falloff and hit-group multipliers still apply).")]
        [Min(0)] public int pistolDamage = 31;
        [Tooltip("Whole shot, split evenly between the pellets.")]
        [Min(0)] public int shotgunDamage = 200;
        [Min(0)] public int rifleDamage = 35;
    }

    [Serializable]
    public class ShooterDroidBalance
    {
        [Min(1)] public int maxHealth = 100;
        [Min(0)] public int projectileDamage = 8;
        [Min(0.1f)] public float projectileSpeed = 25f;
    }

    [Serializable]
    public class EnemyCarBalance
    {
        [Min(1)] public int maxHealth = 300;
        [Min(0)] public int projectileDamage = 10;
        [Min(0.1f)] public float projectileSpeed = 20f;
    }
}
