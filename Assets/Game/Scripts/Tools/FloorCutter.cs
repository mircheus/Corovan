using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.Tools
{
    // 4-stroke floor-cutting tool. Each LMB hold→drag→release records one edge of the
    // cut quadrilateral. Stroke 1 records A and B. Strokes 2 and 3 each chain off the
    // last confirmed point and record one new vertex (C, then D). Stroke 4 chains off
    // D; the release point is ignored — the fourth edge auto-closes D→A and the cut
    // executes immediately. No undo, no cancel.
    public class FloorCutter : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] LayerMask floorMask = ~0;
        [SerializeField] string floorTag = "Floor";
        [SerializeField] LineRenderer previewLineTemplate;   // current stroke (anchor → cursor)
        [SerializeField] LineRenderer confirmedLineTemplate; // persists until cut executes
        [SerializeField] float maxRayDistance = 200f;
        [SerializeField] float lineYOffset = 0.02f;

        enum State { Idle, Drawing }

        State state = State.Idle;
        FloorMeshModifier currentFloor;
        readonly List<Vector3> confirmedWorld = new List<Vector3>(4);
        readonly List<LineRenderer> confirmedLines = new List<LineRenderer>(4);
        LineRenderer activePreview;
        Vector3 currentAnchorWorld;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        void OnDisable()
        {
            ResetAll();
        }

        void Update()
        {
            if (cam == null || Mouse.current == null) return;

            if (state == State.Idle)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    TryBeginStroke();
            }
            else // Drawing
            {
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                    EndStroke();
                else
                    UpdateStrokePreview();
            }
        }

        bool RaycastFloorWorld(out Vector3 worldPoint, out FloorMeshModifier floor)
        {
            worldPoint = default;
            floor = null;
            Ray r = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(r, out RaycastHit hit, maxRayDistance, floorMask, QueryTriggerInteraction.Ignore))
                return false;
            if (!string.IsNullOrEmpty(floorTag) && !hit.collider.CompareTag(floorTag)) return false;
            floor = hit.collider.GetComponent<FloorMeshModifier>();
            if (floor == null) return false;
            worldPoint = hit.point;
            return true;
        }

        void TryBeginStroke()
        {
            if (!RaycastFloorWorld(out var p, out var floor)) return;

            if (confirmedWorld.Count == 0)
            {
                // Stroke 1: press records A; release records B.
                currentFloor = floor;
                currentAnchorWorld = p;
                confirmedWorld.Add(p);
            }
            else
            {
                // Strokes 2..4: must press on the same floor; anchor is last confirmed point.
                if (floor != currentFloor) return;
                currentAnchorWorld = confirmedWorld[confirmedWorld.Count - 1];
            }

            CreatePreview(currentAnchorWorld);
            state = State.Drawing;
        }

        void UpdateStrokePreview()
        {
            if (activePreview == null) return;
            if (!RaycastFloorWorld(out var p, out var floor) || floor != currentFloor) return;
            activePreview.SetPosition(0, currentAnchorWorld + Vector3.up * lineYOffset);
            activePreview.SetPosition(1, p + Vector3.up * lineYOffset);
        }

        void EndStroke()
        {
            int confirmedBefore = confirmedWorld.Count;

            // Stroke 4: fourth edge auto-snaps from D back to A — release point is ignored.
            if (confirmedBefore == 4)
            {
                FinalizeEdge(currentAnchorWorld, confirmedWorld[0]);
                ExecuteCut();
                return;
            }

            // Strokes 1..3: release point becomes the next confirmed vertex.
            if (!RaycastFloorWorld(out var p, out var floor) || floor != currentFloor)
            {
                // Per spec there is no cancel; an off-floor release leaves us no valid vertex.
                // Hard-reset so the player isn't trapped in a half-built shape.
                Debug.LogWarning("[FloorCutter] Stroke released off-floor; aborting cut sequence.", this);
                ResetAll();
                return;
            }

            confirmedWorld.Add(p);
            FinalizeEdge(currentAnchorWorld, p);
            DestroyPreview();
            state = State.Idle;
        }

        void CreatePreview(Vector3 anchorWorld)
        {
            if (previewLineTemplate == null) return;
            activePreview = Instantiate(previewLineTemplate);
            activePreview.gameObject.SetActive(true);
            activePreview.useWorldSpace = true;
            activePreview.positionCount = 2;
            Vector3 p = anchorWorld + Vector3.up * lineYOffset;
            activePreview.SetPosition(0, p);
            activePreview.SetPosition(1, p);
        }

        void DestroyPreview()
        {
            if (activePreview != null) Destroy(activePreview.gameObject);
            activePreview = null;
        }

        void FinalizeEdge(Vector3 a, Vector3 b)
        {
            LineRenderer template = confirmedLineTemplate != null ? confirmedLineTemplate : previewLineTemplate;
            if (template == null) return;
            LineRenderer lr = Instantiate(template);
            lr.gameObject.SetActive(true);
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, a + Vector3.up * lineYOffset);
            lr.SetPosition(1, b + Vector3.up * lineYOffset);
            confirmedLines.Add(lr);
        }

        void ExecuteCut()
        {
            if (currentFloor != null && confirmedWorld.Count == 4)
            {
                Vector3[] localCorners = new Vector3[4];
                for (int i = 0; i < 4; i++)
                    localCorners[i] = currentFloor.transform.InverseTransformPoint(confirmedWorld[i]);
                currentFloor.CutHole(localCorners);
            }
            ResetAll();
        }

        void ResetAll()
        {
            DestroyPreview();
            for (int i = 0; i < confirmedLines.Count; i++)
                if (confirmedLines[i] != null) Destroy(confirmedLines[i].gameObject);
            confirmedLines.Clear();
            confirmedWorld.Clear();
            currentFloor = null;
            state = State.Idle;
        }
    }
}
