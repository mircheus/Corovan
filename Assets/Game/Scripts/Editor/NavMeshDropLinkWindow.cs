#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FPSGame.Editor
{
    /// <summary>Window for <see cref="NavMeshDropLinkGenerator"/>: settings, Generate / Clear, scene preview.</summary>
    public class NavMeshDropLinkWindow : EditorWindow
    {
        private const string PrefsKey = "FPSGame.NavMeshDropLinks.Settings";

        private DropLinkSettings settings;
        private bool limitToSelection;
        private Transform parent;
        private bool preview = true;
        private string status = "";
        private Vector2 scroll;

        [MenuItem("FPS Game/Navigation/Drop Links Generator")]
        public static void Open() => GetWindow<NavMeshDropLinkWindow>("Drop Links");

        private void OnEnable()
        {
            settings = new DropLinkSettings();
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
                JsonUtility.FromJsonOverwrite(json, settings);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Agent", EditorStyles.boldLabel);
            AgentTypePopup();
            AreaPopup();
            settings.costModifier = EditorGUILayout.FloatField(
                new GUIContent("Cost Modifier", "< 0 = use the area cost"), settings.costModifier);
            settings.bidirectional = EditorGUILayout.Toggle(
                new GUIContent("Bidirectional", "Off = drop only"), settings.bidirectional);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Drop", EditorStyles.boldLabel);
            var agent = NavMesh.GetSettingsByID(settings.agentTypeID);
            settings.minDrop = EditorGUILayout.FloatField(
                new GUIContent("Min Drop", $"Keep above the agent's Step Height ({agent.agentClimb:0.##})"), settings.minDrop);
            settings.maxDrop = EditorGUILayout.FloatField("Max Drop", settings.maxDrop);
            settings.maxForward = EditorGUILayout.FloatField(
                new GUIContent("Max Forward", "Max horizontal distance from the edge to the landing"), settings.maxForward);
            settings.landingClearance = EditorGUILayout.FloatField(
                new GUIContent("Landing Clearance", "Gap between the body and the ledge on landing"), settings.landingClearance);
            settings.obstacleMask = LayerMaskField("Obstacle Layers", settings.obstacleMask);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Links", EditorStyles.boldLabel);
            settings.sampleSpacing = EditorGUILayout.FloatField("Sample Spacing", settings.sampleSpacing);
            settings.maxLinkWidth = EditorGUILayout.FloatField("Max Link Width", settings.maxLinkWidth);
            settings.mergeTolerance = EditorGUILayout.FloatField(
                new GUIContent("Merge Tolerance", "Max drop / landing difference inside one link"), settings.mergeTolerance);
            settings.autoUpdate = EditorGUILayout.Toggle(
                new GUIContent("Auto Update", "Links follow a moving parent at runtime"), settings.autoUpdate);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scope", EditorStyles.boldLabel);
            limitToSelection = EditorGUILayout.Toggle(
                new GUIContent("Only Selection Bounds", "Only edges inside the bounds of the selected objects"), limitToSelection);
            parent = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Parent", "Empty = scene root. A moving platform: set it + Auto Update"),
                parent, typeof(Transform), true);

            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28)))
                    Generate();
                if (GUILayout.Button("Clear", GUILayout.Height(28)))
                {
                    int n = NavMeshDropLinkGenerator.Clear(parent);
                    status = n > 0 ? "Generated links removed." : "Nothing to remove.";
                    SceneView.RepaintAll();
                }
            }

            EditorGUI.BeginChangeCheck();
            preview = EditorGUILayout.Toggle("Scene Preview", preview);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUILayout.HelpBox(
                "Bake the NavMesh first: links are found on the baked mesh. Re-generate after every re-bake.",
                MessageType.Info);
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void Generate()
        {
            Bounds? limit = null;
            if (limitToSelection)
            {
                limit = SelectionBounds();
                if (!limit.HasValue)
                {
                    status = "Nothing with renderers/colliders is selected.";
                    return;
                }
            }

            var root = NavMeshDropLinkGenerator.Generate(settings, limit, parent, out var stats);
            status = root != null ? $"Created {stats.links} links.\n{stats}" : $"No drops found.\n{stats}";
            if (root != null)
                EditorGUIUtility.PingObject(root);
            SceneView.RepaintAll();
        }

        private static Bounds? SelectionBounds()
        {
            Bounds? result = null;
            foreach (var go in Selection.gameObjects)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    Encapsulate(r.bounds);
                foreach (var c in go.GetComponentsInChildren<Collider>())
                    Encapsulate(c.bounds);
            }
            if (result.HasValue)
            {
                var b = result.Value;
                b.Expand(new Vector3(1f, 2f, 1f)); // edges sit a little inside the geometry
                result = b;
            }
            return result;

            void Encapsulate(Bounds b)
            {
                if (result.HasValue)
                {
                    var r = result.Value;
                    r.Encapsulate(b);
                    result = r;
                }
                else
                    result = b;
            }
        }

        // ── Scene preview: every generated link as a jump arc with its width ──

        private void OnSceneGUI(SceneView view)
        {
            if (!preview || Event.current.type != EventType.Repaint)
                return;

            var roots = NavMeshDropLinkGenerator.FindRoots(parent);
            foreach (var root in roots)
            {
                foreach (var link in root.GetComponentsInChildren<NavMeshLink>())
                {
                    var t = link.transform;
                    var rot = t.rotation;
                    Vector3 start = t.position + rot * link.startPoint;
                    Vector3 end = t.position + rot * link.endPoint;
                    Vector3 side = rot * Vector3.right * (link.width * 0.5f);

                    Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
                    Handles.DrawLine(start - side, start + side, 3f);
                    Handles.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
                    Handles.DrawLine(end - side, end + side, 3f);

                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                    DrawArc(start, end);
                    if (link.width > 1.5f)
                    {
                        DrawArc(start - side * 0.9f, end - side * 0.9f);
                        DrawArc(start + side * 0.9f, end + side * 0.9f);
                    }
                }
            }
        }

        private static readonly Vector3[] ArcPoints = new Vector3[9];

        private static void DrawArc(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < ArcPoints.Length; i++)
            {
                float k = i / (float)(ArcPoints.Length - 1);
                var p = Vector3.Lerp(a, b, k);
                p.y += Mathf.Sin(k * Mathf.PI) * 0.5f;
                ArcPoints[i] = p;
            }
            Handles.DrawAAPolyLine(2f, ArcPoints);
        }

        // ── Popups ───────────────────────────────────────────────────────

        private void AgentTypePopup()
        {
            int count = NavMesh.GetSettingsCount();
            var names = new string[count];
            int current = 0;
            for (int i = 0; i < count; i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                names[i] = NavMesh.GetSettingsNameFromID(id);
                if (id == settings.agentTypeID)
                    current = i;
            }
            int picked = EditorGUILayout.Popup("Agent Type", current, names);
            settings.agentTypeID = NavMesh.GetSettingsByIndex(picked).agentTypeID;
        }

        private void AreaPopup()
        {
            var names = NavMesh.GetAreaNames();
            var shown = new List<string>();
            var indices = new List<int>();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i]))
                    continue;
                shown.Add(names[i]);
                indices.Add(NavMesh.GetAreaFromName(names[i]));
            }
            int current = Mathf.Max(0, indices.IndexOf(settings.area));
            int picked = EditorGUILayout.Popup("Area", current, shown.ToArray());
            settings.area = indices[picked];
        }

        private static LayerMask LayerMaskField(string label, LayerMask mask)
        {
            var layers = new List<string>();
            var numbers = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(name))
                    continue;
                layers.Add(name);
                numbers.Add(i);
            }

            int compact = 0;
            for (int i = 0; i < numbers.Count; i++)
                if ((mask.value & (1 << numbers[i])) != 0)
                    compact |= 1 << i;

            compact = EditorGUILayout.MaskField(label, compact, layers.ToArray());

            int result = 0;
            for (int i = 0; i < numbers.Count; i++)
                if ((compact & (1 << i)) != 0)
                    result |= 1 << numbers[i];
            mask.value = result;
            return mask;
        }
    }
}
#endif
