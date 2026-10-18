using UnityEngine;
using Mirror;
using FPSGame.Weapons;
using FPSGame.Environment;
using FPSGame.Combat;

namespace FPSGame.Player
{
    public class PlayerShooting : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private WeaponData[] availableWeapons;
        [SerializeField] private GameObject[] weaponModels;
        [Tooltip("Optional per-slot tool component (e.g. FloorCutter). When the active slot has a tool, normal fire/reload is suppressed and the tool component is enabled only on the local owner.")]
        [SerializeField] private Behaviour[] weaponTools;
        [SerializeField] private ParticleSystem[] muzzleFlashEffects;
        [SerializeField] private Transform weaponHolder;
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Velocity (m/s) a killing hit gives a ragdoll, along the shot direction.")]
        [SerializeField] private float hitPushSpeed = 4f;

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        private WeaponData currentWeapon;
        private PlayerHitReporter hitReporter;
        private PlayerHealth ownHealth;

        [SyncVar(hook = nameof(OnAmmoChanged))]
        private int currentAmmo;

        [SyncVar]
        private int reserveAmmo;

        [SyncVar]
        private bool isReloading;

        [SyncVar(hook = nameof(OnWeaponIndexChanged))]
        private int currentWeaponIndex;

        // Bitmask of slots the player has unlocked (picked up). Bit i == slot i.
        [SyncVar(hook = nameof(OnUnlockedMaskChanged))]
        private int unlockedMask;

        // Per-slot ammo, server-side only. Saved/restored on weapon switch.
        private int[] savedMagAmmo;
        private int[] savedReserveAmmo;

        private float lastFireTime;
        private int shotsFired;
        private bool fireHeld;
        private bool isAiming;
        private bool isCarrying;
        private bool isDowned;
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
        // Fires when the set of unlocked weapons changes (pickup). For HUD refresh.
        public event System.Action OnLoadoutChanged;

