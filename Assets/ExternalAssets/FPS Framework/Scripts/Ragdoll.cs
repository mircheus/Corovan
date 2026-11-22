using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Akila.FPSFramework
{
    [AddComponentMenu("Akila/FPS Framework/Player/Ragdoll")]
    public class Ragdoll : MonoBehaviour
    {
        public Rigidbody[] rigidbodies { get; set; }
        public Animator animator;

        // CORVAN: the joints, rebased onto the animated pose when the body goes limp.
        private Joint[] joints;

        private void Start()
        {
            rigidbodies = GetComponentsInChildren<Rigidbody>();
            if (!animator) animator = GetComponent<Animator>();
            joints = GetComponentsInChildren<Joint>(); // CORVAN

            foreach (Rigidbody rb in rigidbodies)
            {
                if (rb != GetComponent<Rigidbody>())
                {
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
            }

            Disable();
        }

        public void Enable()
        {
            if (animator) animator.enabled = false;
            RebaseJoints(); // CORVAN

            foreach (Rigidbody rigidbody in rigidbodies)
            {
                rigidbody.isKinematic = false;
            }
        }

        public void Enable(Vector3 deathForce)
        {
            if (animator) animator.enabled = false;
            RebaseJoints(); // CORVAN

            foreach (Rigidbody rigidbody in rigidbodies)
            {
                if (rigidbody != GetComponent<Rigidbody>())
                {
                    rigidbody.isKinematic = false;

                    // CORVAN: push every ragdoll, not only ones with a character controller —
                    // bots have none and would otherwise just slump.
                    rigidbody.AddForce(deathForce, ForceMode.VelocityChange);

                }
            }
        }

        // CORVAN: a joint keeps the relative pose and anchor of the moment it was created — the
        // prefab's rest pose, in Awake. Bones without a body between two jointed ones (Chest,
        // UpperChest, Neck between Spine and Head) are bent by the animation, so on going limp the
        // joint yanked the head back to where the rest pose had it. Re-creating each joint
        // (connectedBody re-assigned, connected anchor auto) takes the current pose as its zero.
        private void RebaseJoints()
        {
            if (joints == null)
                return;

            Physics.SyncTransforms(); // bodies at the pose the Animator just wrote

            foreach (Joint joint in joints)
            {
                if (!joint)
                    continue;

                Rigidbody connected = joint.connectedBody;
                joint.autoConfigureConnectedAnchor = true;
                joint.connectedBody = null;
                joint.connectedBody = connected;
            }
        }

        public void Disable()
        {
            if (animator) animator.enabled = true;

            foreach (Rigidbody rigidbody in rigidbodies)
            {
                if (rigidbody != GetComponent<Rigidbody>())
                {
                    rigidbody.isKinematic = true;
                }
            }
        }
    }
}