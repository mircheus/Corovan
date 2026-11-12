using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// One world-space keypad key of a <see cref="CodeConsole"/>. Needs a collider for the aim ray
    /// (<c>PlayerInputHandler</c> picks the key the crosshair is on — a cone would be ambiguous with ten
    /// keys side by side). No NetworkIdentity: the press goes through the parent console.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CodeConsoleButton : MonoBehaviour
    {
        [SerializeField, Range(0, 9)] private int digit;

        [Tooltip("Local offset the key travels when pressed.")]
        [SerializeField] private Vector3 pressOffset = new Vector3(0f, 0f, 0.03f);
        [SerializeField, Min(0.01f)] private float pressDuration = 0.12f;

        private CodeConsole console;
        private Vector3 restPosition;
        private float pressedUntil;

        public int Digit => digit;
        public CodeConsole Console => console;

        private void Awake()
        {
            console = GetComponentInParent<CodeConsole>();
            restPosition = transform.localPosition;
            if (console == null)
                Debug.LogError($"[CodeConsoleButton] {name}: no CodeConsole in parents.", this);
        }

        /// <summary>Local, cosmetic: the key dips in for a moment.</summary>
        public void PlayPress()
        {
            pressedUntil = Time.time + pressDuration;
            transform.localPosition = restPosition + pressOffset;
        }

        private void Update()
        {
            if (pressedUntil > 0f && Time.time >= pressedUntil)
            {
                pressedUntil = 0f;
                transform.localPosition = restPosition;
            }
        }
    }
}
