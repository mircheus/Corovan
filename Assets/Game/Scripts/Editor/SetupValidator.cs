#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace FPSGame.Editor
{
    public class SetupValidator : EditorWindow
    {
        private struct CheckResult
        {
            public string label;
            public bool passed;
            public string detail;
        }

        private CheckResult[] results;
        private Vector2 scrollPos;

        [MenuItem("FPSGame/Steam Setup Validator")]
        public static void ShowWindow()
        {
            GetWindow<SetupValidator>("Steam Setup Validator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Steam + Mirror Setup Validator", EditorStyles.boldLabel);
            GUILayout.Space(8);

            if (GUILayout.Button("Run Validation"))
                RunValidation();

            GUILayout.Space(8);

            if (results == null) return;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach (var r in results)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.color = r.passed ? Color.green : Color.red;
                GUILayout.Label(r.passed ? "✓" : "✗", GUILayout.Width(20));
                GUI.color = Color.white;
                GUILayout.Label(r.label);
                if (!string.IsNullOrEmpty(r.detail))
                    GUILayout.Label($"({r.detail})", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void RunValidation()
        {
            results = new CheckResult[]
            {
                CheckType("Mirror.FizzySteam.FizzyFacepunch", "FizzyFacepunch transport type"),
                CheckFacepunchAssembly(),
                CheckType("Mirror.NetworkManager", "Mirror.NetworkManager type"),
                CheckFile(Path.Combine(Application.dataPath, "steam_appid.txt"), "steam_appid.txt in Assets/"),
                CheckFile(Path.Combine(Path.GetDirectoryName(Application.dataPath), "steam_appid.txt"), "steam_appid.txt in project root"),
                CheckSceneNetworkManager(),
            };
            Repaint();
        }

        private static CheckResult CheckFacepunchAssembly()
        {
            const string label = "Facepunch.Steamworks assembly";
            bool found = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name.IndexOf("Facepunch", StringComparison.OrdinalIgnoreCase) >= 0
                       || a.GetName().Name.IndexOf("Steamworks", StringComparison.OrdinalIgnoreCase) >= 0);
            return new CheckResult { label = label, passed = found };
        }

        private static CheckResult CheckType(string typeName, string label)
        {
            bool found = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetType(typeName) != null);
            return new CheckResult { label = label, passed = found };
        }

        private static CheckResult CheckFile(string path, string label)
        {
            bool exists = File.Exists(path);
            return new CheckResult { label = label, passed = exists, detail = exists ? "" : $"Missing: {path}" };
        }

        private static CheckResult CheckSceneNetworkManager()
        {
            const string label = "Active scene has NetworkManager with FizzyFacepunch transport";

            var nm = FindAnyObjectByType<NetworkManager>();
            if (nm == null)
                return new CheckResult { label = label, passed = false, detail = "No NetworkManager in scene" };

            var transport = nm.GetComponent<Transport>();
            if (transport == null)
                return new CheckResult { label = label, passed = false, detail = "No Transport component on NetworkManager" };

            bool isFizzy = transport.GetType().FullName?.IndexOf("FizzyFacepunch", StringComparison.OrdinalIgnoreCase) >= 0
                        || transport.GetType().FullName?.IndexOf("FizzySteam", StringComparison.OrdinalIgnoreCase) >= 0;

            return new CheckResult
            {
                label = label,
                passed = isFizzy,
                detail = isFizzy ? "" : $"Transport is {transport.GetType().Name}, expected FizzyFacepunch"
            };
        }
    }
}
#endif
