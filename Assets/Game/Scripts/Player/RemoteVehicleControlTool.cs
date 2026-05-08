using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    // Player-side tool that remotely commands a CarController to drive to a chosen world point.
    // First fire on a car: binds that car. Subsequent fires: command the bound car to autopilot to the
    // hit point along an L-shaped path (X-axis first, then Z-axis). Cooldown gates all fires.
    // Component is enabled only when this slot is active in PlayerShooting.weaponTools and isOwned.
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

        private CarController boundCar;
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

            if (!RaycastAim(out RaycastHit hit)) return;

            CarController hitCar = hit.collider.GetComponentInParent<CarController>();
            if (hitCar != null)
            {
                if (hitCar.HasDriver || !hitCar.VehicleIsAlive)
                {
                    Debug.Log("[RemoteVehicleTool] Bind refused: car has driver or is destroyed.");
                    return;
                }

                boundCar = hitCar;
                cooldownRemaining = cooldownSeconds;
                Debug.Log($"[RemoteVehicleTool] Bound to car netId={hitCar.netId}.");
                return;
            }

            if (boundCar == null) return;

            if (!boundCar.VehicleIsAlive)
            {
                Debug.Log("[RemoteVehicleTool] Bound car is destroyed; clearing binding.");
                boundCar = null;
                return;
            }

            if (boundCar.HasDriver)
            {
                Debug.Log("[RemoteVehicleTool] Bound car now has a driver; command refused.");
                return;
            }

            CmdCommandAutopilot(boundCar.netId, hit.point);
            cooldownRemaining = cooldownSeconds;
        }

        [Command]
        private void CmdCommandAutopilot(uint carNetId, Vector3 worldPoint)
        {
            if (!NetworkServer.spawned.TryGetValue(carNetId, out NetworkIdentity identity))
                return;

            CarController car = identity.GetComponent<CarController>();
            if (car == null) return;
            if (car.HasDriver) return;
            if (!car.VehicleIsAlive) return;

            car.ServerStartAutopilot(worldPoint);
        }

        private bool RaycastAim(out RaycastHit hit)
        {
            Ray r = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return Physics.Raycast(r, out hit, maxRange, aimMask, QueryTriggerInteraction.Ignore);
        }

        private void UpdatePathPreview()
        {
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

            // Suppress preview when the cursor is on the car itself (bind-style aim).
            if (hit.collider.GetComponentInParent<CarController>() != null)
            {
                DestroyPathLine();
                return;
            }

            EnsurePathLine();

            Vector3 carPos = boundCar.transform.position;
            Vector3 corner = new Vector3(hit.point.x, carPos.y, carPos.z);
            Vector3 end = new Vector3(hit.point.x, carPos.y, hit.point.z);
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
