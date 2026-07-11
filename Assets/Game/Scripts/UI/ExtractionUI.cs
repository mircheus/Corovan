using FPSGame.Environment;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.UI
{
    /// <summary>
    /// Scene-level readout for an <see cref="ExtractionZone"/>: a progress bar that appears while the
    /// car is being held in the zone and fills over the capture. Purely a view — all timing lives on
    /// the zone, synced to every client, so this reads the same values everywhere.
    /// </summary>
    public class ExtractionUI : MonoBehaviour
    {
        [Header("Zone")]
        [Tooltip("The zone to watch. Left empty, the first ExtractionZone in the scene is used.")]
        [SerializeField] private ExtractionZone zone;

        [Header("UI")]
        [Tooltip("Container shown only while the capture is running.")]
        [SerializeField] private GameObject root;
        [SerializeField] private Slider progressBar;
        [Tooltip("Optional — remaining seconds, whole numbers.")]
        [SerializeField] private TextMeshProUGUI countdownText;
        [Tooltip("{0} is replaced by the remaining seconds.")]
        [SerializeField] private string countdownFormat = "Extracting... {0}";

        private void Start()
        {
            if (zone == null)
                zone = FindObjectOfType<ExtractionZone>();

            if (zone != null)
                zone.OnCapturingChanged += OnCapturingChanged;

            // Reflect the current state: hidden by default, or already mid-capture on a late join.
            OnCapturingChanged(zone != null && zone.IsCapturing);
        }

        private void OnDestroy()
        {
            if (zone != null)
                zone.OnCapturingChanged -= OnCapturingChanged;
        }

        private void OnCapturingChanged(bool capturing)
        {
            if (root != null)
                root.SetActive(capturing);
        }

        private void Update()
        {
            // The bar only needs driving while it's on screen.
            if (zone == null || !zone.IsCapturing)
                return;

            float progress = zone.CaptureProgress;

            if (progressBar != null)
                progressBar.value = progress;

            if (countdownText != null)
            {
                float remaining = Mathf.Max(0f, (1f - progress) * zone.CaptureDuration);
                countdownText.text = string.Format(countdownFormat, Mathf.CeilToInt(remaining));
            }
        }
    }
}
