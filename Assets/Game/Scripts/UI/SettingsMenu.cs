using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FPSGame.Config;
using FPSGame.Player;

namespace FPSGame.UI
{
    public class SettingsMenu : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private PlayerCamera playerCamera;

        [Header("Controls Tab")]
        [SerializeField] private Slider sensitivityXSlider;
        [SerializeField] private Slider sensitivityYSlider;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private TextMeshProUGUI sensitivityXValue;
        [SerializeField] private TextMeshProUGUI sensitivityYValue;

        [Header("Video Tab")]
        [SerializeField] private Slider fovSlider;
        [SerializeField] private TMP_Dropdown qualityDropdown;
        [SerializeField] private Toggle vSyncToggle;
        [SerializeField] private TextMeshProUGUI fovValue;

        [Header("Audio Tab")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Slider musicVolumeSlider;

        [Header("Tab Buttons")]
        [SerializeField] private GameObject controlsTab;
        [SerializeField] private GameObject videoTab;
        [SerializeField] private GameObject audioTab;

        private void Start()
        {
            LoadSettings();
            SetupListeners();
        }

        private void LoadSettings()
        {
            if (gameSettings == null)
                return;

            if (sensitivityXSlider != null)
            {
                sensitivityXSlider.value = gameSettings.mouseSensitivityX;
                UpdateSensitivityXText(gameSettings.mouseSensitivityX);
            }

            if (sensitivityYSlider != null)
            {
                sensitivityYSlider.value = gameSettings.mouseSensitivityY;
                UpdateSensitivityYText(gameSettings.mouseSensitivityY);
            }

            if (invertYToggle != null)
                invertYToggle.isOn = gameSettings.invertY;

            if (fovSlider != null)
            {
                fovSlider.value = gameSettings.fieldOfView;
                UpdateFOVText(gameSettings.fieldOfView);
            }

            if (qualityDropdown != null)
                qualityDropdown.value = gameSettings.qualityLevel;

            if (vSyncToggle != null)
                vSyncToggle.isOn = gameSettings.vSync;

            if (masterVolumeSlider != null)
                masterVolumeSlider.value = gameSettings.masterVolume;

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.value = gameSettings.sfxVolume;

            if (musicVolumeSlider != null)
                musicVolumeSlider.value = gameSettings.musicVolume;
        }

        private void SetupListeners()
        {
            if (sensitivityXSlider != null)
                sensitivityXSlider.onValueChanged.AddListener(OnSensitivityXChanged);

            if (sensitivityYSlider != null)
                sensitivityYSlider.onValueChanged.AddListener(OnSensitivityYChanged);

            if (invertYToggle != null)
                invertYToggle.onValueChanged.AddListener(OnInvertYChanged);

            if (fovSlider != null)
                fovSlider.onValueChanged.AddListener(OnFOVChanged);

            if (qualityDropdown != null)
                qualityDropdown.onValueChanged.AddListener(OnQualityChanged);

            if (vSyncToggle != null)
                vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);

            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);

            if (musicVolumeSlider != null)
                musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        public void ShowControlsTab()
        {
            SetActiveTab(controlsTab);
        }

        public void ShowVideoTab()
        {
            SetActiveTab(videoTab);
        }

        public void ShowAudioTab()
        {
            SetActiveTab(audioTab);
        }

        private void SetActiveTab(GameObject activeTab)
        {
            if (controlsTab != null) controlsTab.SetActive(controlsTab == activeTab);
            if (videoTab != null) videoTab.SetActive(videoTab == activeTab);
            if (audioTab != null) audioTab.SetActive(audioTab == activeTab);
        }

        private void OnSensitivityXChanged(float value)
        {
            if (gameSettings != null)
                gameSettings.mouseSensitivityX = value;

            if (playerCamera != null)
                playerCamera.SetSensitivity(value, gameSettings?.mouseSensitivityY ?? value);

            UpdateSensitivityXText(value);
        }

        private void OnSensitivityYChanged(float value)
        {
            if (gameSettings != null)
                gameSettings.mouseSensitivityY = value;

            if (playerCamera != null)
                playerCamera.SetSensitivity(gameSettings?.mouseSensitivityX ?? value, value);

            UpdateSensitivityYText(value);
        }

        private void OnInvertYChanged(bool value)
        {
            if (gameSettings != null)
                gameSettings.invertY = value;
        }

        private void OnFOVChanged(float value)
        {
            if (gameSettings != null)
                gameSettings.fieldOfView = value;

            UpdateFOVText(value);
        }

        private void OnQualityChanged(int value)
        {
            if (gameSettings != null)
            {
                gameSettings.qualityLevel = value;
                QualitySettings.SetQualityLevel(value);
            }
        }

        private void OnVSyncChanged(bool value)
        {
            if (gameSettings != null)
            {
                gameSettings.vSync = value;
                QualitySettings.vSyncCount = value ? 1 : 0;
            }
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (gameSettings != null)
            {
                gameSettings.masterVolume = value;
                AudioListener.volume = value;
            }
        }

        private void OnSFXVolumeChanged(float value)
        {
            if (gameSettings != null)
                gameSettings.sfxVolume = value;
        }

        private void OnMusicVolumeChanged(float value)
        {
            if (gameSettings != null)
                gameSettings.musicVolume = value;
        }

        private void UpdateSensitivityXText(float value)
        {
            if (sensitivityXValue != null)
                sensitivityXValue.text = value.ToString("F2");
        }

        private void UpdateSensitivityYText(float value)
        {
            if (sensitivityYValue != null)
                sensitivityYValue.text = value.ToString("F2");
        }

        private void UpdateFOVText(float value)
        {
            if (fovValue != null)
                fovValue.text = Mathf.RoundToInt(value).ToString();
        }

        public void ApplySettings()
        {
            if (gameSettings != null)
                gameSettings.ApplySettings();
        }

        public void ResetToDefaults()
        {
            if (gameSettings != null)
            {
                gameSettings.mouseSensitivityX = 2.0f;
                gameSettings.mouseSensitivityY = 2.0f;
                gameSettings.invertY = false;
                gameSettings.fieldOfView = 90f;
                gameSettings.qualityLevel = 2;
                gameSettings.vSync = true;
                gameSettings.masterVolume = 1.0f;
                gameSettings.sfxVolume = 1.0f;
                gameSettings.musicVolume = 1.0f;

                LoadSettings();
                ApplySettings();
            }
        }
    }
}
