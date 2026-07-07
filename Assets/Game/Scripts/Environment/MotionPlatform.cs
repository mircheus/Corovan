using UnityEngine;

namespace FPSGame.Environment
{
    // Marks a collider as a surface driven by the MotionDirector. A player only feels the
    // ride (lateral push, and later fall risk) while standing on a MotionPlatform, so stepping
    // off the convoy decouples them from the signal.
    //
    // Put this on the PHYSICAL floor collider the player stands on — not on the tilted visual
    // mesh (that one carries PlatformMotionReceiver and should stay collider-free or separate).
    public class MotionPlatform : MonoBehaviour
    {
    }
}
