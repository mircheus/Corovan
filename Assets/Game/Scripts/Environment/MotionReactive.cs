using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Marks a Rigidbody (loot, prop) as reacting to the MotionDirector's lateral field.
    // The force is applied server-side by MotionDirector; positions replicate via the
    // object's NetworkTransform. Add alongside CarryableObject on loot prefabs.
    [RequireComponent(typeof(Rigidbody))]
    public class MotionReactive : NetworkBehaviour
    {
        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (MotionDirector.Instance != null)
                MotionDirector.Instance.RegisterLoot(rb);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (MotionDirector.Instance != null)
                MotionDirector.Instance.UnregisterLoot(rb);
        }
    }
}
