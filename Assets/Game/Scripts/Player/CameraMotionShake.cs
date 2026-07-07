using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Local player's camera consumer of the MotionDirector signal. Turns the shared ride signal
    // into camera shake: high-frequency jitter scaled by the signal, a bank (roll) into the
    // swerve for readability, and a vertical kick on bumps. A rising rumble during the telegraph
    // window sells the incoming lurch.
    //
    // Pure cosmetic and local — feeds PlayerCamera.SetShakeOffsets. Gated by OnMotionPlatform so
    // the camera only shakes while riding the convoy. Runs for the local owner only.
    public class CameraMotionShake : NetworkBehaviour
    {
        [Header("Jitter")]
        [Tooltip("Positional jitter (metres) at signal magnitude 1.")]
        [SerializeField] private float positionShake = 0.03f;
        [Tooltip("Rotational jitter (degrees) at signal magnitude 1.")]
        [SerializeField] private float rotationShake = 0.8f;
        [SerializeField] private float jitterFrequency = 18f;

        [Header("Directional response")]
        [Tooltip("Camera bank (degrees) into the swerve per lateral signal unit.")]
        [SerializeField] private float leanIntoSwerve = 3f;
        [Tooltip("Vertical camera kick (metres) per vertical bump signal unit.")]
        [SerializeField] private float bumpKick = 0.05f;

        [Header("Telegraph")]
        [Tooltip("Extra jitter intensity added across the telegraph lead window.")]
        [SerializeField] private float telegraphRumble = 0.3f;

        [Header("Gate")]
        [Tooltip("Seconds to fade shake in/out when stepping on/off the platform.")]
        [SerializeField] private float gateFade = 0.2f;

        private PlayerCamera playerCamera;
        private PlayerController controller;
        private float gate;

        private void Awake()
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
            controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (!isOwned)
                return;

            MotionDirector director = MotionDirector.Instance;
            if (playerCamera == null || director == null)
                return;

            float target = (controller != null && controller.OnMotionPlatform) ? 1f : 0f;
            gate = Mathf.MoveTowards(gate, target, Time.deltaTime / Mathf.Max(0.0001f, gateFade));

            if (gate <= 0.001f)
            {
                playerCamera.SetShakeOffsets(Vector3.zero, Vector3.zero);
                return;
            }

            float lateral = director.LateralSignal;
            float vertical = director.VerticalSignal;
            float intensity = Mathf.Abs(lateral) + Mathf.Abs(vertical) + director.TelegraphProgress * telegraphRumble;

            float t = Time.time * jitterFrequency;
            float nx = (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f;
            float nPitch = (Mathf.PerlinNoise(t, 3.3f) - 0.5f) * 2f;
            float nRoll = (Mathf.PerlinNoise(5.1f, t) - 0.5f) * 2f;

            Vector3 pos = new Vector3(nx, ny, 0f) * (positionShake * intensity);
            pos.y += vertical * bumpKick;

            Vector3 rot = new Vector3(nPitch, 0f, nRoll) * (rotationShake * intensity);
            rot.z += -lateral * leanIntoSwerve; // bank into the swerve

            playerCamera.SetShakeOffsets(pos * gate, rot * gate);
        }
    }
}
