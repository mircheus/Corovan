using UnityEngine;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// A loadout slot that is one of our gameplay tools (floor cutter, vehicle remote) rather than an
    /// FPSF weapon. Holding it enables the tool's Behaviour; putting it away disables it.
    ///
    /// Items are only ever activated on the owner, so the tool runs on the owner only — the tool
    /// Behaviour must be disabled in the prefab.
    /// </summary>
    public class ToolItem : Item
    {
        [Tooltip("Tool component on the player (FloorCutter, RemoteVehicleControlTool, ...).")]
        [SerializeField] private Behaviour tool;

        private void Awake()
        {
            Setup(name, null);
        }

        private void OnEnable()
        {
            if (tool != null)
                tool.enabled = true;
        }

        private void OnDisable()
        {
            if (tool != null)
                tool.enabled = false;
        }
    }
}
