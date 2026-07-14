using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Tuning profile for a <see cref="RoadBody"/>. Groups the "feel" parameters (conveyor
    /// grip, tumble limits, airstream, settle thresholds) so a family of props can share one
    /// asset and be re-tuned in a single place — e.g. a "HeavyCrate" profile vs a "LightCan"
    /// profile. Same authoring pattern as <see cref="FPSGame.Weapons.WeaponData"/>.
    ///
    /// Only shared feel values live here. Per-instance concerns (the testing startDetached
    /// flag) stay on the <see cref="RoadBody"/> component.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRoadBodyData", menuName = "FPS Game/Road Body Data")]
    public class RoadBodyData : ScriptableObject
    {
        [Header("Conveyor friction")]
        [Tooltip("Coulomb friction coefficient between the prop and the belt. Drives BOTH the " +
                 "-Z catch-up and the tumble (they are one force by design). ~1.0-1.5 feels grabby.")]
        [SerializeField] private float mu = 0.6f;
        [Tooltip("Angular velocity clamp (rad/s). Default 7 chokes the tumble; too high sends it " +
                 "into a blender. Raise for more dramatic spin on heavy/large props.")]
        [SerializeField] private float maxAngularVelocity = 30f;
        [Tooltip("Light angular damping so the tumble decays readably. Lower = spins longer.")]
        [SerializeField] private float angularDamping = 0.1f;

        [Header("Center of mass")]
        [Tooltip("Offset (local space) added on top of the collider-derived centre of mass. " +
                 "Raising Y lifts the COM above the contact, lengthening the topple lever so " +
                 "heavy props tumble harder off an edge. Zero = pure auto (recommended default). " +
                 "Does NOT touch the inertia tensor, only where torque pivots.")]
        [SerializeField] private Vector3 centerOfMassOffset = Vector3.zero;

        [Header("Roughness")]
        [Tooltip("Multiplier on the vertical kicks the roughness field imparts at the contact point.")]
        [SerializeField] private float bumpImpulseScale = 1.0f;

        [Header("Airstream")]
        [Tooltip("Quadratic air drag against the -V headwind while airborne. Light props (low " +
                 "mass) get flung back harder by the same drag; heavy props barely feel it.")]
        [SerializeField] private float dragCoefficient = 0.02f;

        [Header("Settle")]
        [Tooltip("Slip speed (m/s) below which the prop is considered to match the belt.")]
        [SerializeField] private float settleEpsilon = 0.35f;
        [Tooltip("Consecutive fixed steps under settleEpsilon (with road contact) before settling.")]
        [SerializeField] private int settleFrames = 10;

        [Header("Despawn")]
        [Tooltip("World Z at which the prop is removed once it has drifted far enough back.")]
        [SerializeField] private float despawnZ = -160f;

        public float Mu => mu;
        public float MaxAngularVelocity => maxAngularVelocity;
        public float AngularDamping => angularDamping;
        public Vector3 CenterOfMassOffset => centerOfMassOffset;
        public float BumpImpulseScale => bumpImpulseScale;
        public float DragCoefficient => dragCoefficient;
        public float SettleEpsilon => settleEpsilon;
        public int SettleFrames => settleFrames;
        public float DespawnZ => despawnZ;
    }
}
