using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Car feel C6: camera shake for the local driver — a constant faint Perlin rumble while at the wheel plus a
    /// burst on every dash. Purely visual: it moves its own <see cref="shakeNode"/>, a node above the view
    /// cameras only (the FPSF Inventory with the weapons' muzzles is not under it), so it doesn't fight mouse look
    /// and shooting is unaffected. The passenger and other players never shake.
    /// </summary>
    public class DriverCameraShake : DriverFeelEffect
    {
        [Header("Node")]
        [Tooltip("Offset node between FPSF's 'Camera Holder' and the main + overlay cameras. Nothing else writes it.")]
        [SerializeField] private Transform shakeNode;

        [Header("Base rumble (while driving)")]
        [Tooltip("Degrees per axis.")]
        [SerializeField] private float baseShakeRotation = 0.2f;
        [Tooltip("Metres per axis.")]
        [SerializeField] private float baseShakePosition = 0.005f;
        [Tooltip("Hz — how fast the noise is walked.")]
        [SerializeField] private float baseShakeFrequency = 18f;

        [Header("Dash burst")]
        [Tooltip("Degrees per axis at the start of the burst; position follows at the base position/rotation ratio.")]
        [SerializeField] private float dashShakeAmplitude = 1f;
        [SerializeField, Min(0.001f)] private float dashShakeDuration = 0.3f;

        private Vector3 basePosition;
        private Quaternion baseRotation = Quaternion.identity;
        private float noiseTime;
        private float dashTime = float.PositiveInfinity;
        private readonly float[] seeds = new float[6];

        /// <summary>True while the node is offset at all (for the debug overlay).</summary>
        public bool IsShaking { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            if (shakeNode != null)
            {
                basePosition = shakeNode.localPosition;
                baseRotation = shakeNode.localRotation;
            }
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = Random.value * 100f + i * 17.3f;
        }

        protected override void Tick(float dt)
        {
            if (shakeNode == null)
                return;

            dashTime += dt;
            float dash = 0f;
            if (dashTime < dashShakeDuration)
            {
                float fade = 1f - dashTime / dashShakeDuration;
                dash = dashShakeAmplitude * fade * fade;
            }

            float rotAmp = baseShakeRotation * Weight + dash * Weight;
            float posAmp = baseShakePosition * Weight
                         + (baseShakeRotation > 0f ? dash * Weight * baseShakePosition / baseShakeRotation : 0f);

            IsShaking = rotAmp > 0f || posAmp > 0f;
            if (!IsShaking)
            {
                shakeNode.localPosition = basePosition;
                shakeNode.localRotation = baseRotation;
                return;
            }

            noiseTime += dt * baseShakeFrequency;
            Vector3 rot = new Vector3(Noise(0), Noise(1), Noise(2)) * rotAmp;
            Vector3 pos = new Vector3(Noise(3), Noise(4), Noise(5)) * posAmp;

            shakeNode.localPosition = basePosition + pos;
            shakeNode.localRotation = baseRotation * Quaternion.Euler(rot);
        }

        // Perlin noise remapped to -1..1.
        private float Noise(int channel) => Mathf.PerlinNoise(seeds[channel], noiseTime) * 2f - 1f;

        protected override void OnDash(Vector3 localDirection)
        {
            dashTime = 0f;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            dashTime = float.PositiveInfinity;
            IsShaking = false;
            if (shakeNode != null)
            {
                shakeNode.localPosition = basePosition;
                shakeNode.localRotation = baseRotation;
            }
        }
    }
}
