using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Config;
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
        private PlayerGrenades grenades;
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
        private bool paused;
        private bool disabledForGood; // dead or match over

        // HUD feed: ammo of what is in hand (FPSF's Weapon HUD shows reload/low-ammo alerts itself).
        private int lastAmmo = int.MinValue;
        private int lastReserve = int.MinValue;

        public event System.Action<int, int> OnAmmoChangedEvent;
        /// <summary>Owner: the slot in hand changed (null = hands empty or busy).</summary>
        public event System.Action<LoadoutItem> OnEquippedItemChanged;

        public int WeaponCount => slots.Count;
        public LoadoutItem GetSlot(int index) => index >= 0 && index < slots.Count ? slots[index] : null;
        public int EquippedSlot => equippedSlot;
        public Firearm EquippedFirearm { get; private set; }
        public LoadoutItem EquippedItem => appliedSlot >= 0 && appliedSlot < slots.Count ? slots[appliedSlot] : null;
        private bool HandsBusy => carrying || downed || inVehicle || triggerLock || paused || disabledForGood;

        private void Awake()
        {
            grenades = GetComponent<PlayerGrenades>();
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

            ApplyWeaponBalance();
            selectedSlot = FirstUnlocked();
            Apply();
        }

        // Hits are decided on the owner, so weapon damage only matters here. The FPSF preset is a
        // shared asset: give this firearm its own copy, never write into the original.
        private void ApplyWeaponBalance()
        {
            BalanceConfig config = SceneBalance.Current;
            if (config == null)
                return;

            foreach (LoadoutItem slot in slots)
            {
                int? damage = WeaponDamage(config.player, slot.ItemId);
                var firearm = slot.GetComponent<Firearm>();
                if (damage == null || firearm == null || firearm.preset == null)
                    continue;

                firearm.preset = Instantiate(firearm.preset);
                firearm.preset.damage = damage.Value;
            }
        }

        private static int? WeaponDamage(PlayerBalance balance, string itemId)
        {
            switch (itemId)
            {
                case "pistol": return balance.pistolDamage;
                case "shotgun": return balance.shotgunDamage;
                case "rifle": return balance.rifleDamage;
                default: return null;
            }
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
        public void SetPaused(bool value) { paused = value; Apply(); }
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
            OnEquippedItemChanged?.Invoke(EquippedItem);
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
            if (inventory == null)
                return;
            AmmoProfile profile = AmmoFor(slots[slot]);
            if (profile != null && startingReserve.TryGetValue(profile, out int full))
                profile.amount = Mathf.Max(profile.amount, full);
        }

        // ---------- HUD ----------

        // Ammo a slot draws from: a firearm's ammo type, or the grenade count for the throwable.
        private AmmoProfile AmmoFor(LoadoutItem slot)
        {
            if (slot == null || inventory == null)
                return null;
            var firearm = slot.GetComponent<Firearm>();
            if (firearm != null)
                return firearm.AmmoProfile ?? inventory.FindAmmunition(firearm.preset.ammoType);
            if (slot.GetComponent<Throwable>() != null && grenades != null)
                return inventory.FindAmmunition(grenades.AmmoName);
            return null;
        }

        private void UpdateHudFeed()
        {
            Firearm firearm = EquippedFirearm;
            int ammo;
            int reserve;
            if (firearm != null)
            {
                ammo = firearm.remainingAmmoCount;
                reserve = firearm.AmmoProfile != null ? firearm.AmmoProfile.amount : 0;
            }
            else
            {
                // Grenades: the count in hand, nothing in reserve. Melee/tools: zero.
                AmmoProfile thrown = appliedSlot >= 0 && slots[appliedSlot].GetComponent<Throwable>() != null
                    ? AmmoFor(slots[appliedSlot]) : null;
                ammo = thrown != null ? thrown.amount : 0;
                reserve = 0;
            }

            if (ammo != lastAmmo || reserve != lastReserve)
            {
                lastAmmo = ammo;
                lastReserve = reserve;
                OnAmmoChangedEvent?.Invoke(ammo, reserve);
            }
        }
    }
}
