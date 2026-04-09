using UnityEngine;

namespace FPSGame.Weapons
{
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "FPS Game/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")]
        [SerializeField] private string weaponName = "Weapon";
        [SerializeField] private Sprite weaponIcon;

        [Header("Firing")]
        [SerializeField] private bool isMelee = false;
        [SerializeField] private bool isAutomatic = false;
        [SerializeField] private float fireRate = 0.2f;
        [SerializeField] private int damage = 25;
        [SerializeField] private float range = 100f;
        [Tooltip("SphereCast radius used for melee hit detection. Ignored for ranged weapons.")]
        [SerializeField] private float meleeRadius = 0.5f;
        [SerializeField] private float baseSpread = 0.02f;
        [SerializeField] private float adsSpreadMultiplier = 0.3f;
        [SerializeField] private float movementSpreadMultiplier = 1.5f;

        [Header("Ammo")]
        [SerializeField] private int magazineSize = 12;
        [SerializeField] private int maxReserveAmmo = 120;
        [SerializeField] private float reloadTime = 1.5f;

        [Header("Recoil")]
        [SerializeField] private Vector2[] recoilPattern = new Vector2[]
        {
            new Vector2(0, 1.5f),
            new Vector2(0.2f, 1.3f),
            new Vector2(-0.3f, 1.4f),
            new Vector2(0.1f, 1.2f),
            new Vector2(-0.2f, 1.5f)
        };
        [SerializeField] private float recoilMultiplier = 1f;
        [SerializeField] private float adsRecoilMultiplier = 0.7f;

        [Header("ADS")]
        [SerializeField] private float adsTime = 0.2f;
        [SerializeField] private Vector3 adsPosition = new Vector3(0, -0.1f, 0.1f);

        [Header("Audio")]
        [SerializeField] private AudioClip fireSound;
        [SerializeField] private AudioClip reloadSound;
        [SerializeField] private AudioClip emptySound;

        [Header("Visuals")]
        [SerializeField] private GameObject weaponPrefab;
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private GameObject impactEffectPrefab;

        public string WeaponName => weaponName;
        public Sprite WeaponIcon => weaponIcon;
        public bool IsMelee => isMelee;
        public bool IsAutomatic => isAutomatic;
        public float FireRate => fireRate;
        public int Damage => damage;
        public float Range => range;
        public float MeleeRadius => meleeRadius;
        public float BaseSpread => baseSpread;
        public float AdsSpreadMultiplier => adsSpreadMultiplier;
        public float MovementSpreadMultiplier => movementSpreadMultiplier;
        public int MagazineSize => magazineSize;
        public int MaxReserveAmmo => maxReserveAmmo;
        public float ReloadTime => reloadTime;
        public Vector2[] RecoilPattern => recoilPattern;
        public float RecoilMultiplier => recoilMultiplier;
        public float AdsRecoilMultiplier => adsRecoilMultiplier;
        public float AdsTime => adsTime;
        public Vector3 AdsPosition => adsPosition;
        public AudioClip FireSound => fireSound;
        public AudioClip ReloadSound => reloadSound;
        public AudioClip EmptySound => emptySound;
        public GameObject WeaponPrefab => weaponPrefab;
        public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;
        public GameObject ImpactEffectPrefab => impactEffectPrefab;

        public Vector2 GetRecoilAtIndex(int shotIndex)
        {
            if (recoilPattern == null || recoilPattern.Length == 0)
                return Vector2.zero;

            int index = Mathf.Min(shotIndex, recoilPattern.Length - 1);
            return recoilPattern[index] * recoilMultiplier;
        }
    }
}
