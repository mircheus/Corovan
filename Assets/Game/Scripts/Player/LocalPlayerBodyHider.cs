using UnityEngine;
using Mirror;

namespace FPSGame.Player
{
    /// <summary>
    /// Hides the local player's body from their own camera by moving the
    /// configured renderer roots onto a dedicated layer that the local camera
    /// excludes via its culling mask. Remote players keep the original layer.
    /// </summary>
    public class LocalPlayerBodyHider : NetworkBehaviour
    {
        [Tooltip("Roots whose renderers will be moved to the hidden layer for the local player. Children are included.")]
        [SerializeField] private Transform[] bodyRoots;

        [Tooltip("Layer name used for the local player body. Must exist in Tags & Layers and be excluded from the local camera's Culling Mask.")]
        [SerializeField] private string hiddenLayerName = "LocalPlayerBody";

        [Tooltip("If true, hidden renderers still cast shadows so the player sees their own shadow.")]
        [SerializeField] private bool keepShadows = true;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            int layer = LayerMask.NameToLayer(hiddenLayerName);
            if (layer < 0)
            {
                Debug.LogError($"[LocalPlayerBodyHider] Layer '{hiddenLayerName}' not found. Add it in Project Settings > Tags and Layers.", this);
                return;
            }

            foreach (var root in bodyRoots)
            {
                if (root == null) continue;
                SetLayerRecursive(root.gameObject, layer);

                if (keepShadows)
                {
                    var renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                }
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
