using System.Collections.Generic;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Marker for anything the car radar may show. Add it to an enemy / obstacle / car; enabling the
    /// component puts it on the radar, disabling it (or destroying the object) takes it off.
    /// Pure registry: no networking, every peer registers the objects it has locally.
    /// </summary>
    public class RadarTarget : MonoBehaviour
    {
        public static readonly List<RadarTarget> All = new List<RadarTarget>();

        [Header("Look on the radar")]
        public Color color = Color.red;
        [Tooltip("Blip size as a fraction of the radar width.")]
        [Range(0.02f, 0.25f)] public float size = 0.07f;
        [Tooltip("Optional icon. Empty = a plain square.")]
        public Sprite icon;
        [Tooltip("Blink the blip (use for incoming threats).")]
        public bool pulse;
        public float pulseSpeed = 6f;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);
    }
}
