using UnityEngine;

public class WheelAnimation : MonoBehaviour
{
    [SerializeField] private float wheelSpeed;
    [SerializeField] private Animator animator;

    private void Start()
    {
        animator.speed = wheelSpeed;
    }
}
