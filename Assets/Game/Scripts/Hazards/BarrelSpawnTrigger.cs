using UnityEngine;
using Mirror;
using FPSGame.Vehicle;
using FPSGame.Player;

namespace FPSGame.Hazards
{
    // Attach to a GameObject with a Trigger collider placed along the road.
    // When the wagon (or optionally a player) crosses it, the referenced BarrelSpawners begin dropping barrels.
    // Detection is server-authoritative (mirrors the CargoTrigger pattern) — no NetworkIdentity needed here.
    [RequireComponent(typeof(Collider))]
    public class BarrelSpawnTrigger : MonoBehaviour
    {
        [Header("Spawners")]
        [Tooltip("Barrel spawners to activate. If left empty, any BarrelSpawner found on this object or its children is used.")]
        [SerializeField] private BarrelSpawner[] spawners;

        [Header("Detection")]
        [Tooltip("Only the wagon (CarController) triggers it by default. Enable to also trigger on players on foot.")]
        [SerializeField] private bool triggerOnPlayers = false;
        [Tooltip("Fire only once, then stop reacting. Disable to re-arm the spawners on every crossing.")]
        [SerializeField] private bool oneShot = true;

        private bool triggered;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            if (spawners == null || spawners.Length == 0)
                spawners = GetComponentsInChildren<BarrelSpawner>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active) return;
            if (oneShot && triggered) return;

            bool isWagon = other.GetComponentInParent<CarController>() != null;
            bool isPlayer = triggerOnPlayers && other.GetComponentInParent<PlayerHealth>() != null;
            if (!isWagon && !isPlayer) return;

            triggered = true;
            foreach (var spawner in spawners)
            {
                if (spawner != null)
                    spawner.BeginSpawning();
            }
        }
    }
}
