using UnityEngine;

namespace FPSGame.Environment
{
    public class InfiniteRoad : MonoBehaviour
    {
        [Header("Tiles")]
        [SerializeField] private Transform[] tiles;

        [Header("Settings")]
        [SerializeField] private float scrollSpeed = 20f;
        [SerializeField] private float tileLength = 100f;
        [SerializeField] private float recycleAtZ = -100f;

        private void Update()
        {
            float delta = scrollSpeed * Time.deltaTime;

            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].position -= new Vector3(0f, 0f, delta);

                if (tiles[i].position.z <= recycleAtZ)
                {
                    float furthestZ = float.MinValue;
                    for (int j = 0; j < tiles.Length; j++)
                    {
                        if (tiles[j].position.z > furthestZ)
                            furthestZ = tiles[j].position.z;
                    }

                    Vector3 pos = tiles[i].position;
                    pos.z = furthestZ + tileLength;
                    tiles[i].position = pos;
                }
            }
        }
    }
}
