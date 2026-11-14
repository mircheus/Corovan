using UnityEngine;
using UnityEngine.AI;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Crosses NavMesh links (drop-downs from <c>NavMeshDropLinkGenerator</c>, hand-placed NavMeshLinks)
    /// as a ballistic jump instead of Unity's default straight glide at walking speed.
    ///
    /// Turns off <see cref="NavMeshAgent.autoTraverseOffMeshLink"/>; when the agent reaches a link it
    /// moves the transform along a parabola under <see cref="gravity"/> — up by <see cref="jumpHeight"/>
    /// above the higher end, then down to the landing — and hands the agent back with
    /// <see cref="NavMeshAgent.CompleteOffMeshLink"/>.
    ///
    /// Not networked: only the server's agent is enabled (the brains disable it on clients), so the
    /// jump runs there and clients see it through the NetworkTransform. The brain checks
    /// <see cref="IsJumping"/> and pauses its own steering, facing and attacks while airborne.
    /// Dying mid-air cancels the jump (the agent gets disabled, the ragdoll takes over).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentLinkJumper : MonoBehaviour
    {
        [Tooltip("Apex of the jump above the higher end of the link. 0 = just step off and fall.")]
        [SerializeField] private float jumpHeight = 0.4f;
        [Tooltip("Gravity of the jump (m/s²). Higher = snappier, shorter airtime.")]
        [SerializeField] private float gravity = 18f;
        [Tooltip("Turn towards the jump direction at this rate while airborne.")]
        [SerializeField] private float turnSpeed = 12f;
        [Tooltip("Seconds the agent stands still after landing (absorbing the fall). 0 = runs on.")]
        [SerializeField] private float landingPause = 0.15f;

        // Longest walk to the link's start before jumping; farther = jump from where the agent is.
        private const float MaxApproach = 2f;

        private NavMeshAgent agent;

        private bool approaching, jumping;
        private Vector3 approachTo;
        private Vector3 start, end;
        private float g, vy0, duration, elapsed;
        private float pauseLeft;

        /// <summary>On a link (walking up to the edge or airborne) or in the short landing pause after one.</summary>
        public bool IsJumping => approaching || jumping || pauseLeft > 0f;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.autoTraverseOffMeshLink = false;
        }

        private void Update()
        {
            if (!agent.enabled)
            {
                // Died mid-air (or a client copy): drop everything, the ragdoll owns the body.
                approaching = jumping = false;
                pauseLeft = 0f;
                return;
            }

            if (approaching)
            {
                Approach();
                return;
            }

            if (jumping)
            {
                Fly();
                return;
            }

            if (pauseLeft > 0f)
            {
                pauseLeft -= Time.deltaTime;
                if (pauseLeft <= 0f && agent.isOnNavMesh)
                    agent.isStopped = false;
                return;
            }

            if (agent.isOnOffMeshLink)
                BeginLink(agent.currentOffMeshLinkData);
        }

        // The agent enters a link ~2 radii before its start point (Detour's trigger radius);
        // walk the rest on foot so the jump leaves from the edge, not from the middle of the floor.
        private void BeginLink(OffMeshLinkData link)
        {
            end = link.endPos + Vector3.up * agent.baseOffset;
            approachTo = link.startPos + Vector3.up * agent.baseOffset;

            Vector3 gap = approachTo - transform.position;
            gap.y = 0f;
            if (gap.sqrMagnitude > 0.0025f && gap.sqrMagnitude < MaxApproach * MaxApproach)
                approaching = true;
            else
                BeginJump();
        }

        private void Approach()
        {
            Vector3 pos = transform.position;
            Vector3 gap = approachTo - pos;
            gap.y = 0f;
            float step = agent.speed * Time.deltaTime;

            if (gap.magnitude <= step)
            {
                transform.position = new Vector3(approachTo.x, pos.y, approachTo.z);
                approaching = false;
                BeginJump();
                return;
            }

            transform.position = pos + gap.normalized * step;
            Turn(end - pos);
        }

        private void BeginJump()
        {
            start = transform.position;

            // Ballistic arc: launch speed reaches the apex, then free fall to the landing height.
            g = Mathf.Max(0.1f, gravity);
            float apex = Mathf.Max(start.y, end.y) + Mathf.Max(0f, jumpHeight);
            vy0 = Mathf.Sqrt(2f * g * (apex - start.y));
            float tUp = vy0 / g;
            float tDown = Mathf.Sqrt(2f * (apex - end.y) / g);
            duration = Mathf.Max(0.05f, tUp + tDown);
            elapsed = 0f;
            jumping = true;
        }

        private void Fly()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Min(elapsed, duration);
            float k = t / duration;

            Vector3 pos = Vector3.Lerp(start, end, k);
            pos.y = start.y + vy0 * t - 0.5f * g * t * t;
            transform.position = pos;

            Turn(end - start);

            if (elapsed < duration)
                return;

            transform.position = end;
            jumping = false;

            if (agent.isOnOffMeshLink)
                agent.CompleteOffMeshLink();
            else
                agent.Warp(end); // the brain reset the path mid-air; put the agent where the body is

            if (landingPause > 0f && agent.isOnNavMesh)
            {
                pauseLeft = landingPause;
                agent.isStopped = true;
            }
        }

        private void Turn(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }
    }
}
