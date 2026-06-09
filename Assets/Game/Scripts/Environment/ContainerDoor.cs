using System.Collections;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Main script on a container. Listens to a child ContainerLock; once the lock
    // is shot to death the doors swing open around their local Y axis (tweened with
    // an AnimationCurve). Open state is networked via a SyncVar so every client —
    // and late joiners — see the container in the correct state.
    public class ContainerDoor : NetworkBehaviour
    {
        [Header("Doors")]
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;

        [Header("Lock")]
        [SerializeField] private ContainerLock containerLock;

        [Header("Open State")]
        [Tooltip("Local Y rotation (degrees) added to the left door when open.")]
        [SerializeField] private float leftDoorOpenAngle = -110f;

        [Tooltip("Local Y rotation (degrees) added to the right door when open.")]
        [SerializeField] private float rightDoorOpenAngle = 110f;

        [Tooltip("Seconds the doors take to swing fully open.")]
        [SerializeField] private float openDuration = 1.2f;

        [Tooltip("Easing for the swing: maps normalized time (0..1) to progress (0..1).")]
        [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SyncVar(hook = nameof(OnOpenChanged))]
        private bool isOpen;

        private Quaternion leftClosedRot, rightClosedRot;
        private Quaternion leftOpenRot, rightOpenRot;
        private Coroutine swingRoutine;
        private bool clientStarted;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            // Capture the authored (closed) rotations and precompute the open targets.
            if (leftDoor != null)
            {
                leftClosedRot = leftDoor.localRotation;
                leftOpenRot = leftClosedRot * Quaternion.Euler(0f, leftDoorOpenAngle, 0f);
            }
            if (rightDoor != null)
            {
                rightClosedRot = rightDoor.localRotation;
                rightOpenRot = rightClosedRot * Quaternion.Euler(0f, rightDoorOpenAngle, 0f);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (containerLock != null)
                containerLock.OnBroken += ServerOpen;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (containerLock != null)
                containerLock.OnBroken -= ServerOpen;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientStarted = true;
            // Late joiners: container may already be open — snap doors with no animation.
            if (isOpen)
                ApplyDoorRotations(leftOpenRot, rightOpenRot);
        }

        // Invoked server-side via ContainerLock.OnBroken.
        [Server]
        private void ServerOpen()
        {
            if (isOpen)
                return;
            isOpen = true; // hook drives the swing on every client (host included)
        }

        private void OnOpenChanged(bool oldValue, bool newValue)
        {
            if (!newValue)
                return;

            // Hook can fire during initial spawn (before OnStartClient) for late
            // joiners — in that case snap instead of animating.
            if (!clientStarted)
            {
                ApplyDoorRotations(leftOpenRot, rightOpenRot);
                return;
            }

            if (swingRoutine != null)
                StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingOpen());
        }

        private IEnumerator SwingOpen()
        {
            float t = 0f;
            Quaternion leftFrom = leftDoor != null ? leftDoor.localRotation : Quaternion.identity;
            Quaternion rightFrom = rightDoor != null ? rightDoor.localRotation : Quaternion.identity;

            while (t < openDuration)
            {
                t += Time.deltaTime;
                float k = openCurve.Evaluate(Mathf.Clamp01(t / openDuration));

                if (leftDoor != null)
                    leftDoor.localRotation = Quaternion.Slerp(leftFrom, leftOpenRot, k);
                if (rightDoor != null)
                    rightDoor.localRotation = Quaternion.Slerp(rightFrom, rightOpenRot, k);

                yield return null;
            }

            ApplyDoorRotations(leftOpenRot, rightOpenRot);
            swingRoutine = null;
        }

        private void ApplyDoorRotations(Quaternion left, Quaternion right)
        {
            if (leftDoor != null)
                leftDoor.localRotation = left;
            if (rightDoor != null)
                rightDoor.localRotation = right;
        }
    }
}
