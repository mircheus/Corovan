using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Root marker of a roadside scenery segment (a set of big building blocks) that
    /// <see cref="RoadsideScroller"/> scrolls past the convoy and recycles through a pool.
    ///
    /// Authoring convention — build every segment for the LEFT side of the road:
    /// <list type="bullet">
    /// <item>the segment occupies local Z from 0 to <see cref="Length"/> (the scroller lays them end to end);</item>
    /// <item>local X = 0 is the facade line nearest the road; the road lies towards +X, the blocks extend into -X;</item>
    /// <item>local Y = 0 is the ground.</item>
    /// </list>
    /// The right side uses the same prefab turned 180° around Y.
    ///
    /// Purely visual: no colliders, no NetworkIdentity. An "empty lot" is a prefab with only
    /// this component and a length.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoadsideSegment : MonoBehaviour
    {
        [Tooltip("Length of the segment along local Z (m). The next segment starts where this one ends.")]
        [Min(1f)]
        [SerializeField] private float length = 40f;

        public float Length => Mathf.Max(1f, length);

#if UNITY_EDITOR
        [ContextMenu("Fit Length To Renderers")]
        private void FitLengthToRenderers()
        {
            float maxZ = 0f;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                // Measure in the segment's own space so a placed / rotated instance still fits.
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    float z = transform.InverseTransformPoint(corner).z;
                    if (!any || z > maxZ) maxZ = z;
                    any = true;
                }
            }
            if (!any) return;
            UnityEditor.Undo.RecordObject(this, "Fit Length To Renderers");
            length = Mathf.Max(1f, Mathf.Ceil(maxZ));
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            float l = Length;

            // Segment span on the ground, along the facade line.
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, l));
            Gizmos.DrawLine(new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f));
            Gizmos.DrawLine(new Vector3(-3f, 0f, l), new Vector3(3f, 0f, l));

            // Arrow towards the road (+X).
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            var mid = new Vector3(0f, 0f, l * 0.5f);
            var tip = mid + new Vector3(6f, 0f, 0f);
            Gizmos.DrawLine(mid, tip);
            Gizmos.DrawLine(tip, tip + new Vector3(-1.5f, 0f, 1.5f));
            Gizmos.DrawLine(tip, tip + new Vector3(-1.5f, 0f, -1.5f));
        }
#endif
    }
}
