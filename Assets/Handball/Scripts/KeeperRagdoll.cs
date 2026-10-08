using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Builds a humanoid ragdoll at runtime on the keeper's bones. While animated the bodies are kinematic
    /// (so the ball bounces off her arms and body); when switched on they go limp with the momentum of the dive.
    /// </summary>
    public class KeeperRagdoll : MonoBehaviour
    {
        public Animator animator;
        public PhysicsMaterial material;
        [Tooltip("Extra radius on the hands so saves feel fair (goalkeeper gloves).")]
        public float handRadius = 0.085f;

        public class Part
        {
            public HumanBodyBones bone;
            public Transform transform;
            public Rigidbody body;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
        }

        public readonly List<Part> parts = new List<Part>();
        public bool IsRagdoll { get; private set; }
        public Rigidbody HipsBody { get; private set; }
        public Transform Hips => animator.GetBoneTransform(HumanBodyBones.Hips);

        readonly List<Collider> m_Colliders = new List<Collider>();
        bool m_Built;

        public void Build()
        {
            if (m_Built) return;
            m_Built = true;

            var a = animator;
            var right = transform.right;
            var up = transform.up;
            var fwd = transform.forward;

            AddPart(HumanBodyBones.Hips, null, 9f, CapsuleBetween(a, HumanBodyBones.Hips, HumanBodyBones.Chest, 0.14f, -0.06f));
            AddPart(HumanBodyBones.Chest, HumanBodyBones.Hips, 9f, CapsuleBetween(a, HumanBodyBones.Chest, HumanBodyBones.Neck, 0.135f, 0f),
                right, fwd, -25f, 25f, 15f, 15f);
            AddPart(HumanBodyBones.Head, HumanBodyBones.Chest, 4f, SphereAt(a, HumanBodyBones.Head, up * 0.085f, 0.13f),
                right, fwd, -40f, 35f, 30f, 30f);

            foreach (var side in new[] { -1, 1 })
            {
                bool left = side < 0;
                var upperArm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                var lowerArm = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                var middle = left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal;
                var upperLeg = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
                var lowerLeg = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
                var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
                var toes = left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;

                AddPart(upperArm, HumanBodyBones.Chest, 2f, CapsuleBetween(a, upperArm, lowerArm, 0.05f, 0f),
                    fwd, up, -80f, 80f, 70f, 40f);
                AddPart(lowerArm, upperArm, 1.5f, CapsuleBetween(a, lowerArm, hand, 0.045f, 0f),
                    left ? up : -up, fwd, -5f, 130f, 10f, 10f);
                SphereAt(a, hand, a.GetBoneTransform(middle) ? (a.GetBoneTransform(middle).position - a.GetBoneTransform(hand).position) * 0.8f : Vector3.zero, handRadius);

                AddPart(upperLeg, HumanBodyBones.Hips, 5.5f, CapsuleBetween(a, upperLeg, lowerLeg, 0.08f, 0f),
                    right, fwd, -100f, 30f, 35f, 15f);
                AddPart(lowerLeg, upperLeg, 3.5f, CapsuleBetween(a, lowerLeg, foot, 0.06f, 0f),
                    right, fwd, -5f, 120f, 5f, 5f);
                CapsuleBetween(a, foot, toes, 0.05f, 0.04f);
            }

            foreach (var c in m_Colliders)
            {
                c.sharedMaterial = material;
                c.gameObject.layer = HandballLayers.Keeper;
            }
            SetRagdoll(false);
        }

        Collider CapsuleBetween(Animator a, HumanBodyBones from, HumanBodyBones to, float radius, float extend)
        {
            var bone = a.GetBoneTransform(from);
            var child = a.GetBoneTransform(to);
            if (!bone || !child) return null;
            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            var local = bone.InverseTransformPoint(child.position);
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            int axis = 0;
            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;
            capsule.direction = axis;
            capsule.radius = radius / scale;
            var dir = local.normalized;
            capsule.center = local * 0.5f - dir * (extend / scale) * 0.5f;
            capsule.height = local.magnitude + (extend / scale) + 2f * radius / scale * 0.5f;
            m_Colliders.Add(capsule);
            return capsule;
        }

        Collider SphereAt(Animator a, HumanBodyBones boneId, Vector3 worldOffset, float radius)
        {
            var bone = a.GetBoneTransform(boneId);
            if (!bone) return null;
            var sphere = bone.gameObject.AddComponent<SphereCollider>();
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            sphere.radius = radius / scale;
            sphere.center = bone.InverseTransformPoint(bone.position + worldOffset);
            m_Colliders.Add(sphere);
            return sphere;
        }

        void AddPart(HumanBodyBones boneId, HumanBodyBones? parentId, float mass, Collider collider,
            Vector3 twistAxis = default, Vector3 swingAxis = default, float lowTwist = 0f, float highTwist = 0f, float swing1 = 0f, float swing2 = 0f)
        {
            var bone = animator.GetBoneTransform(boneId);
            if (!bone) return;
            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 12;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.6f;
            var part = new Part { bone = boneId, transform = bone, body = body, lastPosition = bone.position, lastRotation = bone.rotation };
            parts.Add(part);
            if (boneId == HumanBodyBones.Hips) HipsBody = body;

            if (parentId.HasValue)
            {
                var parent = parts.Find(p => p.bone == parentId.Value);
                if (parent == null) return;
                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent.body;
                joint.axis = bone.InverseTransformDirection(twistAxis).normalized;
                joint.swingAxis = bone.InverseTransformDirection(swingAxis).normalized;
                joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
                joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
                joint.swing1Limit = new SoftJointLimit { limit = swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = swing2 };
                joint.enableProjection = true;
                joint.projectionDistance = 0.05f;
                joint.projectionAngle = 15f;
                joint.enablePreprocessing = false;
            }
        }

        /// <summary>Switches between animated (kinematic) and limp physics.</summary>
        public void SetRagdoll(bool on)
        {
            IsRagdoll = on;
            float dt = Time.fixedDeltaTime;
            foreach (var p in parts)
            {
                if (on)
                {
                    var v = (p.transform.position - p.lastPosition) / dt;
                    var dq = p.transform.rotation * Quaternion.Inverse(p.lastRotation);
                    dq.ToAngleAxis(out float angle, out var axis);
                    if (angle > 180f) angle -= 360f;
                    p.body.isKinematic = false;
                    p.body.linearVelocity = Vector3.ClampMagnitude(v, 12f);
                    p.body.angularVelocity = float.IsNaN(axis.x) ? Vector3.zero : Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), 15f);
                    p.body.WakeUp();
                }
                else
                {
                    p.body.isKinematic = true;
                }
            }
        }

        /// <summary>Remembers bone poses so momentum can be handed over when going limp.</summary>
        public void TrackPose()
        {
            foreach (var p in parts)
            {
                p.lastPosition = p.transform.position;
                p.lastRotation = p.transform.rotation;
            }
        }

        public float MaxSpeed()
        {
            float max = 0f;
            foreach (var p in parts) max = Mathf.Max(max, p.body.isKinematic ? 0f : p.body.linearVelocity.magnitude);
            return max;
        }
    }
}
