using System;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>How one point looks on the <see cref="CarRadar"/>. Shared by <see cref="RadarTarget"/> and <see cref="RadarPoints"/>.</summary>
    [Serializable]
    public class RadarBlipStyle
    {
        public Color color = Color.red;
        [Tooltip("Blip size as a fraction of the radar width.")]
        [Range(0.02f, 0.25f)] public float size = 0.07f;
        [Tooltip("Optional icon. Empty = a plain square.")]
        public Sprite icon;
        [Tooltip("Blink the blip (use for incoming threats).")]
        public bool pulse;
        public float pulseSpeed = 6f;
        [Tooltip("Ignore the radar's trackDistance: when far away, the point stays stuck to the radar edge " +
                 "and shows the direction (objectives, destinations).")]
        public bool alwaysTrack;
    }
}