        private void Awake()
        {
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<PlayerCamera>();

            if (playerController == null)
                playerController = GetComponent<PlayerController>();

            hitReporter = GetComponent<PlayerHitReporter>();
            ownHealth = GetComponent<PlayerHealth>();
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

            // Start with only melee weapons unlocked; ranged weapons must be
            // picked up in the world (WeaponPickup) to become usable.
            int mask = 0;
            int firstUnlocked = -1;
            for (int i = 0; i < availableWeapons.Length; i++)
            {
                if (availableWeapons[i] != null && availableWeapons[i].IsMelee)
                {
                    mask |= 1 << i;
                    if (firstUnlocked < 0)
                        firstUnlocked = i;
                }
            }

            // Fallback: if no melee weapon is configured, unlock slot 0 so the
            // player is never stuck holding a locked (unusable) weapon.
            if (firstUnlocked < 0)
            {
                firstUnlocked = 0;
                mask |= 1 << 0;
            }

            unlockedMask = mask;
            currentWeaponIndex = firstUnlocked;
            currentWeapon = availableWeapons[firstUnlocked];
            currentAmmo = savedMagAmmo[firstUnlocked];
            reserveAmmo = savedReserveAmmo[firstUnlocked];
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

        private bool IsToolSlot(int index)
        {
            return weaponTools != null
                && index >= 0 && index < weaponTools.Length
                && weaponTools[index] != null;
        }

        private void HandleFiring()
        {
            if (currentWeapon == null || isReloading || isCarrying || isDowned)
                return;

            if (IsToolSlot(currentWeaponIndex))
                return;

            if (!IsUnlocked(currentWeaponIndex))
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
                PlayMuzzleFlash(currentWeaponIndex);
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

            if (!IsUnlocked(currentWeaponIndex))
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
                // Players, enemies, the car, barrels, locks — one path, with the friendly-fire rules.
                IDamageReceiver receiver = DamageRouter.FindReceiver(hit.collider);
                if (receiver != null && !ReferenceEquals(receiver, ownHealth) && hitReporter != null)
                    hitReporter.ServerApplyHit(receiver, currentWeapon.Damage, direction.normalized * hitPushSpeed);

                FPSGame.Environment.ZombieEnemy zombie = hit.collider.GetComponentInParent<FPSGame.Environment.ZombieEnemy>();
                if (zombie != null)
                    zombie.ServerPlayHitEffect(hit.point, hit.normal);

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
            if (!isOwned)
            {
                PlayMuzzleFlash(currentWeaponIndex);
                OnWeaponFired?.Invoke();
            }
        }

        private void PlayMuzzleFlash(int index)
        {
            if (muzzleFlashEffects == null || index >= muzzleFlashEffects.Length)
                return;
            if (muzzleFlashEffects[index] != null)
                muzzleFlashEffects[index].Play();
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
            if (!isOwned || isDowned || currentWeapon == null || currentWeapon.IsMelee)
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
            if (!isOwned || (isDowned && firing))
                return;

            fireHeld = firing;
            if (!firing && currentWeapon != null && !currentWeapon.IsAutomatic)
            {
                shotsFired = 0;
            }
        }

        public void SetAiming(bool aiming)
        {
            if (!isOwned || (isDowned && aiming))
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
                StowWeapons();
            else if (!isDowned)
                ActivateWeaponModel(currentWeaponIndex);
        }

        // Bleeding out on the floor: the weapon goes away and stays away until a teammate
        // gets this player back up.
        public void SetDowned(bool downed)
        {
            if (!isOwned)
                return;

            isDowned = downed;
            if (downed)
            {
                isAiming = false;
                playerCamera?.SetAiming(false);
                StowWeapons();
            }
            else if (!isCarrying)
            {
                ActivateWeaponModel(currentWeaponIndex);
            }
        }

        private void StowWeapons()
        {
            fireHeld = false;
            if (weaponModels != null)
                foreach (var model in weaponModels)
                    if (model != null) model.SetActive(false);
            if (weaponTools != null)
                foreach (var tool in weaponTools)
                    if (tool != null) tool.enabled = false;
        }

        [Server]
        public void AddAmmo(int amount)
        {
            reserveAmmo = Mathf.Min(reserveAmmo + amount, currentWeapon.MaxReserveAmmo);
        }

        // ── Weapon pickups ────────────────────────────────────────────────────

        public bool IsUnlocked(int slot)
        {
            return slot >= 0 && slot < 32 && (unlockedMask & (1 << slot)) != 0;
        }

        // Owner-side: called by WeaponPickup when the local player overlaps its
        // trigger. Forwards intent to the server, which validates range/grants.
        public void RequestPickup(WeaponPickup pickup)
        {
            if (!isOwned || pickup == null)
                return;
            CmdClaimPickup(pickup.netIdentity);
        }

        [Command]
        private void CmdClaimPickup(NetworkIdentity pickupId)
        {
            if (pickupId == null)
                return;
            var pickup = pickupId.GetComponent<WeaponPickup>();
            if (pickup == null)
                return;

            // Server-side range re-validation (guards against stale/spoofed claims).
            float dist = Vector3.Distance(transform.position, pickup.transform.position);
            if (dist > pickup.PickupRange + 1f)
                return;

            pickup.ServerTryTake(this);
        }

        // Grants the weapon matching `data` in the catalog: unlocks its slot and
        // auto-equips it, or tops up reserve ammo if already owned.
        // Returns true if the pickup should be consumed.
        [Server]
        public bool ServerGrantWeapon(WeaponData data)
        {
            if (availableWeapons == null || data == null)
                return false;

            int slot = System.Array.IndexOf(availableWeapons, data);
            if (slot < 0)
                return false;

            if (IsUnlocked(slot))
            {
                // Already owned — top up reserve ammo to the weapon's maximum.
                savedReserveAmmo[slot] = data.MaxReserveAmmo;
                if (slot == currentWeaponIndex)
                    reserveAmmo = data.MaxReserveAmmo;
                return true;
            }

            unlockedMask |= 1 << slot;
            savedMagAmmo[slot] = data.MagazineSize;
            savedReserveAmmo[slot] = data.MaxReserveAmmo;

            // Auto-equip the newly acquired weapon.
            ServerSwitchTo(slot);
            return true;
        }

        private void OnUnlockedMaskChanged(int oldMask, int newMask)
        {
            OnLoadoutChanged?.Invoke();
        }

        // ── Weapon switching ──────────────────────────────────────────────────

        public int WeaponCount => availableWeapons != null ? availableWeapons.Length : 0;
        public int CurrentWeaponIndex => currentWeaponIndex;

        public void SelectWeaponSlot(int index)
        {
            if (!isOwned || isDowned || availableWeapons == null)
                return;
            if (index < 0 || index >= availableWeapons.Length || index == currentWeaponIndex)
                return;
            if (!IsUnlocked(index))
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
            int next = FindNextUnlocked(1);
            if (next >= 0) SelectWeaponSlot(next);
        }

        public void SelectPreviousWeapon()
        {
            int prev = FindNextUnlocked(-1);
            if (prev >= 0) SelectWeaponSlot(prev);
        }

        // Nearest unlocked slot in the given cycle direction (skips locked slots).
        // Returns -1 if no other unlocked slot exists.
        private int FindNextUnlocked(int dir)
        {
            if (availableWeapons == null || availableWeapons.Length < 2)
                return -1;

            int n = availableWeapons.Length;
            for (int step = 1; step < n; step++)
            {
                int idx = ((currentWeaponIndex + dir * step) % n + n) % n;
                if (IsUnlocked(idx))
                    return idx;
            }
            return -1;
        }

        [Command]
        private void CmdSwitchWeapon(int index)
        {
            if (availableWeapons == null || index < 0 || index >= availableWeapons.Length)
                return;
            if (index == currentWeaponIndex || !IsUnlocked(index))
                return;

            ServerSwitchTo(index);
        }

        // Server-side slot switch. Saves the outgoing slot's ammo and loads the
        // incoming slot's — the SyncVar hooks push the change to all clients.
        [Server]
        private void ServerSwitchTo(int index)
        {
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
            if (weaponModels != null)
            {
                for (int i = 0; i < weaponModels.Length; i++)
                {
                    if (weaponModels[i] != null)
                        weaponModels[i].SetActive(i == index);
                }
            }

            ActivateWeaponTool(index);
        }

        private void ActivateWeaponTool(int index)
        {
            if (weaponTools == null) return;
            for (int i = 0; i < weaponTools.Length; i++)
            {
                if (weaponTools[i] == null) continue;
                // Tools are local-input driven (mouse, camera) — only enable on the owning client.
                weaponTools[i].enabled = isOwned && (i == index);
            }
        }
    }
}
