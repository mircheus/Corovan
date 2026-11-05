using UnityEngine;
using FPSGame.Player;

namespace FPSGame.UI
{
    /// <summary>
    /// The weapon selection panel: shows a <see cref="WeaponSlotUI"/> for every loadout item the local
    /// player owns and highlights the one in hand. Hidden slots drop out of the layout, so the panel's
    /// HorizontalLayoutGroup + ContentSizeFitter shrink it to the visible slots.
    /// Bound by <see cref="HUD"/> to the local player's <see cref="PlayerLoadout"/>.
    /// </summary>
    public class WeaponPanelUI : MonoBehaviour
    {
        [Tooltip("Every slot of the panel. Items with no slot here (e.g. floor_cutter) are simply not shown.")]
        [SerializeField] private WeaponSlotUI[] slots;

        private PlayerLoadout loadout;
        private string equippedId;

        private void Awake()
        {
            // Nothing is owned until a loadout is bound. (Awake can run after Bind when the panel
            // starts inactive, so never hide what Bind already showed.)
            if (loadout != null)
                return;
            foreach (WeaponSlotUI slot in slots)
                if (slot != null)
                    slot.SetVisible(false);
        }

        public void Bind(PlayerLoadout playerLoadout)
        {
            Unbind();
            loadout = playerLoadout;
            if (loadout == null)
                return;

            loadout.OnUnlockedChanged += Refresh;
            loadout.OnEquippedItemChanged += OnEquippedChanged;

            LoadoutItem inHand = loadout.EquippedItem;
            equippedId = inHand != null ? inHand.ItemId : null;
            Refresh();
        }

        public void Unbind()
        {
            if (loadout != null)
            {
                loadout.OnUnlockedChanged -= Refresh;
                loadout.OnEquippedItemChanged -= OnEquippedChanged;
            }
            loadout = null;
        }

        private void OnDestroy() => Unbind();

        private void OnEquippedChanged(LoadoutItem item)
        {
            equippedId = item != null ? item.ItemId : null;
            Refresh();
        }

        private void Refresh()
        {
            if (loadout == null)
                return;

            foreach (WeaponSlotUI slot in slots)
            {
                if (slot == null)
                    continue;

                int index = loadout.IndexOfItem(slot.ItemId);
                bool owned = index >= 0 && loadout.IsUnlocked(index);

                slot.SetVisible(owned);
                if (!owned)
                    continue;

                slot.SetKey((index + 1).ToString());
                slot.SetEquipped(equippedId != null && slot.ItemId == equippedId);
            }
        }
    }
}
