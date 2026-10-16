using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    // Player-side tool that remotely commands a CarController to drive to a chosen world point.
    // The bound car is auto-assigned: whichever vehicle this player most recently drove becomes
    // the target. LMB commands the bound car along an L-shaped path (world X first, then Z).
    // Cooldown gates fires. Component is enabled only when this slot is active in
    // PlayerShooting.weaponTools and isOwned.
    public class RemoteVehicleControlTool : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera cam;
        [SerializeField] private LineRenderer pathLineTemplate;

        [Header("Aiming")]
        [SerializeField] private LayerMask aimMask = ~0;
        [SerializeField] private float maxRange = 200f;
        [SerializeField] private float lineYOffset = 0.05f;

        [Header("Behavior")]
        [SerializeField] private float cooldownSeconds = 0.5f;

        [SyncVar] private uint boundCarNetId;

        private float cooldownRemaining;
        private LineRenderer activePathLine;

        // DIAGNOSTIC: remembers the last logged preview reason so we log only on change (no per-frame spam).
        private string lastPreviewReason;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        // DIAGNOSTIC: component-enabled lifecycle. If the line stops after a vehicle enter/exit and
        // you see an OnDisable here WITHOUT a matching OnEnable, the tool slot was never re-activated
        // (ActivateWeaponTool not called) — hence Update() stops running and no preview is drawn.
        private void OnEnable()
        {
            Debug.Log($"[RemoteVehicleTool] OnEnable  (component enabled).  isOwned={isOwned}, boundCarNetId={boundCarNetId}", this);
        }

        private void OnDisable()
        {
            Debug.Log($"[RemoteVehicleTool] OnDisable (component disabled). isOwned={isOwned}, boundCarNetId={boundCarNetId}", this);
            lastPreviewReason = null; // so the first preview outcome after re-enable always logs
            DestroyPathLine();
        }

        // Called by CarController.ServerEnter when this player takes a driver seat.
        [Server]
        public void ServerSetBoundCar(uint netId)
        {
            boundCarNetId = netId;
        }

        private CarController GetBoundCar()
        {
            if (boundCarNetId == 0) return null;
            if (!NetworkClient.spawned.TryGetValue(boundCarNetId, out NetworkIdentity ni)) return null;
            return ni != null ? ni.GetComponent<CarController>() : null;
        }

        private void Update()
        {
            if (!isOwned) return;
            if (cam == null || Mouse.current == null)
            {
                // DIAGNOSTIC: without a camera or mouse device the whole tool is inert.
                DiagPreview($"aborted: cam={(cam == null ? "NULL" : "ok")}, mouse={(Mouse.current == null ? "NULL" : "ok")}");
                return;
            }

            if (cooldownRemaining > 0f)
                cooldownRemaining -= Time.deltaTime;

            UpdatePathPreview();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                TryFire();
        }

        private void TryFire()
        {
            if (cooldownRemaining > 0f) return;

            CarController boundCar = GetBoundCar();
            if (boundCar == null)
            {
                Debug.Log("[RemoteVehicleTool] No bound car — drive a vehicle first.");
                return;
            }

            if (!boundCar.VehicleIsAlive)
            {
                Debug.Log("[RemoteVehicleTool] Bound car is destroyed.");
                return;
            }

            if (boundCar.HasDriver)
            {
                Debug.Log("[RemoteVehicleTool] Bound car has a driver; command refused.");
                return;
            }

            if (!RaycastAim(out RaycastHit hit)) return;

            // Soft-clamp instead of rejecting: a target "ahead" of the player (higher Z) is pulled
            // back to the player's Z line. Avoids pixel-hunting for a valid spot below the player.
            Vector3 target = hit.point;
            target.z = Mathf.Min(target.z, transform.position.z);

            CmdCommandAutopilot(boundCar.netId, target);
            cooldownRemaining = cooldownSeconds;
        }

        [Command]
        private void CmdCommandAutopilot(uint carNetId, Vector3 worldPoint)
        {
            if (carNetId != boundCarNetId) return;
            if (!NetworkServer.spawned.TryGetValue(carNetId, out NetworkIdentity identity))
                return;

            CarController car = identity.GetComponent<CarController>();
            if (car == null) return;
            if (car.HasDriver) return;
            if (!car.VehicleIsAlive) return;

            // Authoritative soft-clamp: pull targets ahead of the player back to the player's Z line
            // instead of rejecting them (mirrors the client-side clamp in TryFire).
            worldPoint.z = Mathf.Min(worldPoint.z, transform.position.z);

            car.ServerStartAutopilot(worldPoint);
        }

        private bool RaycastAim(out RaycastHit hit)
        {
            Ray r = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return Physics.Raycast(r, out hit, maxRange, aimMask, QueryTriggerInteraction.Ignore);
        }

        private void UpdatePathPreview()
        {
            CarController boundCar = GetBoundCar();
            if (boundCar == null || pathLineTemplate == null)
            {
                // DIAGNOSTIC: most common causes — no car driven yet (boundCarNetId==0) or the
                // pathLineTemplate reference is missing on Player.prefab.
                DiagPreview($"no line: boundCar={(boundCar == null ? $"NULL (boundCarNetId={boundCarNetId})" : "ok")}, template={(pathLineTemplate == null ? "NULL" : "ok")}");
                DestroyPathLine();
                return;
            }

            if (!RaycastAim(out RaycastHit hit))
            {
                // DIAGNOSTIC: aim ray hit nothing within maxRange on aimMask (aiming at sky/void, or mask excludes the floor layer).
                DiagPreview("no line: aim raycast hit nothing (check aimMask / maxRange / where you're aiming)");
                DestroyPathLine();
                return;
            }

            DiagPreview($"drawing line: car@{boundCar.transform.position}, hit={hit.point} on '{hit.collider.name}'");

            EnsurePathLine();

            Vector3 carPos = boundCar.transform.position;
            // Clamp the drawn destination so the line never extends past the player's Z,
            // matching the summon restriction in TryFire / CmdCommandAutopilot.
            float clampedZ = Mathf.Min(hit.point.z, transform.position.z);
            Vector3 corner = new Vector3(hit.point.x, carPos.y, carPos.z);
            Vector3 end = new Vector3(hit.point.x, carPos.y, clampedZ);
            Vector3 yOff = Vector3.up * lineYOffset;

            activePathLine.SetPosition(0, carPos + yOff);
            activePathLine.SetPosition(1, corner + yOff);
            activePathLine.SetPosition(2, end + yOff);
        }

        private void EnsurePathLine()
        {
            if (activePathLine != null) return;
            activePathLine = Instantiate(pathLineTemplate);
            activePathLine.gameObject.SetActive(true);
            activePathLine.useWorldSpace = true;
            activePathLine.positionCount = 3;

            // DIAGNOSTIC: a null/deleted material renders the line invisible (or magenta) even though it "draws".
            var mat = activePathLine.sharedMaterial;
            // Debug.Log($"[RemoteVehicleTool] Instantiated path line. material={(mat == null ? "NULL (invisible!)" : mat.name)}, width={activePathLine.startWidth}->{activePathLine.endWidth}, enabled={activePathLine.enabled}", activePathLine);
        }

        // DIAGNOSTIC: logs the preview outcome only when the reason string changes, to avoid per-frame console spam.
        private void DiagPreview(string reason)
        {
            if (reason == lastPreviewReason) return;
            lastPreviewReason = reason;
            // Debug.Log($"[RemoteVehicleTool] preview -> {reason}");
        }

        private void DestroyPathLine()
        {
            if (activePathLine != null)
                Destroy(activePathLine.gameObject);
            activePathLine = null;
        }
    }
}
