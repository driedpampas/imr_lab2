using UnityEngine;

namespace Handball
{
    /// <summary>Cartoon stars circling Unity-chan's head while she is down after a dive.</summary>
    public class DizzyStars : MonoBehaviour
    {
        public Transform followHead;
        public Transform[] stars;
        public float radius = 0.22f;
        public float speed = 4f;
        public float height = 0.2f;

        float m_Scale;
        bool m_Visible;

        void Awake()
        {
            SetActive(false);
        }

        public void Show(bool visible)
        {
            m_Visible = visible;
            if (visible) SetActive(true);
        }

        void SetActive(bool active)
        {
            foreach (var s in stars) if (s) s.gameObject.SetActive(active);
        }

        void LateUpdate()
        {
            m_Scale = Mathf.MoveTowards(m_Scale, m_Visible ? 1f : 0f, Time.deltaTime * 4f);
            if (!m_Visible && m_Scale <= 0f)
            {
                SetActive(false);
                return;
            }
            if (followHead) transform.position = followHead.position + Vector3.up * height;
            for (int i = 0; i < stars.Length; i++)
            {
                if (!stars[i]) continue;
                float a = Time.time * speed + i * Mathf.PI * 2f / stars.Length;
                stars[i].localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a * 2f) * 0.04f, Mathf.Sin(a) * radius);
                stars[i].localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg * 2f, 0f);
                stars[i].localScale = Vector3.one * (0.09f * m_Scale);
            }
        }
    }
}
