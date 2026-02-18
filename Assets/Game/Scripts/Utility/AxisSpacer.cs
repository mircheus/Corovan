using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class AxisSpacer : MonoBehaviour
{
    public enum Axis
    {
        X,
        Y,
        Z
    }

    [Header("Settings")]
    [SerializeField] private Axis axis = Axis.X;
    [SerializeField] private float spacing = 1.0f;

    [Header("Objects To Move")]
    [SerializeField] private Transform[] objects;

    [Header("Debug")]
    [SerializeField] private bool autoApply = true;

    private Dictionary<Transform, Vector3> initialLocalPositions =
        new Dictionary<Transform, Vector3>();

    private void OnValidate()
    {
        if (!autoApply) return;
        ApplySpacing();
    }

    [ContextMenu("Cache Initial Positions")]
    private void CacheInitialPositions()
    {
        initialLocalPositions.Clear();

        foreach (var obj in objects)
        {
            if (obj == null) continue;
            initialLocalPositions[obj] = obj.localPosition;
        }

        Debug.Log("Initial positions cached.");
    }

    [ContextMenu("Reset To Initial Positions")]
    private void ResetToInitialPositions()
    {
        foreach (var pair in initialLocalPositions)
        {
            if (pair.Key == null) continue;
            pair.Key.localPosition = pair.Value;
        }

        Debug.Log("Positions reset.");
    }

    private void ApplySpacing()
    {
        if (objects == null) return;

        foreach (var obj in objects)
        {
            if (obj == null) continue;

            if (!initialLocalPositions.ContainsKey(obj))
                continue;

            Vector3 basePos = initialLocalPositions[obj];
            Vector3 newPos = basePos;

            float direction = GetDirection(basePos);

            switch (axis)
            {
                case Axis.X:
                    newPos.x = direction * Mathf.Abs(spacing);
                    break;

                case Axis.Y:
                    newPos.y = direction * Mathf.Abs(spacing);
                    break;

                case Axis.Z:
                    newPos.z = direction * Mathf.Abs(spacing);
                    break;
            }

            obj.localPosition = newPos;
        }
    }

    private float GetDirection(Vector3 localPos)
    {
        switch (axis)
        {
            case Axis.X: return Mathf.Sign(localPos.x);
            case Axis.Y: return Mathf.Sign(localPos.y);
            case Axis.Z: return Mathf.Sign(localPos.z);
        }

        return 1f;
    }
}
