using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    /// <summary>Unity-chan's facial expressions, blended through the blend shapes of her brows, eyes and mouth.</summary>
    public class KeeperFace : MonoBehaviour
    {
        public enum Expression { Default, Smile, Angry, Sad, Confused }

        [Tooltip("Blend shape weight per second (0..100).")]
        public float blendSpeed = 600f;

        public Expression Current => Time.time < m_Until ? m_Expression : Expression.Default;

        struct Shape
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public Expression expression;
        }

        readonly List<Shape> m_Shapes = new List<Shape>();
        Expression m_Expression;
        float m_Until = -1f;

        void Awake()
        {
            // Her face meshes name their shapes BLW_/EYE_/MTH_ + expression, e.g. "blendShape1.MTH_SMILE1".
            var suffixes = new (string suffix, Expression expression)[]
            {
                ("_SMILE1", Expression.Smile), ("_ANG1", Expression.Angry), ("_SAP", Expression.Sad), ("_CONF", Expression.Confused),
            };
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                for (int i = 0; mesh && i < mesh.blendShapeCount; i++)
                {
                    foreach (var (suffix, expression) in suffixes)
                    {
                        if (mesh.GetBlendShapeName(i).EndsWith(suffix))
                            m_Shapes.Add(new Shape { renderer = renderer, index = i, expression = expression });
                    }
                }
            }
        }

        /// <summary>Holds an expression for a while, then relaxes back to her default face.</summary>
        public void Show(Expression expression, float seconds)
        {
            m_Expression = expression;
            m_Until = Time.time + seconds;
        }

        void Update()
        {
            var current = Current;
            float step = blendSpeed * Time.deltaTime;
            foreach (var shape in m_Shapes)
            {
                float weight = shape.renderer.GetBlendShapeWeight(shape.index);
                float target = shape.expression == current ? 100f : 0f;
                if (!Mathf.Approximately(weight, target))
                    shape.renderer.SetBlendShapeWeight(shape.index, Mathf.MoveTowards(weight, target, step));
            }
        }
    }
}
