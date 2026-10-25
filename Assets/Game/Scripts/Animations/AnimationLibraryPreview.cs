using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FPSGame.Animations
{
    // Plays one clip of an animation library FBX through a PlayableGraph, no Animator Controller needed.
    // Put a subclass on a model with an Animator and switch the enum in Play mode.
    // The FBX is Humanoid, so any humanoid avatar can play its clips.
    [RequireComponent(typeof(Animator))]
    public abstract class AnimationLibraryPreview<TAnim> : MonoBehaviour where TAnim : struct, Enum
    {
        private const string ClipPrefix = "Armature|";

        [SerializeField] private TAnim animationToPlay;
        [SerializeField, Min(0f)] private float speed = 1f;
        [Tooltip("Restart non-looping clips when they end.")]
        [SerializeField] private bool loop = true;
        [Tooltip("Off: root motion clips (*_RM) play in place.")]
        [SerializeField] private bool applyRootMotion;
        [Tooltip("Filled from the FBX automatically in the Editor.")]
        [SerializeField] private AnimationClip[] clips;

        private Animator animator;
        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable clipPlayable;
        private TAnim playing;
        private Vector3 startPosition;
        private Quaternion startRotation;

        protected abstract string FbxPath { get; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            animator.runtimeAnimatorController = null;
            if (animator.avatar == null || !animator.avatar.isHuman)
                Debug.LogWarning($"[{GetType().Name}] Animator has no humanoid Avatar — humanoid clips will stay in T-pose.", this);
            startPosition = transform.localPosition;
            startRotation = transform.localRotation;

            graph = PlayableGraph.Create($"{name}.{GetType().Name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            Play(animationToPlay);
            graph.Play();
        }

        private void Update()
        {
            if (!graph.IsValid())
                return;

            if (!EqualityComparer<TAnim>.Default.Equals(playing, animationToPlay))
                Play(animationToPlay);

            if (!clipPlayable.IsValid())
                return;

            clipPlayable.SetSpeed(speed);
            animator.applyRootMotion = applyRootMotion;

            AnimationClip clip = clipPlayable.GetAnimationClip();
            if (loop && !clip.isLooping && clipPlayable.GetTime() >= clip.length)
                Restart();
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        public void Play(TAnim anim)
        {
            animationToPlay = anim;
            playing = anim;

            AnimationClip clip = FindClip(anim);
            if (clip == null)
            {
                Debug.LogWarning($"[{GetType().Name}] Clip '{ClipPrefix}{anim}' not found in the clip list.", this);
                return;
            }

            if (clipPlayable.IsValid())
                clipPlayable.Destroy();

            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(clipPlayable);
            Restart();
        }

        private void Restart()
        {
            if (!applyRootMotion)
                transform.SetLocalPositionAndRotation(startPosition, startRotation);
            clipPlayable.SetTime(0);
            clipPlayable.SetTime(0); // twice so the previous frame's root delta is not applied
        }

        private AnimationClip FindClip(TAnim anim)
        {
            if (clips == null)
                return null;

            string wanted = anim.ToString();
            foreach (AnimationClip clip in clips)
            {
                if (clip == null)
                    continue;
                string clipName = clip.name.StartsWith(ClipPrefix) ? clip.name.Substring(ClipPrefix.Length) : clip.name;
                if (clipName == wanted)
                    return clip;
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (clips == null || clips.Length == 0)
                LoadClipsFromFbx();
            AssignAvatarIfMissing();
        }

        // An Animator added by RequireComponent starts without an Avatar; humanoid clips need the FBX one.
        private void AssignAvatarIfMissing()
        {
            var anim = GetComponent<Animator>();
            if (anim == null || anim.avatar != null)
                return;

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (asset is Avatar avatar)
                {
                    anim.avatar = avatar;
                    EditorUtility.SetDirty(anim);
                    return;
                }
            }
        }

        [ContextMenu("Reload Clips From FBX")]
        private void LoadClipsFromFbx()
        {
            var list = new List<AnimationClip>();
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                // Skip Unity's hidden "__preview__" copies.
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    list.Add(clip);
            }
            clips = list.ToArray();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
