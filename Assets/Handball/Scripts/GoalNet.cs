using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    /// <summary>
    /// Procedural goal net (back, roof and side panels) with soft colliders and a springy
    /// bulge where the ball hits it. Local space: x sideways, y up, z into the net; origin at the goal line.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GoalNet : MonoBehaviour
    {
        public float width = 3f;
        public float height = 2f;
        public float depthTop = 0.8f;
        public float depthBottom = 1.0f;
        public float cellSize = 0.12f;
        public float meshSquareSize = 0.1f;
        public PhysicsMaterial netMaterial;

        [Header("Bulge")]
        public float pokeRadius = 0.55f;
        public float pokeStrength = 0.045f;
        public float stiffness = 90f;
        public float damping = 7f;

        Mesh m_Mesh;
        Vector3[] m_Rest;
        Vector3[] m_Current;
        Vector3[] m_Offset;
        Vector3[] m_OffsetVel;
        float m_ActiveTime;

        void OnEnable()
        {
            Build();
        }

        void OnDisable()
        {
            if (m_Mesh)
            {
                if (Application.isPlaying) Destroy(m_Mesh); else DestroyImmediate(m_Mesh);
            }
        }

        void Build()
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float hw = width * 0.5f;
            // Back panel
            AddPanel(verts, uvs, tris, new Vector3(-hw, 0, depthBottom), new Vector3(hw, 0, depthBottom), new Vector3(-hw, height, depthTop), new Vector3(hw, height, depthTop));
            // Roof
            AddPanel(verts, uvs, tris, new Vector3(-hw, height, 0), new Vector3(hw, height, 0), new Vector3(-hw, height, depthTop), new Vector3(hw, height, depthTop));
            // Sides
            AddPanel(verts, uvs, tris, new Vector3(-hw, 0, 0), new Vector3(-hw, 0, depthBottom), new Vector3(-hw, height, 0), new Vector3(-hw, height, depthTop));
            AddPanel(verts, uvs, tris, new Vector3(hw, 0, depthBottom), new Vector3(hw, 0, 0), new Vector3(hw, height, depthTop), new Vector3(hw, height, 0));

            m_Mesh = new Mesh { name = "GoalNet", hideFlags = HideFlags.DontSave };
            m_Mesh.SetVertices(verts);
            m_Mesh.SetUVs(0, uvs);
            m_Mesh.SetTriangles(tris, 0);
            m_Mesh.RecalculateNormals();
            m_Mesh.RecalculateBounds();
            m_Mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;

            m_Rest = verts.ToArray();
            m_Current = verts.ToArray();
            m_Offset = new Vector3[m_Rest.Length];
            m_OffsetVel = new Vector3[m_Rest.Length];

            if (Application.isPlaying) BuildColliders();
        }

        void AddPanel(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // a-b bottom edge, c-d top edge
            float lenU = Vector3.Distance(a, b);
            float lenV = Vector3.Distance(a, c);
            int nu = Mathf.Max(1, Mathf.CeilToInt(lenU / cellSize));
            int nv = Mathf.Max(1, Mathf.CeilToInt(lenV / cellSize));
            int start = verts.Count;
            for (int j = 0; j <= nv; j++)
            {
                float v = (float)j / nv;
                for (int i = 0; i <= nu; i++)
                {
                    float u = (float)i / nu;
                    var bottom = Vector3.Lerp(a, b, u);
                    var top = Vector3.Lerp(c, d, u);
                    verts.Add(Vector3.Lerp(bottom, top, v));
                    uvs.Add(new Vector2(u * lenU / meshSquareSize, v * lenV / meshSquareSize));
                }
            }
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int i0 = start + j * (nu + 1) + i;
                    int i1 = i0 + 1;
                    int i2 = i0 + nu + 1;
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }
        }

        void BuildColliders()
        {
            var existing = transform.Find("Colliders");
            if (existing) Destroy(existing.gameObject);
            var root = new GameObject("Colliders").transform;
            root.SetParent(transform, false);

            float hw = width * 0.5f;
            const float t = 0.06f;
            float backDepth = (depthTop + depthBottom) * 0.5f;
            float slope = Mathf.Atan2(depthBottom - depthTop, height) * Mathf.Rad2Deg;
            AddBox(root, "Back", new Vector3(0, height * 0.5f, backDepth + t * 0.5f), new Vector3(width + 0.2f, height + 0.2f, t), Quaternion.Euler(-slope, 0, 0));
            AddBox(root, "Roof", new Vector3(0, height + t * 0.5f, depthTop * 0.5f), new Vector3(width + 0.2f, t, depthTop + 0.1f), Quaternion.identity);
            AddBox(root, "Left", new Vector3(-hw - t * 0.5f, height * 0.5f, backDepth * 0.5f), new Vector3(t, height + 0.1f, backDepth + 0.1f), Quaternion.identity);
            AddBox(root, "Right", new Vector3(hw + t * 0.5f, height * 0.5f, backDepth * 0.5f), new Vector3(t, height + 0.1f, backDepth + 0.1f), Quaternion.identity);
        }

        void AddBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.sharedMaterial = netMaterial;
        }

        /// <summary>Pushes the net in around a world-space point, in the direction the ball was travelling.</summary>
        public void Poke(Vector3 worldPoint, Vector3 worldVelocity)
        {
            if (m_Rest == null) return;
            var local = transform.InverseTransformPoint(worldPoint);
            var dir = transform.InverseTransformDirection(worldVelocity);
            float speed = dir.magnitude;
            if (speed < 0.5f) return;
            dir /= speed;
            float impulse = pokeStrength * Mathf.Clamp(speed, 0f, 18f);
            for (int i = 0; i < m_Rest.Length; i++)
            {
                float d = Vector3.Distance(m_Rest[i], local);
                if (d > pokeRadius) continue;
                float falloff = 1f - d / pokeRadius;
                falloff *= falloff;
                m_OffsetVel[i] += dir * (impulse * falloff * 25f);
            }
            m_ActiveTime = 2.5f;
        }

        void Update()
        {
            if (!Application.isPlaying || m_ActiveTime <= 0f || m_Rest == null) return;
            m_ActiveTime -= Time.deltaTime;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            for (int i = 0; i < m_Rest.Length; i++)
            {
                var accel = -stiffness * m_Offset[i] - damping * m_OffsetVel[i];
                m_OffsetVel[i] += accel * dt;
                m_Offset[i] += m_OffsetVel[i] * dt;
                m_Current[i] = m_Rest[i] + m_Offset[i];
            }
            m_Mesh.vertices = m_Current;
            m_Mesh.RecalculateBounds();
        }
    }
}
