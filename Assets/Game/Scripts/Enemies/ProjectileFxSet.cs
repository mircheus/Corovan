using UnityEngine;

namespace FPSGame.Enemies
{
    /// <summary>
    /// The three visual parts of one projectile kind: muzzle flash (at the barrel when it fires), the
    /// projectile body (rides along with the networked projectile) and the impact (where it lands).
    /// Lives on its own prefab so a weapon swaps its whole look by swapping one reference; any part
    /// may be left empty. Purely cosmetic: spawned locally on every peer, never networked.
    /// </summary>
    public class ProjectileFxSet : MonoBehaviour
    {
        [SerializeField] private GameObject muzzle;
        [SerializeField] private GameObject projectile;
        [SerializeField] private GameObject hit;

        [Header("Scale")]
        [Tooltip("Multiplies the size of each part, so one effect can be reused at another size.")]
        [SerializeField, Min(0.01f)] private float muzzleScale = 1f;
        [SerializeField, Min(0.01f)] private float projectileScale = 1f;
        [SerializeField, Min(0.01f)] private float hitScale = 1f;

        /// <summary>Plays the muzzle flash at the barrel, pointing along the shot.</summary>
        public void PlayMuzzle(Vector3 position, Vector3 direction)
        {
            if (muzzle != null && direction.sqrMagnitude > 0.0001f)
                SpawnOneShot(muzzle, position, Quaternion.LookRotation(direction), muzzleScale);
        }

        /// <summary>Plays the impact, facing out of the surface that was hit.</summary>
        public void PlayHit(Vector3 position, Vector3 normal)
        {
            if (hit != null)
                SpawnOneShot(hit, position, normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity, hitScale);
        }

        /// <summary>Attaches the projectile body to a flying projectile; it is destroyed with it.</summary>
        public GameObject AttachProjectile(Transform parent)
        {
            if (projectile == null)
                return null;

            GameObject body = Instantiate(projectile, parent);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale *= projectileScale;
            return body;
        }

        private static void SpawnOneShot(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            GameObject instance = Instantiate(prefab, position, rotation);
            instance.transform.localScale *= scale;
            Destroy(instance, Lifetime(instance));
        }

        // Long enough for the slowest particle system of the effect to finish.
        private static float Lifetime(GameObject instance)
        {
            float longest = 0.1f;
            foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                float life = main.duration + main.startDelay.constantMax + main.startLifetime.constantMax;
                if (life > longest)
                    longest = life;
            }
            return longest;
        }
    }
}
