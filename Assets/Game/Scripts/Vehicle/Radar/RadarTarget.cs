using System.Collections.Generic;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Marker for anything the car radar may show. Add it to an enemy / obstacle / car; enabling the
    /// component puts it on the radar, disabling it (or destroying the object) takes it off.
    /// Pure registry: no networking, every peer registers the objects it has locally.
    /// For fixed scene points configured in one place, see <see cref="RadarPoints"/>.
    /// </summary>
    public class RadarTarget : MonoBehaviour
    {
        public static readonly List<RadarTarget> All = new List<RadarTarget>();

        [Header("Look on the radar")]
        public RadarBlipStyle style = new RadarBlipStyle();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);
    }
}
