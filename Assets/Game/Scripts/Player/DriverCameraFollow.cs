using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Car feel C3: the driver's view takes a share of the car body's visual turn (<see cref="FPSGame.Vehicle.CarBodyVisual"/>)
    /// on top of free mouse look — yaw by <see cref="cameraYawFollow"/>, pitch and roll by <see cref="cameraTiltFollow"/>.
    /// The camera rig itself is never under the car's visual root (the player is only pinned to the seat), so
    /// without this it gets none of the body's motion. Local driver only.
    /// </summary>
    public class DriverCameraFollow : DriverFeelEffect
    {
        [Header("Share of the body's turn")]
        [SerializeField, Range(0f, 1f)] private float cameraYawFollow = 0.5f;
        [SerializeField, Range(0f, 1f)] private float cameraTiltFollow = 0.3f;

        // Last body angles seen while driving; kept for the fade-out after leaving the car.
        private float bodyYaw, bodyPitch, bodyRoll;
        private Quaternion carRotation = Quaternion.identity;

        public Vector3 CurrentOffsetAngles { get; private set; }

        protected override void Tick(float dt)
        {
            if (Body != null)
            {
                bodyYaw = Body.Yaw;
                bodyPitch = Body.Pitch;
                bodyRoll = Body.Roll;
                carRotation = Body.transform.rotation;
            }

            float w = Weight;
            float yaw = bodyYaw * cameraYawFollow * w;
            float pitch = bodyPitch * cameraTiltFollow * w;
            float roll = bodyRoll * cameraTiltFollow * w;
            CurrentOffsetAngles = new Vector3(pitch, yaw, roll);

            if (playerCamera == null)
                return;

            if (w <= 0f)
            {
                playerCamera.SetBaseRotationOffset(Quaternion.identity);
                return;
            }

            // Same composition as CarBodyVisual (yaw, then nose-up pitch, then roll), in the car's frame.
            Quaternion local = Quaternion.AngleAxis(yaw, Vector3.up)
                             * Quaternion.AngleAxis(-pitch, Vector3.right)
                             * Quaternion.AngleAxis(roll, Vector3.forward);
            playerCamera.SetBaseRotationOffset(carRotation * local * Quaternion.Inverse(carRotation));
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (playerCamera != null)
                playerCamera.SetBaseRotationOffset(Quaternion.identity);
        }
    }
}
