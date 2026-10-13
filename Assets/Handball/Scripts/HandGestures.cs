using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Handball
{
    /// <summary>Hand signs the player can show Unity-chan.</summary>
    public enum HandGesture { None, ThumbsUp, ThumbsDown, Peace, MiddleFinger, Rock, Wave }

    /// <summary>
    /// Lets an empty hand make a sign with the controller's buttons and reports the sign once it has been held up:
    /// primary button = thumbs up (turn the hand over for thumbs down), secondary = peace, both = rock,
    /// thumbstick click = middle finger, and actually waving an open hand = wave. The XR Interaction Simulator
    /// already maps these buttons to 1, 2 and 3; N (thumbs down) and B (wave) stand in for the two signs that need
    /// a real wrist. The pose itself is one of the model's gesture animations, blended in on its own animator layer.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HandGestures : MonoBehaviour
    {
        public const string LayerName = "Gesture";

        public bool leftHand;
        [Tooltip("Controller this hand belongs to; a hand that is holding something cannot make signs.")]
        public Transform controller;
        [Tooltip("The thumb's last two bones: which way it points tells thumbs up from thumbs down.")]
        public Transform thumbBase, thumbTip;
        [Tooltip("Seconds a sign has to be held before it counts.")]
        public float holdTime = 0.3f;
        [Tooltip("How fast the hand moves into and out of a sign, in full blends per second.")]
        public float blendSpeed = 7f;

        /// <summary>The sign the hand is being asked to make right now.</summary>
        public HandGesture Pose { get; private set; }
        /// <summary>The sign as someone looking at the hand would read it; None until it has been held for a moment.</summary>
        public HandGesture Shown { get; private set; }

        Animator m_Animator;
        XRBaseInteractor[] m_Interactors;
        InputAction m_Primary, m_Secondary, m_StickClick, m_ThumbsDownKey, m_WaveKey, m_LeftHandKey;
        readonly WaveDetector m_Wave = new WaveDetector();
        int m_Layer;
        float m_Weight, m_Held;

        void Awake()
        {
            m_Animator = GetComponent<Animator>();
            m_Layer = m_Animator.GetLayerIndex(LayerName);
            if (!controller && transform.parent) controller = transform.parent.parent;
            m_Interactors = controller ? controller.GetComponentsInChildren<XRBaseInteractor>(true) : new XRBaseInteractor[0];

            var hand = leftHand ? "{LeftHand}" : "{RightHand}";
            m_Primary = Button($"<XRController>{hand}/{{PrimaryButton}}", $"<XRController>{hand}/primaryButton");
            m_Secondary = Button($"<XRController>{hand}/{{SecondaryButton}}", $"<XRController>{hand}/secondaryButton");
            m_StickClick = Button($"<XRController>{hand}/{{Primary2DAxisClick}}", $"<XRController>{hand}/primary2DAxisClick", $"<XRController>{hand}/thumbstickClicked");
            m_ThumbsDownKey = Button("<Keyboard>/n");
            m_WaveKey = Button("<Keyboard>/b");
            m_LeftHandKey = Button("<Keyboard>/shift");
        }

        static InputAction Button(params string[] bindings)
        {
            var action = new InputAction(type: InputActionType.Button);
            foreach (var binding in bindings) action.AddBinding(binding);
            return action;
        }

        void OnEnable()
        {
            foreach (var action in Actions) action.Enable();
        }

        void OnDisable()
        {
            foreach (var action in Actions) action.Disable();
        }

        void OnDestroy()
        {
            foreach (var action in Actions) action.Dispose();
        }

        InputAction[] Actions => new[] { m_Primary, m_Secondary, m_StickClick, m_ThumbsDownKey, m_WaveKey, m_LeftHandKey };

        void Update()
        {
            bool busy = false;
            foreach (var interactor in m_Interactors)
                busy |= interactor && interactor.hasSelection;

            var pose = busy ? HandGesture.None : ReadPose();
            if (pose != Pose)
            {
                Pose = pose;
                m_Held = 0f;
                if (pose != HandGesture.None && m_Layer >= 0)
                {
                    // Snap straight to the pose when the layer is invisible, otherwise ease from the previous sign.
                    if (m_Weight <= 0.01f) m_Animator.Play(pose.ToString(), m_Layer, 0f);
                    else m_Animator.CrossFadeInFixedTime(pose.ToString(), 0.12f, m_Layer);
                }
            }
            m_Held += Time.deltaTime;
            m_Weight = Mathf.MoveTowards(m_Weight, Pose != HandGesture.None ? 1f : 0f, blendSpeed * Time.deltaTime);
            if (m_Layer >= 0) m_Animator.SetLayerWeight(m_Layer, Mathf.SmoothStep(0f, 1f, m_Weight));

            bool waving = !busy && Pose == HandGesture.None && UpdateWave();
            Shown = waving ? HandGesture.Wave : (m_Held >= holdTime && m_Weight >= 0.95f ? Read(Pose) : HandGesture.None);
        }

        HandGesture ReadPose()
        {
            // Keyboard stand-ins follow the simulator's convention: the right hand, or the left while Shift is down.
            bool keysAreMine = m_LeftHandKey.IsPressed() == leftHand;
            if (keysAreMine && m_WaveKey.IsPressed()) return HandGesture.Wave;
            if (keysAreMine && m_ThumbsDownKey.IsPressed()) return HandGesture.ThumbsDown;
            bool primary = m_Primary.IsPressed(), secondary = m_Secondary.IsPressed();
            if (primary && secondary) return HandGesture.Rock;
            if (m_StickClick.IsPressed()) return HandGesture.MiddleFinger;
            if (secondary) return HandGesture.Peace;
            if (primary) return HandGesture.ThumbsUp;
            return HandGesture.None;
        }

        /// <summary>A thumb sign means whatever the thumb actually points at, so turning the hand over flips it.</summary>
        HandGesture Read(HandGesture pose)
        {
            if (pose != HandGesture.ThumbsUp && pose != HandGesture.ThumbsDown) return pose;
            if (!thumbBase || !thumbTip) return pose;
            float up = Vector3.Dot((thumbTip.position - thumbBase.position).normalized, Vector3.up);
            return up > 0.35f ? HandGesture.ThumbsUp : up < -0.35f ? HandGesture.ThumbsDown : HandGesture.None;
        }

        bool UpdateWave()
        {
            var head = Camera.main ? Camera.main.transform : null;
            if (!head || !controller) return false;
            var offset = controller.position - head.position;
            bool raised = offset.y > -0.35f; // chest height or above
            return m_Wave.Sample(Time.time, Vector3.Dot(offset, head.right), raised);
        }
    }

    /// <summary>
    /// Spots a wave in the sideways position of a raised hand: three changes of direction, each after a swing of at
    /// least <see cref="minSwing"/> and no more than <see cref="window"/> seconds after the one before.
    /// </summary>
    public class WaveDetector
    {
        public float minSwing = 0.06f;
        public float window = 1.3f;
        public float linger = 0.5f;

        float m_Extreme, m_LastTurn = -10f;
        int m_Direction, m_Turns;
        bool m_Started;

        /// <summary>Feeds one sample; returns true while the hand counts as waving.</summary>
        public bool Sample(float time, float sideways, bool raised)
        {
            if (!raised)
            {
                m_Started = false;
                m_Turns = 0;
                return false;
            }
            if (!m_Started)
            {
                m_Started = true;
                m_Extreme = sideways;
                m_Direction = 0;
                m_Turns = 0;
            }
            if (m_Turns > 0 && time - m_LastTurn > window) m_Turns = 0;

            float travel = sideways - m_Extreme;
            if (m_Direction == 0)
            {
                if (Mathf.Abs(travel) < minSwing) return false;
                m_Direction = travel > 0f ? 1 : -1;
                m_Extreme = sideways;
            }
            else if (travel * m_Direction > 0f)
            {
                m_Extreme = sideways; // still going the same way: push the turning point out
            }
            else if (-travel * m_Direction >= minSwing)
            {
                m_Direction = -m_Direction;
                m_Extreme = sideways;
                m_Turns++;
                m_LastTurn = time;
            }
            return m_Turns >= 3 && time - m_LastTurn <= linger;
        }
    }
}
