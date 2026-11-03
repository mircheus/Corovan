using Mirror;
using UnityEngine;
using FPSGame.Config;

namespace FPSGame.Environment
{
    /// <summary>
    /// What an <see cref="EnemyCarController"/> does with its target: chase and shoot, stay ahead and drop
    /// obstacles, ... One behaviour component per enemy car prefab, next to the controller.
    /// The controller owns health, death, wreck and target selection; the behaviour owns movement and attacks.
    /// All Server* hooks are called by the controller on the server only.
    /// </summary>
    public abstract class EnemyCarBehaviour : NetworkBehaviour
    {
        protected EnemyCarController Car { get; private set; }

        protected virtual void Awake()
        {
            Car = GetComponent<EnemyCarController>();
        }

        /// <summary>Every server frame while the car is alive and has a valid target.</summary>
        public abstract void ServerTick();

        /// <summary>Every server frame while the car is alive and has no target.</summary>
        public virtual void ServerIdle() { }

        /// <summary>The target was picked, replaced or lost-and-replaced (not called when it is just lost).</summary>
        public virtual void ServerOnTargetChanged() { }

        /// <summary>The car was destroyed. The controller has already stopped ticking the behaviour.</summary>
        public virtual void ServerOnDied() { }

        /// <summary>Scene balance, applied once on server start after the controller's own values.</summary>
        public virtual void ServerApplyBalance(EnemyCarBalance balance) { }

        /// <summary>Velocity the wreck carries on with when the car dies.</summary>
        public virtual Vector3 ServerVelocity => Car.Agent.enabled ? Car.Agent.velocity : Vector3.zero;
    }
}
