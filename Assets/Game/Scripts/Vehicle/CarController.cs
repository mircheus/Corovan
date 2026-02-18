using UnityEngine;
using Mirror;

namespace FPSGame.Vehicle
{
    public class CarController : NetworkBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 15f;
        [SerializeField] private float strafeSpeed = 10f;
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float deceleration = 12f;

        [Header("Driver")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private float interactDistance = 4f;

        [SyncVar(hook = nameof(OnDriverChanged))]
        private uint driverNetId;

        private Rigidbody rb;
        private Vector2 moveInput;
        private Vector3 currentVelocity;

        public bool HasDriver => driverNetId != 0;
        public Transform DriverSeat => driverSeat;
        public Transform ExitPoint => exitPoint;
        public float InteractDistance => interactDistance;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            if (!HasDriver || !isOwned)
                return;

            Vector3 targetVelocity = transform.forward * (moveInput.y * moveSpeed)
                                   + transform.right * (moveInput.x * strafeSpeed);

            float rate = targetVelocity.sqrMagnitude > currentVelocity.sqrMagnitude
                ? acceleration
                : deceleration;

            currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, rate * Time.fixedDeltaTime);

            if (currentVelocity.sqrMagnitude > 0.001f)
                rb.MovePosition(rb.position + currentVelocity * Time.fixedDeltaTime);
        }

        public void SetMoveInput(Vector2 input)
        {
            moveInput = input;
        }

        [Server]
        public void ServerEnter(NetworkConnectionToClient conn, NetworkIdentity player)
        {
            if (HasDriver)
            {
                return;
            }

            netIdentity.AssignClientAuthority(conn);
            driverNetId = player.netId;
        }

        [Server]
        public void ServerExit()
        {
            if (!HasDriver)
                return;

            uint oldDriverId = driverNetId;
            driverNetId = 0;
            moveInput = Vector2.zero;
            currentVelocity = Vector3.zero;
            netIdentity.RemoveClientAuthority();
        }

        private void OnDriverChanged(uint oldId, uint newId)
        {
            if (newId != 0)
            {
                // Driver entered
                if (NetworkClient.spawned.TryGetValue(newId, out NetworkIdentity driverIdentity))
                {
                    SetDriverVisible(driverIdentity.gameObject, false);
                }
            }
            else if (oldId != 0)
            {
                // Driver exited
                if (NetworkClient.spawned.TryGetValue(oldId, out NetworkIdentity driverIdentity))
                {
                    SetDriverVisible(driverIdentity.gameObject, true);
                    if (exitPoint != null)
                    {
                        driverIdentity.transform.position = exitPoint.position;
                        driverIdentity.transform.rotation = exitPoint.rotation;
                    }
                    else
                    {
                        driverIdentity.transform.position = transform.position + transform.right * 2.5f;
                    }
                }
            }
        }

        private void SetDriverVisible(GameObject driver, bool visible)
        {
            var characterController = driver.GetComponent<CharacterController>();
            if (characterController != null)
                characterController.enabled = visible;

            var playerController = driver.GetComponent<FPSGame.Player.PlayerController>();
            if (playerController != null)
                playerController.SetVehicleMode(!visible);

            if (!visible)
            {
                // Parent driver to car so they move with it
                driver.transform.SetParent(driverSeat != null ? driverSeat : transform);
                driver.transform.localPosition = Vector3.zero;
                driver.transform.localRotation = Quaternion.identity;
            }
            else
            {
                // Unparent driver from car
                driver.transform.SetParent(null);
            }

            // Hide/show renderers on driver, but keep weapon visible
            var shooting = driver.GetComponent<FPSGame.Player.PlayerShooting>();
            Transform weaponHolder = shooting != null ? shooting.WeaponHolder : null;

            foreach (var renderer in driver.GetComponentsInChildren<Renderer>())
            {
                if (!visible && weaponHolder != null && renderer.transform.IsChildOf(weaponHolder))
                    continue;

                renderer.enabled = visible;
            }
        }
    }
}
