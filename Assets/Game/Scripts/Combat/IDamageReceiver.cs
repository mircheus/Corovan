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
}
