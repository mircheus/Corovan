using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Environment;

namespace FPSGame.Player
{
    /// <summary>
    /// The player's weapons and tools, on top of the FPS Framework Inventory.
    ///
    /// Slots are the LoadoutItems baked (inactive) under the Inventory, in child order. Which slots
    /// are usable is server state (unlockedMask: defaults + WeaponPickups). Which one is in hand is
    /// the owner's choice, mirrored to the server (equippedSlot) for everyone else.
    ///
    /// FPSF's Inventory does the actual swapping (activating one item), but its own Update — which
    /// reads 1-9/scroll and would ignore locks — is switched off; we drive it instead. While the hands
    /// are busy (carrying loot or a keycard, driving, downed, dead, match over) nothing is held, so
    /// FPSF weapons cannot fire, aim or reload.
    ///
    /// Ammo lives in the FPSF firearms on the owner (client-side, like hits).
    /// </summary>
    public class PlayerLoadout : NetworkBehaviour
    {
        [Tooltip("Unlock every slot at spawn. For testing weapons without pickups.")]
        [SerializeField] private bool unlockAllForTesting;

        [SyncVar(hook = nameof(OnUnlockedMaskChanged))]
        private int unlockedMask;

        // -1 = nothing in hand. Owner-driven; others use it for the third-person view.
        [SyncVar]
        private int equippedSlot = -1;

        private Inventory inventory;
        private readonly List<LoadoutItem> slots = new List<LoadoutItem>();
        // Reserve each ammo type started with; a pickup of an owned weapon refills to this.
        private readonly Dictionary<AmmoProfile, int> startingReserve = new Dictionary<AmmoProfile, int>();

        // Owner-side state.
        private int selectedSlot = -1;
        private int appliedSlot = -1;
        private bool carrying;
        private bool downed;
        private bool inVehicle;
        private bool triggerLock;     // the button that threw/dropped loot is still held
        private bool disabledForGood; // dead or match over

        // HUD feed (same shape PlayerShooting had).
        private int lastAmmo = int.MinValue;
        private int lastReserve = int.MinValue;
        private bool lastReloading;

        public event System.Action<int, int> OnAmmoChangedEvent;
        public event System.Action OnReloadStarted;
        public event System.Action OnReloadFinished;
        /// <summary>Owner: the item in hand changed (null = nothing, or not a firearm).</summary>
        public event System.Action<Firearm> OnEquippedFirearmChanged;
        public event System.Action OnLoadoutChanged;

        public int WeaponCount => slots.Count;
        public LoadoutItem GetSlot(int index) => index >= 0 && index < slots.Count ? slots[index] : null;
        public int EquippedSlot => equippedSlot;
        public Firearm EquippedFirearm { get; private set; }
        private bool HandsBusy => carrying || downed || inVehicle || triggerLock || disabledForGood;

        private void Awake()
        {
            inventory = GetComponentInChildren<Inventory>(true);
            if (inventory == null)
                return;

            foreach (Transform child in inventory.transform)
            {
                var item = child.GetComponent<LoadoutItem>();
                if (item != null)
                    slots.Add(item);
            }

            // We switch items; FPSF's own Update would read 1-9/scroll and ignore locked slots.
            // (Awake still runs on a disabled component, so its item list is gathered either way.)
            inventory.enabled = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            int mask = 0;
            for (int i = 0; i < slots.Count; i++)
                if (unlockAllForTesting || slots[i].UnlockedByDefault)
                    mask |= 1 << i;
            unlockedMask = mask;
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (inventory != null && inventory.ammo != null)
                foreach (AmmoProfile profile in inventory.ammo)
                    if (profile != null)
                        startingReserve[profile] = profile.amount;

            selectedSlot = FirstUnlocked();
            Apply();
        }

        private void Update()
        {
            if (!isOwned)
                return;
            UpdateHudFeed();
        }

        // ---------- hands busy ----------

        public void SetCarryingMode(bool value) { carrying = value; Apply(); }
        public void SetDowned(bool value) { downed = value; Apply(); }
        public void SetInVehicle(bool value) { inVehicle = value; Apply(); }
        /// <summary>Keep weapons away while the LMB/RMB press that threw or dropped loot is held.</summary>
        public void SetTriggerLock(bool value) { if (value != triggerLock) { triggerLock = value; Apply(); } }
        /// <summary>Dead, or the match is over: nothing in hand from now on.</summary>
        public void DisableForGood() { disabledForGood = true; Apply(); }

        // ---------- selection (owner) ----------

        public bool IsUnlocked(int slot) => slot >= 0 && slot < 32 && (unlockedMask & (1 << slot)) != 0;

        public bool IsEquipped(string itemId) =>
            appliedSlot >= 0 && appliedSlot < slots.Count && slots[appliedSlot].ItemId == itemId;

        public void SelectWeaponSlot(int index)
        {
            if (!isOwned || !IsUnlocked(index) || index >= slots.Count)
                return;
            selectedSlot = index;
            Apply();
        }

