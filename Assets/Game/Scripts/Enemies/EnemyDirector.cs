using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Top-level wave director. Runs two independent tracks of waves in parallel — one for enemies on the
    /// platform (droids, zombies), one for enemy cars — by ordering enemies from existing
    /// <see cref="EnemySpawner"/>s in the scene (<see cref="EnemySpawner.ServerRequest"/>). It never spawns
    /// anything itself: where, how fast and how many at once stay the spawner's settings (spawn points,
    /// spawnInterval, maxAlive). Spawners used here should normally be set to Manual.
    ///
    /// A wave is either Combat (a list of {spawner, count}) or Pause (a breather of N seconds).
    /// A combat wave ends by its <see cref="WaveEnd"/>: all its enemies killed, a timer, or whichever comes first.
    /// When a timer ends a wave early, enemies it hasn't spawned yet keep coming alongside the next wave.
    ///
    /// Server only; Start/Stop are safe to call from UnityEvents on any peer. The current wave of each track
    /// is synced (index + state) for a HUD.
    /// </summary>
    public class EnemyDirector : NetworkBehaviour
    {
        public enum WaveKind { Combat, Pause }
        public enum WaveEnd { AllKilled, Timer, AllKilledOrTimer }
        public enum TrackEnd { Stop, LoopAll, RepeatLast }
        public enum TrackState : byte { Idle, Combat, Pause, Finished }

        [Serializable]
        public class WaveEntry
        {
            [Tooltip("Spawner that defines the enemy type and where it appears.")]
            public EnemySpawner spawner;
            [Min(1)] public int count = 4;
        }

        [Serializable]
        public class Wave
        {
            [Tooltip("Label for logs and the inspector.")]
            public string name;
            public WaveKind kind = WaveKind.Combat;

            [Tooltip("Combat: enemies of the wave. Several entries spawn in parallel (each spawner its own pace).")]
            public List<WaveEntry> enemies = new List<WaveEntry>();
            [Tooltip("Combat: when the next wave starts.")]
            public WaveEnd endCondition = WaveEnd.AllKilled;
            [Tooltip("Combat with Timer / AllKilledOrTimer: seconds from the wave start. " +
                     "Pause: length of the breather.")]
            [Min(0f)] public float duration = 30f;
        }

        [Serializable]
        public class WaveTrack
        {
            public Wave[] waves = Array.Empty<Wave>();
            [Tooltip("After the last wave: stop, start over from the first, or keep repeating the last one.")]
            public TrackEnd onEnd = TrackEnd.Stop;
            [Tooltip("Fired on the server when the track finishes (onEnd = Stop).")]
            public UnityEvent onFinished = new UnityEvent();
        }

        [Header("Activation")]
        [Tooltip("Start both tracks when the server starts. Off = wait for ServerStart / ServerStartPlatform / ServerStartVehicle.")]
        [SerializeField] private bool startOnServerStart = true;
        [Tooltip("Seconds before the first wave of each track once it starts.")]
        [SerializeField, Min(0f)] private float startDelay = 3f;

        [Header("Tracks")]
        [Tooltip("Waves of enemies on the platform (droids, zombies).")]
        [SerializeField] private WaveTrack platformWaves = new WaveTrack();
        [Tooltip("Waves of enemy cars.")]
        [SerializeField] private WaveTrack vehicleWaves = new WaveTrack();

        [Header("Debug")]
        [SerializeField] private bool logWaves = true;

        // Synced for a HUD: wave index (-1 = none yet) and what the track is doing.
        [SyncVar] private int platformWaveIndex = -1;
        [SyncVar] private TrackState platformState = TrackState.Idle;
        [SyncVar] private int vehicleWaveIndex = -1;
        [SyncVar] private TrackState vehicleState = TrackState.Idle;

        private Coroutine platformRoutine;
        private Coroutine vehicleRoutine;
        private readonly List<EnemySpawner.SpawnRequest> platformRequests = new List<EnemySpawner.SpawnRequest>();
        private readonly List<EnemySpawner.SpawnRequest> vehicleRequests = new List<EnemySpawner.SpawnRequest>();

        public int PlatformWaveIndex => platformWaveIndex;
        public TrackState PlatformState => platformState;
        public int VehicleWaveIndex => vehicleWaveIndex;
        public TrackState VehicleState => vehicleState;

        /// <summary>Server: (isVehicleTrack, waveIndex, wave) when a wave begins.</summary>
        public event Action<bool, int, Wave> ServerWaveStarted;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (startOnServerStart)
                ServerStart();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerStop();
        }

        /// <summary>Start both tracks (each only if not running). Safe on any peer.</summary>
        public void ServerStart()
        {
            ServerStartPlatform();
            ServerStartVehicle();
        }

        /// <summary>Stop both tracks; unspawned remainders are cancelled, living enemies keep fighting. Safe on any peer.</summary>
        public void ServerStop()
        {
            ServerStopPlatform();
            ServerStopVehicle();
        }

        public void ServerStartPlatform()
        {
            if (!NetworkServer.active || platformRoutine != null)
                return;
            platformRoutine = StartCoroutine(RunTrack(false));
        }

        public void ServerStartVehicle()
        {
            if (!NetworkServer.active || vehicleRoutine != null)
                return;
            vehicleRoutine = StartCoroutine(RunTrack(true));
        }

        public void ServerStopPlatform()
        {
            if (!NetworkServer.active)
                return;
            if (platformRoutine != null)
                StopCoroutine(platformRoutine);
            platformRoutine = null;
            CancelAll(platformRequests);
            platformState = TrackState.Idle;
        }

        public void ServerStopVehicle()
        {
            if (!NetworkServer.active)
                return;
            if (vehicleRoutine != null)
                StopCoroutine(vehicleRoutine);
            vehicleRoutine = null;
            CancelAll(vehicleRequests);
            vehicleState = TrackState.Idle;
        }

        // ── Track runner ────────────────────────────────────────────────────────────────

        private IEnumerator RunTrack(bool vehicle)
        {
            WaveTrack track = vehicle ? vehicleWaves : platformWaves;
            List<EnemySpawner.SpawnRequest> requests = vehicle ? vehicleRequests : platformRequests;
            string label = vehicle ? "Vehicle" : "Platform";

            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            if (track.waves == null || track.waves.Length == 0)
            {
                Finish(vehicle, track);
                yield break;
            }

            int index = 0;
            while (true)
            {
                Wave wave = track.waves[index];
                SetState(vehicle, index, wave.kind == WaveKind.Pause ? TrackState.Pause : TrackState.Combat);
                if (logWaves)
                    Debug.Log($"[EnemyDirector] {label} wave {index + 1}/{track.waves.Length} " +
                              $"'{wave.name}' ({wave.kind}) started.", this);
                ServerWaveStarted?.Invoke(vehicle, index, wave);

                if (wave.kind == WaveKind.Pause)
                    yield return new WaitForSeconds(wave.duration);
                else
                    yield return RunCombatWave(wave, requests, label, index);

                // Requests of a wave cut short by its timer keep spawning; forget only the finished ones.
                requests.RemoveAll(r => r.AllDead);
                yield return null; // a looping track of empty waves must not spin within one frame

                index++;
                if (index < track.waves.Length)
                    continue;

                switch (track.onEnd)
                {
                    case TrackEnd.LoopAll:
                        index = 0;
                        continue;
                    case TrackEnd.RepeatLast:
                        index = track.waves.Length - 1;
                        continue;
                }

                Finish(vehicle, track);
                yield break;
            }
        }

        private IEnumerator RunCombatWave(Wave wave, List<EnemySpawner.SpawnRequest> trackRequests, string label, int index)
        {
            var waveRequests = new List<EnemySpawner.SpawnRequest>();
            foreach (WaveEntry entry in wave.enemies)
            {
                if (entry == null || entry.spawner == null || entry.count <= 0)
                {
                    Debug.LogWarning($"[EnemyDirector] {label} wave {index + 1} '{wave.name}': entry without a spawner or count, skipped.", this);
                    continue;
                }
                EnemySpawner.SpawnRequest request = entry.spawner.ServerRequest(entry.count);
                waveRequests.Add(request);
                trackRequests.Add(request);
            }

            bool useKills = wave.endCondition != WaveEnd.Timer;
            bool useTimer = wave.endCondition != WaveEnd.AllKilled;
            float endTime = Time.time + wave.duration;

            while (true)
            {
                if (useTimer && Time.time >= endTime)
                    yield break;
                if (useKills && AllDead(waveRequests))
                    yield break;
                yield return null;
            }
        }

        private static bool AllDead(List<EnemySpawner.SpawnRequest> requests)
        {
            foreach (EnemySpawner.SpawnRequest request in requests)
                if (!request.AllDead)
                    return false;
            return true;
        }

        private static void CancelAll(List<EnemySpawner.SpawnRequest> requests)
        {
            foreach (EnemySpawner.SpawnRequest request in requests)
                request.owner.ServerCancel(request);
            requests.Clear();
        }

        private void Finish(bool vehicle, WaveTrack track)
        {
            SetState(vehicle, vehicle ? vehicleWaveIndex : platformWaveIndex, TrackState.Finished);
            if (vehicle) vehicleRoutine = null; else platformRoutine = null;
            if (logWaves)
                Debug.Log($"[EnemyDirector] {(vehicle ? "Vehicle" : "Platform")} track finished.", this);
            track.onFinished?.Invoke();
        }

        private void SetState(bool vehicle, int index, TrackState state)
        {
            if (vehicle)
            {
                vehicleWaveIndex = index;
                vehicleState = state;
            }
            else
            {
                platformWaveIndex = index;
                platformState = state;
            }
        }

        private void OnValidate()
        {
            foreach (WaveTrack track in new[] { platformWaves, vehicleWaves })
            {
                if (track?.waves == null)
                    continue;
                foreach (Wave wave in track.waves)
                {
                    if (wave != null && string.IsNullOrEmpty(wave.name))
                        wave.name = wave.kind == WaveKind.Pause ? "Pause" : "Wave";
                }
            }
        }
    }
}
