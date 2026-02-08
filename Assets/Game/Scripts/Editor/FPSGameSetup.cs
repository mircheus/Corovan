#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using Mirror;
using FPSGame.Player;
using FPSGame.Weapons;
using kcp2k;

namespace FPSGame.Editor
{
    public static class FPSGameSetup
    {
        [MenuItem("FPS Game/Setup/Create Player Prefab")]
        public static void CreatePlayerPrefab()
        {
            // Create player root
            GameObject player = new GameObject("Player");
            player.layer = LayerMask.NameToLayer("Player");
            player.tag = "Player";

            // Add NetworkIdentity first (required for Mirror)
            NetworkIdentity networkIdentity = player.AddComponent<NetworkIdentity>();

            // Add NetworkTransform for position/rotation sync
            NetworkTransformUnreliable networkTransform = player.AddComponent<NetworkTransformUnreliable>();
            networkTransform.syncDirection = SyncDirection.ClientToServer;

            // Add CharacterController
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2.0f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0, 1, 0);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;

            // Create camera holder
            GameObject cameraHolder = new GameObject("CameraHolder");
            cameraHolder.transform.SetParent(player.transform);
            cameraHolder.transform.localPosition = new Vector3(0, 1.6f, 0);

            // Create main camera
            GameObject cameraGO = new GameObject("PlayerCamera");
            cameraGO.transform.SetParent(cameraHolder.transform);
            cameraGO.transform.localPosition = Vector3.zero;

            Camera cam = cameraGO.AddComponent<Camera>();
            cam.fieldOfView = 90;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.enabled = false; // Disabled by default, enabled for local player

            AudioListener audioListener = cameraGO.AddComponent<AudioListener>();
            audioListener.enabled = false; // Disabled by default, enabled for local player

            // Create weapon holder
            GameObject weaponHolder = new GameObject("WeaponHolder");
            weaponHolder.transform.SetParent(cameraGO.transform);
            weaponHolder.transform.localPosition = new Vector3(0.2f, -0.2f, 0.5f);

            // Add player scripts
            player.AddComponent<PlayerController>();

            PlayerCamera playerCamera = cameraHolder.AddComponent<PlayerCamera>();
            SerializedObject pcSO = new SerializedObject(playerCamera);
            pcSO.FindProperty("playerBody").objectReferenceValue = player.transform;
            pcSO.FindProperty("playerCamera").objectReferenceValue = cam;
            pcSO.FindProperty("audioListener").objectReferenceValue = audioListener;
            pcSO.ApplyModifiedProperties();

            player.AddComponent<PlayerHealth>();

            PlayerShooting playerShooting = player.AddComponent<PlayerShooting>();
            SerializedObject psSO = new SerializedObject(playerShooting);
            psSO.FindProperty("playerCamera").objectReferenceValue = playerCamera;
            psSO.FindProperty("weaponHolder").objectReferenceValue = weaponHolder.transform;
            psSO.ApplyModifiedProperties();

            player.AddComponent<PlayerInputHandler>();

            // Add PlayerInput component
            PlayerInput playerInput = player.AddComponent<PlayerInput>();
            playerInput.enabled = false; // Disabled by default, enabled for local player

            // Try to find and assign the input actions asset
            string[] guids = AssetDatabase.FindAssets("InputSystem_Actions t:InputActionAsset");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (inputActions != null)
                {
                    playerInput.actions = inputActions;
                    playerInput.defaultActionMap = "Player";
                }
            }

            // Save as prefab
            string prefabPath = "Assets/Game/Prefabs/Player/Player.prefab";
            EnsureDirectoryExists("Assets/Game/Prefabs/Player");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
            Object.DestroyImmediate(player);

