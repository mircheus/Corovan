#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPSGame.Editor
{
    /// <summary>Settings of a drop-link pass. Serializable so the window can keep them in EditorPrefs.</summary>
    [Serializable]
    public class DropLinkSettings
    {
        [Tooltip("NavMesh agent type the links are for (Humanoid = zombies, droids).")]
        public int agentTypeID = 0;
        [Tooltip("Area of the generated links. 'Jump' (cost 2) makes agents prefer stairs when they are about as short.")]
        public int area = 2;
        [Tooltip("Link cost modifier; < 0 = use the area cost.")]
        public float costModifier = -1f;

        [Tooltip("Smallest drop that gets a link. Keep it above the agent's Step Height, or every curb gets one.")]
        public float minDrop = 1f;
        [Tooltip("Largest drop that gets a link.")]
        public float maxDrop = 6f;
        [Tooltip("Largest horizontal distance from the edge to the landing point.")]
        public float maxForward = 3f;
        [Tooltip("Clearance between the agent's body and the ledge on landing (added to 2 × agent radius).")]
        public float landingClearance = 0.25f;

        [Tooltip("Distance between edge samples. Smaller = more precise link ends, slower.")]
        public float sampleSpacing = 0.5f;
        [Tooltip("Longest single link; longer ledges are split into several links.")]
        public float maxLinkWidth = 6f;
        [Tooltip("Neighbouring samples join one link when their drop / landing distance differ by less than this.")]
        public float mergeTolerance = 0.4f;

        [Tooltip("Colliders tested for walls at the edge and ground below.")]
        public LayerMask obstacleMask = Physics.DefaultRaycastLayers & ~(1 << 21); // 21 = PlayerHitbox

        [Tooltip("Off = drop only, the agent can't climb back up by this link.")]
        public bool bidirectional = false;
        [Tooltip("Links follow their transform at runtime (needed if they are parented to a moving platform).")]
        public bool autoUpdate = false;
    }

    /// <summary>What one pass found, for the window and the test.</summary>
    public struct DropLinkStats
    {
        public int boundaryEdges;
        public int samples;
        public int validSamples;
        public int links;
        public override string ToString() =>
            $"boundary edges {boundaryEdges}, samples {samples}, valid {validSamples}, links {links}";
    }

    /// <summary>
    /// Finds NavMesh ledges an agent can drop from and places one-way <see cref="NavMeshLink"/>s on them —
    /// the old "Generated Off Mesh Links / Drop Height" that NavMeshSurface doesn't have.
    ///
    /// Works on the baked NavMesh (NavMesh.CalculateTriangulation): every triangle edge used by only one
    /// triangle is the mesh border. Each border edge is sampled; a sample becomes a drop when
    ///   • nothing blocks the way out (wall rays at knee and head height),
    ///   • the ground below, past the ledge, is between minDrop and maxDrop lower,
    ///   • there is NavMesh of the same agent type right there.
    /// Neighbouring valid samples are merged into wide links along the ledge.
    /// </summary>
    public static class NavMeshDropLinkGenerator
    {
        public const string RootName = "NavMesh Drop Links (generated)";

        /// <summary>A stretch of ledge: from a to b along the edge, outward normal n, landing (fwd ahead, drop down).</summary>
        public struct Segment
        {
            public Vector3 a, b, n;
            public float drop, fwd;
            public float Length => Vector3.Distance(a, b);
        }

        // ── Public entry points ──────────────────────────────────────────

        /// <summary>
        /// Replaces the generated links of the active scene. Undoable.
        /// <paramref name="limit"/> = only edges inside these bounds; <paramref name="parent"/> = where the root goes.
        /// </summary>
        public static GameObject Generate(DropLinkSettings s, Bounds? limit, Transform parent, out DropLinkStats stats)
        {
            Clear(parent);

            var segments = FindSegments(s, limit, out stats);
            stats.links = segments.Count;
            if (segments.Count == 0)
                return null;

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate NavMesh Drop Links");
            if (parent != null)
                Undo.SetTransformParent(root.transform, parent, "Generate NavMesh Drop Links");

            for (int i = 0; i < segments.Count; i++)
                CreateLink(segments[i], s, root.transform, i);

            EditorSceneManager.MarkSceneDirty(root.scene);
            return root;
        }

        /// <summary>Deletes generated roots of the active scene (or the one under <paramref name="parent"/>). Undoable.</summary>
        public static int Clear(Transform parent = null)
        {
            int removed = 0;
            foreach (var root in FindRoots(parent))
            {
                Undo.DestroyObjectImmediate(root);
                removed++;
            }
            return removed;
        }

        public static List<GameObject> FindRoots(Transform parent = null)
        {
            var result = new List<GameObject>();
            if (parent != null)
            {
                foreach (Transform child in parent)
                    if (child.name == RootName)
                        result.Add(child.gameObject);
                return result;
            }

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName)
                    result.Add(go);
            return result;
        }

        // ── Detection ────────────────────────────────────────────────────

        public static List<Segment> FindSegments(DropLinkSettings s, Bounds? limit, out DropLinkStats stats)
        {
            stats = default;
            Physics.SyncTransforms(); // edit mode: freshly moved colliders aren't in the physics scene yet

            var agent = NavMesh.GetSettingsByID(s.agentTypeID);
            float radius = agent.agentRadius;
            float height = agent.agentHeight;
            var filter = new NavMeshQueryFilter { agentTypeID = s.agentTypeID, areaMask = NavMesh.AllAreas };

            float wallRayLength = radius * 2f + s.landingClearance;
            float kneeHeight = Mathf.Min(agent.agentClimb + 0.1f, height * 0.5f);
            float spacing = Mathf.Max(0.05f, s.sampleSpacing);
            const QueryTriggerInteraction noTriggers = QueryTriggerInteraction.Ignore;

            var segments = new List<Segment>();
            foreach (var edge in BoundaryEdges(NavMesh.CalculateTriangulation()))
            {
                stats.boundaryEdges++;
                Vector3 A = edge.a, B = edge.b;
                Vector3 n = edge.n;

                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(A, B) / spacing));
                int runStart = -1;
                float dropSum = 0f, fwdSum = 0f;

                for (int i = 0; i <= count; i++)
                {
                    bool valid = false;
                    float drop = 0f, fwd = 0f;

                    if (i < count)
                    {
                        Vector3 p = Vector3.Lerp(A, B, (i + 0.5f) / count);
                        if (!limit.HasValue || limit.Value.Contains(p))
                        {
                            stats.samples++;
                            valid = TestSample(p, n, s, filter, wallRayLength, kneeHeight, height, out drop, out fwd);
                            if (valid)
                                stats.validSamples++;
                        }
                    }

                    if (valid)
                    {
                        if (runStart < 0)
                        {
                            runStart = i;
                            dropSum = fwdSum = 0f;
                        }
                        dropSum += drop;
                        fwdSum += fwd;
                        continue;
                    }

                    if (runStart >= 0)
                    {
                        int runCount = i - runStart;
                        segments.Add(new Segment
                        {
                            a = Vector3.Lerp(A, B, (float)runStart / count),
                            b = Vector3.Lerp(A, B, (float)i / count),
                            n = n,
                            drop = dropSum / runCount,
                            fwd = fwdSum / runCount,
                        });
                        runStart = -1;
                    }
                }
            }

            MergeSegments(segments, s);
            SplitLongSegments(segments, s.maxLinkWidth);
            return segments;

            bool TestSample(Vector3 p, Vector3 n, DropLinkSettings st, NavMeshQueryFilter f, float wallRay,
                float knee, float agentHeight, out float drop, out float fwd)
            {
                drop = fwd = 0f;

                // The triangulation holds every agent type: keep only edges of ours.
                if (!NavMesh.SamplePosition(p, out var onMesh, 0.3f, f) ||
                    (onMesh.position - p).sqrMagnitude > 0.3f * 0.3f)
                    return false;

                // A wall or railing right past the edge: the border is there because of it, not a ledge.
                if (Physics.Raycast(p + Vector3.up * knee, n, wallRay, st.obstacleMask, noTriggers) ||
                    Physics.Raycast(p + Vector3.up * (agentHeight - 0.1f), n, wallRay, st.obstacleMask, noTriggers))
                    return false;

                // Ground below the landing spot: agent centre lands radius + clearance past the physical ledge.
                Vector3 q = p + n * wallRay;
                if (!Physics.Raycast(q + Vector3.up * 0.5f, Vector3.down, out var hit, st.maxDrop + 1.5f,
                        st.obstacleMask, noTriggers))
                    return false;

                float groundDrop = p.y - hit.point.y;
                if (groundDrop < st.minDrop)
                    return false; // floor goes on (obstacle erosion, a step) or a lip blocks it

                if (!NavMesh.SamplePosition(hit.point, out var land, radius + 1f, f))
                    return false;
                if (Mathf.Abs(land.position.y - hit.point.y) > 0.6f)
                    return false; // the nearest NavMesh is another level, not the ground we hit

                drop = p.y - land.position.y;
                fwd = Vector3.Dot(land.position - p, n);
                return drop >= st.minDrop && drop <= st.maxDrop && fwd > 0.05f && fwd <= st.maxForward;
            }
        }

        private struct Edge
        {
            public Vector3 a, b, n;
        }

        /// <summary>Border edges of the triangulation with their outward horizontal normal.</summary>
        private static IEnumerable<Edge> BoundaryEdges(NavMeshTriangulation tri)
        {
            // Weld: tiles and polygons repeat vertices.
            const float weld = 0.05f;
            var keyToIndex = new Dictionary<Vector3Int, int>();
            var verts = new List<Vector3>();
            var remap = new int[tri.vertices.Length];
            for (int i = 0; i < tri.vertices.Length; i++)
            {
                var v = tri.vertices[i];
                var key = Vector3Int.RoundToInt(v / weld);
                if (!keyToIndex.TryGetValue(key, out int idx))
                {
                    idx = verts.Count;
                    verts.Add(v);
                    keyToIndex.Add(key, idx);
                }
                remap[i] = idx;
            }

            // edge (min,max) → (use count, opposite vertex of the first triangle)
            var edges = new Dictionary<long, (int count, int opposite)>();
            var idxs = tri.indices;
            for (int t = 0; t + 2 < idxs.Length; t += 3)
            {
                int i0 = remap[idxs[t]], i1 = remap[idxs[t + 1]], i2 = remap[idxs[t + 2]];
                if (i0 == i1 || i1 == i2 || i0 == i2)
                    continue; // degenerate after welding
                AddEdge(i0, i1, i2);
                AddEdge(i1, i2, i0);
                AddEdge(i2, i0, i1);
            }

            foreach (var kv in edges)
            {
                if (kv.Value.count != 1)
                    continue;

                int ia = (int)(kv.Key >> 32), ib = (int)(kv.Key & 0xffffffff);
                Vector3 a = verts[ia], b = verts[ib], c = verts[kv.Value.opposite];
                Vector3 dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                    continue; // vertical edge

                Vector3 n = Vector3.Cross(Vector3.up, dir).normalized;
                Vector3 toInside = c - a;
                toInside.y = 0f;
                if (Vector3.Dot(n, toInside) > 0f)
                    n = -n;

                yield return new Edge { a = a, b = b, n = n };
            }

            void AddEdge(int a, int b, int opposite)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                edges[key] = edges.TryGetValue(key, out var e) ? (e.count + 1, e.opposite) : (1, opposite);
            }
        }

        // ── Merging ──────────────────────────────────────────────────────

        /// <summary>Joins collinear touching segments of one ledge (a straight ledge is many short NavMesh edges).</summary>
        private static void MergeSegments(List<Segment> segs, DropLinkSettings s)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    for (int j = i + 1; j < segs.Count; j++)
                    {
                        if (!TryMerge(segs[i], segs[j], s, out var merged))
                            continue;
                        segs[i] = merged;
                        segs.RemoveAt(j);
                        j--;
                        changed = true;
                    }
                }
            }
        }

        private static bool TryMerge(Segment s1, Segment s2, DropLinkSettings s, out Segment merged)
        {
            merged = default;
            if (Vector3.Dot(s1.n, s2.n) < 0.985f) // ~10°
                return false;
            if (Mathf.Abs(s1.drop - s2.drop) > s.mergeTolerance || Mathf.Abs(s1.fwd - s2.fwd) > s.mergeTolerance)
                return false;
            if (Mathf.Abs(s1.a.y - s2.a.y) > 0.3f)
                return false;

            Vector3 t = Vector3.Cross(Vector3.up, s1.n).normalized; // along the ledge
            Vector3 o = s1.a;

            // Collinear: s2 lies on s1's line (horizontally).
            if (Mathf.Abs(Vector3.Dot(Flat(s2.a - o), s1.n)) > 0.15f || Mathf.Abs(Vector3.Dot(Flat(s2.b - o), s1.n)) > 0.15f)
                return false;

            float a1 = Vector3.Dot(s1.a - o, t), b1 = Vector3.Dot(s1.b - o, t);
            float a2 = Vector3.Dot(s2.a - o, t), b2 = Vector3.Dot(s2.b - o, t);
            float min1 = Mathf.Min(a1, b1), max1 = Mathf.Max(a1, b1);
            float min2 = Mathf.Min(a2, b2), max2 = Mathf.Max(a2, b2);

            float gap = Mathf.Max(min2 - max1, min1 - max2);
            if (gap > s.sampleSpacing * 0.75f)
                return false;

            float lo = Mathf.Min(min1, min2), hi = Mathf.Max(max1, max2);
            float w1 = Mathf.Max(s1.Length, 0.01f), w2 = Mathf.Max(s2.Length, 0.01f);
            float k = w2 / (w1 + w2);
            float y = Mathf.Lerp((s1.a.y + s1.b.y) * 0.5f, (s2.a.y + s2.b.y) * 0.5f, k);

            Vector3 pa = o + t * lo, pb = o + t * hi;
            pa.y = pb.y = y;
            merged = new Segment
            {
                a = pa,
                b = pb,
                n = (s1.n * w1 + s2.n * w2).normalized,
                drop = Mathf.Lerp(s1.drop, s2.drop, k),
                fwd = Mathf.Lerp(s1.fwd, s2.fwd, k),
            };
            return true;
        }

        private static void SplitLongSegments(List<Segment> segs, float maxWidth)
        {
            if (maxWidth <= 0f)
                return;
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var seg = segs[i];
                int parts = Mathf.CeilToInt(seg.Length / maxWidth);
                if (parts <= 1)
                    continue;
                segs.RemoveAt(i);
                for (int p = 0; p < parts; p++)
                {
                    var part = seg;
                    part.a = Vector3.Lerp(seg.a, seg.b, (float)p / parts);
                    part.b = Vector3.Lerp(seg.a, seg.b, (float)(p + 1) / parts);
                    segs.Insert(i + p, part);
                }
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ── Link creation ────────────────────────────────────────────────

        private static void CreateLink(Segment seg, DropLinkSettings s, Transform root, int index)
        {
            var go = new GameObject($"DropLink_{index:000}");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation((seg.a + seg.b) * 0.5f, Quaternion.LookRotation(seg.n, Vector3.up));

            var link = go.AddComponent<NavMeshLink>();
            link.agentTypeID = s.agentTypeID;
            link.area = s.area;
            link.costModifier = s.costModifier;
            link.bidirectional = s.bidirectional;
            link.autoUpdate = s.autoUpdate;
            link.width = seg.Length;

            // Local frame: Z = out over the ledge, X = along it (the link's width axis).
            // The parent may be scaled; the points are in unscaled local space, so undo only its rotation.
            link.startPoint = Vector3.zero;
            link.endPoint = Quaternion.Inverse(go.transform.rotation) * (seg.n * seg.fwd + Vector3.down * seg.drop);
        }
    }
}
#endif
