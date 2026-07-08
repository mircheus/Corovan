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

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        private void OnDisable()
        {
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
            if (cam == null || Mouse.current == null) return;

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
                DestroyPathLine();
                return;
            }

            if (!RaycastAim(out RaycastHit hit))
            {
                DestroyPathLine();
                return;
            }

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
        }

        private void DestroyPathLine()
        {
            if (activePathLine != null)
                Destroy(activePathLine.gameObject);
            activePathLine = null;
        }
    }
}
