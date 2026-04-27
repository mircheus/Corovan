using UnityEngine;
using Mirror;
using FPSGame.Environment;
using FPSGame.Networking;

namespace FPSGame.Vehicle
{
    public class CarController : NetworkBehaviour
    {
        [Header("References: ")]
        [SerializeField] private Rigidbody rb;
        
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 15f;
        [SerializeField] private float strafeSpeed = 10f;
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float deceleration = 12f;

        [Header("Driver")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private float interactDistance = 4f;

        [Header("Vehicle Health")]
        [SerializeField] private int maxVehicleHealth = 200;

        [Header("Destruction FX")]
        [SerializeField] private GameObject fireEffect;
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionDelay = 2f;
        [SerializeField] private float explosionDuration = 2f;

        [Header("Collision Damage")]
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField] private float minImpactSpeed = 3f;
        [SerializeField] private float damagePerImpactSpeed = 2f;
        [SerializeField] private float collisionDamageCooldown = 0.5f;

        private float lastCollisionDamageTime;

        [SyncVar(hook = nameof(OnDriverChanged))]
        private uint driverNetId;

        [SyncVar]
        private int vehicleHealth;

        [SyncVar]
        private bool vehicleIsAlive = true;

        private Vector2 moveInput;
        private Vector3 currentVelocity;
        private bool isExploded;

        public bool HasDriver => driverNetId != 0;
        public Transform DriverSeat => driverSeat;
        public Transform ExitPoint => exitPoint;
        public float InteractDistance => interactDistance;
        public int VehicleHealth => vehicleHealth;
        public int MaxVehicleHealth => maxVehicleHealth;
        public bool VehicleIsAlive => vehicleIsAlive;

        private void Awake()
        {
            // rb = GetComponent<Rigidbody>();/**/
            rb.freezeRotation = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            vehicleHealth = maxVehicleHealth;
            vehicleIsAlive = true;
        }

        [Server]
        public void ServerDepositLoot(CarryableObject carryable)
        {
            if (carryable == null)
                return;

            int lootValue = carryable.Value;
            NetworkServer.Destroy(carryable.gameObject);

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.AddScore(lootValue);
        }

        [Server]
        public void TakeDamage(int damage)
        {
            Debug.Log($"[CAR] TakeDamage: {damage}");
            if (!vehicleIsAlive || damage <= 0)
                return;

            vehicleHealth = Mathf.Max(0, vehicleHealth - damage);
            Debug.Log($"[CAR] Health: {vehicleHealth}");
            if (vehicleHealth <= 0)
                StartDestruction();
        }

        [Server]
        private void StartDestruction()
        {
            if (!vehicleIsAlive) return;
            vehicleIsAlive = false;
            RpcStartFire();
            Invoke(nameof(ServerExplode), explosionDelay);
        }

        [Server]
        private void ServerExplode()
        {
            if (driverNetId != 0 &&
                NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity driverIdentity))
            {
                var health = driverIdentity.GetComponent<FPSGame.Player.PlayerHealth>();
                if (health != null)
                    health.SetHealth(0);
            }

            RpcPlayExplosion();
        }

        [ClientRpc]
        private void RpcStartFire()
        {
            if (fireEffect != null)
                fireEffect.SetActive(true);
        }

        [ClientRpc]
        private void RpcPlayExplosion()
        {
            if (fireEffect != null)
                fireEffect.SetActive(false);

            isExploded = true;
            currentVelocity = Vector3.zero;

            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;

            if (explosionEffect == null) return;
            explosionEffect.SetActive(true);
            CancelInvoke(nameof(HideExplosion));
            Invoke(nameof(HideExplosion), explosionDuration);
        }

        private void HideExplosion()
        {
            if (explosionEffect != null)
                explosionEffect.SetActive(false);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isOwned || !HasDriver)
                return;

            if ((obstacleLayers.value & (1 << collision.gameObject.layer)) == 0)
                return;
            
            float impactSpeed = currentVelocity.magnitude;
            Debug.Log($"impactSpeed: {impactSpeed}");
            if (impactSpeed < minImpactSpeed)
                return;

            CmdReportCollision(impactSpeed);
        }

        [Command]
        private void CmdReportCollision(float impactSpeed)
        {
            if (Time.time - lastCollisionDamageTime < collisionDamageCooldown)
                return;

            if (impactSpeed < minImpactSpeed)
                return;

            lastCollisionDamageTime = Time.time;
            int damage = Mathf.FloorToInt((impactSpeed - minImpactSpeed) * damagePerImpactSpeed);
            TakeDamage(damage);
        }

        private void FixedUpdate()
        {
            if (!HasDriver || !isOwned || isExploded)
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