        public void SelectNextWeapon() => SelectWeaponSlot(FindNextUnlocked(1));
        public void SelectPreviousWeapon() => SelectWeaponSlot(FindNextUnlocked(-1));

        private int FindNextUnlocked(int dir)
        {
            int n = slots.Count;
            for (int step = 1; step <= n; step++)
            {
                int idx = ((selectedSlot + dir * step) % n + n) % n;
                if (IsUnlocked(idx))
                    return idx;
            }
            return selectedSlot;
        }

        private int FirstUnlocked()
        {
            for (int i = 0; i < slots.Count; i++)
                if (IsUnlocked(i))
                    return i;
            return -1;
        }

        // Puts the right item in hand (or none) through FPSF's Inventory.
        private void Apply()
        {
            if (!isOwned || inventory == null)
                return;

            if (!IsUnlocked(selectedSlot))
                selectedSlot = FirstUnlocked();

            int target = HandsBusy ? -1 : selectedSlot;
            if (target == appliedSlot)
                return;

            inventory.Switch(target); // -1 deactivates every item
            appliedSlot = target;

            EquippedFirearm = target >= 0 ? slots[target].GetComponent<Firearm>() : null;
            OnEquippedFirearmChanged?.Invoke(EquippedFirearm);
            lastAmmo = lastReserve = int.MinValue; // force a HUD refresh

            CmdSetEquipped(target);
        }

        [Command]
        private void CmdSetEquipped(int slot)
        {
            equippedSlot = slot >= 0 && slot < slots.Count && IsUnlocked(slot) ? slot : -1;
        }

        private void OnUnlockedMaskChanged(int oldMask, int newMask)
        {
            OnLoadoutChanged?.Invoke();
            // First unlock while empty-handed (e.g. no default item): take it.
            if (isOwned && !IsUnlocked(selectedSlot))
            {
                selectedSlot = FirstUnlocked();
                Apply();
            }
        }

        // ---------- pickups (server) ----------

        public void RequestPickup(WeaponPickup pickup)
        {
            if (!isOwned || pickup == null)
                return;
            CmdPickup(pickup.netIdentity);
        }

        [Command]
        private void CmdPickup(NetworkIdentity pickupIdentity)
        {
            if (pickupIdentity == null)
                return;
            var pickup = pickupIdentity.GetComponent<WeaponPickup>();
            if (pickup == null)
                return;
            // Range re-validation (guards against stale/spoofed claims).
            if (Vector3.Distance(transform.position, pickup.transform.position) > pickup.PickupRange + 1f)
                return;
            pickup.ServerTryTake(this);
        }

        /// <summary>
        /// A pickup for itemId: unlocks its slot and puts it in hand, or — already owned — refills that
        /// weapon's reserve ammo. False (pickup not consumed) only for an unknown id.
        /// </summary>
        [Server]
        public bool ServerGrant(string itemId)
        {
            int slot = slots.FindIndex(s => s.ItemId == itemId);
            if (slot < 0)
            {
                Debug.LogWarning($"[PlayerLoadout] No loadout slot with id '{itemId}'.", this);
                return false;
            }

            if (IsUnlocked(slot))
            {
                TargetRefillReserve(slot);
                return true;
            }

            unlockedMask |= 1 << slot;
            TargetEquip(slot);
            return true;
        }

        [TargetRpc]
        private void TargetEquip(int slot)
        {
            // The SyncVar may land after this RPC; unlock locally so the selection sticks.
            unlockedMask |= 1 << slot;
            SelectWeaponSlot(slot);
        }

        // Ammo is client-side: the owner tops its own reserve up to what the game started with.
        [TargetRpc]
        private void TargetRefillReserve(int slot)
        {
            var firearm = slots[slot].GetComponent<Firearm>();
            if (firearm == null || inventory == null)
                return;
            AmmoProfile profile = firearm.AmmoProfile ?? inventory.FindAmmunition(firearm.preset.ammoType);
            if (profile != null && startingReserve.TryGetValue(profile, out int full))
                profile.amount = Mathf.Max(profile.amount, full);
        }

        // ---------- HUD ----------

        private void UpdateHudFeed()
        {
            Firearm firearm = EquippedFirearm;
            int ammo = firearm != null ? firearm.remainingAmmoCount : 0;
            int reserve = firearm != null && firearm.AmmoProfile != null ? firearm.AmmoProfile.amount : 0;
            if (ammo != lastAmmo || reserve != lastReserve)
            {
                lastAmmo = ammo;
                lastReserve = reserve;
                OnAmmoChangedEvent?.Invoke(ammo, reserve);
            }

            bool reloading = firearm != null && firearm.IsReloading;
            if (reloading != lastReloading)
            {
                lastReloading = reloading;
                if (reloading) OnReloadStarted?.Invoke();
                else OnReloadFinished?.Invoke();
            }
        }
    }
}
