using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Gaze;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Handball
{
    /// <summary>
    /// Shapes the release velocity of a thrown ball. XRGrabInteractable looks this up on the
    /// releasing interactor's parents (via <see cref="IXRAimAssist"/>) when the ball is let go.
    ///
    /// Physical mode (headset): the real hand velocity, slightly scaled, nudged a few degrees towards the goal.
    /// Point-and-throw mode (XR Interaction Simulator): the simulator cannot move a controller fast enough to
    /// throw, so the ball is launched on a ballistic arc towards where the player is looking.
    /// </summary>
    public class HandballThrowAssist : MonoBehaviour, IXRAimAssist
    {
        public enum Mode { Auto, Physical, PointAndThrow }

        public Mode mode = Mode.Auto;
        [Tooltip("Head / camera used to aim in point-and-throw mode.")]
        public Transform aimSource;

        [Header("Physical throw")]
        public float velocityScale = 1.15f;
        [Tooltip("Max degrees the throw is bent towards the goal mouth.")]
        public float maxAssistAngle = 7f;
        [Tooltip("Only throws already within this angle of the goal get assisted.")]
        public float assistCone = 25f;
        public float maxSpeed = 24f;

        [Header("Point-and-throw")]
        [Tooltip("Horizontal speed of a point-and-throw shot (m/s). Handball penalties are ~15-20 m/s.")]
        public float pointThrowSpeed = 12.5f;
        [Tooltip("Random spread (m) at the target, so not every shot is laser-precise.")]
        public float pointThrowSpread = 0.05f;

        public static bool SimulatorActive => XRInteractionSimulator.instance != null && XRInteractionSimulator.instance.isActiveAndEnabled;

        public bool UsePointAndThrow => mode == Mode.PointAndThrow || (mode == Mode.Auto && SimulatorActive);

        public Vector3 GetAssistedVelocity(in Vector3 source, in Vector3 velocity, float gravity)
        {
            return GetAssistedVelocity(source, velocity, gravity, maxAssistAngle);
        }

        public Vector3 GetAssistedVelocity(in Vector3 source, in Vector3 velocity, float gravity, float maxAngle)
        {
            if (UsePointAndThrow)
            {
                if (TryGetAimTarget(out var target))
                {
                    target += (Vector3)(Random.insideUnitCircle * pointThrowSpread);
                    return Ballistics.SolveLaunch(source, target, pointThrowSpeed, gravity);
                }
                return velocity;
            }
            return AssistPhysicalThrow(velocity, source, maxAngle);
        }

        Vector3 AssistPhysicalThrow(Vector3 velocity, Vector3 source, float maxAngle)
        {
            var v = Vector3.ClampMagnitude(velocity * velocityScale, maxSpeed);
            var game = HandballGame.Instance;
            if (!game || !game.goal || v.magnitude < 3f) return v;

            // Bend only the horizontal heading, and only for throws that were already roughly on target.
            var toGoal = game.GoalCenter - source;
            var flatV = new Vector3(v.x, 0f, v.z);
            var flatGoal = new Vector3(toGoal.x, 0f, toGoal.z);
            float angle = Vector3.SignedAngle(flatV, flatGoal, Vector3.up);
            if (Mathf.Abs(angle) > assistCone) return v;
            float bend = Mathf.Clamp(angle, -maxAngle, maxAngle);
            return Quaternion.AngleAxis(bend, Vector3.up) * v;
        }

        /// <summary>Where the player is aiming: the gaze ray hitting the goal plane (clamped near the goal).</summary>
        public bool TryGetAimTarget(out Vector3 target)
        {
            target = default;
            var game = HandballGame.Instance;
            var aim = aimSource ? aimSource : (Camera.main ? Camera.main.transform : null);
            if (!game || !game.goal || !aim) return false;

            var goal = game.goal;
            var plane = new Plane(goal.forward, goal.position);
            var ray = new Ray(aim.position, aim.forward);
            if (!plane.Raycast(ray, out float distance) || distance > 40f)
            {
                // Looking away from the goal: lob towards a point straight ahead on the field.
                target = aim.position + Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized * 6f;
                target.y = 0.5f;
                return true;
            }

            var local = goal.InverseTransformPoint(ray.GetPoint(distance));
            local.x = Mathf.Clamp(local.x, -4f, 4f);
            local.y = Mathf.Clamp(local.y, 0.12f, 3.5f);
            local.z = 0f;
            target = goal.TransformPoint(local);
            return true;
        }
    }
}
