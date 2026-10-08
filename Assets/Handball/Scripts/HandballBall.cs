using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Handball
{
    /// <summary>
    /// A throwable handball. Floats on its rack slot until grabbed, reports throws to the game,
    /// plays impact sounds and returns to the rack once it has come to rest.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public class HandballBall : MonoBehaviour
    {
        enum State { OnRack, Held, Loose }

        [Header("Rack")]
        public BallRack rack;
        public int slot;

        [Header("Respawn")]
        public float restBeforeRespawn = 1.6f;
        public float goalRespawnDelay = 2.2f;
        public float maxDistanceFromRack = 35f;

        [Header("Feel")]
        public TrailRenderer trail;
        public float trailMinSpeed = 4.5f;
        public float groundDamping = 0.7f;

        public Rigidbody Rigidbody { get; private set; }
        public XRGrabInteractable Grab { get; private set; }
        public Shot CurrentShot { get; set; }
        public bool IsHeld => m_State == State.Held;
        public bool IsOnRack => m_State == State.OnRack;
        public bool IsResting => m_State == State.Loose && m_SlowTime > 0.4f;

        State m_State = State.OnRack;
        int m_ReleaseCheckFrames;
        float m_SlowTime;
        float m_LastGroundContact = -10f;
        float m_LastImpactSound;
        float m_RespawnAt = -1f;
        float m_PopInTime = -1f;
        Vector3 m_BaseScale;

        HandballGame Game => HandballGame.Instance;

        void Awake()
        {
            Rigidbody = GetComponent<Rigidbody>();
            Grab = GetComponent<XRGrabInteractable>();
            m_BaseScale = transform.localScale;
        }

        void OnEnable()
        {
            Grab.selectEntered.AddListener(OnGrabbed);
            Grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            Grab.selectEntered.RemoveListener(OnGrabbed);
            Grab.selectExited.RemoveListener(OnReleased);
            if (Game) Game.ShotResolved -= OnShotResolved;
        }

        void Start()
        {
            if (Game) Game.ShotResolved += OnShotResolved;
            if (rack) PlaceOnRack(false);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            m_State = State.Held;
            m_RespawnAt = -1f;
            m_ReleaseCheckFrames = 0;
            transform.localScale = m_BaseScale;
            m_PopInTime = -1f;
            if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Grab, transform.position, 0.6f);
            if (args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(0.35f, 0.06f);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (Grab.isSelected) return; // still held by the other hand
            m_State = State.Loose;
            m_SlowTime = 0f;
            // XRGrabInteractable applies the throw velocity in LateUpdate, so read it on the next physics steps.
            m_ReleaseCheckFrames = 3;
        }

        void OnShotResolved(Shot shot)
        {
            if (shot.ball != this || m_State != State.Loose) return;
            if (shot.result == ShotResult.Goal)
                m_RespawnAt = Time.time + goalRespawnDelay;
        }

        void FixedUpdate()
        {
            if (m_ReleaseCheckFrames > 0 && m_State == State.Loose)
            {
                m_ReleaseCheckFrames--;
                var v = Rigidbody.linearVelocity;
                if (Game && v.magnitude >= Game.minShotSpeed)
                {
                    m_ReleaseCheckFrames = 0;
                    Game.ReportThrow(this, Rigidbody.position, v);
                    if (Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Whoosh, Rigidbody.position, Mathf.Clamp01(v.magnitude / 15f));
                }
            }

            if (m_State == State.OnRack && rack)
            {
                // Knocked off the rack by another ball: let it fall like any loose ball.
                if (Vector3.Distance(Rigidbody.position, rack.SlotPosition(slot)) > 0.2f)
                {
                    m_State = State.Loose;
                    Rigidbody.useGravity = true;
                }
            }

            if (m_State == State.Loose)
            {
                bool grounded = Time.time - m_LastGroundContact < 0.15f;
                float speed = Rigidbody.linearVelocity.magnitude;
                Rigidbody.linearDamping = grounded && speed < 4f ? groundDamping : 0.02f;
                Rigidbody.angularDamping = grounded ? 1.5f : 0.3f;
            }
        }

        void Update()
        {
            UpdatePopIn();

            if (trail) trail.emitting = m_State == State.Loose && Rigidbody.linearVelocity.magnitude > trailMinSpeed;

            if (m_State != State.Loose || !rack) return;

            if (Rigidbody.linearVelocity.magnitude < 0.25f) m_SlowTime += Time.deltaTime;
            else m_SlowTime = 0f;

            bool lost = Rigidbody.position.y < -2f || Vector3.Distance(Rigidbody.position, rack.transform.position) > maxDistanceFromRack;
            bool settled = m_SlowTime > restBeforeRespawn;
            bool scheduled = m_RespawnAt > 0f && Time.time >= m_RespawnAt;
            bool shotPending = CurrentShot != null && CurrentShot.result == ShotResult.Pending;
            if (lost || scheduled || (settled && !shotPending))
                PlaceOnRack(true);
        }

        /// <summary>Throws the ball from code (tests, demo shots). Behaves exactly like a released throw.</summary>
        public void Launch(Vector3 position, Vector3 velocity)
        {
            if (Grab.isSelected) return;
            m_State = State.Loose;
            m_SlowTime = 0f;
            m_RespawnAt = -1f;
            m_ReleaseCheckFrames = 0;
            transform.localScale = m_BaseScale;
            m_PopInTime = -1f;
            Rigidbody.useGravity = true;
            Rigidbody.position = position;
            transform.position = position;
            Rigidbody.linearVelocity = velocity;
            Rigidbody.angularVelocity = Vector3.Cross(Vector3.up, velocity) * 0.5f;
            if (Game)
            {
                Game.ReportThrow(this, position, velocity);
                if (Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Whoosh, position, Mathf.Clamp01(velocity.magnitude / 15f));
            }
        }

        /// <summary>Teleports the ball back to its rack slot, floating and ready to grab.</summary>
        public void PlaceOnRack(bool withEffect)
        {
            if (!rack || Grab.isSelected) return;
            if (withEffect && rack.respawnPuff)
            {
                rack.respawnPuff.transform.position = transform.position;
                rack.respawnPuff.Emit(8);
            }

            m_State = State.OnRack;
            m_RespawnAt = -1f;
            m_SlowTime = 0f;
            CurrentShot = null;
            Rigidbody.useGravity = false;
            Rigidbody.linearDamping = 0.5f;
            Rigidbody.angularDamping = 0.5f;
            Rigidbody.position = rack.SlotPosition(slot);
            Rigidbody.rotation = Random.rotation;
            transform.SetPositionAndRotation(Rigidbody.position, Rigidbody.rotation);
            Rigidbody.linearVelocity = Vector3.zero;
            Rigidbody.angularVelocity = Random.onUnitSphere * 0.6f; // a lazy idle spin
            if (trail) trail.Clear();

            if (withEffect)
            {
                m_PopInTime = 0f;
                transform.localScale = Vector3.one * 0.01f;
                if (Game && Game.sfx) Game.sfx.Play(SfxPlayer.Cue.Respawn, transform.position, 0.35f);
            }
        }

        void UpdatePopIn()
        {
            if (m_PopInTime < 0f) return;
            m_PopInTime += Time.deltaTime;
            const float duration = 0.35f;
            float t = Mathf.Clamp01(m_PopInTime / duration);
            // Overshoot ease-out for a springy "pop".
            float s = 1f + 2.2f * Mathf.Pow(t - 1f, 3) + 1.2f * Mathf.Pow(t - 1f, 2);
            transform.localScale = m_BaseScale * Mathf.Max(0.01f, s);
            if (t >= 1f)
            {
                transform.localScale = m_BaseScale;
                m_PopInTime = -1f;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            var other = collision.collider;
            float speed = collision.relativeVelocity.magnitude;
            var point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

            if (other.gameObject.layer == HandballLayers.Keeper)
            {
                if (Game) Game.ReportKeeperTouch(this);
                PlayImpact(SfxPlayer.Cue.KeeperHit, point, speed, 2f);
                var keeper = other.GetComponentInParent<GoalkeeperController>();
                if (keeper) keeper.OnBallTouched(this);
                return;
            }

            if (other.GetComponent<GoalPost>())
            {
                if (Game) Game.ReportPostHit(this);
                PlayImpact(SfxPlayer.Cue.Post, point, speed, 1.5f);
                return;
            }

            var net = other.GetComponentInParent<GoalNet>();
            if (net)
            {
                net.Poke(point, collision.relativeVelocity);
                PlayImpact(SfxPlayer.Cue.NetHit, point, speed, 2f);
                return;
            }

            if (other.attachedRigidbody && other.attachedRigidbody.GetComponent<HandballBall>())
            {
                PlayImpact(SfxPlayer.Cue.Bounce, point, speed * 0.6f, 1f);
                return;
            }

            PlayImpact(SfxPlayer.Cue.Bounce, point, speed, 1f);
        }

        void OnCollisionStay(Collision collision)
        {
            if (collision.collider.gameObject.layer != HandballLayers.Keeper && !collision.collider.attachedRigidbody)
                m_LastGroundContact = Time.time;
        }

        void PlayImpact(SfxPlayer.Cue cue, Vector3 point, float speed, float minSpeed)
        {
            if (speed < minSpeed * 0.5f || Time.time - m_LastImpactSound < 0.07f) return;
            m_LastImpactSound = Time.time;
            if (Game && Game.sfx) Game.sfx.Play(cue, point, Mathf.Clamp01(speed / 10f));
        }
    }

    public static class HandballLayers
    {
        public const int Ball = 6;
        public const int Keeper = 7;
    }
}
