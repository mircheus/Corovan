using UnityEngine;
using Mirror;

namespace FPSGame.Combat
{
    /// <summary>
    /// The one server-side door for weapon damage, whoever fired it (an FPSF weapon or the axe
    /// reported by a client, a server-side explosion).
    /// </summary>
    public static class DamageRouter
    {
        public static IDamageReceiver FindReceiver(Component hit) =>
            hit != null ? hit.GetComponentInParent<IDamageReceiver>() : null;

        [Server]
        public static bool ServerApplyHit(IDamageReceiver receiver, int damage, Vector3 force)
        {
            if (receiver == null || receiver.IsDead || damage <= 0)
                return false;

            if (receiver is IHitForceReceiver forceReceiver)
                forceReceiver.ServerSetHitForce(force);

            receiver.TakeDamage(damage);
            return true;
        }
    }
}
