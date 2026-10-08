using System;
using System.Collections.Generic;
using UnityEngine;

namespace Handball
{
    /// <summary>Small pooled one-shot audio player with per-cue clip variations and pitch jitter.</summary>
    public class SfxPlayer : MonoBehaviour
    {
        public enum Cue
        {
            Grab, Whoosh, Bounce, Post, NetHit, KeeperHit, KeeperLand, KeeperJump,
            GoalJingle, Cheer, PartyPop, SaveJingle, Miss, Respawn, Boing, Whistle, Celebrate
        }

        [Serializable]
        public class Entry
        {
            public Cue cue;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 1f;
            public Vector2 pitch = new Vector2(0.95f, 1.05f);
            [Tooltip("0 = 2D (jingles), 1 = fully positional.")]
            [Range(0f, 1f)] public float spatialBlend = 0.85f;
        }

        public List<Entry> entries = new List<Entry>();
        public int voices = 16;
        [Range(0f, 1f)] public float masterVolume = 0.9f;

        readonly List<AudioSource> m_Pool = new List<AudioSource>();
        Dictionary<Cue, Entry> m_Lookup;
        int m_Next;

        void Awake()
        {
            m_Lookup = new Dictionary<Cue, Entry>();
            foreach (var e in entries) m_Lookup[e.cue] = e;
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 40f;
                src.dopplerLevel = 0f;
                m_Pool.Add(src);
            }
        }

        public bool Has(Cue cue) => m_Lookup != null && m_Lookup.TryGetValue(cue, out var e) && e.clips != null && e.clips.Length > 0;

        public void Play(Cue cue, Vector3 position, float volumeScale = 1f)
        {
            if (m_Lookup == null || !m_Lookup.TryGetValue(cue, out var entry) || entry.clips == null || entry.clips.Length == 0) return;
            var clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
            if (!clip) return;

            var src = NextVoice();
            src.transform.position = position;
            src.spatialBlend = entry.spatialBlend;
            src.pitch = UnityEngine.Random.Range(entry.pitch.x, entry.pitch.y);
            src.volume = entry.volume * volumeScale * masterVolume;
            src.clip = clip;
            src.Play();
        }

        AudioSource NextVoice()
        {
            // Prefer an idle voice; otherwise steal round-robin.
            for (int i = 0; i < m_Pool.Count; i++)
            {
                var candidate = m_Pool[(m_Next + i) % m_Pool.Count];
                if (!candidate.isPlaying)
                {
                    m_Next = (m_Next + i + 1) % m_Pool.Count;
                    return candidate;
                }
            }
            var stolen = m_Pool[m_Next];
            m_Next = (m_Next + 1) % m_Pool.Count;
            return stolen;
        }
    }
}
