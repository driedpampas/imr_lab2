using TMPro;
using UnityEngine;

namespace Handball
{
    /// <summary>World-space scoreboard on top of the goal: goal counter, keeper saves and pop-up messages.</summary>
    public class ScoreboardUI : MonoBehaviour
    {
        public enum PopupStyle { Goal, Save, Miss, Info }

        public TMP_Text goalsValue;
        public TMP_Text savesValue;
        public TMP_Text popup;
        public RectTransform goalsPunchTarget;
        public RectTransform savesPunchTarget;

        [Header("Colours")]
        public Color goalColor = new Color(1f, 0.83f, 0.2f);
        public Color saveColor = new Color(0.35f, 0.75f, 1f);
        public Color missColor = new Color(0.85f, 0.85f, 0.9f);

        int m_Goals = -1;
        int m_Saves = -1;
        float m_GoalsPunch = 10f;
        float m_SavesPunch = 10f;
        float m_PopupTime = 10f;
        Color m_PopupColor;
        Vector3 m_PopupBase;

        const float PopupDuration = 1.6f;

        void Awake()
        {
            if (popup)
            {
                m_PopupBase = popup.rectTransform.localPosition;
                popup.gameObject.SetActive(false);
            }
        }

        public void SetScore(int goals, int saves, bool animate)
        {
            if (goalsValue) goalsValue.text = goals.ToString();
            if (savesValue) savesValue.text = saves.ToString();
            if (animate && goals != m_Goals && m_Goals >= 0) m_GoalsPunch = 0f;
            if (animate && saves != m_Saves && m_Saves >= 0) m_SavesPunch = 0f;
            m_Goals = goals;
            m_Saves = saves;
        }

        public void ShowPopup(string text, PopupStyle style)
        {
            if (!popup) return;
            popup.text = text;
            m_PopupColor = style switch
            {
                PopupStyle.Goal => goalColor,
                PopupStyle.Save => saveColor,
                PopupStyle.Miss => missColor,
                _ => Color.white
            };
            m_PopupTime = 0f;
            popup.gameObject.SetActive(true);
        }

        void Update()
        {
            Punch(goalsPunchTarget, ref m_GoalsPunch, 1.6f);
            Punch(savesPunchTarget, ref m_SavesPunch, 1.35f);

            if (goalsValue)
            {
                // A gentle rainbow shimmer on the goal counter right after scoring.
                float glow = Mathf.Clamp01(1f - m_GoalsPunch / 1.2f);
                var rainbow = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.8f, 1f), 0.55f, 1f);
                goalsValue.color = Color.Lerp(Color.white, rainbow, glow);
            }

            if (popup && popup.gameObject.activeSelf)
            {
                m_PopupTime += Time.deltaTime;
                float t = m_PopupTime / PopupDuration;
                if (t >= 1f)
                {
                    popup.gameObject.SetActive(false);
                    return;
                }
                float pop = t < 0.15f ? Mathf.Lerp(0.2f, 1.25f, t / 0.15f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((t - 0.15f) / 0.15f));
                popup.rectTransform.localScale = Vector3.one * pop;
                popup.rectTransform.localPosition = m_PopupBase + Vector3.up * (t * 40f);
                popup.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(m_PopupTime * 10f) * 4f * (1f - t));
                var c = m_PopupColor;
                c.a = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                popup.color = c;
            }
        }

        static void Punch(RectTransform target, ref float time, float strength)
        {
            if (!target) return;
            time += Time.deltaTime;
            const float duration = 0.6f;
            if (time >= duration)
            {
                target.localScale = Vector3.one;
                return;
            }
            float t = time / duration;
            float s = 1f + (strength - 1f) * Mathf.Sin(t * Mathf.PI) * (1f - t) * 1.6f;
            target.localScale = Vector3.one * s;
        }
    }
}
