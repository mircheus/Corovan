using UnityEngine;
using FPSGame.Weapons;

namespace FPSGame.Player
{
    public class PlayerShooting : MonoBehaviour
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
        private int currentAmmo;
        private int reserveAmmo;
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

        public event System.Action<int, int> OnAmmoChanged;
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

        private void Start()
        {
            if (startingWeapon != null)
            {
                EquipWeapon(startingWeapon);
            }
        }

        private void Update()
        {
            HandleReloading();
            HandleFiring();
        }

        public void EquipWeapon(WeaponData weapon)
        {
            currentWeapon = weapon;
            currentAmmo = weapon.MagazineSize;
            reserveAmmo = weapon.MaxReserveAmmo;
            isReloading = false;
            shotsFired = 0;
            lastFireTime = 0;

            OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
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
                    StartReload();
                }
                return;
            }

            Fire();
        }

        private void Fire()
        {
            lastFireTime = Time.time;
            currentAmmo--;

            Ray aimRay = playerCamera.GetAimRay();
            Vector3 direction = ApplySpread(aimRay.direction);

            PerformHitscan(aimRay.origin, direction);

            Vector2 recoil = GetCurrentRecoil();
            playerCamera.AddRecoil(recoil);

            shotsFired++;

            OnWeaponFired?.Invoke();
            OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);

            if (showDebugRays)
            {
                Debug.DrawRay(aimRay.origin, direction * currentWeapon.Range, Color.red, 0.5f);
            }
        }

        private void PerformHitscan(Vector3 origin, Vector3 direction)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, currentWeapon.Range, hitMask, QueryTriggerInteraction.Ignore))
            {
                ProcessHit(hit);

                if (showDebugRays)
                {
                    Debug.DrawLine(origin, hit.point, Color.green, 0.5f);
                }
            }
        }

        private void ProcessHit(RaycastHit hit)
        {
            PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();

            if (targetHealth != null && targetHealth.gameObject != gameObject)
            {
                targetHealth.TakeDamage(currentWeapon.Damage, hit.point, -hit.normal);
                Debug.Log($"Hit {hit.collider.gameObject.name} for {currentWeapon.Damage} damage");
            }
            else
            {
                Debug.Log($"Hit {hit.collider.gameObject.name} at {hit.point}");
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
                FinishReload();
            }
        }

        public void StartReload()
        {
            if (isReloading || currentAmmo >= currentWeapon.MagazineSize || reserveAmmo <= 0)
                return;

            isReloading = true;
            reloadTimer = currentWeapon.ReloadTime;
            shotsFired = 0;

            OnReloadStarted?.Invoke();
        }

        private void FinishReload()
        {
            int ammoNeeded = currentWeapon.MagazineSize - currentAmmo;
            int ammoToReload = Mathf.Min(ammoNeeded, reserveAmmo);

            currentAmmo += ammoToReload;
            reserveAmmo -= ammoToReload;
            isReloading = false;

            OnReloadFinished?.Invoke();
            OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
        }

        public void SetFiring(bool firing)
        {
            fireHeld = firing;
            if (!firing && currentWeapon != null && !currentWeapon.IsAutomatic)
            {
                shotsFired = 0;
            }
        }

        public void SetAiming(bool aiming)
        {
            isAiming = aiming;
            playerCamera?.SetAiming(aiming);
        }
    }
}
