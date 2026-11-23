using UnityEngine;
using UnityEngine.InputSystem;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    /// <summary>
    /// Car feel A/B switches and the driver's debug overlay (local driver only, IMGUI). Each key flips one effect
    /// so it can be felt with and without: C1 inertia and C2 body visual on the driven car (this client only —
    /// C1 runs here anyway, C2 only changes this client's view), C3 / C5 / C6 on this player. The same flags are
    /// inspector checkboxes on the components themselves.
    /// </summary>
    public class CarFeelDebug : MonoBehaviour
    {
        [SerializeField] private bool hotkeysEnabled = true;
        [SerializeField] private Key toggleInertiaKey = Key.F1;
        [SerializeField] private Key toggleBodyVisualKey = Key.F2;
        [SerializeField] private Key toggleCameraFollowKey = Key.F3;
        [SerializeField] private Key toggleFovKey = Key.F4;
        [SerializeField] private Key toggleShakeKey = Key.F10;
        [SerializeField] private Key toggleOverlayKey = Key.F12;
        [SerializeField] private bool showOverlay;

        private PlayerInputHandler input;
        private DriverCameraFollow cameraFollow;
        private DriverFovController fov;
        private DriverCameraShake shake;
        private GUIStyle style;

        private void Awake()
        {
            input = GetComponent<PlayerInputHandler>();
            cameraFollow = GetComponent<DriverCameraFollow>();
            fov = GetComponent<DriverFovController>();
            shake = GetComponent<DriverCameraShake>();
        }

        private CarController DrivenCar => input != null && input.isOwned && input.IsInVehicle ? input.OccupiedVehicle : null;

        private void Update()
        {
            if (!hotkeysEnabled || input == null || !input.isOwned)
                return;
            var kb = Keyboard.current;
            if (kb == null)
                return;

            CarController car = DrivenCar;
            CarBodyVisual body = car != null ? car.GetComponent<CarBodyVisual>() : null;

            if (Pressed(kb, toggleInertiaKey) && car != null)
                car.InertiaEnabled = !car.InertiaEnabled;
            if (Pressed(kb, toggleBodyVisualKey) && body != null)
                body.EffectEnabled = !body.EffectEnabled;
            if (Pressed(kb, toggleCameraFollowKey) && cameraFollow != null)
                cameraFollow.EffectEnabled = !cameraFollow.EffectEnabled;
            if (Pressed(kb, toggleFovKey) && fov != null)
                fov.EffectEnabled = !fov.EffectEnabled;
            if (Pressed(kb, toggleShakeKey) && shake != null)
                shake.EffectEnabled = !shake.EffectEnabled;
            if (Pressed(kb, toggleOverlayKey))
                showOverlay = !showOverlay;
        }

        private static bool Pressed(Keyboard kb, Key key) => key != Key.None && kb[key].wasPressedThisFrame;

        private void OnGUI()
        {
            if (!showOverlay)
                return;
            CarController car = DrivenCar;
            if (car == null)
                return;
            CarBodyVisual body = car.GetComponent<CarBodyVisual>();

            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Car feel</b>");
            sb.AppendLine($"[{toggleInertiaKey}] C1 inertia: {OnOff(car.InertiaEnabled)}");
            sb.AppendLine($"[{toggleBodyVisualKey}] C2 body visual: {OnOff(body != null && body.EffectEnabled)}");
            sb.AppendLine($"[{toggleCameraFollowKey}] C3 camera follow: {OnOff(cameraFollow != null && cameraFollow.EffectEnabled)}");
            sb.AppendLine($"[{toggleFovKey}] C5 FOV: {OnOff(fov != null && fov.EffectEnabled)}");
            sb.AppendLine($"[{toggleShakeKey}] C6 shake: {OnOff(shake != null && shake.EffectEnabled)}");
            if (body != null)
            {
                Vector3 v = body.Velocity;
                Vector3 a = body.Acceleration;
                Vector3 aRef = body.ReferenceAcceleration;
                sb.AppendLine($"rel. speed  x {v.x,6:F1}  z {v.z,6:F1} m/s");
                sb.AppendLine($"accel/aRef  x {a.x / aRef.x,6:F2}  z {a.z / aRef.z,6:F2}");
                sb.AppendLine($"motion  yaw {body.Yaw,6:F1}  pitch {body.Pitch,5:F1}  roll {body.Roll,5:F1}");
                sb.AppendLine($"body    yaw {body.VisualYaw,6:F1}  pitch {body.VisualPitch,5:F1}  roll {body.VisualRoll,5:F1}");
            }
            if (cameraFollow != null)
            {
                Vector3 c = cameraFollow.CurrentOffsetAngles;
                sb.AppendLine($"camera  yaw {c.y,5:F1}  pitch {c.x,5:F1}  roll {c.z,5:F1}");
            }
            if (fov != null)
                sb.AppendLine($"FOV offset {fov.CurrentOffset,5:F1}°");
            if (shake != null)
                sb.AppendLine($"shake: {(shake.IsShaking ? "active" : "off")}");
            sb.Append($"[{toggleOverlayKey}] hide");

            var rect = new Rect(10f, Screen.height * 0.35f, 360f, 280f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), sb.ToString(), style);
        }

        private static string OnOff(bool on) => on ? "<color=#7f7>on</color>" : "<color=#f77>off</color>";
    }
}