            Selection.activeObject = prefab;
            Debug.Log($"Player prefab created at: {prefabPath}");
        }

        [MenuItem("FPS Game/Setup/Create Pistol WeaponData")]
        public static void CreatePistolWeaponData()
        {
            WeaponData pistol = ScriptableObject.CreateInstance<WeaponData>();

            SerializedObject so = new SerializedObject(pistol);
            so.FindProperty("weaponName").stringValue = "Pistol";
            so.FindProperty("isAutomatic").boolValue = false;
            so.FindProperty("fireRate").floatValue = 0.2f;
            so.FindProperty("damage").floatValue = 25f;
            so.FindProperty("range").floatValue = 100f;
            so.FindProperty("baseSpread").floatValue = 0.02f;
            so.FindProperty("adsSpreadMultiplier").floatValue = 0.3f;
            so.FindProperty("movementSpreadMultiplier").floatValue = 1.5f;
            so.FindProperty("magazineSize").intValue = 12;
            so.FindProperty("maxReserveAmmo").intValue = 120;
            so.FindProperty("reloadTime").floatValue = 1.5f;
            so.FindProperty("recoilMultiplier").floatValue = 1f;
            so.FindProperty("adsRecoilMultiplier").floatValue = 0.7f;
            so.FindProperty("adsTime").floatValue = 0.2f;

            // Set recoil pattern
            SerializedProperty recoilArray = so.FindProperty("recoilPattern");
            recoilArray.arraySize = 5;
            recoilArray.GetArrayElementAtIndex(0).vector2Value = new Vector2(0, 1.5f);
            recoilArray.GetArrayElementAtIndex(1).vector2Value = new Vector2(0.2f, 1.3f);
            recoilArray.GetArrayElementAtIndex(2).vector2Value = new Vector2(-0.3f, 1.4f);
            recoilArray.GetArrayElementAtIndex(3).vector2Value = new Vector2(0.1f, 1.2f);
            recoilArray.GetArrayElementAtIndex(4).vector2Value = new Vector2(-0.2f, 1.5f);

            so.ApplyModifiedProperties();

            string assetPath = "Assets/Game/ScriptableObjects/Pistol.asset";
            EnsureDirectoryExists("Assets/Game/ScriptableObjects");

            AssetDatabase.CreateAsset(pistol, assetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = pistol;
            Debug.Log($"Pistol WeaponData created at: {assetPath}");
        }

        [MenuItem("FPS Game/Setup/Create Test Scene")]
        public static void CreateTestScene()
        {
            // Create ground plane
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(10, 1, 10);
            ground.layer = LayerMask.NameToLayer("Default");

            // Create some obstacle cubes
            for (int i = 0; i < 5; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Obstacle_{i}";
                cube.transform.position = new Vector3(
                    Random.Range(-20f, 20f),
                    0.5f,
                    Random.Range(-20f, 20f)
                );
                cube.transform.localScale = new Vector3(
                    Random.Range(1f, 3f),
                    Random.Range(1f, 4f),
                    Random.Range(1f, 3f)
                );
            }

            // Create ramp for slope testing
            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            ramp.transform.position = new Vector3(10, 1, 0);
            ramp.transform.localScale = new Vector3(5, 0.5f, 5);
            ramp.transform.rotation = Quaternion.Euler(0, 0, 30);

            // Create directional light
            GameObject lightGO = new GameObject("Directional Light");
            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Create spawn points
            for (int i = 0; i < 4; i++)
            {
                GameObject spawnPoint = new GameObject($"SpawnPoint_{i}");
                float angle = i * 90f * Mathf.Deg2Rad;
                spawnPoint.transform.position = new Vector3(Mathf.Cos(angle) * 15f, 1f, Mathf.Sin(angle) * 15f);
                spawnPoint.transform.LookAt(Vector3.zero);
            }

            Debug.Log("Test scene objects created!");
            Debug.Log("To test multiplayer: Add CustomNetworkManager to an empty GameObject and assign the Player prefab.");
        }

        [MenuItem("FPS Game/Setup/Create Network Manager")]
        public static void CreateNetworkManager()
        {
            // Check if NetworkManager already exists
            if (Object.FindObjectOfType<NetworkManager>() != null)
            {
                Debug.LogWarning("NetworkManager already exists in the scene!");
                return;
            }

            GameObject networkManagerGO = new GameObject("NetworkManager");
            FPSGame.Networking.CustomNetworkManager networkManager = networkManagerGO.AddComponent<FPSGame.Networking.CustomNetworkManager>();

            // Add KCP Transport
            kcp2k.KcpTransport transport = networkManagerGO.AddComponent<kcp2k.KcpTransport>();

            // Add NetworkManagerHUD for Host/Join buttons
            networkManagerGO.AddComponent<NetworkManagerHUD>();

            // Configure NetworkManager
            SerializedObject so = new SerializedObject(networkManager);
            so.FindProperty("transport").objectReferenceValue = transport;

            // Try to find and assign player prefab
            string prefabPath = "Assets/Game/Prefabs/Player/Player.prefab";
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (playerPrefab != null)
            {
                so.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            }
            else
            {
                Debug.LogWarning("Player prefab not found. Please assign it manually in the NetworkManager.");
            }

            // Find spawn points
            GameObject[] spawnPointObjects = GameObject.FindGameObjectsWithTag("Respawn");
            if (spawnPointObjects.Length == 0)
            {
                // Try to find by name
                Transform[] allTransforms = Object.FindObjectsOfType<Transform>();
                System.Collections.Generic.List<Transform> spawnPoints = new System.Collections.Generic.List<Transform>();
                foreach (Transform t in allTransforms)
                {
                    if (t.name.StartsWith("SpawnPoint"))
                    {
                        spawnPoints.Add(t);
                    }
                }

                if (spawnPoints.Count > 0)
                {
                    SerializedProperty spawnPointsProp = so.FindProperty("spawnPoints");
                    spawnPointsProp.arraySize = spawnPoints.Count;
                    for (int i = 0; i < spawnPoints.Count; i++)
                    {
                        spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
                    }
                }
            }

            so.ApplyModifiedProperties();

            Selection.activeObject = networkManagerGO;
            Debug.Log("NetworkManager created! Assign spawn points if needed.");
        }

        [MenuItem("FPS Game/Setup/Setup All (Player + Weapon + Scene + Network)")]
        public static void SetupAll()
        {
            CreatePistolWeaponData();
            CreatePlayerPrefab();
            CreateTestScene();
            CreateNetworkManager();
            Debug.Log("Full FPS Game setup complete!");
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] folders = path.Split('/');
                string currentPath = folders[0];

                for (int i = 1; i < folders.Length; i++)
                {
                    string newPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = newPath;
                }
            }
        }
    }
}
#endif
