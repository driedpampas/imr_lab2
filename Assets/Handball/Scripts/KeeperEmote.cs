using TMPro;
using UnityEngine;

namespace Handball
{
    /// <summary>A little speech bubble above Unity-chan's head that pops in, wobbles and fades.</summary>
    public class KeeperEmote : MonoBehaviour
    {
        public enum Mood { Neutral, Happy, Sad }

        public TMP_Text label;
        public Transform bubble;
        public Transform followHead;
        public Vector3 offset = new Vector3(0f, 0.42f, 0f);
        public float duration = 1.8f;
        public Color neutralColor = new Color(0.25f, 0.25f, 0.35f);
        public Color happyColor = new Color(0.95f, 0.35f, 0.55f);
        public Color sadColor = new Color(0.3f, 0.45f, 0.85f);

        float m_Time = 99f;

        void Awake()
        {
            if (bubble) bubble.gameObject.SetActive(false);
        }

        public void Show(string text, Mood mood)
        {
            if (!label || !bubble) return;
            label.text = text;
            label.color = mood switch { Mood.Happy => happyColor, Mood.Sad => sadColor, _ => neutralColor };
            m_Time = 0f;
            bubble.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (!bubble || !bubble.gameObject.activeSelf) return;
            m_Time += Time.deltaTime;
            if (followHead)
            {
                var headPos = followHead.position;
                // Stay upright above the head even while she is diving or lying down.
                transform.position = new Vector3(headPos.x, Mathf.Max(headPos.y, 0.3f), headPos.z) + offset;
            }
            float t = m_Time / duration;
            if (t >= 1f)
            {
                bubble.gameObject.SetActive(false);
                return;
            }
            float pop = t < 0.1f ? Mathf.Lerp(0.3f, 1.15f, t / 0.1f) : (t < 0.2f ? Mathf.Lerp(1.15f, 1f, (t - 0.1f) / 0.1f) : 1f);
            float fade = t > 0.85f ? 1f - (t - 0.85f) / 0.15f : 1f;
            bubble.localScale = Vector3.one * (pop * Mathf.Max(0.01f, fade));
            bubble.localPosition = Vector3.up * (Mathf.Sin(m_Time * 6f) * 0.015f);
        }
    }
}
