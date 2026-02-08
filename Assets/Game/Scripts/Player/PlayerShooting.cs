using UnityEngine;
using Mirror;
using FPSGame.Weapons;

namespace FPSGame.Player
{
    public class PlayerShooting : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private WeaponData startingWeapon;
        [SerializeField] private Transform weaponHolder;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        private WeaponData currentWeapon;

        [SyncVar(hook = nameof(OnAmmoChanged))]
        private int currentAmmo;

        [SyncVar]
        private int reserveAmmo;

        [SyncVar]
        private bool isReloading;

        private float lastFireTime;
        private int shotsFired;
        private bool fireHeld;
        private bool isAiming;
        private float reloadTimer;

        public WeaponData CurrentWeapon => currentWeapon;
        public int CurrentAmmo => currentAmmo;
        public int ReserveAmmo => reserveAmmo;
        public bool IsReloading => isReloading;
        public bool IsAiming => isAiming;

        public event System.Action<int, int> OnAmmoChangedEvent;
        public event System.Action OnWeaponFired;
        public event System.Action OnReloadStarted;
        public event System.Action OnReloadFinished;

        private void Awake()
        {
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<PlayerCamera>();

            if (playerController == null)
                playerController = GetComponent<PlayerController>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            if (startingWeapon != null)
            {
                currentWeapon = startingWeapon;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (startingWeapon != null)
            {
                currentWeapon = startingWeapon;
                currentAmmo = startingWeapon.MagazineSize;
                reserveAmmo = startingWeapon.MaxReserveAmmo;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (startingWeapon != null)
            {
                currentWeapon = startingWeapon;
            }
            OnAmmoChangedEvent?.Invoke(currentAmmo, reserveAmmo);
        }

        private void Update()
        {
            if (!isOwned)
                return;

            HandleReloading();
            HandleFiring();
        }

        private void OnAmmoChanged(int oldAmmo, int newAmmo)
        {
            OnAmmoChangedEvent?.Invoke(newAmmo, reserveAmmo);
        }

        private void HandleFiring()
        {
            if (currentWeapon == null || isReloading)
                return;

            bool canFire = Time.time >= lastFireTime + currentWeapon.FireRate;

            if (fireHeld && canFire)
            {
                if (currentWeapon.IsAutomatic || shotsFired == 0)
                {
                    TryFire();
                }
            }

            if (!fireHeld)
            {
                shotsFired = 0;
            }
        }

        private void TryFire()
        {
            if (currentAmmo <= 0)
            {
                if (reserveAmmo > 0)
                {
                    CmdStartReload();
                }
                return;
            }

            Fire();
        }

        private void Fire()
        {
            lastFireTime = Time.time;

            Ray aimRay = playerCamera.GetAimRay();
            Vector3 direction = ApplySpread(aimRay.direction);

            // Send shot to server
            CmdFire(aimRay.origin, direction);

            // Apply recoil locally for responsive feel
            Vector2 recoil = GetCurrentRecoil();
            playerCamera.AddRecoil(recoil);

            shotsFired++;

            OnWeaponFired?.Invoke();

            if (showDebugRays)
            {
                Debug.DrawRay(aimRay.origin, direction * currentWeapon.Range, Color.red, 0.5f);
            }
        }

        [Command]
        private void CmdFire(Vector3 origin, Vector3 direction)
        {
            if (currentWeapon == null || currentAmmo <= 0 || isReloading)
                return;

            currentAmmo--;

            // Server performs hit detection
            if (Physics.Raycast(origin, direction, out RaycastHit hit, currentWeapon.Range, hitMask, QueryTriggerInteraction.Ignore))
            {
                PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();

                if (targetHealth != null && targetHealth.gameObject != gameObject)
                {
                    targetHealth.TakeDamage(currentWeapon.Damage, hit.point, -hit.normal);
                }

                // Notify all clients of hit effect
                RpcOnHit(hit.point, hit.normal);
            }

            // Notify all clients of shot fired
            RpcOnFire();
        }

        [ClientRpc]
        private void RpcOnFire()
        {
            // Play muzzle flash, sound effects for all clients
            if (!isOwned)
            {
                OnWeaponFired?.Invoke();
            }
        }

        [ClientRpc]
        private void RpcOnHit(Vector3 hitPoint, Vector3 hitNormal)
        {
            // Spawn impact effect at hit point
            if (currentWeapon != null && currentWeapon.ImpactEffectPrefab != null)
            {
                Instantiate(currentWeapon.ImpactEffectPrefab, hitPoint, Quaternion.LookRotation(hitNormal));
            }
        }

        private Vector3 ApplySpread(Vector3 direction)
        {
            float spread = currentWeapon.BaseSpread;

            if (isAiming)
                spread *= currentWeapon.AdsSpreadMultiplier;

            if (playerController != null && playerController.Velocity.sqrMagnitude > 0.1f)
                spread *= currentWeapon.MovementSpreadMultiplier;

            float spreadX = Random.Range(-spread, spread);
            float spreadY = Random.Range(-spread, spread);

            Quaternion spreadRotation = Quaternion.Euler(spreadY, spreadX, 0);
            return spreadRotation * direction;
        }

        private Vector2 GetCurrentRecoil()
        {
            Vector2 recoil = currentWeapon.GetRecoilAtIndex(shotsFired);

            if (isAiming)
                recoil *= currentWeapon.AdsRecoilMultiplier;

            return recoil;
        }

        private void HandleReloading()
        {
            if (!isReloading)
                return;

            reloadTimer -= Time.deltaTime;

            if (reloadTimer <= 0)
            {
                reloadTimer = 0;
            }
        }

        public void StartReload()
        {
            if (!isOwned)
                return;

            CmdStartReload();
        }

        [Command]
        private void CmdStartReload()
        {
            if (isReloading || currentAmmo >= currentWeapon.MagazineSize || reserveAmmo <= 0)
                return;

            isReloading = true;
            RpcOnReloadStarted();

            // Use Invoke to finish reload after delay
            Invoke(nameof(ServerFinishReload), currentWeapon.ReloadTime);
        }

        [Server]
        private void ServerFinishReload()
        {
            int ammoNeeded = currentWeapon.MagazineSize - currentAmmo;
            int ammoToReload = Mathf.Min(ammoNeeded, reserveAmmo);

            currentAmmo += ammoToReload;
            reserveAmmo -= ammoToReload;
            isReloading = false;

            RpcOnReloadFinished();
        }

        [ClientRpc]
        private void RpcOnReloadStarted()
        {
            reloadTimer = currentWeapon.ReloadTime;
            OnReloadStarted?.Invoke();
        }

        [ClientRpc]
        private void RpcOnReloadFinished()
        {
            OnReloadFinished?.Invoke();
            OnAmmoChangedEvent?.Invoke(currentAmmo, reserveAmmo);
        }

        public void SetFiring(bool firing)
        {
            if (!isOwned)
                return;

            fireHeld = firing;
            if (!firing && currentWeapon != null && !currentWeapon.IsAutomatic)
            {
                shotsFired = 0;
            }
        }

        public void SetAiming(bool aiming)
        {
            if (!isOwned)
                return;

            isAiming = aiming;
            playerCamera?.SetAiming(aiming);
        }

        [Server]
        public void AddAmmo(int amount)
        {
            reserveAmmo = Mathf.Min(reserveAmmo + amount, currentWeapon.MaxReserveAmmo);
        }
    }
}
