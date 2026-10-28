using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Marks an FPS Framework item baked under the player's Inventory as a loadout slot.
    /// Slot index = child order under the Inventory (keys 1-9). Items are stored inactive in the
    /// prefab: only the owner's PlayerLoadout ever activates one, so FPSF's per-item Awake (input,
    /// weapon HUD canvas) never runs on other players' copies.
    /// </summary>
    public class LoadoutItem : MonoBehaviour
    {
        [Tooltip("Id that WeaponPickups grant, e.g. \"pistol\", \"grapple\".")]
        [SerializeField] private string itemId;
        [SerializeField] private bool unlockedByDefault;
        [Tooltip("Can be held while driving (one-handed). Other slots are put away in a vehicle.")]
        [SerializeField] private bool usableInVehicle;

        public string ItemId => itemId;
        public bool UnlockedByDefault => unlockedByDefault;
        public bool UsableInVehicle => usableInVehicle;
    }
}
