using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Unity-chan the goalkeeper. Idles in front of the goal; when a shot is released she predicts where it will
    /// cross her plane, reacts after a short (human) delay and either blocks standing, jumps, or dives across the goal.
    /// After a dive she flops into a ragdoll, lies dizzy for a moment (the player's window to score),
    /// springs back up and shuffles back to the centre.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class GoalkeeperController : MonoBehaviour
    {
        public enum State { Idle, Blocking, Diving, Down, GettingUp, Returning, Celebrating }

        [Header("References")]
        public Animator animator;
        public KeeperRagdoll ragdoll;
        public KeeperEmote emote;
        public DizzyStars dizzy;
        [Tooltip("What she looks at when no ball is flying (the player's head).")]
        public Transform lookAtFallback;

        [Header("Placement")]
        [Tooltip("Distance in front of the goal line where she stands.")]
        public float homeDepth = 0.7f;

        [Header("Reaction (difficulty)")]
        public float reactionTime = 0.21f;
        public float reactionJitter = 0.08f;
        [Tooltip("Std-dev (m) of her error when reading where the ball goes.")]
        public float perceptionError = 0.18f;
        [Range(0f, 1f)] public float wrongGuessChance = 0.08f;
        [Tooltip("How fast her hands can travel towards the ball (m/s).")]
        public float handSpeed = 3.3f;
        public float windup = 0.05f;

        [Header("Body")]
        public float restHandHeight = 1.15f;
        public float hipsHeight = 0.87f;
        [Tooltip("Hips to wrists with arms stretched overhead (Unity-chan has short anime arms).")]
        public float upperReach = 0.72f;
        public float standingReachX = 0.48f;
        public float standMinY = 0.5f;
        public float standMaxY = 1.7f;
        public float maxJump = 0.45f;

        [Header("Recovery")]
        [Tooltip("Seconds she keeps flying (scripted) after reaching the ball before going limp.")]
        public float ragdollHold = 0.06f;
        public float downTime = 2.2f;
        public float getUpTime = 0.85f;
        public float returnSpeed = 2.4f;
        [Range(0f, 1f)] public float celebrateChance = 0.6f;

        static readonly int s_Idle = Animator.StringToHash("Idle");
        static readonly int s_Celebrate = Animator.StringToHash("RisingPunch");
        static readonly int s_FistPump = Animator.StringToHash("Jab");

        public State CurrentState => m_State;
        public bool CanReact => m_State == State.Idle || m_State == State.Returning;

        State m_State = State.Idle;
        float m_StateTime;

        // Shot plan, in keeper plane coordinates (u = her right, y = up) relative to her feet at plan time.
        Vector3 m_PlanOrigin;
        float m_MoveStart, m_Arrive, m_PlanEnd;
        Vector2 m_HandTarget;
        Vector2 m_HipsTarget;
        Vector2 m_DiveDir;
        float m_Crouch, m_Jump;
        HandballBall m_TrackedBall;
        bool m_PushedOff;
        bool m_LastShotSaved;

        // IK
        float m_HandWeight;
        Vector3 m_LeftHandGoal, m_RightHandGoal;
        Vector3 m_LookPoint;
        float m_LookWeight = 1f;

        // Get-up blending
        Quaternion[] m_CapturedLocal;
        Transform[] m_Bones;
        Vector3 m_CapturedHipsPos;
        Quaternion m_CapturedHipsRot;
        float m_DownSince;
        bool m_Settled;

        HandballGame Game => HandballGame.Instance;
        Transform Goal => Game ? Game.goal : null;

        public Vector3 HomePosition => Goal ? Goal.TransformPoint(0f, 0f, -homeDepth) : transform.position;
        public Quaternion HomeRotation => Goal ? Quaternion.LookRotation(-Goal.forward, Vector3.up) : transform.rotation;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!ragdoll) ragdoll = GetComponentInChildren<KeeperRagdoll>();
            var relay = animator.GetComponent<KeeperIKRelay>();
            if (!relay) relay = animator.gameObject.AddComponent<KeeperIKRelay>();
            relay.keeper = this;
            animator.updateMode = AnimatorUpdateMode.Fixed;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var bones = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = animator.GetBoneTransform((HumanBodyBones)i);
                if (t) bones.Add(t);
            }
            m_Bones = bones.ToArray();
            m_CapturedLocal = new Quaternion[m_Bones.Length];
        }

        void Start()
        {
            if (Goal) transform.SetPositionAndRotation(HomePosition, HomeRotation);
            ragdoll.Build();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (emote && !emote.followHead) emote.followHead = head;
            if (dizzy && !dizzy.followHead) dizzy.followHead = head;
            if (Game)
            {
                Game.ShotStarted += OnShotStarted;
                Game.ShotResolved += OnShotResolved;
            }
            m_LookPoint = transform.position + transform.forward * 5f + Vector3.up * 1.3f;
        }

        void OnDestroy()
        {
            if (Game)
            {
                Game.ShotStarted -= OnShotStarted;
                Game.ShotResolved -= OnShotResolved;
            }
        }

        void SetState(State state)
        {
            m_State = state;
            m_StateTime = 0f;
        }

        // ------------------------------------------------------------------ shot handling

        void OnShotStarted(Shot shot)
        {
            m_TrackedBall = shot.ball;
            if (!CanReact) return;

            var p0 = shot.origin;
            var v0 = shot.velocity;
            var feet = transform.position;
            var fwd = HomeRotation * Vector3.forward;
            if (!Ballistics.TimeToPlane(p0, v0, feet, fwd, out float tPlane) || tPlane > 3f) return;

            // Is it even going towards the goal? Ignore wild throws (she just watches them).
            var goalLocal = Vector3.zero;
            if (Ballistics.TimeToPlane(p0, v0, Goal.position, -Goal.forward, out float tGoal))
                goalLocal = Goal.InverseTransformPoint(Ballistics.PositionAt(p0, v0, tGoal));
            if (!Game.IsInsideGoalMouth(goalLocal, 0.7f))
            {
                if (emote && Random.value < 0.5f) emote.Show(Random.value < 0.5f ? "?" : "Hmm?", KeeperEmote.Mood.Neutral);
                return;
            }

            var crossing = Ballistics.PositionAt(p0, v0, tPlane);
            // A ball that lands short bounces up again; she aims for a low save.
            crossing.y = Mathf.Max(crossing.y, 0.15f);
            var right = HomeRotation * Vector3.right;
            var target = new Vector2(Vector3.Dot(crossing - feet, right), crossing.y);
            target += Random.insideUnitCircle * perceptionError;
            if (Random.value < wrongGuessChance) target.x = -target.x;

            float reaction = reactionTime + Random.value * reactionJitter;
            PlanMove(target, Time.time + reaction, Time.time + tPlane);
        }

        void PlanMove(Vector2 desired, float startTime, float ballArrival)
        {
            m_PlanOrigin = transform.position;
            m_PushedOff = false;
            var rest = new Vector2(0f, restHandHeight);
            float available = Mathf.Max(0f, ballArrival - startTime - windup);
            var toTarget = desired - rest;
            float needed = toTarget.magnitude;
            float reach = Mathf.Min(needed, handSpeed * available);
            var hand = needed > 1e-3f ? rest + toTarget * (reach / needed) : rest;
            m_HandTarget = hand;

            m_MoveStart = startTime + windup;
            float travelTime = Mathf.Max(0.16f, reach / handSpeed);
            // Time the save to meet the ball rather than arriving early and falling past it.
            m_Arrive = Mathf.Max(m_MoveStart + travelTime, ballArrival - 0.03f);
            m_Crouch = 0f;
            m_Jump = 0f;

            bool central = Mathf.Abs(hand.x) <= standingReachX;
            if (central && hand.y >= standMinY && hand.y <= standMaxY)
            {
                m_Crouch = hand.y < 0.85f ? (0.85f - hand.y) * 0.8f : 0f;
                m_PlanEnd = Mathf.Max(m_Arrive, ballArrival) + 0.35f;
                SetState(State.Blocking);
            }
            else if (central && hand.y > standMaxY)
            {
                m_Jump = Mathf.Min(maxJump, hand.y - standMaxY + 0.1f);
                m_Arrive = Mathf.Max(m_Arrive, m_MoveStart + 0.22f);
                m_PlanEnd = m_Arrive + 0.4f;
                SetState(State.Blocking);
            }
            else
            {
                var hipsRest = new Vector2(0f, hipsHeight);
                var dir = (hand - hipsRest).normalized;
                // Clamp the body angle between "nearly upright" and "slightly head-down".
                float angle = Mathf.Atan2(Mathf.Abs(dir.x), dir.y) * Mathf.Rad2Deg;
                angle = Mathf.Clamp(angle, 25f, 100f) * Mathf.Deg2Rad;
                dir = new Vector2(Mathf.Sign(hand.x) * Mathf.Sin(angle), Mathf.Cos(angle));
                m_DiveDir = dir;
                m_HipsTarget = hand - dir * upperReach;
                m_HipsTarget.y = Mathf.Max(m_HipsTarget.y, 0.28f);
                m_Arrive = Mathf.Max(m_Arrive, m_MoveStart + 0.24f);
                m_PlanEnd = m_Arrive + ragdollHold;
                SetState(State.Diving);
                if (dizzy) dizzy.Show(false);
            }
        }

        void OnShotResolved(Shot shot)
        {
            if (shot.result == ShotResult.Save)
            {
                m_LastShotSaved = true;
                if (emote) emote.Show(Pick("Saved!", "Nope~!", "Not today!", "Hehe~", "Too easy!"), KeeperEmote.Mood.Happy);
                if (m_State == State.Idle || m_State == State.Blocking)
                    Celebrate();
            }
            else if (shot.result == ShotResult.Goal)
            {
                m_LastShotSaved = false;
                if (emote) emote.Show(Pick("Ehh?!", "Nooo~", "Muu...", "No fair!", "Wah!"), KeeperEmote.Mood.Sad);
            }
        }

        public void OnBallTouched(HandballBall ball)
        {
            if (Game && Game.sfx && m_State == State.Diving) Game.sfx.Play(SfxPlayer.Cue.KeeperHit, ball.transform.position, 0.4f);
        }

        void Celebrate()
        {
            if (Random.value > celebrateChance) return;
            bool big = Random.value < 0.5f;
            m_HandWeight = 0f;
            var p = transform.position;
            transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), HomeRotation);
            animator.CrossFadeInFixedTime(big ? s_Celebrate : s_FistPump, 0.12f);
            if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Celebrate, transform.position + Vector3.up, 0.7f);
            SetState(State.Celebrating);
            m_PlanEnd = Time.time + (big ? 1.1f : 0.55f);
        }

        /// <summary>Instantly puts her back in goal, standing and ready (tests / restarting a round).</summary>
        public void ResetToHome()
        {
            if (ragdoll.IsRagdoll) ragdoll.SetRagdoll(false);
            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.Fixed;
            animator.Play(s_Idle, 0, 0f);
            m_HandWeight = 0f;
            transform.SetPositionAndRotation(HomePosition, HomeRotation);
            if (dizzy) dizzy.Show(false);
            SetState(State.Idle);
        }

        // ------------------------------------------------------------------ simulation

        void FixedUpdate()
        {
            m_StateTime += Time.fixedDeltaTime;
            float now = Time.time;
            ragdoll.TrackPose();
            UpdateLookTarget();

            switch (m_State)
            {
                case State.Idle:
                    m_HandWeight = Mathf.MoveTowards(m_HandWeight, 0f, Time.fixedDeltaTime * 4f);
                    break;

                case State.Blocking:
                    TickBlock(now);
                    break;

                case State.Diving:
                    TickDive(now);
                    break;

                case State.Down:
                    TickDown(now);
                    break;

                case State.Returning:
                    TickReturn();
                    break;

                case State.Celebrating:
                    if (now >= m_PlanEnd) SetState(State.Idle);
                    break;
            }
        }

        void UpdateLookTarget()
        {
            Vector3 target;
            if (m_TrackedBall && !m_TrackedBall.IsOnRack)
                target = m_TrackedBall.transform.position;
            else if (lookAtFallback)
                target = lookAtFallback.position;
            else
                target = transform.position + transform.forward * 5f + Vector3.up * 1.4f;
            m_LookPoint = Vector3.Lerp(m_LookPoint, target, 1f - Mathf.Exp(-Time.fixedDeltaTime * 12f));
        }

        Vector3 PlaneToWorld(Vector2 p)
        {
            var right = HomeRotation * Vector3.right;
            return m_PlanOrigin + right * p.x + Vector3.up * p.y;
        }

        void TickBlock(float now)
        {
            float s = Mathf.Clamp01((now - m_MoveStart) / Mathf.Max(0.01f, m_Arrive - m_MoveStart));
            float e = 1f - Mathf.Pow(1f - s, 2.5f);
            if (now < m_MoveStart) e = 0f;

            var handWorld = PlaneToWorld(m_HandTarget);
            SetHandGoals(handWorld, HomeRotation * Vector3.right, 0.11f);
            m_HandWeight = e;

            // Side-step towards the ball so the body helps cover it, not just the arms.
            var right = HomeRotation * Vector3.right;
            float step = Mathf.Clamp(m_HandTarget.x * 0.7f, -0.32f, 0.32f) * e;
            var basePos = new Vector3(m_PlanOrigin.x, 0f, m_PlanOrigin.z) + right * step;
            float lift = 0f;
            if (m_Jump > 0f)
            {
                float jt = Mathf.Clamp01((now - m_MoveStart) / Mathf.Max(0.01f, m_PlanEnd - m_MoveStart));
                lift = m_Jump * Mathf.Sin(jt * Mathf.PI);
            }
            float crouch = -m_Crouch * e;
            transform.SetPositionAndRotation(basePos + Vector3.up * (lift + crouch), HomeRotation);

            if (now >= m_PlanEnd)
            {
                m_HandWeight = 0f;
                transform.position = basePos;
                if (m_Jump > 0f && Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.KeeperLand, transform.position, 0.4f);
                SetState(State.Returning);
            }
        }

        void TickDive(float now)
        {
            var rot0 = HomeRotation;
            var right = rot0 * Vector3.right;
            if (now < m_MoveStart)
            {
                // Wind-up: a quick dip before pushing off.
                float w = Mathf.Clamp01(m_StateTime / Mathf.Max(0.01f, windup + reactionTime));
                transform.SetPositionAndRotation(m_PlanOrigin + Vector3.down * (0.06f * w), rot0);
                return;
            }

            if (!m_PushedOff)
            {
                m_PushedOff = true;
                if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.KeeperJump, transform.position, 0.6f);
            }

            float duration = Mathf.Max(0.01f, m_Arrive - m_MoveStart);
            float raw = (now - m_MoveStart) / duration;
            float s = Mathf.Clamp01(raw);
            // Ease out but keep some speed at the end so the ragdoll inherits the dive's momentum.
            float e = 0.55f * s + 0.45f * (1f - (1f - s) * (1f - s));
            float overshoot = Mathf.Max(0f, raw - 1f) * 0.55f;

            var hipsRest = new Vector2(0f, hipsHeight);
            var hips2 = Vector2.LerpUnclamped(hipsRest, m_HipsTarget, e + overshoot);
            hips2.y += Mathf.Sin(s * Mathf.PI) * 0.12f; // a little hop arc
            var diveWorld = (right * m_DiveDir.x + Vector3.up * m_DiveDir.y).normalized;
            var up = Vector3.Slerp(Vector3.up, diveWorld, e);
            var rot = Quaternion.FromToRotation(Vector3.up, up) * rot0;
            var hipsWorld = PlaneToWorld(hips2);
            transform.SetPositionAndRotation(hipsWorld - rot * new Vector3(0f, hipsHeight, 0f), rot);

            SetHandGoals(PlaneToWorld(m_HandTarget), Vector3.Cross(diveWorld, rot0 * Vector3.forward).normalized, 0.1f);
            m_HandWeight = Mathf.Clamp01(e * 1.6f);

            if (now >= m_PlanEnd)
                GoLimp();
        }

        void GoLimp()
        {
            m_HandWeight = 0f;
            // Freeze the animator on the current pose and let physics take over.
            animator.enabled = false;
            ragdoll.SetRagdoll(true);
            m_DownSince = Time.time;
            m_Settled = false;
            SetState(State.Down);
        }

        void TickDown(float now)
        {
            if (!m_Settled)
            {
                bool slow = ragdoll.MaxSpeed() < 0.6f;
                if ((slow && now - m_DownSince > 0.5f) || now - m_DownSince > 2.0f)
                {
                    m_Settled = true;
                    m_DownSince = now;
                    if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.KeeperLand, ragdoll.Hips.position, 0.8f);
                    if (dizzy) dizzy.Show(true);
                }
                return;
            }

            if (now - m_DownSince >= downTime)
                BeginGetUp();
        }

        void BeginGetUp()
        {
            for (int i = 0; i < m_Bones.Length; i++) m_CapturedLocal[i] = m_Bones[i].localRotation;
            var hips = ragdoll.Hips;
            m_CapturedHipsPos = hips.position;
            m_CapturedHipsRot = hips.rotation;

            ragdoll.SetRagdoll(false);
            // Stand up where she landed, facing the player again.
            var ground = new Vector3(m_CapturedHipsPos.x, 0f, m_CapturedHipsPos.z);
            transform.SetPositionAndRotation(ground, HomeRotation);

            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.Play(s_Idle, 0, 0f);
            animator.Update(0f);
            if (dizzy) dizzy.Show(false);
            SetState(State.GettingUp);
        }

        void LateUpdate()
        {
            if (m_State != State.GettingUp) return;
            m_StateTime += Time.deltaTime;
            float t = Mathf.Clamp01(m_StateTime / getUpTime);
            float w = t * t * (3f - 2f * t);

            // Bones: blend from the ragdoll pose to the live idle pose.
            for (int i = 0; i < m_Bones.Length; i++)
                m_Bones[i].localRotation = Quaternion.Slerp(m_CapturedLocal[i], m_Bones[i].localRotation, w);

            var hips = ragdoll.Hips;
            var animatedPos = hips.position;
            var animatedRot = hips.rotation;
            float hop = Mathf.Sin(t * Mathf.PI) * 0.25f;
            hips.position = Vector3.Lerp(m_CapturedHipsPos, animatedPos, w) + Vector3.up * hop;
            hips.rotation = Quaternion.Slerp(m_CapturedHipsRot, animatedRot, w);

            if (t >= 1f)
            {
                animator.updateMode = AnimatorUpdateMode.Fixed;
                if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Boing, transform.position + Vector3.up, 0.7f);
                if (m_LastShotSaved)
                {
                    animator.CrossFadeInFixedTime(s_FistPump, 0.1f);
                    m_LastShotSaved = false;
                }
                SetState(State.Returning);
            }
        }

        void TickReturn()
        {
            var home = HomePosition;
            var flat = new Vector3(transform.position.x, 0f, transform.position.z);
            var to = home - flat;
            float dist = to.magnitude;
            float step = returnSpeed * Time.fixedDeltaTime;
            if (dist <= step)
            {
                transform.SetPositionAndRotation(home, HomeRotation);
                SetState(State.Idle);
                return;
            }
            flat += to / dist * step;
            // Little sideways hops, like a keeper shuffling back into position.
            float hop = Mathf.Abs(Mathf.Sin(m_StateTime * Mathf.PI * 3.2f)) * 0.07f;
            transform.SetPositionAndRotation(flat + Vector3.up * hop, HomeRotation);
        }

        // ------------------------------------------------------------------ IK

        void SetHandGoals(Vector3 center, Vector3 spreadAxis, float spread)
        {
            // Her right hand goes towards her right side of the target.
            m_RightHandGoal = center + spreadAxis * spread;
            m_LeftHandGoal = center - spreadAxis * spread;
        }

        /// <summary>Called from <see cref="KeeperIKRelay"/> during the animator's IK pass.</summary>
        public void ApplyIK(int layerIndex)
        {
            animator.SetLookAtWeight(m_LookWeight, 0.15f, 0.8f, 1f, 0.6f);
            animator.SetLookAtPosition(m_LookPoint);

            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, m_HandWeight);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, m_HandWeight);
            if (m_HandWeight > 0f)
            {
                animator.SetIKPosition(AvatarIKGoal.LeftHand, m_LeftHandGoal);
                animator.SetIKPosition(AvatarIKGoal.RightHand, m_RightHandGoal);
            }

            // Keep the feet planted while crouching for a low block.
            float footWeight = m_State == State.Blocking && m_Crouch > 0f ? 1f : 0f;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, footWeight);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, footWeight);
            if (footWeight > 0f)
            {
                float lift = transform.position.y;
                var lf = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
                var rf = animator.GetIKPosition(AvatarIKGoal.RightFoot);
                animator.SetIKPosition(AvatarIKGoal.LeftFoot, lf - Vector3.up * lift);
                animator.SetIKPosition(AvatarIKGoal.RightFoot, rf - Vector3.up * lift);
            }
        }

        static string Pick(params string[] options) => options[Random.Range(0, options.Length)];

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (m_State != State.Diving && m_State != State.Blocking) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(PlaneToWorld(m_HandTarget), 0.1f);
        }
#endif
    }
}
