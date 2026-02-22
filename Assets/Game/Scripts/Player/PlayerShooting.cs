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
        [SerializeField] private WeaponData[] availableWeapons;
        [SerializeField] private GameObject[] weaponModels;
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

        [SyncVar(hook = nameof(OnWeaponIndexChanged))]
        private int currentWeaponIndex;

        // Per-slot ammo, server-side only. Saved/restored on weapon switch.
        private int[] savedMagAmmo;
        private int[] savedReserveAmmo;

        private float lastFireTime;
        private int shotsFired;
        private bool fireHeld;
        private bool isAiming;
        private bool isCarrying;
        private float reloadTimer;

        public Transform WeaponHolder => weaponHolder;
        public WeaponData CurrentWeapon => currentWeapon;
        public int CurrentAmmo => currentAmmo;
        public int ReserveAmmo => reserveAmmo;
        public bool IsReloading => isReloading;
        public bool IsAiming => isAiming;

        public event System.Action<int, int> OnAmmoChangedEvent;
        public event System.Action OnWeaponFired;
        public event System.Action OnReloadStarted;
        public event System.Action OnReloadFinished;
        public event System.Action<int, WeaponData> OnWeaponSwitched;

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
            if (availableWeapons != null && availableWeapons.Length > 0)
                currentWeapon = availableWeapons[currentWeaponIndex];
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (availableWeapons == null || availableWeapons.Length == 0)
                return;

            savedMagAmmo = new int[availableWeapons.Length];
            savedReserveAmmo = new int[availableWeapons.Length];
            for (int i = 0; i < availableWeapons.Length; i++)
            {
                if (availableWeapons[i] != null)
                {
                    savedMagAmmo[i] = availableWeapons[i].MagazineSize;
                    savedReserveAmmo[i] = availableWeapons[i].MaxReserveAmmo;
                }
            }

            currentWeaponIndex = 0;
            currentWeapon = availableWeapons[0];
            currentAmmo = savedMagAmmo[0];
            reserveAmmo = savedReserveAmmo[0];
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Initialise currentWeapon from the already-synced index (late-join safe).
            if (availableWeapons != null && currentWeaponIndex < availableWeapons.Length)
                currentWeapon = availableWeapons[currentWeaponIndex];
            ActivateWeaponModel(currentWeaponIndex);
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
            if (currentWeapon == null || isReloading || isCarrying)
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
            // Melee weapons have no ammo requirement.
            if (!currentWeapon.IsMelee)
            {
                if (currentAmmo <= 0)
                {
                    if (reserveAmmo > 0)
                        CmdStartReload();
                    return;
                }
            }

            Fire();
        }

        private void Fire()
        {
            lastFireTime = Time.time;

            Ray aimRay = playerCamera.GetAimRay();
            // Melee uses exact camera direction — no spread, no recoil.
            Vector3 direction = currentWeapon.IsMelee ? aimRay.direction : ApplySpread(aimRay.direction);

            CmdFire(aimRay.origin, direction);

            if (!currentWeapon.IsMelee)
            {
                playerCamera.AddRecoil(GetCurrentRecoil());
            }

            shotsFired++;
            OnWeaponFired?.Invoke();

            if (showDebugRays)
            {
                Color rayColor = currentWeapon.IsMelee ? Color.yellow : Color.red;
                Debug.DrawRay(aimRay.origin, direction * currentWeapon.Range, rayColor, 0.5f);
            }
        }

        [Command]
        private void CmdFire(Vector3 origin, Vector3 direction)
        {
            if (currentWeapon == null || isReloading)
                return;

            // Ranged weapons consume ammo; melee does not.
            if (!currentWeapon.IsMelee)
            {
                if (currentAmmo <= 0)
                    return;
                currentAmmo--;
            }

            // Melee: SphereCast for a wide, forgiving close-range sweep.
            // Ranged: precise Raycast.
            bool didHit;
            RaycastHit hit;
            if (currentWeapon.IsMelee)
                didHit = Physics.SphereCast(origin, currentWeapon.MeleeRadius, direction, out hit,
                    currentWeapon.Range, hitMask, QueryTriggerInteraction.Ignore);
            else
                didHit = Physics.Raycast(origin, direction, out hit,
                    currentWeapon.Range, hitMask, QueryTriggerInteraction.Ignore);

            if (didHit)
            {
                PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                if (targetHealth != null && targetHealth.gameObject != gameObject)
                    targetHealth.TakeDamage(currentWeapon.Damage, hit.point, -hit.normal);

                FPSGame.Environment.DestroyableObject destroyable = hit.collider.GetComponentInParent<FPSGame.Environment.DestroyableObject>();
                if (destroyable != null)
                    destroyable.TakeHit();

                FPSGame.Environment.DestructionTarget destructionTarget = hit.collider.GetComponentInParent<FPSGame.Environment.DestructionTarget>();
                if (destructionTarget != null)
                    destructionTarget.TakeHit();

                RpcOnHit(hit.point, hit.normal);
            }

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
            if (!isOwned || currentWeapon == null || currentWeapon.IsMelee)
                return;

            CmdStartReload();
        }

        [Command]
        private void CmdStartReload()
        {
            if (isReloading || currentWeapon == null || currentWeapon.IsMelee
                || currentAmmo >= currentWeapon.MagazineSize || reserveAmmo <= 0)
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

        public void SetCarryingMode(bool carrying)
        {
            if (!isOwned)
                return;

            isCarrying = carrying;
            if (carrying)
            {
                fireHeld = false;
                if (weaponModels != null)
                    foreach (var model in weaponModels)
                        if (model != null) model.SetActive(false);
            }
            else
            {
                ActivateWeaponModel(currentWeaponIndex);
            }
        }

        [Server]
        public void AddAmmo(int amount)
        {
            reserveAmmo = Mathf.Min(reserveAmmo + amount, currentWeapon.MaxReserveAmmo);
        }

        // ── Weapon switching ──────────────────────────────────────────────────

        public int WeaponCount => availableWeapons != null ? availableWeapons.Length : 0;
        public int CurrentWeaponIndex => currentWeaponIndex;

        public void SelectWeaponSlot(int index)
        {
            if (!isOwned || availableWeapons == null)
                return;
            if (index < 0 || index >= availableWeapons.Length || index == currentWeaponIndex)
                return;

            // Optimistic local update for responsive feel; server authoritative state
            // follows via SyncVar hook once CmdSwitchWeapon is processed.
            currentWeapon = availableWeapons[index];
            ActivateWeaponModel(index);
            OnWeaponSwitched?.Invoke(index, currentWeapon);

            CmdSwitchWeapon(index);
        }

        public void SelectNextWeapon()
        {
            if (availableWeapons == null || availableWeapons.Length < 2) return;
            SelectWeaponSlot((currentWeaponIndex + 1) % availableWeapons.Length);
        }

        public void SelectPreviousWeapon()
        {
            if (availableWeapons == null || availableWeapons.Length < 2) return;
            SelectWeaponSlot((currentWeaponIndex - 1 + availableWeapons.Length) % availableWeapons.Length);
        }

        [Command]
        private void CmdSwitchWeapon(int index)
        {
            if (availableWeapons == null || index < 0 || index >= availableWeapons.Length)
                return;
            if (index == currentWeaponIndex)
                return;

            // Cancel any in-progress reload.
            if (isReloading)
            {
                CancelInvoke(nameof(ServerFinishReload));
                isReloading = false;
            }

            // Save ammo for the outgoing slot.
            savedMagAmmo[currentWeaponIndex] = currentAmmo;
            savedReserveAmmo[currentWeaponIndex] = reserveAmmo;

            // Switch to the new slot — SyncVar hook fires on all clients.
            currentWeaponIndex = index;
            currentWeapon = availableWeapons[index];
            currentAmmo = savedMagAmmo[index];
            reserveAmmo = savedReserveAmmo[index];
        }

        private void OnWeaponIndexChanged(int oldIndex, int newIndex)
        {
            if (availableWeapons == null || newIndex >= availableWeapons.Length)
                return;
            currentWeapon = availableWeapons[newIndex];
            ActivateWeaponModel(newIndex);
            OnWeaponSwitched?.Invoke(newIndex, currentWeapon);
        }

        private void ActivateWeaponModel(int index)
        {
            if (weaponModels == null) return;
            for (int i = 0; i < weaponModels.Length; i++)
            {
                if (weaponModels[i] != null)
                    weaponModels[i].SetActive(i == index);
            }
        }
    }
}
