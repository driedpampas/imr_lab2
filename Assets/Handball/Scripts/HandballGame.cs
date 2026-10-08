using System;
using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    public enum ShotResult { Pending, Goal, Save, Miss }

    /// <summary>One throw, from release until it is resolved as a goal, a save or a miss.</summary>
    public class Shot
    {
        public HandballBall ball;
        public float startTime;
        public Vector3 origin;
        public Vector3 velocity;
        public bool keeperTouched;
        public float keeperTouchTime;
        public bool hitPost;
        public ShotResult result = ShotResult.Pending;
    }

    /// <summary>
    /// Owns the score and decides the outcome of each shot. Other components report events
    /// (throw, keeper touch, goal line crossed) and react to <see cref="ShotStarted"/> / <see cref="ShotResolved"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HandballGame : MonoBehaviour
    {
        public static HandballGame Instance { get; private set; }

        [Header("Field")]
        [Tooltip("Goal origin: centre of the goal line on the ground, forward pointing into the net.")]
        public Transform goal;
        public Vector2 goalSize = new Vector2(3f, 2f);

        [Header("Rules")]
        [Tooltip("Seconds after the keeper touches the ball before it counts as a save (a deflection can still go in).")]
        public float saveConfirmDelay = 1.0f;
        [Tooltip("Seconds after the throw before an unresolved shot counts as a miss.")]
        public float shotTimeout = 3.5f;
        public float minShotSpeed = 2.0f;

        [Header("Feedback")]
        public ScoreboardUI scoreboard;
        public ConfettiBurst[] goalConfetti;
        public SfxPlayer sfx;

        public int Goals { get; private set; }
        public int Saves { get; private set; }
        public int Misses { get; private set; }
        public int Shots { get; private set; }
        public int Streak { get; private set; }

        public event Action<Shot> ShotStarted;
        public event Action<Shot> ShotResolved;

        readonly List<Shot> m_ActiveShots = new List<Shot>();

        public IReadOnlyList<Shot> ActiveShots => m_ActiveShots;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (scoreboard) scoreboard.SetScore(Goals, Saves, false);
            if (sfx) sfx.Play(SfxPlayer.Cue.Whistle, goal ? goal.position : transform.position, 0.6f);
        }

        /// <summary>World-space centre of the goal mouth.</summary>
        public Vector3 GoalCenter => goal.position + Vector3.up * (goalSize.y * 0.5f);

        /// <summary>Converts a world point to goal-local (x = sideways, y = height, z = depth into the net).</summary>
        public Vector3 ToGoalLocal(Vector3 world) => goal.InverseTransformPoint(world);

        public bool IsInsideGoalMouth(Vector3 goalLocal, float margin = 0f)
        {
            return Mathf.Abs(goalLocal.x) <= goalSize.x * 0.5f + margin && goalLocal.y >= -margin && goalLocal.y <= goalSize.y + margin;
        }

        public Shot ReportThrow(HandballBall ball, Vector3 position, Vector3 velocity)
        {
            if (velocity.magnitude < minShotSpeed) return null;

            // A ball that is re-thrown resolves its previous shot as a miss.
            if (ball.CurrentShot != null && ball.CurrentShot.result == ShotResult.Pending)
                Resolve(ball.CurrentShot, ShotResult.Miss);

            var shot = new Shot { ball = ball, startTime = Time.time, origin = position, velocity = velocity };
            ball.CurrentShot = shot;
            m_ActiveShots.Add(shot);
            Shots++;
            ShotStarted?.Invoke(shot);
            return shot;
        }

        public void ReportKeeperTouch(HandballBall ball)
        {
            var shot = ball.CurrentShot;
            if (shot == null || shot.result != ShotResult.Pending || shot.keeperTouched) return;
            shot.keeperTouched = true;
            shot.keeperTouchTime = Time.time;
        }

        public void ReportPostHit(HandballBall ball)
        {
            var shot = ball.CurrentShot;
            if (shot != null && shot.result == ShotResult.Pending) shot.hitPost = true;
        }

        public void ReportGoalLineCrossed(HandballBall ball)
        {
            var shot = ball.CurrentShot;
            if (shot == null || shot.result != ShotResult.Pending) return;
            Resolve(shot, ShotResult.Goal);
        }

        void Update()
        {
            for (int i = m_ActiveShots.Count - 1; i >= 0; i--)
            {
                var shot = m_ActiveShots[i];
                if (shot.result != ShotResult.Pending)
                {
                    m_ActiveShots.RemoveAt(i);
                    continue;
                }

                if (shot.ball == null || shot.ball.IsHeld)
                {
                    Resolve(shot, ShotResult.Miss);
                    continue;
                }

                float age = Time.time - shot.startTime;
                if (shot.keeperTouched && Time.time - shot.keeperTouchTime >= saveConfirmDelay)
                    Resolve(shot, ShotResult.Save);
                else if (age >= shotTimeout || (age > 0.5f && shot.ball.IsResting))
                    Resolve(shot, shot.keeperTouched ? ShotResult.Save : ShotResult.Miss);
            }
        }

        void Resolve(Shot shot, ShotResult result)
        {
            shot.result = result;
            switch (result)
            {
                case ShotResult.Goal:
                    Goals++;
                    Streak++;
                    CelebrateGoal(shot);
                    break;
                case ShotResult.Save:
                    Saves++;
                    Streak = 0;
                    if (scoreboard) scoreboard.ShowPopup("SAVED!", ScoreboardUI.PopupStyle.Save);
                    if (sfx) sfx.Play(SfxPlayer.Cue.SaveJingle, GoalCenter, 0.8f);
                    break;
                case ShotResult.Miss:
                    Misses++;
                    Streak = 0;
                    if (scoreboard) scoreboard.ShowPopup(shot.hitPost ? "POST!" : "MISS", ScoreboardUI.PopupStyle.Miss);
                    if (sfx) sfx.Play(SfxPlayer.Cue.Miss, GoalCenter, 0.5f);
                    break;
            }
            if (scoreboard) scoreboard.SetScore(Goals, Saves, result != ShotResult.Miss);
            ShotResolved?.Invoke(shot);
        }

        void CelebrateGoal(Shot shot)
        {
            if (goalConfetti != null)
                foreach (var c in goalConfetti) if (c) c.Burst();

            var at = shot.ball ? shot.ball.transform.position : GoalCenter;
            if (sfx)
            {
                sfx.Play(SfxPlayer.Cue.NetHit, at, 1f);
                sfx.Play(SfxPlayer.Cue.PartyPop, GoalCenter + Vector3.up, 0.9f);
                sfx.Play(SfxPlayer.Cue.GoalJingle, GoalCenter, 0.85f);
                sfx.Play(SfxPlayer.Cue.Cheer, GoalCenter, 0.7f);
            }
            if (scoreboard)
                scoreboard.ShowPopup(Streak >= 3 ? $"GOAL! x{Streak}" : "GOAL!", ScoreboardUI.PopupStyle.Goal);
        }
    }
}
