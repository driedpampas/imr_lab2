using UnityEngine;

namespace Handball
{
    /// <summary>Projectile math shared by the throw assist, the trajectory preview and the goalkeeper.</summary>
    public static class Ballistics
    {
        public static Vector3 PositionAt(Vector3 p0, Vector3 v0, float t)
        {
            return p0 + v0 * t + 0.5f * Physics.gravity * t * t;
        }

        public static Vector3 VelocityAt(Vector3 v0, float t)
        {
            return v0 + Physics.gravity * t;
        }

        /// <summary>
        /// Time at which a projectile crosses the plane (point, normal), travelling against the normal.
        /// Gravity is assumed perpendicular to the normal (vertical plane).
        /// </summary>
        public static bool TimeToPlane(Vector3 p0, Vector3 v0, Vector3 planePoint, Vector3 planeNormal, out float t)
        {
            float closing = Vector3.Dot(v0, planeNormal);
            float distance = Vector3.Dot(planePoint - p0, planeNormal);
            t = 0f;
            if (Mathf.Abs(closing) < 1e-3f) return false;
            t = distance / closing;
            return t > 0f;
        }

        /// <summary>
        /// Launch velocity that carries a projectile from source to target with the given horizontal speed.
        /// </summary>
        public static Vector3 SolveLaunch(Vector3 source, Vector3 target, float horizontalSpeed, float gravity)
        {
            var delta = target - source;
            var horizontal = new Vector3(delta.x, 0f, delta.z);
            float distance = horizontal.magnitude;
            float t = Mathf.Max(0.2f, distance / Mathf.Max(0.1f, horizontalSpeed));
            var velocity = horizontal / t;
            velocity.y = delta.y / t + 0.5f * gravity * t;
            return velocity;
        }
    }
}
