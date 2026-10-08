using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Handball.EditorTools
{
    /// <summary>Renders preview shots of the handball scene to Logs/shots (used to review the look headlessly).</summary>
    public static class HandballCapture
    {
        public static readonly (string name, Vector3 pos, Vector3 target, float fov)[] Views =
        {
            ("player", new Vector3(0f, 1.55f, -0.35f), new Vector3(0f, 1.25f, 7f), 70f),
            ("overview", new Vector3(9f, 6f, -7f), new Vector3(0f, 1f, 4f), 55f),
            ("keeper", new Vector3(0.2f, 1.25f, 4.6f), new Vector3(0f, 1.05f, 6.3f), 40f),
            ("face", new Vector3(0f, 1.32f, 5.6f), new Vector3(0f, 1.3f, 6.3f), 30f),
            ("rack_right", new Vector3(0f, 1.55f, -0.35f), new Vector3(0.7f, 1.4f, -0.1f), 70f),
            ("behind", new Vector3(-3f, 2.5f, 13f), new Vector3(0f, 1.2f, 5f), 60f),
        };

        [MenuItem("Handball/Debug/Capture Shots")]
        public static void CaptureScene()
        {
            EditorSceneManager.OpenScene(HandballSceneBuilder.ScenePath);
            PoseKeeperIdle();
            foreach (var v in Views) Capture(v.name, v.pos, v.target, v.fov);
            Debug.Log("[HandballCapture] done");
        }

        public static void BuildAndCapture()
        {
            HandballSceneBuilder.Build();
            CaptureScene();
        }

        static void PoseKeeperIdle()
        {
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            if (!keeper || !keeper.animator) return;
            var clip = keeper.animator.runtimeAnimatorController.animationClips[0];
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(keeper.animator.gameObject, clip, 0.2f);
            AnimationMode.EndSampling();
        }

        public static void Capture(string name, Vector3 pos, Vector3 target, float fov, int width = 1280, int height = 720)
        {
            Directory.CreateDirectory("Logs/shots");
            var go = new GameObject("Capture Camera");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 400f;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes($"Logs/shots/{name}.png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }
}
