using System.Collections;
using UnityEngine;
using Mirror;
using FPSGame.Vehicle;
using FPSGame.Player;

namespace FPSGame.Hazards
{
    // Attach to a GameObject with a Trigger collider placed along the road.
    // When the wagon (or optionally a player) crosses it, this trigger drives a set of BarrelSpawners:
    // on each tick it picks a random spawner and asks it to Spawn(), waiting a random interval between ticks.
    // Detection and spawning are server-authoritative (mirrors the CargoTrigger pattern) — no NetworkIdentity needed here.
    // Referenced spawners should have Manual Control enabled so they don't also run their own spawn loop.
    [RequireComponent(typeof(Collider))]
    public class BarrelSpawnTrigger : MonoBehaviour
    {
        [Header("Spawners")]
        [Tooltip("Barrel spawners to drive. If left empty, any BarrelSpawner found on this object or its children is used. Each should have Manual Control enabled.")]
        [SerializeField] private BarrelSpawner[] spawners;

        [Header("Spawn Timing")]
        [Tooltip("Minimum seconds between spawns.")]
        [SerializeField] private float spawnIntervalMin = 0.75f;
        [Tooltip("Maximum seconds between spawns. Each tick waits a random time in [min, max].")]
        [SerializeField] private float spawnIntervalMax = 2f;
        [Tooltip("Optional delay before the first barrel after the trigger fires. 0 = spawn on first tick immediately.")]
        [SerializeField] private float initialDelay = 0f;
        [Tooltip("Chance (0–1) that a tick fires two different spawners at once instead of one. Requires at least two spawners.")]
        [Range(0f, 1f)]
        [SerializeField] private float doubleSpawnChance = 0.2f;

        [Header("Detection")]
        [Tooltip("Only the wagon (CarController) triggers it by default. Enable to also trigger on players on foot.")]
        [SerializeField] private bool triggerOnPlayers = false;
        [Tooltip("Fire only once, then stop reacting. Disable to re-arm on every crossing.")]
        [SerializeField] private bool oneShot = true;

        private bool triggered;
        private Coroutine spawnRoutine;
        private int[] order;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            if (spawners == null || spawners.Length == 0)
                spawners = GetComponentsInChildren<BarrelSpawner>();

            // Reusable index list, shuffled each tick to pick spawners in a random order.
            order = new int[spawners.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active) return;
            if (oneShot && triggered) return;

            bool isWagon = other.GetComponentInParent<CarController>() != null;
            bool isPlayer = triggerOnPlayers && other.GetComponentInParent<PlayerHealth>() != null;
            if (!isWagon && !isPlayer) return;

            triggered = true;
            if (spawnRoutine == null)
                spawnRoutine = StartCoroutine(SpawnLoop());
        }

        private IEnumerator SpawnLoop()
        {
            if (initialDelay > 0f)
                yield return new WaitForSeconds(initialDelay);

            while (true)
            {
                SpawnTick();
                yield return new WaitForSeconds(Random.Range(spawnIntervalMin, spawnIntervalMax));
            }
        }

        // Fires one — or, by chance, two — different spawners this tick. Shuffles the spawner order so a
        // double spawn is a random pair (not necessarily adjacent), then fires the first N that actually
        // have a barrel available, so empty pools are skipped and a tick isn't wasted.
        private void SpawnTick()
        {
            if (spawners == null || spawners.Length == 0) return;

            int desired = spawners.Length >= 2 && Random.value < doubleSpawnChance ? 2 : 1;

            ShuffleOrder();
            int spawned = 0;
            for (int i = 0; i < order.Length && spawned < desired; i++)
            {
                BarrelSpawner spawner = spawners[order[i]];
                if (spawner != null && spawner.Spawn())
                    spawned++;
            }
        }

        // Fisher–Yates shuffle of the reusable index array.
        private void ShuffleOrder()
        {
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }
    }
}
