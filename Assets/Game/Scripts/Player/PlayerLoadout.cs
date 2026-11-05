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
    /// are busy (carrying loot or a keycard, downed, dead, match over) nothing is held, so FPSF
    /// weapons cannot fire, aim or reload. While driving the hands start empty (LMB is the car's gun) and
    /// only UsableInVehicle slots (the pistol) can be taken out — their key toggles them, scroll cycles
    /// through "empty" too. The slot chosen on foot is kept and comes back on exit.
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
        private int vehicleSlot = -1; // what is in hand while driving; -1 = empty (the car's gun fires)
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
        /// <summary>Owner: the set of unlocked slots changed (also raised once when the local player starts).</summary>
        public event System.Action OnUnlockedChanged;

        public int WeaponCount => slots.Count;
        /// <summary>Slot index of the item with this id, or -1.</summary>
        public int IndexOfItem(string itemId) => slots.FindIndex(s => s.ItemId == itemId);
        public LoadoutItem GetSlot(int index) => index >= 0 && index < slots.Count ? slots[index] : null;
        public int EquippedSlot => equippedSlot;
        public Firearm EquippedFirearm { get; private set; }
        public LoadoutItem EquippedItem => appliedSlot >= 0 && appliedSlot < slots.Count ? slots[appliedSlot] : null;
        private bool HandsBusy => carrying || downed || triggerLock || paused || disabledForGood;
        /// <summary>Owner: nothing in hand and nothing keeping the hands busy — a vehicle's gun may fire.</summary>
        public bool HandsFree => !HandsBusy && appliedSlot < 0;

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
            OnUnlockedChanged?.Invoke();
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
        public void SetInVehicle(bool value) { inVehicle = value; vehicleSlot = -1; Apply(); }
        /// <summary>Keep weapons away while the LMB/RMB press that threw or dropped loot is held.</summary>
        public void SetTriggerLock(bool value) { if (value != triggerLock) { triggerLock = value; Apply(); } }
        public void SetPaused(bool value) { paused = value; Apply(); }
        /// <summary>Dead, or the match is over: nothing in hand from now on.</summary>
        public void DisableForGood() { disabledForGood = true; Apply(); }

        // ---------- selection (owner) ----------

        public bool IsUnlocked(int slot) => slot >= 0 && slot < 32 && (unlockedMask & (1 << slot)) != 0;

        public bool IsEquipped(string itemId) =>
            appliedSlot >= 0 && appliedSlot < slots.Count && slots[appliedSlot].ItemId == itemId;

        // Unlocked, and holdable where the player is now (in a vehicle: UsableInVehicle only).
        private bool IsSelectable(int slot) =>
            IsUnlocked(slot) && slot < slots.Count && (!inVehicle || slots[slot].UsableInVehicle);

        public void SelectWeaponSlot(int index)
        {
            if (!isOwned || !IsSelectable(index))
                return;
            // Driving: the key takes the slot out, or puts it back away.
            if (inVehicle)
                vehicleSlot = vehicleSlot == index ? -1 : index;
            else
                selectedSlot = index;
            Apply();
        }

        public void SelectNextWeapon() => Cycle(1);
        public void SelectPreviousWeapon() => Cycle(-1);

        private void Cycle(int dir)
        {
            if (!isOwned)
                return;
            if (inVehicle)
            {
                vehicleSlot = FindNextVehicleSlot(dir);
                Apply();
            }
            else
            {
                SelectWeaponSlot(FindNextUnlocked(dir));
            }
        }

        // Driving: steps through "empty hands" (-1) and the slots usable in a vehicle.
        private int FindNextVehicleSlot(int dir)
        {
            int n = slots.Count + 1; // position 0 = empty hands
            for (int step = 1; step <= n; step++)
            {
                int idx = ((vehicleSlot + 1 + dir * step) % n + n) % n - 1;
                if (idx < 0 || IsSelectable(idx))
                    return idx;
            }
            return vehicleSlot;
        }

        private int FindNextUnlocked(int dir)
        {
            int n = slots.Count;
            for (int step = 1; step <= n; step++)
            {
                int idx = ((selectedSlot + dir * step) % n + n) % n;
                if (IsSelectable(idx))
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

            int wanted = inVehicle ? vehicleSlot : selectedSlot;
            int target = HandsBusy || !IsSelectable(wanted) ? -1 : wanted;
            if (target == appliedSlot)
            {
                // Same item stays in hand across entering/leaving a car: only the reload block changes.
                UpdateReloadBlock();
                return;
            }

            inventory.Switch(target); // -1 deactivates every item
            appliedSlot = target;

            EquippedFirearm = target >= 0 ? slots[target].GetComponent<Firearm>() : null;
            UpdateReloadBlock();
            OnEquippedItemChanged?.Invoke(EquippedItem);
            lastAmmo = lastReserve = int.MinValue; // force a HUD refresh

            CmdSetEquipped(target);
        }

        // No reloading at the wheel: both hands are needed for a magazine.
        private void UpdateReloadBlock()
        {
            if (EquippedFirearm != null)
                EquippedFirearm.ReloadBlocked = inVehicle;
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
            OnUnlockedChanged?.Invoke();
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
            // Set locally, so the SyncVar hook may not fire when the server value arrives.
            OnUnlockedChanged?.Invoke();
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
