using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Handball
{
    /// <summary>
    /// While a ball is held in point-and-throw mode, draws the arc the ball will fly along
    /// and a reticle where it will reach the goal plane.
    /// </summary>
    public class ThrowTrajectoryPreview : MonoBehaviour
    {
        public HandballThrowAssist assist;
        public LineRenderer line;
        public Transform reticle;
        public Renderer reticleRenderer;
        public Color onTargetColor = new Color(1f, 0.85f, 0.2f, 1f);
        public Color offTargetColor = new Color(1f, 1f, 1f, 0.6f);
        public int maxPoints = 60;
        public float timeStep = 0.025f;

        readonly List<HandballBall> m_Held = new List<HandballBall>();
        readonly List<Vector3> m_Points = new List<Vector3>();
        MaterialPropertyBlock m_Block;
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            m_Block = new MaterialPropertyBlock();
            foreach (var ball in FindObjectsByType<HandballBall>(FindObjectsSortMode.None))
            {
                var b = ball;
                b.Grab.selectEntered.AddListener(_ => { if (!m_Held.Contains(b)) m_Held.Add(b); });
                b.Grab.selectExited.AddListener(_ => { if (!b.Grab.isSelected) m_Held.Remove(b); });
            }
            SetVisible(false);
        }

        void LateUpdate()
        {
            var game = HandballGame.Instance;
            var ball = m_Held.Count > 0 ? m_Held[m_Held.Count - 1] : null;
            if (!ball || !assist || !assist.UsePointAndThrow || !game || !assist.TryGetAimTarget(out var target))
            {
                SetVisible(false);
                return;
            }

            var start = ball.transform.position;
            var v0 = Ballistics.SolveLaunch(start, target, assist.pointThrowSpeed, -Physics.gravity.y);
            var goal = game.goal;
            m_Points.Clear();
            for (int i = 0; i < maxPoints; i++)
            {
                var p = Ballistics.PositionAt(start, v0, i * timeStep);
                m_Points.Add(p);
                if (p.y < 0f || goal.InverseTransformPoint(p).z > 0.15f) break;
            }
            line.positionCount = m_Points.Count;
            line.SetPositions(m_Points.ToArray());

            var local = goal.InverseTransformPoint(target);
            bool onTarget = game.IsInsideGoalMouth(local, -0.05f);
            reticle.position = target - goal.forward * 0.02f;
            reticle.rotation = Quaternion.LookRotation(goal.forward);
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 8f);
            reticle.localScale = Vector3.one * (0.32f * pulse);

            var color = onTarget ? onTargetColor : offTargetColor;
            line.startColor = new Color(color.r, color.g, color.b, 0.15f);
            line.endColor = color;
            if (reticleRenderer)
            {
                reticleRenderer.GetPropertyBlock(m_Block);
                m_Block.SetColor(s_BaseColor, color);
                reticleRenderer.SetPropertyBlock(m_Block);
            }
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (line) line.enabled = visible;
            if (reticle) reticle.gameObject.SetActive(visible);
        }
    }
}
