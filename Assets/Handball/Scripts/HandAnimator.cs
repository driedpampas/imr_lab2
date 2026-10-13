using UnityEngine;
using UnityEngine.InputSystem;

namespace Handball
{
    /// <summary>
    /// Plays the hand model's finger animations from the controller it is attached to: the grip value scrubs the
    /// Grab animation (middle, ring and little finger) and the trigger value scrubs the Trigger animation (index
    /// finger). An analog squeeze is followed directly; a digital press (keyboard, XR Interaction Simulator) is
    /// eased so the fingers still close smoothly.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HandAnimator : MonoBehaviour
    {
        public const string GripParameter = "Grip";
        public const string TriggerParameter = "Trigger";

        [Tooltip("Grip / select value of this hand's controller (0..1).")]
        public InputActionProperty gripValue;
        [Tooltip("Trigger / activate value of this hand's controller (0..1).")]
        public InputActionProperty triggerValue;
        [Tooltip("How fast the fingers follow the input: full open-to-closed movements per second.")]
        public float fingerSpeed = 7f;

        /// <summary>How far the Grab animation is played, 0 (open) to 1 (closed).</summary>
        public float Grip { get; private set; }
        /// <summary>How far the Trigger animation is played, 0 (open) to 1 (pulled).</summary>
        public float Trigger { get; private set; }

        static readonly int s_Grip = Animator.StringToHash(GripParameter);
        static readonly int s_Trigger = Animator.StringToHash(TriggerParameter);

        Animator m_Animator;

        void Awake()
        {
            m_Animator = GetComponent<Animator>();
        }

        void Update()
        {
            var step = fingerSpeed * Time.deltaTime;
            Grip = Mathf.MoveTowards(Grip, Read(gripValue), step);
            Trigger = Mathf.MoveTowards(Trigger, Read(triggerValue), step);
            m_Animator.SetFloat(s_Grip, Grip);
            m_Animator.SetFloat(s_Trigger, Trigger);
        }

        static float Read(InputActionProperty property)
        {
            var action = property.action;
            return action != null ? Mathf.Clamp01(action.ReadValue<float>()) : 0f;
        }
    }
}
