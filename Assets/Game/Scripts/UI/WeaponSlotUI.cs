using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.UI
{
    /// <summary>
    /// One icon in the weapon panel. Knows which loadout item it shows (<see cref="ItemId"/>), the key
    /// label, and tints its background when the item is in hand. Shown only while the player owns the item.
    /// </summary>
    public class WeaponSlotUI : MonoBehaviour
    {
        [Tooltip("LoadoutItem id this slot shows, e.g. \"pistol\", \"rifle\".")]
        [SerializeField] private string itemId;
        [Tooltip("Key label. Empty = the slot's number in the player's loadout (1-9).")]
        [SerializeField] private string keyLabel;
        [SerializeField] private TMP_Text keyText;

        [Header("Background")]
        [Tooltip("The slot's root Image. Defaults to the Image on this object.")]
        [SerializeField] private Image background;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color equippedColor = new Color(1f, 0.85f, 0.3f, 1f);

        public string ItemId => itemId;
        public string KeyLabel => keyLabel;

        private void Awake()
        {
            if (background == null)
                background = GetComponent<Image>();
        }

        /// <summary>Writes the key label. An empty label falls back to <paramref name="fallbackKey"/>.</summary>
        public void SetKey(string fallbackKey)
        {
            if (keyText != null)
                keyText.text = string.IsNullOrEmpty(keyLabel) ? fallbackKey : keyLabel;
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        public void SetEquipped(bool equipped)
        {
            if (background == null)
                background = GetComponent<Image>();
            if (background != null)
                background.color = equipped ? equippedColor : normalColor;
        }
    }
}
