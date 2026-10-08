using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Handball.EditorTools
{
    /// <summary>
    /// Generates every procedural asset the handball scene uses (textures, materials, meshes,
    /// physics materials, Unity-chan's animator and the post-processing profile).
    /// </summary>
    public static class HandballArt
    {
        public const string Root = "Assets/Handball/Generated";
        const string TexDir = Root + "/Textures";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string PhysDir = Root + "/Physics";

        static Shader Toon => Shader.Find("Handball/Toon");
        static Shader Fx => Shader.Find("Handball/UnlitFx");

        readonly static Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();

        public static void EnsureFolders()
        {
            foreach (var dir in new[] { Root, TexDir, MatDir, MeshDir, PhysDir })
            {
                if (AssetDatabase.IsValidFolder(dir)) continue;
                var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
            }
        }

        // ------------------------------------------------------------------ textures

        public static Texture2D GrassTexture() => MakeTexture("T_Grass", 256, 256, (x, y) =>
        {
            // Two mowing stripes per tile with a little blade noise.
            bool stripe = x < 128;
            var a = stripe ? new Color(0.47f, 0.80f, 0.36f) : new Color(0.42f, 0.74f, 0.33f);
            float n = Hash(x, y) * 0.06f - 0.03f;
            float blade = Mathf.PerlinNoise(x * 0.11f, y * 0.37f) * 0.05f;
            return new Color(a.r + n, a.g + n + blade, a.b + n * 0.5f, 1f);
        }, TextureWrapMode.Repeat);

        public static Texture2D NetTexture() => MakeTexture("T_Net", 64, 64, (x, y) =>
        {
            bool line = x < 4 || y < 4;
            return line ? Color.white : new Color(1, 1, 1, 0);
        }, TextureWrapMode.Repeat, alpha: true);

        public static Texture2D BallTexture() => MakeTexture("T_Ball", 512, 256, (x, y) =>
        {
            // Equirectangular panels: white base with wavy pink, sky-blue and yellow bands.
            float lon = x / 512f * Mathf.PI * 2f;
            float lat = (y / 256f - 0.5f) * Mathf.PI;
            float wave = Mathf.Sin(lon * 3f) * 0.35f;
            float band = lat + wave;
            var white = new Color(0.98f, 0.97f, 0.94f);
            var pink = new Color(1f, 0.38f, 0.6f);
            var blue = new Color(0.3f, 0.7f, 1f);
            var yellow = new Color(1f, 0.85f, 0.25f);
            Color c = white;
            if (Mathf.Abs(band) < 0.22f) c = pink;
            else if (Mathf.Abs(band - 0.75f) < 0.16f || Mathf.Abs(band + 0.75f) < 0.16f) c = blue;
            float seam = Mathf.Abs(Mathf.Sin(lon * 2f + lat)) < 0.03f ? 0.75f : 1f;
            if (Mathf.Abs(lat) > 1.3f) c = yellow;
            return c * seam;
        }, TextureWrapMode.Repeat);

        public static Texture2D StripeTexture(string name, bool vertical) => MakeTexture(name, vertical ? 1 : 2, vertical ? 2 : 1, (x, y) =>
        {
            int i = vertical ? y : x;
            return i == 0 ? new Color(0.95f, 0.25f, 0.3f) : new Color(0.98f, 0.98f, 0.98f);
        }, TextureWrapMode.Repeat, point: true);

        public static Texture2D RoundedRect() => MakeTexture("T_RoundedRect", 128, 128, (x, y) =>
        {
            const float r = 40f;
            float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (127 - r)));
            float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (127 - r)));
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(r - d + 0.5f);
            return new Color(1, 1, 1, a);
        }, TextureWrapMode.Clamp, alpha: true, spriteBorder: 44);

        public static Texture2D RingTexture() => MakeTexture("T_Ring", 128, 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f;
            float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.1f);
            float dot = Mathf.Clamp01(1f - d / 0.18f);
            return new Color(1, 1, 1, Mathf.Max(ring, dot));
        }, TextureWrapMode.Clamp, alpha: true);

        public static Texture2D DotTexture() => MakeTexture("T_Dot", 64, 64, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
            // One round dot per tile, with a transparent gap: reads as a dotted line on a LineRenderer.
            float a = x < 32 ? Mathf.Clamp01((0.9f - Vector2.Distance(new Vector2(x, y), new Vector2(16f, 31.5f)) / 16f) * 4f) : 0f;
            return new Color(1, 1, 1, a);
        }, TextureWrapMode.Repeat, alpha: true);

        public static Texture2D SoftCircle() => MakeTexture("T_SoftCircle", 64, 64, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
            return new Color(1, 1, 1, Mathf.SmoothStep(1f, 0f, d));
        }, TextureWrapMode.Clamp, alpha: true);

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h & 0xFFFF) / 65535f;
        }

        static Texture2D MakeTexture(string name, int w, int h, System.Func<int, int, Color> pixel, TextureWrapMode wrap,
            bool alpha = false, bool point = false, int spriteBorder = -1)
        {
            EnsureFolders();
            var path = $"{TexDir}/{name}.png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = pixel(x, y);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            importer.mipmapEnabled = !point && spriteBorder < 0;
            importer.alphaIsTransparency = alpha;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.anisoLevel = 4;
            if (spriteBorder >= 0)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteBorder = new Vector4(spriteBorder, spriteBorder, spriteBorder, spriteBorder);
                importer.spritePixelsPerUnit = 100;
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ materials

        public class ToonSettings
        {
            public Color color = Color.white;
            public Texture texture;
            public Vector2 tiling = Vector2.one;
            public Color shade = new Color(0.72f, 0.68f, 0.86f);
            public float outline = 0f;
            public Color outlineColor = new Color(0.22f, 0.16f, 0.28f);
            public float rim = 0.2f;
            public Color emission = Color.black;
            public bool alphaClip;
            public bool transparent;
            public bool doubleSided;
            public float threshold = 0.05f;
            public float softness = 0.06f;
            public float ambient = 0.35f;
            public bool instancing = true;
        }

        public static Material ToonMaterial(string name, ToonSettings s)
        {
            EnsureFolders();
            var path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Toon);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Toon;
            mat.SetColor("_BaseColor", s.color);
            mat.SetTexture("_BaseMap", s.texture);
            mat.SetTextureScale("_BaseMap", s.tiling);
            mat.SetColor("_ShadeColor", s.shade);
            mat.SetFloat("_ShadeThreshold", s.threshold);
            mat.SetFloat("_ShadeSoftness", s.softness);
            mat.SetFloat("_AmbientStrength", s.ambient);
            mat.SetFloat("_RimStrength", s.rim);
            mat.SetColor("_EmissionColor", s.emission);
            mat.SetFloat("_OutlineWidth", s.outline);
            mat.SetColor("_OutlineColor", s.outlineColor);
            mat.SetFloat("_Cull", s.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            mat.SetFloat("_AlphaClip", s.alphaClip ? 1f : 0f);
            if (s.alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            if (s.transparent)
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = s.alphaClip ? (int)RenderQueue.AlphaTest : -1;
                mat.SetOverrideTag("RenderType", s.alphaClip ? "TransparentCutout" : "Opaque");
            }
            mat.SetShaderPassEnabled("SRPDefaultUnlit", s.outline > 0f && !s.transparent);
            mat.SetShaderPassEnabled("ShadowCaster", !s.transparent);
            mat.enableInstancing = s.instancing;
            EditorUtility.SetDirty(mat);
            s_Materials[name] = mat;
            return mat;
        }

        public static Material FxMaterial(string name, Texture texture, Color color, bool zWrite = false)
        {
            EnsureFolders();
            var path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Fx);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Fx;
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_ZWrite", zWrite ? 1f : 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ physics materials

        public static PhysicsMaterial PhysicsMat(string name, float bounce, float friction, PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Average)
        {
            EnsureFolders();
            var path = $"{PhysDir}/{name}.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (!pm)
            {
                pm = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(pm, path);
            }
            pm.bounciness = bounce;
            pm.dynamicFriction = friction;
            pm.staticFriction = friction;
            pm.bounceCombine = bounceCombine;
            pm.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        // ------------------------------------------------------------------ meshes

        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            EnsureFolders();
            var path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Flat ribbon along a polyline on the ground (for field lines).</summary>
        public static Mesh GroundStrip(List<Vector2> points, float width, float height)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var normals = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
            {
                var prev = points[Mathf.Max(0, i - 1)];
                var next = points[Mathf.Min(points.Count - 1, i + 1)];
                var dir = (next - prev).normalized;
                var side = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                var p = points[i];
                verts.Add(new Vector3(p.x + side.x, height, p.y + side.y));
                verts.Add(new Vector3(p.x - side.x, height, p.y - side.y));
                normals.Add(Vector3.up); normals.Add(Vector3.up);
                if (i > 0)
                {
                    int b = verts.Count - 4;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            // Wind consistently upwards regardless of polyline direction.
            if (mesh.triangles.Length >= 3)
            {
                var t = mesh.triangles;
                var n = Vector3.Cross(verts[t[1]] - verts[t[0]], verts[t[2]] - verts[t[0]]);
                if (n.y < 0)
                {
                    for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                    mesh.triangles = t;
                }
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Puffy five-point star (for the dizzy effect and decorations).</summary>
        public static Mesh StarMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int points = 5;
            const float depth = 0.25f;
            verts.Add(new Vector3(0, 0, -depth));
            verts.Add(new Vector3(0, 0, depth));
            for (int i = 0; i < points * 2; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = i % 2 == 0 ? 1f : 0.45f;
                verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
            int n = points * 2;
            for (int i = 0; i < n; i++)
            {
                int a = 2 + i, b = 2 + (i + 1) % n;
                tris.AddRange(new[] { 0, b, a });
                tris.AddRange(new[] { 1, a, b });
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Triangle pennant for bunting, hanging down from its top edge.</summary>
        public static Mesh PennantMesh()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, -1f, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0) });
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ animator & post

        public static AnimatorController KeeperController()
        {
            EnsureFolders();
            var path = $"{Root}/AC_UnityChanKeeper.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var layer = controller.layers[0];
            layer.iKPass = true;
            controller.layers = new[] { layer };
            var sm = controller.layers[0].stateMachine;

            AnimationClip Clip(string fbx) => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

            var idle = sm.AddState("Idle");
            idle.motion = Clip(HandballAssetSetup.IdleAnimPath);
            sm.defaultState = idle;

            foreach (var (name, fbx, speed) in new[] { ("RisingPunch", HandballAssetSetup.RisingAnimPath, 1f), ("Jab", HandballAssetSetup.JabAnimPath, 1f) })
            {
                var state = sm.AddState(name);
                state.motion = Clip(fbx);
                state.speed = speed;
                var back = state.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        public static VolumeProfile PostProfile()
        {
            EnsureFolders();
            var path = $"{Root}/VP_Handball.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.6f);

            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(14f);
            color.postExposure.Override(0.15f);
            color.contrast.Override(6f);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.5f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
