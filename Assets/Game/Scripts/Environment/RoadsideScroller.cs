using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Scrolls roadside scenery (<see cref="RoadsideSegment"/> prefabs: sets of big building
    /// blocks) past the convoy to keep up the illusion that it drives forward. Two lanes, left
    /// and right of the road; the right one uses the same prefabs turned 180° around Y.
    ///
    /// Runs on every peer, nothing is networked:
    /// <list type="bullet">
    /// <item>its own <see cref="speed"/>, independent of <see cref="RoadScroll"/>. The travelled
    /// distance is analytic: speed × <see cref="NetworkTime.time"/> (the same on the host and every
    /// client), so no per-frame accumulator and no sync. Offline it runs on local time;</item>
    /// <item>which prefab is segment #k of a lane is a pure hash of (seed, lane, k), so every
    /// client — late joiners included — sees the same buildings in the same order.</item>
    /// </list>
    /// Segments are laid end to end in "ground" space (segment k starts where k-1 ends); the
    /// ones inside [Distance - keepBehind, Distance + spawnAhead] are taken from a per-prefab
    /// pool, the rest go back to it.
    ///
    /// Lives in the scroller's local space: its position is the road centre at ground height,
    /// the road runs along its +Z and the scenery moves along its -Z.
    ///
    /// Changing the speed at runtime (Inspector or <see cref="SetSpeed"/>) re-bases the clock on
    /// that peer only — no jump, but that peer's layout drifts from the others'. Fine for tuning;
    /// a gameplay speed change would need to reach every peer (e.g. a SyncVar elsewhere).
    /// </summary>
    public class RoadsideScroller : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public RoadsideSegment prefab;
            [Min(0f)] public float weight;
        }

        [Serializable]
        public struct Lane
        {
            public bool enabled;
            [Tooltip("Distance (m) from the road centre to the facade line of this lane.")]
            [Min(0f)] public float offsetX;
            [Tooltip("Extra height (m) of this lane above the scroller.")]
            public float offsetY;
        }

        [Header("Speed")]
        [Tooltip("How fast the scenery moves past (m/s). Independent of RoadScroll. Negative = backwards.")]
        [SerializeField] private float speed = 20f;

        [Header("Segments")]
        [Tooltip("Prefabs to pick from (weighted). Authored for the left side, see RoadsideSegment.")]
        [SerializeField] private Entry[] segments = Array.Empty<Entry>();
        [Tooltip("Same seed = same sequence of buildings on every peer and every run.")]
        [SerializeField] private int seed = 12345;

        [Header("Lanes")]
        [SerializeField] private Lane left = new Lane { enabled = true, offsetX = 30f };
        [SerializeField] private Lane right = new Lane { enabled = true, offsetX = 30f };

        [Header("Visible window (m, along the road)")]
        [Tooltip("Segments are kept until they start this far ahead of the scroller.")]
        [Min(1f)] [SerializeField] private float spawnAhead = 600f;
        [Tooltip("Segments are released once they end this far behind the scroller.")]
        [Min(0f)] [SerializeField] private float keepBehind = 150f;

        [Header("Pool")]
        [Tooltip("Instances created per prefab on start (the pool grows on demand anyway).")]
        [Min(0)] [SerializeField] private int prewarmPerPrefab = 2;

        private readonly Dictionary<RoadsideSegment, Stack<RoadsideSegment>> pool =
            new Dictionary<RoadsideSegment, Stack<RoadsideSegment>>();

        private readonly LaneRuntime[] lanes = new LaneRuntime[2];
        private float totalWeight;

        // distance = baseDistance + appliedSpeed * (clock - baseTime). Base is (0, 0) until the
        // speed changes, i.e. plain speed × NetworkTime.time — identical on every peer.
        private double baseDistance;
        private double baseTime;
        private float appliedSpeed;
        private bool clockNetworked;

        /// <summary>Current scroll speed (m/s).</summary>
        public float Speed => speed;

        /// <summary>Distance the scenery has travelled (m).</summary>
        public double Distance => baseDistance + appliedSpeed * (Clock - baseTime);

        private static double Clock => NetworkServer.active || NetworkClient.active
            ? NetworkTime.time
            : Time.timeAsDouble;

        private static readonly Quaternion RightRotation = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>One side of the road: a contiguous run of active segments [headIndex, headIndex + Count).</summary>
        private sealed class LaneRuntime
        {
            public int id;          // 0 = left, 1 = right (part of the hash)
            public bool mirrored;
            public Vector2 offset;  // signed X, Y
            public int headIndex;   // index of the first active segment (cursor when none is active)
            public double headStart;// ground Z where headIndex starts
            public double tailEnd;  // ground Z where the last active segment ends
            public readonly List<RoadsideSegment> active = new List<RoadsideSegment>();
        }

        private void Awake()
        {
            totalWeight = 0f;
            foreach (var e in segments)
                if (e.prefab != null && e.weight > 0f)
                    totalWeight += e.weight;

            if (totalWeight <= 0f)
            {
                Debug.LogWarning($"{nameof(RoadsideScroller)} on '{name}': no segment prefab with weight > 0 — disabled.", this);
                enabled = false;
                return;
            }

            appliedSpeed = speed;
            clockNetworked = NetworkServer.active || NetworkClient.active;

            if (left.enabled)
                lanes[0] = new LaneRuntime { id = 0, mirrored = false, offset = new Vector2(-left.offsetX, left.offsetY) };
            if (right.enabled)
                lanes[1] = new LaneRuntime { id = 1, mirrored = true, offset = new Vector2(right.offsetX, right.offsetY) };

            foreach (var e in segments)
            {
                if (e.prefab == null || e.weight <= 0f) continue;
                for (int i = 0; i < prewarmPerPrefab; i++)
                    Release(e.prefab, CreateInstance(e.prefab));
            }
        }

        /// <summary>Change the scroll speed on this peer without a jump (local, not synced).</summary>
        public void SetSpeed(float newSpeed)
        {
            speed = newSpeed;
            Rebase();
        }

        private void Rebase()
        {
            baseDistance = Distance;
            baseTime = Clock;
            appliedSpeed = speed;
        }

        private void OnValidate()
        {
            if (keepBehind > spawnAhead) keepBehind = spawnAhead;
        }

        private void LateUpdate()
        {
            // Network started / stopped: back to the shared formula (the window absorbs the jump).
            bool networked = NetworkServer.active || NetworkClient.active;
            if (networked != clockNetworked)
            {
                clockNetworked = networked;
                if (networked && appliedSpeed == speed) { baseDistance = 0.0; baseTime = 0.0; }
                else Rebase();
            }
            if (speed != appliedSpeed) Rebase(); // Inspector tweak in Play mode

            double distance = Distance;
            double min = distance - keepBehind;
            double max = distance + spawnAhead;

            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i];
                if (lane == null) continue;
                UpdateWindow(lane, min, max);
                PlaceLane(lane, distance);
            }
        }

        // ── Window ───────────────────────────────────────────────────────────

        private void UpdateWindow(LaneRuntime lane, double min, double max)
        {
            var active = lane.active;

            // Drop segments that left the window at the back...
            while (active.Count > 0 && lane.headStart + SegmentLength(lane, lane.headIndex) <= min)
            {
                Release(PrefabAt(lane, lane.headIndex), active[0]);
                active.RemoveAt(0);
                lane.headStart += SegmentLength(lane, lane.headIndex);
                lane.headIndex++;
            }
            // ...and at the front (only when the belt runs backwards).
            while (active.Count > 0)
            {
                int lastIndex = lane.headIndex + active.Count - 1;
                float lastLength = SegmentLength(lane, lastIndex);
                if (lane.tailEnd - lastLength < max) break;
                Release(PrefabAt(lane, lastIndex), active[active.Count - 1]);
                active.RemoveAt(active.Count - 1);
                lane.tailEnd -= lastLength;
            }

            // Nothing left (first frame, or a jump such as a late join): move the cursor so
            // headIndex is the segment that covers `min`. Lengths are deterministic, so
            // stepping from any index gives the same layout.
            if (active.Count == 0)
            {
                while (lane.headStart + SegmentLength(lane, lane.headIndex) <= min)
                {
                    lane.headStart += SegmentLength(lane, lane.headIndex);
                    lane.headIndex++;
                }
                while (lane.headStart > min)
                {
                    lane.headIndex--;
                    lane.headStart -= SegmentLength(lane, lane.headIndex);
                }
                lane.tailEnd = lane.headStart;
            }

            // Fill ahead.
            while (lane.tailEnd < max)
            {
                int index = lane.headIndex + active.Count;
                active.Add(Get(PrefabAt(lane, index), lane));
                lane.tailEnd += SegmentLength(lane, index);
            }
            // Fill behind (belt running backwards).
            while (lane.headStart > min)
            {
                lane.headIndex--;
                lane.headStart -= SegmentLength(lane, lane.headIndex);
                active.Insert(0, Get(PrefabAt(lane, lane.headIndex), lane));
            }
        }

        private void PlaceLane(LaneRuntime lane, double distance)
        {
            double start = lane.headStart;
            for (int i = 0; i < lane.active.Count; i++)
            {
                var segment = lane.active[i];
                float length = segment.Length;
                // Mirrored: the prefab's local Z [0, L] maps to [-L, 0], so its pivot sits at the end.
                double pivot = lane.mirrored ? start + length : start;
                segment.transform.localPosition = new Vector3(lane.offset.x, lane.offset.y, (float)(pivot - distance));
                start += length;
            }
        }

        // ── Deterministic choice ────────────────────────────────────────────

        private RoadsideSegment PrefabAt(LaneRuntime lane, int index)
        {
            float pick = Hash01(seed, lane.id, index) * totalWeight;
            RoadsideSegment last = null;
            foreach (var e in segments)
            {
                if (e.prefab == null || e.weight <= 0f) continue;
                last = e.prefab;
                if (pick < e.weight) return e.prefab;
                pick -= e.weight;
            }
            return last; // float rounding at the very top of the range
        }

        private float SegmentLength(LaneRuntime lane, int index) => PrefabAt(lane, index).Length;

        /// <summary>Integer hash → [0, 1). Same on every platform (no UnityEngine.Random state).</summary>
        private static float Hash01(int seed, int lane, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)lane * 0x85EBCA77u;
                h ^= (uint)index * 0xC2B2AE3Du;
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        // ── Pool ─────────────────────────────────────────────────────────────

        private RoadsideSegment CreateInstance(RoadsideSegment prefab)
        {
            var instance = Instantiate(prefab, transform);
            instance.name = prefab.name;
            return instance;
        }

        private RoadsideSegment Get(RoadsideSegment prefab, LaneRuntime lane)
        {
            RoadsideSegment instance = null;
            if (pool.TryGetValue(prefab, out var stack) && stack.Count > 0)
                instance = stack.Pop();
            if (instance == null)
                instance = CreateInstance(prefab);

            // Set on every take: a pooled instance may have served the other side.
            instance.transform.localRotation = lane.mirrored ? RightRotation : Quaternion.identity;
            instance.gameObject.SetActive(true);
            return instance;
        }

        private void Release(RoadsideSegment prefab, RoadsideSegment instance)
        {
            instance.gameObject.SetActive(false);
            if (!pool.TryGetValue(prefab, out var stack))
                pool[prefab] = stack = new Stack<RoadsideSegment>();
            stack.Push(instance);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            DrawLaneGizmo(left, -1f);
            DrawLaneGizmo(right, 1f);
        }

        private void DrawLaneGizmo(Lane lane, float sign)
        {
            if (!lane.enabled) return;
            float x = sign * lane.offsetX;
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
            Gizmos.DrawLine(new Vector3(x, lane.offsetY, -keepBehind), new Vector3(x, lane.offsetY, spawnAhead));
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            Gizmos.DrawLine(new Vector3(x - 5f, lane.offsetY, -keepBehind), new Vector3(x + 5f, lane.offsetY, -keepBehind));
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawLine(new Vector3(x - 5f, lane.offsetY, spawnAhead), new Vector3(x + 5f, lane.offsetY, spawnAhead));
        }
#endif
    }
}
