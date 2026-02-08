using UnityEngine;

namespace FPSGame.Config
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "FPS Game/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        [Header("Mouse Settings")]
        [Range(0.1f, 10f)]
        public float mouseSensitivityX = 2.0f;
        [Range(0.1f, 10f)]
        public float mouseSensitivityY = 2.0f;
        public bool invertY = false;

        [Header("FOV Settings")]
        [Range(60f, 120f)]
        public float fieldOfView = 90f;

        [Header("Audio Settings")]
        [Range(0f, 1f)]
        public float masterVolume = 1.0f;
        [Range(0f, 1f)]
        public float sfxVolume = 1.0f;
        [Range(0f, 1f)]
        public float musicVolume = 1.0f;

        [Header("Graphics Settings")]
        public int qualityLevel = 2;
        public bool vSync = true;
        public int targetFrameRate = 60;

        [Header("Crosshair Settings")]
        public Color crosshairColor = Color.white;
        public float crosshairSize = 1.0f;
        public float crosshairThickness = 2.0f;
        public bool showDynamicSpread = true;

        [Header("Gameplay Settings")]
        public bool headBobEnabled = true;
        public float headBobIntensity = 1.0f;

        public void ApplySettings()
        {
            QualitySettings.SetQualityLevel(qualityLevel);
            QualitySettings.vSyncCount = vSync ? 1 : 0;
            Application.targetFrameRate = targetFrameRate;
            AudioListener.volume = masterVolume;
        }
    }
}
