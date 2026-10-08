using UnityEngine;

namespace Handball
{
    /// <summary>Configures a ParticleSystem as a burst of fluttering, colourful confetti.</summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ConfettiBurst : MonoBehaviour
    {
        public int count = 70;
        public float speedMin = 2.5f;
        public float speedMax = 5.5f;
        public float coneAngle = 35f;
        public float size = 0.045f;
        [Tooltip("Confetti settles on this plane instead of falling through the field.")]
        public Transform groundPlane;
        public Color[] palette =
        {
            new Color(1f, 0.36f, 0.55f), new Color(1f, 0.82f, 0.25f), new Color(0.36f, 0.82f, 1f),
            new Color(0.55f, 0.95f, 0.45f), new Color(0.78f, 0.55f, 1f), new Color(1f, 0.6f, 0.3f)
        };

        ParticleSystem m_System;

        void Awake()
        {
            m_System = GetComponent<ParticleSystem>();
            Configure();
        }

        void Configure()
        {
            var ps = m_System;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(size * 0.4f, size * 0.7f);
            main.startSizeZ = 1f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 3;

            var gradient = new Gradient();
            var keys = new GradientColorKey[Mathf.Min(8, palette.Length)];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(palette[i], keys.Length == 1 ? 0f : (float)i / (keys.Length - 1));
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            gradient.mode = GradientMode.Fixed;
            main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[0]);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.05f;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rotation.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.9f;
            limit.dampen = 0.08f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.4f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var alpha = new Gradient();
            alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = alpha;

            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.dampen = 0.9f;
            collision.bounce = 0.05f;
            collision.lifetimeLoss = 0f;
            if (groundPlane) collision.SetPlane(0, groundPlane);
            else collision.enabled = false;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
        }

        public void Burst()
        {
            if (!m_System) return;
            m_System.Emit(count);
        }
    }
}
