using UnityEngine;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    /// <summary>
    /// Base of the driver's camera feel effects (C3 / C5 / C6) on the player prefab. Local driver only:
    /// tracks the car this player drives (not a passenger seat), its <see cref="CarBodyVisual"/>, the dash
    /// event, and a 0..1 weight that fades in on taking the wheel and out on leaving it (or on the switch
    /// being turned off) over <see cref="enterExitBlend"/> seconds.
    /// </summary>
    public abstract class DriverFeelEffect : MonoBehaviour
    {
        [Header("Switch (A/B)")]
        [SerializeField] protected bool effectEnabled = true;
        [Tooltip("Seconds the effect takes to fade in when taking the wheel and out when leaving it.")]
        [SerializeField, Min(0f)] protected float enterExitBlend = 0.3f;

        protected PlayerInputHandler input;
        protected PlayerCamera playerCamera;

        /// <summary>Car driven by the local player right now; null on foot, in a passenger seat or on other copies.</summary>
        protected CarController Car { get; private set; }
        protected CarBodyVisual Body { get; private set; }

        /// <summary>0..1 fade of the effect.</summary>
        public float Weight { get; private set; }

        public bool EffectEnabled
        {
            get => effectEnabled;
            set => effectEnabled = value;
        }

        protected virtual void Awake()
        {
            input = GetComponent<PlayerInputHandler>();
            playerCamera = GetComponentInChildren<PlayerCamera>(true);
        }

        protected virtual void OnDisable()
        {
            BindCar(null);
            Weight = 0f;
        }

        private void LateUpdate()
        {
            if (input == null || !input.isOwned)
                return;

            CarController driven = input.IsInVehicle ? input.OccupiedVehicle : null;
            if (driven != Car)
                BindCar(driven);

            float target = effectEnabled && Car != null && Car.VehicleIsAlive ? 1f : 0f;
            Weight = enterExitBlend > 0f
                ? Mathf.MoveTowards(Weight, target, Time.deltaTime / enterExitBlend)
                : target;

            Tick(Time.deltaTime);
        }

        private void BindCar(CarController car)
        {
            if (Car != null)
                Car.OnDashStarted -= HandleDash;
            Car = car;
            Body = car != null ? car.GetComponent<CarBodyVisual>() : null;
            if (Car != null)
                Car.OnDashStarted += HandleDash;
        }

        private void HandleDash()
        {
            if (effectEnabled && Car != null)
                OnDash(Car.LastDashLocalDirection);
        }

        /// <summary>Every frame on the local player (Weight already updated). Must also settle the effect back to
        /// neutral while Weight is 0.</summary>
        protected abstract void Tick(float dt);

        /// <summary>The driven car has just dashed (local direction, unit).</summary>
        protected virtual void OnDash(Vector3 localDirection) { }
    }
}
