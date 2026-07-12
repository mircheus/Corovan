using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Passive data provider that sits on the road's (static) collider. It answers two
    /// questions for a <see cref="RoadBody"/> resting on the road:
    ///  - how fast the belt moves under a given point (<see cref="GetSurfaceVelocity"/>), and
    ///  - how rough the surface is there (<see cref="GetRoughness"/>).
    ///
    /// It applies NOTHING itself. The conveyor logic (Coulomb friction against the belt
    /// velocity, bump impulses) lives in <see cref="RoadBody.OnCollisionStay"/>, precisely so
    /// it acts ONLY on props that carry a RoadBody. Everything else that touches this same
    /// road — pursuers on WheelColliders, players on CharacterControllers — must be left
    /// alone, or the conveyor would wrongly drag them backward.
    ///
    /// The road collider is expected to be STATIC in world space. The belt speed is a virtual
    /// field derived from <see cref="RoadScroll"/>, not real collider motion, and the road's
    /// PhysicMaterial friction MUST be 0 (see road-physics-spec.md §7) so PhysX doesn't fight
    /// the hand-computed friction.
    /// </summary>
    public class RoadSurface : MonoBehaviour
    {
        [Header("Roughness")]
        [Tooltip("Peak height of the roughness field, in metres.")]
        [SerializeField] private float bumpAmplitude = 0.06f;
        [Tooltip("Wavelength along the direction of travel, in metres.")]
        [SerializeField] private float bumpScaleZ = 3.0f;
        [Tooltip("Wavelength across the road, in metres.")]
        [SerializeField] private float bumpScaleX = 2.0f;

        // Sideways belt drift (m/s) written by MotionDirector during a swerve. Server-side.
        private float _lateralDrift;

        /// <summary>
        /// Belt velocity at a world point. Z is the convoy speed; X is the sideways drift a
        /// convoy manoeuvre imparts. Falls back to zero before RoadScroll has spawned.
        /// </summary>
        public Vector3 GetSurfaceVelocity(Vector3 worldPos)
        {
            Vector3 v = RoadScroll.Instance != null ? RoadScroll.Instance.BeltVelocity : Vector3.zero;
            v.x += _lateralDrift;
            return v;
        }

        /// <summary>
        /// Roughness profile, sampled in the ROAD's frame of reference (ground Z), so the same
        /// landscape feeds both the truck shake and prop hops. A settled prop's ground Z is
        /// constant, so its hops naturally stop — physically correct.
        /// </summary>
        public float GetRoughness(float worldX, float worldZ)
        {
            double groundZ = RoadScroll.Instance != null ? RoadScroll.Instance.ToGroundZ(worldZ) : worldZ;
            float n = Mathf.PerlinNoise(
                worldX / bumpScaleX,
                (float)(groundZ / bumpScaleZ) % 1024f);
            return (n - 0.5f) * 2f * bumpAmplitude;
        }

        /// <summary>
        /// Set the sideways belt drift (m/s). Server-only by convention (called from
        /// MotionDirector during a swerve); RoadSurface is a plain MonoBehaviour so it can't
        /// carry Mirror's [Server] guard.
        /// </summary>
        public void SetLateralDrift(float driftMetersPerSecond) => _lateralDrift = driftMetersPerSecond;
    }
}
