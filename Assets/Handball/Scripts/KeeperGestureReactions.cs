using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Unity-chan answers the hand signs the player shows her: she waves back, thanks them for a thumbs up, sulks at
    /// a thumbs down, and takes real offence at a middle finger, which leaves her fired up (and harder to beat) for
    /// a while. She has to be standing ready and the player has to be looking her way; while she is answering she
    /// is a touch slower to react to a shot.
    /// </summary>
    public class KeeperGestureReactions : MonoBehaviour
    {
        public GoalkeeperController keeper;
        public KeeperFace face;
        [Tooltip("Seconds before she answers another sign.")]
        public float cooldown = 0.8f;
        [Tooltip("The player must be facing her within this angle for her to notice a sign.")]
        public float noticeAngle = 55f;
        [Tooltip("Seconds she stays fired up after being insulted.")]
        public float firedUpTime = 12f;

        /// <summary>The last sign she answered, and when (for tests and other scripts).</summary>
        public HandGesture LastAnswered { get; private set; }
        public float LastAnsweredTime { get; private set; } = -1f;
        public event System.Action<HandGesture> Answered;

        class Reaction
        {
            public string[] lines;
            public KeeperEmote.Mood mood;
            public KeeperFace.Expression expression;
            public string animatorState;
            public GoalkeeperController.ReactionArm arm;
            public bool downcast;
            public float duration;
            public SfxPlayer.Cue cue;
        }

        static readonly Dictionary<HandGesture, Reaction> s_Reactions = new Dictionary<HandGesture, Reaction>
        {
            [HandGesture.ThumbsUp] = new Reaction
            {
                lines = new[] { "Thanks~!", "You too!", "Aww, yay!" }, mood = KeeperEmote.Mood.Happy, expression = KeeperFace.Expression.Smile,
                arm = GoalkeeperController.ReactionArm.Raise, duration = 1.4f, cue = SfxPlayer.Cue.Celebrate,
            },
            [HandGesture.ThumbsDown] = new Reaction
            {
                lines = new[] { "Hmph!", "So mean...", "Muu..." }, mood = KeeperEmote.Mood.Sad, expression = KeeperFace.Expression.Sad,
                downcast = true, duration = 1.6f, cue = SfxPlayer.Cue.Miss,
            },
            [HandGesture.Peace] = new Reaction
            {
                lines = new[] { "Peace~!", "Cheese!", "Vee~!" }, mood = KeeperEmote.Mood.Happy, expression = KeeperFace.Expression.Smile,
                arm = GoalkeeperController.ReactionArm.Raise, duration = 1.4f, cue = SfxPlayer.Cue.Celebrate,
            },
            [HandGesture.MiddleFinger] = new Reaction
            {
                lines = new[] { "Rude!!", "Foul!!", "How rude!" }, mood = KeeperEmote.Mood.Angry, expression = KeeperFace.Expression.Angry,
                animatorState = "Jab", duration = 1.0f, cue = SfxPlayer.Cue.Whistle,
            },
            [HandGesture.Rock] = new Reaction
            {
                lines = new[] { "Yeah!!", "Rock on!", "Woo~!" }, mood = KeeperEmote.Mood.Happy, expression = KeeperFace.Expression.Smile,
                animatorState = "RisingPunch", duration = 1.1f, cue = SfxPlayer.Cue.Celebrate,
            },
            [HandGesture.Wave] = new Reaction
            {
                lines = new[] { "Hi hi~!", "Hello~!", "Yoo-hoo!" }, mood = KeeperEmote.Mood.Happy, expression = KeeperFace.Expression.Smile,
                arm = GoalkeeperController.ReactionArm.Wave, duration = 1.8f, cue = SfxPlayer.Cue.Celebrate,
            },
        };

        HandGestures[] m_Hands;
        HandGesture[] m_Seen;
        bool[] m_AnsweredThis;
        float m_NextAnswer;

        void Awake()
        {
            if (!keeper) keeper = GetComponent<GoalkeeperController>();
            if (!face) face = GetComponentInChildren<KeeperFace>();
        }

        void Start()
        {
            m_Hands = FindObjectsByType<HandGestures>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            m_Seen = new HandGesture[m_Hands.Length];
            m_AnsweredThis = new bool[m_Hands.Length];
        }

        void Update()
        {
            for (int i = 0; i < m_Hands.Length; i++)
            {
                var hand = m_Hands[i];
                var gesture = hand && hand.isActiveAndEnabled ? hand.Shown : HandGesture.None;
                if (gesture != m_Seen[i])
                {
                    m_Seen[i] = gesture;
                    m_AnsweredThis[i] = false;
                }
                // One answer per sign: holding it does not repeat, but she does answer late if she was busy.
                if (gesture == HandGesture.None || m_AnsweredThis[i] || Time.time < m_NextAnswer || !PlayerIsFacingHer()) continue;
                if (Answer(gesture)) m_AnsweredThis[i] = true;
            }
        }

        bool PlayerIsFacingHer()
        {
            var head = Camera.main ? Camera.main.transform : null;
            if (!head) return true;
            var toHer = keeper.transform.position + Vector3.up * 1.2f - head.position;
            return Vector3.Angle(head.forward, toHer) <= noticeAngle;
        }

        /// <summary>Makes her answer a sign now. Returns false if she is busy (diving, down, mid-save...).</summary>
        public bool Answer(HandGesture gesture)
        {
            if (!s_Reactions.TryGetValue(gesture, out var reaction)) return false;
            if (!keeper.BeginReaction(reaction.duration, reaction.animatorState, reaction.arm, reaction.downcast)) return false;

            if (keeper.emote) keeper.emote.Show(reaction.lines[Random.Range(0, reaction.lines.Length)], reaction.mood);
            bool insulted = gesture == HandGesture.MiddleFinger;
            if (insulted) keeper.FireUp(firedUpTime);
            if (face) face.Show(reaction.expression, insulted ? firedUpTime : reaction.duration + 0.4f);
            var game = HandballGame.Instance;
            if (game && game.sfx) game.sfx.Play(reaction.cue, keeper.transform.position + Vector3.up, 0.6f);

            LastAnswered = gesture;
            LastAnsweredTime = Time.time;
            m_NextAnswer = Time.time + reaction.duration + cooldown;
            Answered?.Invoke(gesture);
            return true;
        }
    }
}
