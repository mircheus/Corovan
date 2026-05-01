using System;
using UnityEngine;

namespace FPSGame.Tools
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public class FloorMeshModifier : MonoBehaviour
    {
        // Fired after the floor mesh is rebuilt with a hole.
        // Payload: 4 hole corners in floor LOCAL space, ordered counter-clockwise as
        // viewed from +Y looking down (i.e. positive shoelace area).
        public event Action<Vector3[]> OnHoleCreated;

        public enum WindingOverride { Auto, ForceUp, ForceDown }

        [Tooltip("Auto: detect from the original mesh's average normal Y. Use ForceUp/ForceDown if auto-detection picks the wrong side.")]
        [SerializeField] WindingOverride windingOverride = WindingOverride.Auto;

        Mesh originalMesh;
        Mesh runtimeMesh;
        MeshFilter mf;
        MeshCollider mc;
        Bounds floorLocalBounds;
        bool initialized;
        bool flipWinding; // true = emit triangles with reversed winding so normals match the original mesh

        void Awake()
        {
            mf = GetComponent<MeshFilter>();
            mc = GetComponent<MeshCollider>();

            if (mf.sharedMesh == null)
            {
                Debug.LogError($"[FloorMeshModifier] No mesh on {name}", this);
                return;
            }

            originalMesh = Instantiate(mf.sharedMesh);
            originalMesh.name = mf.sharedMesh.name + " (Original Copy)";

            runtimeMesh = Instantiate(originalMesh);
            runtimeMesh.name = mf.sharedMesh.name + " (Runtime)";

            mf.sharedMesh = runtimeMesh;
            mc.convex = false;
            mc.sharedMesh = runtimeMesh;

            floorLocalBounds = originalMesh.bounds;

            // Match the original mesh's face orientation so the cut band doesn't render inverted.
            switch (windingOverride)
            {
                case WindingOverride.ForceUp:   flipWinding = false; break;
                case WindingOverride.ForceDown: flipWinding = true;  break;
                default: flipWinding = AverageLocalNormalY(originalMesh) < 0f; break;
            }

            initialized = true;
        }

        static float AverageLocalNormalY(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            float sumY = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                // Unnormalized triangle normal Y component, weighted by area.
                sumY += Vector3.Cross(b - a, c - a).y;
            }
            return sumY;
        }

        public void CutHole(Vector3[] localCorners)
        {
            if (!initialized || localCorners == null || localCorners.Length != 4) return;

            // Work on a copy and ensure CCW (positive shoelace in XZ → up-facing normal).
            Vector3[] hole = new Vector3[4];
            for (int i = 0; i < 4; i++) hole[i] = localCorners[i];
            if (SignedArea2D(hole) < 0f) Array.Reverse(hole);

            if (!IsConvexCCW(hole))
                Debug.LogWarning("[FloorMeshModifier] Cut shape is concave; visual result may be incorrect.", this);

            float fxMin = floorLocalBounds.min.x, fxMax = floorLocalBounds.max.x;
            float fzMin = floorLocalBounds.min.z, fzMax = floorLocalBounds.max.z;
            float y = floorLocalBounds.center.y;

            // Floor corners CCW viewed from above.
            Vector3[] floor = new[]
            {
                new Vector3(fxMin, y, fzMin),
                new Vector3(fxMax, y, fzMin),
                new Vector3(fxMax, y, fzMax),
                new Vector3(fxMin, y, fzMax),
            };

            // Reject degenerate / out-of-bounds holes.
            for (int i = 0; i < 4; i++)
            {
                if (hole[i].x < fxMin || hole[i].x > fxMax ||
                    hole[i].z < fzMin || hole[i].z > fzMax)
                {
                    Debug.LogWarning("[FloorMeshModifier] Hole corner outside floor bounds; aborting cut.", this);
                    return;
                }
            }

            // Pair each floor corner with a hole corner by smallest angular offset
            // around the hole centroid. Both rings are CCW so this is a pure rotation k.
            Vector3 hc = (hole[0] + hole[1] + hole[2] + hole[3]) * 0.25f;
            int offset = FindBestOffset(hole, floor, hc);

            BuildBandMesh(floor, hole, offset, fxMin, fzMin, fxMax - fxMin, fzMax - fzMin);

            // Notify with the (possibly reordered) hole corners.
            OnHoleCreated?.Invoke(hole);
        }

        void BuildBandMesh(Vector3[] floor, Vector3[] hole, int offset,
                           float uvOriginX, float uvOriginZ, float uvW, float uvH)
        {
            // 8 verts (4 floor + 4 hole) → 8 triangles forming a band.
            Vector3[] verts = new Vector3[8];
            Vector2[] uvs = new Vector2[8];
            int[] tris = new int[24];

            for (int i = 0; i < 4; i++)
            {
                int hi = (i + offset) & 3;
                verts[i]     = floor[i];
                verts[i + 4] = hole[hi];
                uvs[i]     = new Vector2((floor[i].x  - uvOriginX) / uvW, (floor[i].z  - uvOriginZ) / uvH);
                uvs[i + 4] = new Vector2((hole[hi].x  - uvOriginX) / uvW, (hole[hi].z  - uvOriginZ) / uvH);
            }

            int t = 0;
            for (int i = 0; i < 4; i++)
            {
                int i1 = (i + 1) & 3;
                int Fi = i, Fn = i1, Hi = i + 4, Hn = i1 + 4;
                // Quad (Fi → Fn → Hn → Hi) is CCW viewed from +Y; split into two CCW triangles.
                // Reverse winding when the original mesh faces -Y locally.
                if (!flipWinding)
                {
                    tris[t++] = Fi; tris[t++] = Fn; tris[t++] = Hn;
                    tris[t++] = Fi; tris[t++] = Hn; tris[t++] = Hi;
                }
                else
                {
                    tris[t++] = Fi; tris[t++] = Hn; tris[t++] = Fn;
                    tris[t++] = Fi; tris[t++] = Hi; tris[t++] = Hn;
                }
            }

            runtimeMesh.Clear();
            runtimeMesh.vertices  = verts;
            runtimeMesh.uv        = uvs;
            runtimeMesh.triangles = tris;
            runtimeMesh.RecalculateNormals();
            runtimeMesh.RecalculateTangents();
            runtimeMesh.RecalculateBounds();

            mc.sharedMesh = null;
            mc.sharedMesh = runtimeMesh;
        }

        static float SignedArea2D(Vector3[] poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector3 p = poly[i];
                Vector3 q = poly[(i + 1) % poly.Length];
                a += p.x * q.z - q.x * p.z;
            }
            return 0.5f * a;
        }

        static bool IsConvexCCW(Vector3[] p)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = p[i], b = p[(i + 1) & 3], c = p[(i + 2) & 3];
                float cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
                if (cross < 0f) return false;
            }
            return true;
        }

        static int FindBestOffset(Vector3[] hole, Vector3[] floor, Vector3 center)
        {
            int bestK = 0;
            float bestSum = float.MaxValue;
            float[] fA = new float[4];
            float[] hA = new float[4];
            for (int i = 0; i < 4; i++)
            {
                fA[i] = Mathf.Atan2(floor[i].z - center.z, floor[i].x - center.x);
                hA[i] = Mathf.Atan2(hole[i].z  - center.z, hole[i].x  - center.x);
            }

            for (int k = 0; k < 4; k++)
            {
                float s = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float d = Mathf.DeltaAngle(fA[i] * Mathf.Rad2Deg, hA[(i + k) & 3] * Mathf.Rad2Deg);
                    s += Mathf.Abs(d);
                }
                if (s < bestSum) { bestSum = s; bestK = k; }
            }
            return bestK;
        }

        void OnDestroy()
        {
            if (runtimeMesh != null) Destroy(runtimeMesh);
            if (originalMesh != null) Destroy(originalMesh);
        }
    }
}
