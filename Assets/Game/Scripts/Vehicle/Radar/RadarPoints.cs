using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Scene list of map points every <see cref="CarRadar"/> shows (parking zone, exits, objectives...).
    /// Put one on any scene object, drag the objects into <see cref="points"/> and set each one's look.
    /// The blip sits at the object's position: for a <c>DecryptParkingZone</c> drag its anchor.
    /// Client-side only, no networking: the scene exists on every peer. Several RadarPoints may coexist.
    /// </summary>
    public class RadarPoints : MonoBehaviour
    {
        [Serializable]
        public class Point
        {
            [Tooltip("Inspector label only.")]
            public string name;
            public Transform target;
            [Tooltip("Off = hidden. Can also be switched at runtime with SetVisible.")]
            public bool visible = true;
            public RadarBlipStyle style = new RadarBlipStyle { color = Color.cyan, size = 0.09f, alwaysTrack = true };
        }

        public static readonly List<RadarPoints> All = new List<RadarPoints>();

        [SerializeField] private List<Point> points = new List<Point>();

        public IReadOnlyList<Point> Points => points;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Show / hide every point that follows <paramref name="target"/> (e.g. from a UnityEvent or an objective).</summary>
        public void SetVisible(Transform target, bool visible)
        {
            foreach (Point p in points)
                if (p.target == target) p.visible = visible;
        }

        /// <summary>Show / hide a point by its index in the list (UnityEvent-friendly pair below).</summary>
        public void SetVisible(int index, bool visible)
        {
            if (index >= 0 && index < points.Count) points[index].visible = visible;
        }

        public void Show(int index) => SetVisible(index, true);
        public void Hide(int index) => SetVisible(index, false);
    }
}
