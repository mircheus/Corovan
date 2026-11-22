using UnityEngine;

namespace FPSGame.Combat
{
    /// <summary>
    /// Anything that takes int damage on the server: players, enemies, the car, barrels, locks.
    /// Weapons look it up with GetComponentInParent from the collider they hit, so one receiver on
    /// a NetworkBehaviour covers all of its child colliders.
    /// </summary>
    public interface IDamageReceiver
    {
        int CurrentHealth { get; }
        int MaxHealth { get; }
        bool IsDead { get; }

        /// <summary>Server only.</summary>
        void TakeDamage(int damage);
    }

    /// <summary>
    /// Optional: receivers that react physically to the killing blow (a ragdoll's push). The force
    /// is handed over just before TakeDamage.
    /// </summary>
    public interface IHitForceReceiver
    {
        void ServerSetHitForce(Vector3 force);
    }

    /// <summary>
    /// Optional: components that want to know who hit them (an enemy that turns on its shooter).
    /// Looked up on the receiver's GameObject after a player's hit has been applied. Server only.
    /// </summary>
    public interface IAttackerListener
    {
        void ServerOnHitBy(GameObject attacker);
    }

    /// <summary>
    /// Optional: receivers that can be shoved (a dash ram). Looked up on the receiver's GameObject and called
    /// before the hit's damage, so a killing blow can carry the shove into the death. Server only.
    /// </summary>
    public interface IKnockbackReceiver
    {
        /// <summary>Horizontal knock speed (units/sec) along the push.</summary>
        void ServerKnockback(Vector3 velocity);
    }
}
