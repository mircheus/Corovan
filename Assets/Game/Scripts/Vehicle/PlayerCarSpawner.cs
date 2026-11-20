using FPSGame.Environment;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Call-a-car panel: completing the hold on the <see cref="InteractableObject"/> on this object
    /// spawns <see cref="carPrefab"/> at <see cref="spawnPoint"/> — but only while there is no player car
    /// on the map (exploded wrecks don't count, and are removed on the call with <see cref="removeWrecksOnSpawn"/>)
    /// and the spawn area is clear. Otherwise the button is blocked and players see why (warning prompt +
    /// red lamp). Hold duration / range / prompt live on the InteractableObject.
    ///
    /// Server polls the state every <see cref="checkInterval"/> and re-checks on the press itself
    /// (two players finishing the hold in the same frame still get one car).
    /// The car prefab must be in CustomNetworkManager.runtimeSpawnPrefabs.
    /// </summary>
    [RequireComponent(typeof(InteractableObject))]
    public class PlayerCarSpawner : NetworkBehaviour
    {
        [Tooltip("Player car to spawn (CarController + NetworkIdentity). Register it in NetworkManager runtimeSpawnPrefabs.")]
        [SerializeField] private GameObject carPrefab;

        [Tooltip("Where the car appears (position + rotation of the car's pivot). Empty = this object.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("How often the server re-checks whether a car may be spawned (seconds).")]
        [SerializeField] private float checkInterval = 0.5f;

        [Tooltip("When a new car is called, remove every exploded player car (wreck) from the map first: " +
                 "occupants are put out, stowed loot and the safe are let go.")]
        [SerializeField] private bool removeWrecksOnSpawn = true;

        [Header("Spawn area")]
        [Tooltip("Box that must be free of players and solid colliders, in spawn point space. " +
                 "Its bottom sits Ground Clearance above the spawn point. Zero size = no check.")]
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(2.6f, 1.8f, 5f);

        [Tooltip("Bottom of the area box above the spawn point, so the floor isn't counted as an obstacle.")]
        [SerializeField] private float groundClearance = 0.3f;

        [Tooltip("Colliders on these layers block the spawn (a wreck, loot, an enemy). Triggers are ignored. " +
                 "Players are checked separately, by position.")]
        [SerializeField] private LayerMask obstructionLayers = ~0;

        [Header("Messages")]
        [SerializeField] private string carExistsText = "Машина уже на карте";
        [SerializeField] private string areaBlockedText = "Место для машины занято";
        [SerializeField] private string noPrefabText = "Нет машины для вызова";

        [Header("Lamp")]
        [Tooltip("Renderers tinted by state through a MaterialPropertyBlock (_BaseColor/_Color, _EmissionColor).")]
        [SerializeField] private Renderer[] lampRenderers;
        [SerializeField] private Color readyColor = new Color(0.2f, 1f, 0.3f);
        [SerializeField] private Color blockedColor = new Color(1f, 0.15f, 0.1f);
        [SerializeField] private float emissionIntensity = 2f;

        [Space]
        [Tooltip("Fires on every peer when a car has been spawned (sound, light, etc.).")]
        [SerializeField] private UnityEvent onCarSpawned;

        private const float PlayerRadius = 0.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Collider[] overlapBuffer = new Collider[16];
        private InteractableObject interactable;
        private MaterialPropertyBlock block;
        private float nextCheckTime;
        private int shownLampState = -1;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

        private void Awake()
        {
            interactable = GetComponent<InteractableObject>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            interactable.OnInteracted += HandleInteracted;

            if (carPrefab == null || !carPrefab.TryGetComponent(out CarController _) ||
                !carPrefab.TryGetComponent(out NetworkIdentity _))
                Debug.LogWarning($"[PlayerCarSpawner] '{name}': car prefab is missing or has no CarController/NetworkIdentity.", this);

            ServerRefresh();
        }

        public override void OnStopServer()
        {
            interactable.OnInteracted -= HandleInteracted;
            base.OnStopServer();
        }

        private void Update()
        {
            if (isServer && Time.time >= nextCheckTime)
                ServerRefresh();

            UpdateLamp();
        }

        [Server]
        private void ServerRefresh()
        {
            nextCheckTime = Time.time + checkInterval;
            interactable.ServerSetBlocked(ServerBlockReason());
        }

        // Null = a car may be spawned now.
        [Server]
        private string ServerBlockReason()
        {
            if (carPrefab == null)
                return noPrefabText;
            if (AnyPlayerCarPresent())
                return carExistsText;
            if (!SpawnAreaClear())
                return areaBlockedText;
            return null;
        }

        [Server]
        private void HandleInteracted(GameObject instigator)
        {
            // The poll may be up to checkInterval old — decide on the current state.
            string reason = ServerBlockReason();
            if (reason != null)
            {
                interactable.ServerSetBlocked(reason);
                return;
            }

            if (removeWrecksOnSpawn)
                ServerRemoveWrecks();

            Transform point = SpawnPoint;
            GameObject car = Instantiate(carPrefab, point.position, point.rotation);
            NetworkServer.Spawn(car);
            Debug.Log($"[PlayerCarSpawner] '{name}': spawned '{car.name}' for {(instigator != null ? instigator.name : "?")}.", this);

            // The new car blocks the button right away; no waiting for the next poll.
            ServerRefresh();
            RpcCarSpawned();
        }

        [ClientRpc]
        private void RpcCarSpawned()
        {
            onCarSpawned?.Invoke();
        }

        // Any player car that isn't a wreck counts, wherever it is (scene car, an earlier spawn, a remote-
        // controlled one) — a burning one too, until it explodes (its driver may still be inside).
        private static bool AnyPlayerCarPresent()
        {
            foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (!car.IsWreck)
                    return true;
            }
            return false;
        }

        [Server]
        private static void ServerRemoveWrecks()
        {
            foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (car.IsWreck)
                    car.ServerRemoveWreck();
            }
        }

        private bool SpawnAreaClear()
        {
            if (spawnAreaSize.x <= 0f || spawnAreaSize.y <= 0f || spawnAreaSize.z <= 0f)
                return true;

            Transform point = SpawnPoint;
            Vector3 half = spawnAreaSize * 0.5f;
            Vector3 center = AreaCenter(point);

            // Players by position: on the server other players' copies have their CharacterController
            // off, so no physics query would see them.
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn?.identity == null)
                    continue;
                Vector3 local = point.InverseTransformPoint(conn.identity.transform.position);
                local.y -= groundClearance + half.y;
                if (Mathf.Abs(local.x) <= half.x + PlayerRadius &&
                    Mathf.Abs(local.z) <= half.z + PlayerRadius &&
                    local.y <= half.y && local.y >= -half.y - 2f) // feet up to 2 m below the box bottom
                    return false;
            }

            int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, point.rotation,
                obstructionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit != null && !hit.transform.IsChildOf(transform))
                    return false;
            }
            return true;
        }

        private Vector3 AreaCenter(Transform point)
        {
            return point.position + point.up * (groundClearance + spawnAreaSize.y * 0.5f);
        }

        private void UpdateLamp()
        {
            if (lampRenderers == null || lampRenderers.Length == 0)
                return;

            int state = interactable.IsBlocked ? 1 : 0;
            if (state == shownLampState)
                return;
            shownLampState = state;

            block ??= new MaterialPropertyBlock();
            Color color = state == 1 ? blockedColor : readyColor;
            foreach (Renderer lamp in lampRenderers)
            {
                if (lamp == null)
                    continue;
                lamp.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                block.SetColor(EmissionColorId, color * emissionIntensity);
                lamp.SetPropertyBlock(block);
            }
        }

        private void OnDrawGizmos()
        {
            Transform point = SpawnPoint;
            if (point == null)
                return;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawLine(transform.position, point.position);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(AreaCenter(point), point.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, spawnAreaSize);
            // Arrow along the car's forward.
            Vector3 front = Vector3.forward * (spawnAreaSize.z * 0.5f);
            Gizmos.DrawLine(front, front + new Vector3(-0.5f, 0f, -0.8f));
            Gizmos.DrawLine(front, front + new Vector3(0.5f, 0f, -0.8f));
            Gizmos.matrix = old;
        }
    }
}
