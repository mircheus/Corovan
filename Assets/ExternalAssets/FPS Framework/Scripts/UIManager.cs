using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine;

namespace Akila.FPSFramework
{
    [AddComponentMenu("Akila/FPS Framework/UI/UI Mananger")]
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance;

        public bool _damageIndicator = true;
        public bool _healthDisplay = true;
        public bool _hitmarker = true;
        public bool _killFeed = true;

        [Space]
        public bool hitmarkerSFX = true;
        public bool killSFX = true;

        public DamageIndicator DamageIndicator { get; set; }
        public HealthDisplay HealthDisplay { get; set; }
        public Hitmarker Hitmarker { get; set; }
        public KillFeed KillFeed { get; set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            DamageIndicator = GetComponentInChildren<DamageIndicator>();
            HealthDisplay = GetComponentInChildren<HealthDisplay>();
            Hitmarker = GetComponentInChildren<Hitmarker>();
            KillFeed = GetComponentInChildren<KillFeed>();
        }

        private void Update()
        {
            // CORVAN: the game uses only some HUD elements; skip the ones that are not there.
            if (DamageIndicator) DamageIndicator.gameObject.SetActive(_damageIndicator);
            if (HealthDisplay) HealthDisplay.gameObject.SetActive(_healthDisplay);
            if (Hitmarker) Hitmarker.gameObject.SetActive(_hitmarker);
            if (KillFeed) KillFeed.gameObject.SetActive(_killFeed);

            if (Hitmarker) Hitmarker.useSFX = hitmarkerSFX;
            if (KillFeed) KillFeed.useSFX = killSFX;
        }

        /// <summary>
        /// updates player name in player's card
        /// </summary>
        /// <param name="name"></param>
        public void SetName(string name)
        {
            if (HealthDisplay) HealthDisplay.actorNameText.text = name; // CORVAN: optional element
        }

        public void LoadGame(string name)
        {
            StartCoroutine(LoadingScreen.Instance.LoadSceneAsync(name));
        }

        public void Quit()
        {
            Application.Quit();
        }
    }
}