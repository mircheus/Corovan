using System.Collections.Generic;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Test feed for <see cref="CarRadar"/>: fake targets that fly around the car, local to this peer.
    /// Approachers come from behind and overtake (pulsing red), orbiters circle the car (orange).
    /// Remove the component once real systems register their own <see cref="RadarTarget"/>s.
    /// </summary>
    public class RadarTestSpawner : MonoBehaviour
    {
        [SerializeField] private int approachers = 3;
        [SerializeField] private int orbiters = 2;
        [SerializeField] private float spawnDistance = 120f;
        [SerializeField] private Vector2 approachSpeed = new Vector2(6f, 14f);
        [SerializeField] private float sideSpread = 10f;
        [SerializeField] private float orbitRadius = 12f;
        [Tooltip("Spawn even when this peer is not driving the car.")]
        [SerializeField] private bool ignoreDriverCheck;

        private class Fake
        {
            public Transform t;
            public bool orbit;
            public float x, z, speed, angle, radius;
        }

        private CarController car;
        private readonly List<Fake> fakes = new List<Fake>();

        private void Awake() => car = GetComponent<CarController>();

        private void Update()
        {
            bool run = ignoreDriverCheck || (car != null && car.isOwned && car.HasDriver);
            if (run && fakes.Count == 0) Spawn();
            else if (!run && fakes.Count > 0) Clear();
            if (fakes.Count == 0) return;

            Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            foreach (Fake f in fakes)
            {
                Vector3 local;
                if (f.orbit)
                {
                    f.angle += f.speed * Time.deltaTime;
                    local = new Vector3(Mathf.Cos(f.angle), 0f, Mathf.Sin(f.angle)) * f.radius;
                }
                else
                {
                    f.z += f.speed * Time.deltaTime;
                    if (f.z > 30f) Respawn(f);
                    local = new Vector3(f.x, 0f, f.z);
                }
                f.t.position = transform.position + yaw * local;
            }
        }

        private void Spawn()
        {
            for (int i = 0; i < approachers; i++) fakes.Add(Create(false, i));
            for (int i = 0; i < orbiters; i++) fakes.Add(Create(true, i));
        }

        private Fake Create(bool orbit, int index)
        {
            var go = new GameObject(orbit ? "RadarFake_Orbiter" : "RadarFake_Approacher");
            var target = go.AddComponent<RadarTarget>();
            target.style.color = orbit ? new Color(1f, 0.6f, 0.1f) : Color.red;
            target.style.pulse = !orbit;
            var f = new Fake { t = go.transform, orbit = orbit };
            if (orbit)
            {
                f.radius = orbitRadius * (1f + 0.5f * index);
                f.angle = Random.value * Mathf.PI * 2f;
                f.speed = (index % 2 == 0 ? 1f : -1f) * Random.Range(0.3f, 0.8f);
            }
            else
            {
                Respawn(f);
                f.z = -spawnDistance * Random.value; // spread the first wave
            }
            return f;
        }

        private void Respawn(Fake f)
        {
            f.x = Random.Range(-sideSpread, sideSpread);
            f.z = -spawnDistance;
            f.speed = Random.Range(approachSpeed.x, approachSpeed.y);
        }

        private void Clear()
        {
            foreach (Fake f in fakes) if (f.t != null) Destroy(f.t.gameObject);
            fakes.Clear();
        }

        private void OnDestroy() => Clear();
    }
}
