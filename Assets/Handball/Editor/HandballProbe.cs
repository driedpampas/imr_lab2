using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Handball.EditorTools
{
    /// <summary>
    /// Diagnostics used while building the project: dumps facts about the Unity-chan rig,
    /// animation clips and the candidate audio clips to Logs/probe.txt.
    /// </summary>
    public static class HandballProbe
    {
        [MenuItem("Handball/Debug/Run Probe")]
        public static void Run()
        {
            HandballAssetSetup.ConfigureUnityChanImport();

            var sb = new StringBuilder();
            ProbeModel(sb);
            ProbeClips(sb);
            ProbeAudio(sb, "Assets/Handball/Audio/_Candidates");
            ProbeAudio(sb, "Assets/Handball/Audio/Kenney");
            File.WriteAllText("Logs/probe.txt", sb.ToString());
            Debug.Log("[Probe] done");
        }

        static void ProbeModel(StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandballAssetSetup.UnityChanModelPath);
            var go = Object.Instantiate(prefab);
            var animator = go.GetComponent<Animator>();
            sb.AppendLine($"== Model: animator={animator != null} avatar={(animator ? animator.avatar : null)} human={(animator && animator.avatar ? animator.avatar.isHuman : false)} valid={(animator && animator.avatar ? animator.avatar.isValid : false)}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = string.Join(", ", r.sharedMaterials.Select(m => m ? m.name + "(" + (m.mainTexture ? m.mainTexture.name : "-") + ")" : "null"));
                sb.AppendLine($"  renderer {r.GetType().Name} '{r.name}' bounds={r.bounds.center:F3}/{r.bounds.size:F3} mats=[{mats}]");
            }
            if (animator && animator.isHuman)
            {
                foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone) continue;
                    var t = animator.GetBoneTransform(b);
                    if (t) sb.AppendLine($"  bone {b,-22} {t.name,-28} pos={t.position:F3}");
                }
            }
            var all = go.GetComponentsInChildren<Transform>(true);
            sb.AppendLine($"  transforms={all.Length} root children: {string.Join(", ", go.transform.Cast<Transform>().Select(t => t.name))}");
            Object.DestroyImmediate(go);
        }

        static void ProbeClips(StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandballAssetSetup.UnityChanModelPath);
            foreach (var path in HandballAssetSetup.UnityChanAnimPaths)
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                foreach (var clip in clips)
                {
                    sb.AppendLine($"== Clip '{clip.name}' in {Path.GetFileName(path)} len={clip.length:F2}s human={clip.humanMotion} loop={clip.isLooping} fps={clip.frameRate}");
                    var go = Object.Instantiate(prefab);
                    var animator = go.GetComponent<Animator>();
                    foreach (var t in new[] { 0f, clip.length * 0.25f, clip.length * 0.5f, clip.length * 0.75f, clip.length })
                    {
                        clip.SampleAnimation(go, t);
                        if (animator && animator.isHuman)
                        {
                            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                            var head = animator.GetBoneTransform(HumanBodyBones.Head);
                            var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                            sb.AppendLine($"   t={t:F2} hips={hips.position:F2} head={head.position:F2} lfoot={lf.position:F2} hipsFwd={hips.forward:F2} hipsUp={hips.up:F2}");
                        }
                    }
                    Object.DestroyImmediate(go);
                }
            }
        }

        static void ProbeAudio(StringBuilder sb, string folder)
        {
            sb.AppendLine($"== Audio in {folder}");
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var data = new float[clip.samples * clip.channels];
                bool ok = clip.GetData(data, 0);
                if (!ok) { sb.AppendLine($"  {clip.name} len={clip.length:F2} (no data)"); continue; }
                int ch = clip.channels;
                var mono = new float[clip.samples];
                for (int i = 0; i < mono.Length; i++) { float s = 0; for (int c = 0; c < ch; c++) s += data[i * ch + c]; mono[i] = s / ch; }
                float peak = mono.Max(Mathf.Abs);
                // Pitch at a few points (autocorrelation), plus loudness.
                var pitches = new StringBuilder();
                for (int k = 0; k < 6; k++)
                {
                    int center = (int)(mono.Length * (0.08f + k * 0.16f));
                    pitches.Append($"{EstimatePitch(mono, center, clip.frequency):F0} ");
                }
                sb.AppendLine($"  {clip.name,-26} len={clip.length:F2}s peak={peak:F2} pitchHz=[{pitches}]");
            }
        }

        static float EstimatePitch(float[] x, int center, int sr)
        {
            int win = Mathf.Min(2048, x.Length / 4);
            int start = Mathf.Clamp(center - win / 2, 0, x.Length - win - sr / 80 - 1);
            if (start < 0) return 0;
            int minLag = sr / 1200, maxLag = sr / 80;
            float best = 0; int bestLag = 0;
            float energy = 0;
            for (int i = 0; i < win; i++) energy += x[start + i] * x[start + i];
            if (energy < 1e-4f) return 0;
            for (int lag = minLag; lag < maxLag; lag++)
            {
                float s = 0;
                for (int i = 0; i < win; i++) s += x[start + i] * x[start + i + lag];
                if (s > best) { best = s; bestLag = lag; }
            }
            return bestLag > 0 ? (float)sr / bestLag : 0;
        }
    }
}
