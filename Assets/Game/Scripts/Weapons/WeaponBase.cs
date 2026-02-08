using UnityEngine;

namespace FPSGame.Weapons
{
    public abstract class WeaponBase : MonoBehaviour
    {
        [SerializeField] protected WeaponData weaponData;

        protected int currentAmmo;
        protected int reserveAmmo;
        protected bool isReloading;
        protected float lastFireTime;
        protected int shotsFired;

        public WeaponData Data => weaponData;
        public int CurrentAmmo => currentAmmo;
        public int ReserveAmmo => reserveAmmo;
        public bool IsReloading => isReloading;
        public bool CanFire => !isReloading && currentAmmo > 0 && Time.time >= lastFireTime + weaponData.FireRate;

        protected virtual void Awake()
        {
            if (weaponData != null)
            {
                currentAmmo = weaponData.MagazineSize;
                reserveAmmo = weaponData.MaxReserveAmmo;
            }
        }

        public virtual void Initialize(WeaponData data)
        {
            weaponData = data;
            currentAmmo = data.MagazineSize;
            reserveAmmo = data.MaxReserveAmmo;
            isReloading = false;
            lastFireTime = 0;
            shotsFired = 0;
        }

        public abstract bool Fire(Ray aimRay, bool isAiming, bool isMoving);

        public virtual void StartReload()
        {
            if (isReloading || currentAmmo >= weaponData.MagazineSize || reserveAmmo <= 0)
                return;

            isReloading = true;
            shotsFired = 0;
        }

        public virtual void FinishReload()
        {
            if (!isReloading)
                return;

            int ammoNeeded = weaponData.MagazineSize - currentAmmo;
            int ammoToReload = Mathf.Min(ammoNeeded, reserveAmmo);

            currentAmmo += ammoToReload;
            reserveAmmo -= ammoToReload;
            isReloading = false;
        }

        public virtual void CancelReload()
        {
            isReloading = false;
        }

        public void AddAmmo(int amount)
        {
            reserveAmmo = Mathf.Min(reserveAmmo + amount, weaponData.MaxReserveAmmo);
        }

        protected float GetCurrentSpread(bool isAiming, bool isMoving)
        {
            float spread = weaponData.BaseSpread;

            if (isAiming)
                spread *= weaponData.AdsSpreadMultiplier;

            if (isMoving)
                spread *= weaponData.MovementSpreadMultiplier;

            return spread;
        }

        protected Vector2 GetCurrentRecoil(bool isAiming)
        {
            Vector2 recoil = weaponData.GetRecoilAtIndex(shotsFired);

            if (isAiming)
                recoil *= weaponData.AdsRecoilMultiplier;

            return recoil;
        }

        protected Vector3 ApplySpread(Vector3 direction, float spread)
        {
            float spreadX = Random.Range(-spread, spread);
            float spreadY = Random.Range(-spread, spread);

            Quaternion spreadRotation = Quaternion.Euler(spreadY, spreadX, 0);
            return spreadRotation * direction;
        }
    }
}
