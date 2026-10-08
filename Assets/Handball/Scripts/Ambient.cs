using UnityEngine;

namespace Handball
{
    /// <summary>Gentle procedural motion for set dressing: bobbing, swaying, spinning and drifting.</summary>
    public class Ambient : MonoBehaviour
    {
        public Vector3 bobAxis = Vector3.up;
        public float bobAmplitude = 0.1f;
        public float bobSpeed = 1f;
        public Vector3 swayAxis = Vector3.forward;
        public float swayDegrees = 0f;
        public float swaySpeed = 1f;
        public Vector3 spinAxis = Vector3.up;
        public float spinSpeed = 0f;
        [Tooltip("World-space drift (m/s); wraps around within driftRange of the start position.")]
        public Vector3 drift = Vector3.zero;
        public float driftRange = 60f;
        public bool randomPhase = true;

        Vector3 m_StartPos;
        Quaternion m_StartRot;
        float m_Phase;
        Vector3 m_DriftOffset;

        void Start()
        {
            m_StartPos = transform.localPosition;
            m_StartRot = transform.localRotation;
            m_Phase = randomPhase ? Random.value * 100f : 0f;
        }

        void Update()
        {
            float t = Time.time + m_Phase;
            m_DriftOffset += drift * Time.deltaTime;
            if (drift != Vector3.zero && m_DriftOffset.magnitude > driftRange) m_DriftOffset = -m_DriftOffset.normalized * driftRange;
            transform.localPosition = m_StartPos + m_DriftOffset + bobAxis * (Mathf.Sin(t * bobSpeed) * bobAmplitude);
            var rot = m_StartRot;
            if (swayDegrees != 0f) rot *= Quaternion.AngleAxis(Mathf.Sin(t * swaySpeed) * swayDegrees, swayAxis);
            if (spinSpeed != 0f) rot *= Quaternion.AngleAxis(t * spinSpeed, spinAxis);
            transform.localRotation = rot;
        }
    }
}
