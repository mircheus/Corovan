using System.Collections.Generic;
using Mirror;
using UnityEngine;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    /// <summary>
    /// Server-side aggro zone. When a player-driven car enters any of the
    /// child trigger colliders, every enemy listed in <see cref="enemies"/>
    /// is notified via <see cref="EnemyCarController.RegisterTarget"/>.
    ///
    /// Place one or more trigger Colliders (any shape) on this GameObject or
    /// its children. The zone object itself doesn't move, so no Rigidbody is
    /// needed here — the player car already carries a Rigidbody, which is
    /// enough for trigger events to fire.
    /// </summary>
    public class EnemyAggroZone : MonoBehaviour
    {
        [Tooltip("Enemies that will be notified when a player car enters this zone.")]
        [SerializeField] private List<EnemyCarController> enemies = new();
        [SerializeField] private bool enableTriggerExit = false;

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active)
                return;

            var car = other.GetComponentInParent<CarController>();
            if (car == null)
                return;

            foreach (var enemy in enemies)
            {
                if (enemy != null)
                    enemy.RegisterTarget(car);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!enableTriggerExit)
                return;
            
            if (!NetworkServer.active)
                return;

            var car = other.GetComponentInParent<CarController>();
            if (car == null)
                return;

            foreach (var enemy in enemies)
            {
                if (enemy != null)
                    enemy.UnregisterTarget(car);
            }
        }
    }
}
