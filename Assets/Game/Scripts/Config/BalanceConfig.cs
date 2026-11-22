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
        public PlayerCarBalance playerCar = new PlayerCarBalance();
        public ShooterDroidBalance shooterDroid = new ShooterDroidBalance();
        public EnemyCarBalance enemyCar = new EnemyCarBalance();
        public ZombieBalance zombie = new ZombieBalance();
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

    // Every CarController (player cars) shares one section.
    [Serializable]
    public class PlayerCarBalance
    {
        [Min(1)] public int maxHealth = 200;
        [Tooltip("CarDashRam: damage of a dash ram at full dash speed (server).")]
        [Min(0)] public int dashRamDamage = 100;
    }

    [Serializable]
    public class ShooterDroidBalance
    {
        [Min(1)] public int maxHealth = 100;
        [Min(0)] public int projectileDamage = 8;
        [Min(0.1f)] public float projectileSpeed = 25f;
    }

    [Serializable]
    public class ZombieBalance
    {
        [Min(1)] public int maxHealth = 80;
        [Tooltip("Damage of one scratch.")]
        [Min(0)] public int attackDamage = 15;
    }

    [Serializable]
    public class EnemyCarBalance
    {
        [Min(1)] public int maxHealth = 300;
        [Min(0)] public int projectileDamage = 10;
        [Min(0.1f)] public float projectileSpeed = 20f;
    }
}
