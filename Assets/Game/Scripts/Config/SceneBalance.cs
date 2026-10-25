using UnityEngine;

namespace FPSGame.Config
{
    /// <summary>
    /// Picks the <see cref="BalanceConfig"/> for the scene it sits in. Receivers read
    /// <see cref="Current"/> when they spawn (players and enemies appear after scene load, so
    /// nothing is pushed). The scene exists on every peer, so clients can read it too.
    /// No SceneBalance in the scene — or no config on it — means prefab values stay in effect.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SceneBalance : MonoBehaviour
    {
        [SerializeField] private BalanceConfig config;

        public static BalanceConfig Current { get; private set; }

        private void Awake()
        {
            if (Current != null && Current != config)
                Debug.LogWarning($"[SceneBalance] More than one SceneBalance loaded; '{name}' replaces '{Current.name}'.", this);
            Current = config;
        }

        private void OnDestroy()
        {
            if (Current == config)
                Current = null;
        }
    }
}
