using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Plays one-shot destruction effects (any number) that live as inactive Play On Awake
    /// children of the destroyed object: activating one starts it. Nothing is instantiated.
    /// The destroyed object only hides its renderers/colliders, so the effects stay where they are.
    /// Purely cosmetic: called locally on every client, never networked.
    /// </summary>
    public static class DestructionEffects
    {
        public static void Play(GameObject[] effects)
        {
            if (effects == null)
                return;

            foreach (GameObject effect in effects)
            {
                if (effect == null)
                    continue;

                if (!effect.scene.IsValid())
                {
                    Debug.LogWarning($"Destruction effect '{effect.name}' is a prefab asset; place it as a child of the object instead.", effect);
                    continue;
                }

                effect.SetActive(true);
            }
        }
    }
}
