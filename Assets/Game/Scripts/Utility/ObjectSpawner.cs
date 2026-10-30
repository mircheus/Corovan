using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.Utility
{
    /// <summary>
    /// Universal spawner: creates <see cref="prefab"/> at this object's position every
    /// <see cref="spawnInterval"/> seconds while enabled; <see cref="toggleKey"/> switches spawning on/off.
    ///
    /// Works with any prefab:
    ///   • With a NetworkIdentity — spawned on the server via NetworkServer.Spawn, so every peer sees it.
    ///     Only the server/host runs the loop and reacts to the hotkey; on a pure client this spawner idles.
    ///     The prefab must be in CustomNetworkManager.runtimeSpawnPrefabs.
    ///   • Without one — plain local Instantiate on every peer that has this spawner (each peer toggles its own).
    ///
    /// Needs no NetworkIdentity of its own, so it can be dropped into any scene, online or offline.
    /// </summary>
    public class ObjectSpawner : MonoBehaviour
    {
        [Header("Object")]
        [SerializeField] private GameObject prefab;
        [Tooltip("Spawn with this spawner's rotation; otherwise with the prefab's own rotation.")]
        [SerializeField] private bool useSpawnerRotation = true;
        [Tooltip("Parent spawned objects to this spawner (local, non-networked prefabs only).")]
        [SerializeField] private bool parentToSpawner = false;

        [Header("Timing")]
        [Tooltip("Seconds between spawns.")]
        [SerializeField, Min(0.01f)] private float spawnInterval = 1f;
        [Tooltip("Spawn immediately when spawning is switched on, instead of after the first interval.")]
        [SerializeField] private bool spawnImmediately = true;

        [Header("Control")]
        [Tooltip("Spawning is on from the start.")]
        [SerializeField] private bool spawnOnStart = false;
        [Tooltip("Key that toggles spawning on/off. None = no hotkey (use SetSpawning / ToggleSpawning).")]
        [SerializeField] private Key toggleKey = Key.F8;

        [Header("Limits")]
        [Tooltip("Max objects from this spawner alive at once; a destroyed one frees its slot. 0 = unlimited.")]
        [SerializeField, Min(0)] private int maxAlive = 0;
        [Tooltip("Destroy each spawned object after this many seconds. 0 = keep forever.")]
        [SerializeField, Min(0f)] private float lifetime = 0f;

        private readonly List<GameObject> alive = new List<GameObject>();
        private bool isSpawning;
        private float nextSpawnTime;
        private bool isNetworked;
        private bool pendingStart;

        public bool IsSpawning => isSpawning;

        private void Awake()
        {
            isNetworked = prefab != null && prefab.GetComponent<NetworkIdentity>() != null;
        }

        private void Start()
        {
            if (prefab == null)
                Debug.LogWarning($"[ObjectSpawner] {name}: prefab is not assigned.", this);
            pendingStart = spawnOnStart;
        }

        private void Update()
        {
            if (!CanSpawnHere())
            {
                // Server stopped: its spawned objects are gone, start over when it comes back.
                if (isSpawning)
                {
                    isSpawning = false;
                    alive.Clear();
                    pendingStart = spawnOnStart;
                }
                return;
            }

            // Deferred until the server is up: a scene played directly runs Start before the host starts.
            if (pendingStart)
            {
                pendingStart = false;
                SetSpawning(true);
            }

            if (toggleKey != Key.None && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                ToggleSpawning();

            if (isSpawning && Time.time >= nextSpawnTime)
            {
                nextSpawnTime = Time.time + spawnInterval;
                TrySpawn();
            }
        }

        public void ToggleSpawning() => SetSpawning(!isSpawning);

        /// <summary>Switch spawning on/off. Safe to call from a UnityEvent on any peer.</summary>
        public void SetSpawning(bool on)
        {
            if (on == isSpawning || !CanSpawnHere())
                return;

            isSpawning = on;
            nextSpawnTime = Time.time + (spawnImmediately ? 0f : spawnInterval);
            Debug.Log($"[ObjectSpawner] {name}: spawning {(on ? "ON" : "OFF")}.", this);
        }

        /// <summary>Spawn one object right now (ignores the timer, respects maxAlive).</summary>
        public void SpawnOnce()
        {
            if (CanSpawnHere())
                TrySpawn();
        }

        // Networked prefabs are server objects; a client copy of the scene must not create them.
        private bool CanSpawnHere() => !isNetworked || NetworkServer.active;

        private void TrySpawn()
        {
            if (prefab == null)
                return;

            if (maxAlive > 0)
            {
                alive.RemoveAll(o => o == null);
                if (alive.Count >= maxAlive)
                    return;
            }

            Quaternion rotation = useSpawnerRotation ? transform.rotation : prefab.transform.rotation;
            Transform parent = parentToSpawner && !isNetworked ? transform : null;
            GameObject spawned = Instantiate(prefab, transform.position, rotation, parent);

            if (isNetworked)
                NetworkServer.Spawn(spawned);

            if (maxAlive > 0)
                alive.Add(spawned);

            if (lifetime > 0f)
            {
                if (isNetworked)
                    StartCoroutine(DestroyNetworkedAfter(spawned, lifetime));
                else
                    Destroy(spawned, lifetime);
            }
        }

        private IEnumerator DestroyNetworkedAfter(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (obj != null && NetworkServer.active)
                NetworkServer.Destroy(obj);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = isSpawning ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
        }
    }
}
