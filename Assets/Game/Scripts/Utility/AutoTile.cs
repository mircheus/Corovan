using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class AutoTile : MonoBehaviour
{
    [SerializeField] private bool isWall = false;

    private Renderer rend;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        UpdateTiling();
    }

    private void Update()
    {
        UpdateTiling();
    }

    private void UpdateTiling()
    {
        if (rend == null) return;

        Vector3 scale = transform.localScale;

        Vector2 tiling;

        if (isWall)
        {
            // Стена → X / Y
            tiling = new Vector2(scale.x, scale.y);
        }
        else
        {
            // Пол → X / Z
            tiling = new Vector2(scale.x, scale.z);
        }

        rend.sharedMaterial.mainTextureScale = tiling;
    }
}