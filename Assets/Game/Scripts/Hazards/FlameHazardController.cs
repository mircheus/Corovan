using System;
using UnityEngine;
using Mirror;

namespace FPSGame.Hazards
{
    /// <summary>
    /// Runs a set of <see cref="FlameHazard"/>s through repeating patterns: a pattern is a list of
    /// steps, each lighting some flames for a while, with an optional all-out pause after it.
    /// <para>
    /// Server-only, and a plain MonoBehaviour on purpose: the flames sync themselves, and a
    /// NetworkIdentity here would forbid keeping them as children (Mirror allows no nested identities).
    /// </para>
    /// </summary>
    public class FlameHazardController : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            [Tooltip("Indices in the Flames array lit during this step. Every other flame is out.")]
            public int[] flames;
            [Tooltip("Seconds these flames stay lit.")]
            [Min(0.05f)]
            public float duration = 2f;
            [Tooltip("Seconds with every flame out before the next step. 0 = switch straight away.")]
            [Min(0f)]
            public float pauseAfter = 0.5f;
        }

        [Serializable]
        public class Pattern
        {
            [Tooltip("Only a label for the Inspector.")]
            public string name;
            [Tooltip("Played in order, then from the first step again.")]
            public Step[] steps;
        }

        private const int NoPattern = -1;

        [SerializeField] private FlameHazard[] flames;
        [SerializeField] private Pattern[] patterns;

        [Header("Start")]
        [Tooltip("Start by itself as soon as the flames are spawned. Off = waits for Play / PlayPattern.")]
        [SerializeField] private bool playOnStart = true;
        [Tooltip("Index in the Patterns array used by Play On Start and Play().")]
        [Min(0)]
        [SerializeField] private int startPattern;

        private Pattern current;
        private int stepIndex;
        private bool inPause;
        private float phaseEndsAt;

        // A request waits here until the flames are spawned: before that their TurnOn does nothing.
        private int pendingPattern = NoPattern;

        public bool IsPlaying => current != null;

        private void Awake()
        {
            if (playOnStart)
                pendingPattern = startPattern;
        }

        // ── Public control ────────────────────────────────────────────────
        // Safe to wire to UnityEvents that fire on every peer: clients do nothing.

        /// <summary>Plays the Start Pattern from its first step.</summary>
        public void Play() => PlayPattern(startPattern);

        /// <summary>Switches to a pattern (index in the Patterns array) from its first step.</summary>
        public void PlayPattern(int index)
        {
            if (NetworkServer.active)
                pendingPattern = index;
        }

        /// <summary>Stops the pattern and puts every flame out.</summary>
        public void Stop()
        {
            if (!NetworkServer.active)
                return;

            pendingPattern = NoPattern;
            current = null;
            ApplyStep(null);
        }

        // ── Server tick ───────────────────────────────────────────────────

        private void Update()
        {
            if (!NetworkServer.active)
                return;

            if (pendingPattern != NoPattern && FlamesSpawned())
            {
                Begin(pendingPattern);
                pendingPattern = NoPattern;
                return;
            }

            if (current == null || Time.time < phaseEndsAt)
                return;

            // Adding to phaseEndsAt instead of Time.time keeps the rhythm from drifting by a frame per phase.
            Step step = current.steps[stepIndex];
            if (!inPause && step.pauseAfter > 0f)
            {
                inPause = true;
                ApplyStep(null);
                phaseEndsAt += step.pauseAfter;
                return;
            }

            EnterStep((stepIndex + 1) % current.steps.Length, phaseEndsAt);
        }

        private bool FlamesSpawned()
        {
            foreach (FlameHazard flame in flames)
            {
                if (flame != null && !flame.isServer)
                    return false;
            }
            return true;
        }

        private void Begin(int index)
        {
            if (patterns == null || index < 0 || index >= patterns.Length || patterns[index].steps.Length == 0)
            {
                Debug.LogWarning($"[FlameHazardController] '{name}': pattern {index} doesn't exist or has no steps.", this);
                return;
            }

            current = patterns[index];
            WarnAboutBadIndices(index);
            EnterStep(0, Time.time);
        }

        private void EnterStep(int index, float startTime)
        {
            stepIndex = index;
            inPause = false;
            Step step = current.steps[index];
            ApplyStep(step);
            phaseEndsAt = startTime + step.duration;
        }

        // Each flame is set exactly once, so one lit in two steps in a row doesn't flicker.
        // Null = all out. TurnOn / TurnOff also take over from a flame's own cycle.
        private void ApplyStep(Step step)
        {
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null)
                    continue;

                if (step != null && Array.IndexOf(step.flames, i) >= 0)
                    flames[i].TurnOn();
                else
                    flames[i].TurnOff();
            }
        }

        private void WarnAboutBadIndices(int patternIndex)
        {
            foreach (Step step in current.steps)
            {
                foreach (int flame in step.flames)
                {
                    if (flame < 0 || flame >= flames.Length || flames[flame] == null)
                        Debug.LogWarning($"[FlameHazardController] '{name}': pattern {patternIndex} uses flame {flame}, which isn't assigned.", this);
                }
            }
        }
    }
}
