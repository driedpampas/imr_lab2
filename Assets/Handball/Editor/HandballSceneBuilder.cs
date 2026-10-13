using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Handball.EditorTools
{
    /// <summary>
    /// Builds Assets/Handball/Scenes/Handball.unity from scratch: XR rig, field, goal, Unity-chan,
    /// balls, scoreboard, audio and decoration. Menu: Handball/Build Scene.
    /// </summary>
    public static class HandballSceneBuilder
    {
        public const string ScenePath = "Assets/Handball/Scenes/Handball.unity";
        const string XrOriginPrefab = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        static readonly Vector3 GoalPosition = new Vector3(0f, 0f, 7f);
        const float BallRadius = 0.095f;
        const float RackHeight = 1.5f;
        const float RackRadius = 0.72f;

        static Material s_White, s_Grass, s_Line, s_PostV, s_PostH, s_Net, s_Ball, s_Wood, s_Pink, s_Mint, s_Lavender,
            s_Trunk, s_LeafA, s_LeafB, s_LeafC, s_Cloud, s_Hill, s_Stem, s_StarMat, s_Bubble, s_Confetti, s_Trajectory, s_Reticle, s_Puff, s_String;
        static Material[] s_Flowers, s_Flags, s_Balloons;
        static PhysicsMaterial s_BallPhys, s_GrassPhys, s_PostPhys, s_NetPhys, s_KeeperPhys;
        static Mesh s_Star, s_Pennant, s_Quad;
        static Sprite s_Rounded;

        [MenuItem("Handball/Build Scene")]
        public static void Build()
        {
            HandballProjectSetup.Run();
            CreateAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Random.InitState(20261012);

            var sun = BuildLighting();
            var ground = BuildGround();
            BuildFieldLines();
            var goal = BuildGoal();
            var rig = BuildRig(out var camera);
            var sfx = BuildAudio();
            var scoreboard = BuildScoreboard(goal);
            var confetti = BuildConfetti(goal, ground.transform);
            var game = new GameObject("Handball Game").AddComponent<HandballGame>();
            game.goal = goal;
            game.scoreboard = scoreboard;
            game.goalConfetti = confetti;
            game.sfx = sfx;

            BuildKeeper(camera);
            BuildRacksAndBalls(rig);
            BuildTrajectoryPreview(rig);
            BuildHintSign();
            BuildScenery(goal);
            BuildPostProcessing();

            EnsureFolder("Assets/Handball/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[HandballSceneBuilder] Scene built at " + ScenePath);
        }

        static void SetBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ assets

        static void CreateAssets()
        {
            HandballArt.EnsureFolders();
            var grassTex = HandballArt.GrassTexture();
            var netTex = HandballArt.NetTexture();
            var ballTex = HandballArt.BallTexture();
            var stripeV = HandballArt.StripeTexture("T_StripeV", true);
            var stripeH = HandballArt.StripeTexture("T_StripeH", false);
            HandballArt.RoundedRect();
            var ring = HandballArt.RingTexture();
            var dot = HandballArt.DotTexture();
            var soft = HandballArt.SoftCircle();
            s_Rounded = AssetDatabase.LoadAssetAtPath<Sprite>(HandballArt.Root + "/Textures/T_RoundedRect.png");

            HandballArt.ToonSettings T(Color c, float outline = 0f) => new HandballArt.ToonSettings { color = c, outline = outline };

            s_White = HandballArt.ToonMaterial("M_White", T(Color.white, 0.006f));
            s_Grass = HandballArt.ToonMaterial("M_Grass", new HandballArt.ToonSettings
            {
                texture = grassTex, tiling = new Vector2(30, 30), shade = new Color(0.75f, 0.85f, 0.8f), rim = 0f, ambient = 0.3f, instancing = false
            });
            s_Line = HandballArt.ToonMaterial("M_FieldLine", new HandballArt.ToonSettings
            {
                color = new Color(0.97f, 0.97f, 0.95f), shade = new Color(0.92f, 0.92f, 0.95f), rim = 0f, ambient = 0.1f
            });
            s_PostV = HandballArt.ToonMaterial("M_PostVertical", new HandballArt.ToonSettings
            {
                texture = stripeV, tiling = new Vector2(1, 2.08f / 0.4f), outline = 0f, rim = 0.25f
            });
            s_PostH = HandballArt.ToonMaterial("M_PostCrossbar", new HandballArt.ToonSettings
            {
                texture = stripeH, tiling = new Vector2(3.16f / 0.4f, 1), outline = 0f, rim = 0.25f
            });
            s_Net = HandballArt.ToonMaterial("M_Net", new HandballArt.ToonSettings
            {
                texture = netTex, alphaClip = true, doubleSided = true, shade = new Color(0.85f, 0.85f, 0.9f), rim = 0f, ambient = 0.4f
            });
            s_Ball = HandballArt.ToonMaterial("M_Ball", new HandballArt.ToonSettings
            {
                texture = ballTex, outline = 0.004f, rim = 0.3f, threshold = 0.0f
            });
            s_Wood = HandballArt.ToonMaterial("M_Wood", T(new Color(0.86f, 0.62f, 0.42f), 0.008f));
            s_Pink = HandballArt.ToonMaterial("M_Pink", T(new Color(1f, 0.55f, 0.72f), 0.008f));
            s_Mint = HandballArt.ToonMaterial("M_Mint", T(new Color(0.55f, 0.92f, 0.8f), 0.008f));
            s_Lavender = HandballArt.ToonMaterial("M_Lavender", T(new Color(0.72f, 0.62f, 1f), 0.008f));
            s_Trunk = HandballArt.ToonMaterial("M_Trunk", T(new Color(0.62f, 0.42f, 0.3f), 0.02f));
            s_LeafA = HandballArt.ToonMaterial("M_LeafA", T(new Color(0.42f, 0.78f, 0.38f), 0.03f));
            s_LeafB = HandballArt.ToonMaterial("M_LeafB", T(new Color(0.55f, 0.86f, 0.45f), 0.03f));
            s_LeafC = HandballArt.ToonMaterial("M_LeafC", T(new Color(1f, 0.72f, 0.82f), 0.03f)); // blossom trees
            s_Cloud = HandballArt.ToonMaterial("M_Cloud", new HandballArt.ToonSettings
            {
                color = Color.white, shade = new Color(0.85f, 0.88f, 1f), emission = new Color(0.12f, 0.12f, 0.14f), rim = 0.4f, outline = 0f
            });
            s_Hill = HandballArt.ToonMaterial("M_Hill", new HandballArt.ToonSettings
            {
                color = new Color(0.58f, 0.82f, 0.5f), shade = new Color(0.8f, 0.85f, 0.9f), rim = 0.1f
            });
            s_Stem = HandballArt.ToonMaterial("M_Stem", T(new Color(0.35f, 0.65f, 0.3f)));
            s_String = HandballArt.ToonMaterial("M_String", T(new Color(0.95f, 0.95f, 0.95f)));
            s_StarMat = HandballArt.ToonMaterial("M_Star", new HandballArt.ToonSettings
            {
                color = new Color(1f, 0.88f, 0.25f), emission = new Color(0.5f, 0.4f, 0.05f), outline = 0f, doubleSided = true
            });
            s_Flowers = new[]
            {
                HandballArt.ToonMaterial("M_FlowerPink", T(new Color(1f, 0.5f, 0.7f))),
                HandballArt.ToonMaterial("M_FlowerYellow", T(new Color(1f, 0.88f, 0.3f))),
                HandballArt.ToonMaterial("M_FlowerWhite", T(new Color(1f, 1f, 0.97f))),
                HandballArt.ToonMaterial("M_FlowerBlue", T(new Color(0.55f, 0.7f, 1f))),
            };
            s_Flags = new[]
            {
                HandballArt.ToonMaterial("M_FlagPink", new HandballArt.ToonSettings { color = new Color(1f, 0.45f, 0.65f), doubleSided = true }),
                HandballArt.ToonMaterial("M_FlagYellow", new HandballArt.ToonSettings { color = new Color(1f, 0.85f, 0.3f), doubleSided = true }),
                HandballArt.ToonMaterial("M_FlagBlue", new HandballArt.ToonSettings { color = new Color(0.4f, 0.72f, 1f), doubleSided = true }),
                HandballArt.ToonMaterial("M_FlagMint", new HandballArt.ToonSettings { color = new Color(0.5f, 0.92f, 0.75f), doubleSided = true }),
            };
            s_Balloons = new[]
            {
                HandballArt.ToonMaterial("M_BalloonRed", new HandballArt.ToonSettings { color = new Color(1f, 0.35f, 0.45f), rim = 0.5f, outline = 0.006f }),
                HandballArt.ToonMaterial("M_BalloonYellow", new HandballArt.ToonSettings { color = new Color(1f, 0.85f, 0.3f), rim = 0.5f, outline = 0.006f }),
                HandballArt.ToonMaterial("M_BalloonBlue", new HandballArt.ToonSettings { color = new Color(0.45f, 0.7f, 1f), rim = 0.5f, outline = 0.006f }),
                HandballArt.ToonMaterial("M_BalloonPurple", new HandballArt.ToonSettings { color = new Color(0.75f, 0.55f, 1f), rim = 0.5f, outline = 0.006f }),
            };
            s_Bubble = HandballArt.FxMaterial("M_Bubble", AssetDatabase.LoadAssetAtPath<Texture2D>(HandballArt.Root + "/Textures/T_RoundedRect.png"), Color.white);
            s_Confetti = HandballArt.FxMaterial("M_Confetti", null, Color.white, true);
            s_Trajectory = HandballArt.FxMaterial("M_Trajectory", dot, Color.white);
            s_Trajectory.SetTextureScale("_BaseMap", new Vector2(9f, 1f)); // LineTextureMode.Tile repeats per metre
            s_Reticle = HandballArt.FxMaterial("M_Reticle", ring, Color.white);
            s_Puff = HandballArt.FxMaterial("M_Puff", soft, new Color(1f, 1f, 1f, 0.8f));

            s_BallPhys = HandballArt.PhysicsMat("PM_Ball", 0.72f, 0.5f);
            s_GrassPhys = HandballArt.PhysicsMat("PM_Grass", 0.5f, 0.7f);
            s_PostPhys = HandballArt.PhysicsMat("PM_Post", 0.65f, 0.2f);
            s_NetPhys = HandballArt.PhysicsMat("PM_Net", 0f, 1f, PhysicsMaterialCombine.Minimum);
            s_KeeperPhys = HandballArt.PhysicsMat("PM_Keeper", 0.35f, 0.6f);

            s_Star = HandballArt.SaveMesh(HandballArt.StarMesh(), "SM_Star");
            s_Pennant = HandballArt.SaveMesh(HandballArt.PennantMesh(), "SM_Pennant");
            s_Quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = false, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------ world

        static Light BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(42f, 25f, 0f);
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            var sky = new Material(Shader.Find("Handball/GradientSky"));
            AssetDatabase.CreateAsset(sky, HandballArt.Root + "/Materials/M_Sky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.86f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.78f, 0.85f, 0.78f);
            RenderSettings.ambientGroundColor = new Color(0.5f, 0.58f, 0.45f);
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(0.55f, 0.6f, 0.6f));
            probe.AddDirectionalLight(Vector3.up, new Color(0.35f, 0.4f, 0.5f), 1f);
            probe.AddDirectionalLight(Vector3.down, new Color(0.15f, 0.2f, 0.1f), 1f);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.84f, 0.92f, 1f);
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 130f;
            return sun;
        }

        static GameObject BuildGround()
        {
            var ground = Prim(PrimitiveType.Plane, "Ground", null, Vector3.zero, new Vector3(16f, 1f, 16f), s_Grass, true);
            ground.GetComponent<Collider>().sharedMaterial = s_GrassPhys;
            ground.isStatic = true;
            return ground;
        }

        static void BuildFieldLines()
        {
            var root = Group("Field Lines").transform;
            const float w = 0.05f;
            const float h = 0.004f;
            float goalZ = GoalPosition.z;

            void Strip(string name, List<Vector2> pts, float height = h)
            {
                var mesh = HandballArt.SaveMesh(HandballArt.GroundStrip(pts, w, height), "SM_Line_" + name);
                MeshObject(name, root, mesh, s_Line);
            }

            List<Vector2> DArc(float radius, int segments)
            {
                var pts = new List<Vector2>();
                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.Lerp(180f, 270f, i / (float)segments) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(-1.5f + Mathf.Cos(a) * radius, goalZ + Mathf.Sin(a) * radius));
                }
                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.Lerp(270f, 360f, i / (float)segments) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(1.5f + Mathf.Cos(a) * radius, goalZ + Mathf.Sin(a) * radius));
                }
                return pts;
            }

            Strip("Goal Line", new List<Vector2> { new Vector2(-10f, goalZ), new Vector2(10f, goalZ) });
            Strip("Goal Area 6m", DArc(6f, 28));
            Strip("Penalty 7m", new List<Vector2> { new Vector2(-0.5f, goalZ - 7f), new Vector2(0.5f, goalZ - 7f) }, h + 0.001f);
            Strip("Keeper 4m", new List<Vector2> { new Vector2(-0.075f, goalZ - 4f), new Vector2(0.075f, goalZ - 4f) }, h + 0.001f);

            // Dashed 9 m free-throw line.
            var nine = DArc(9f, 80);
            float acc = 0f;
            bool on = true;
            var dash = new List<Vector2> { nine[0] };
            int index = 0;
            for (int i = 1; i < nine.Count; i++)
            {
                acc += Vector2.Distance(nine[i - 1], nine[i]);
                dash.Add(nine[i]);
                if (acc >= 0.3f)
                {
                    if (on && dash.Count > 1) Strip("FreeThrow 9m " + index++, new List<Vector2>(dash));
                    on = !on;
                    acc = 0f;
                    dash.Clear();
                    dash.Add(nine[i]);
                }
            }
        }

        static Transform BuildGoal()
        {
            var goal = Group("Goal").transform;
            goal.position = GoalPosition;
            const float t = 0.08f;

            var left = Prim(PrimitiveType.Cube, "Post Left", goal, new Vector3(-1.5f - t / 2, 1.04f, t / 2), new Vector3(t, 2.08f, t), s_PostV, true);
            var right = Prim(PrimitiveType.Cube, "Post Right", goal, new Vector3(1.5f + t / 2, 1.04f, t / 2), new Vector3(t, 2.08f, t), s_PostV, true);
            var bar = Prim(PrimitiveType.Cube, "Crossbar", goal, new Vector3(0f, 2f + t / 2, t / 2), new Vector3(3f + 2 * t, t, t), s_PostH, true);
            foreach (var p in new[] { left, right, bar })
            {
                p.AddComponent<GoalPost>();
                p.GetComponent<Collider>().sharedMaterial = s_PostPhys;
            }
            // Back frame on the ground.
            Prim(PrimitiveType.Cube, "Frame Back", goal, new Vector3(0f, 0.02f, 1.0f), new Vector3(3.1f, 0.04f, 0.04f), s_White);
            Prim(PrimitiveType.Cube, "Frame Left", goal, new Vector3(-1.53f, 0.02f, 0.5f), new Vector3(0.04f, 0.04f, 1f), s_White);
            Prim(PrimitiveType.Cube, "Frame Right", goal, new Vector3(1.53f, 0.02f, 0.5f), new Vector3(0.04f, 0.04f, 1f), s_White);

            var netGo = Group("Net", goal);
            netGo.AddComponent<MeshFilter>();
            var netRenderer = netGo.AddComponent<MeshRenderer>();
            netRenderer.sharedMaterial = s_Net;
            netRenderer.shadowCastingMode = ShadowCastingMode.On;
            var net = netGo.AddComponent<GoalNet>();
            net.netMaterial = s_NetPhys;

            var trigger = Group("Goal Trigger", goal);
            trigger.transform.localPosition = new Vector3(0f, 1f, BallRadius + 0.4f);
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 2f, 0.8f);
            trigger.AddComponent<GoalTrigger>();
            return goal;
        }

        static XROrigin BuildRig(out Camera camera)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefab);
            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rigGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -0.35f), Quaternion.identity);
            var origin = rigGo.GetComponent<XROrigin>();
            origin.CameraYOffset = 1.55f;
            camera = origin.Camera;
            camera.farClipPlane = 400f;
            camera.nearClipPlane = 0.02f;
            var camData = camera.GetComponent<UniversalAdditionalCameraData>() ?? camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            // Edits made from code to a prefab instance must be recorded or they are lost when the scene is saved.
            // Without a tracking subsystem (simulator) the rig keeps the offset object's authored height, so move it too.
            var offsetObject = origin.CameraFloorOffsetObject.transform;
            offsetObject.localPosition = new Vector3(offsetObject.localPosition.x, origin.CameraYOffset, offsetObject.localPosition.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(offsetObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(origin);
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(camData);

            foreach (var name in new[] { "Left Controller", "Right Controller" })
            {
                var controller = rigGo.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
                var assist = controller.gameObject.AddComponent<HandballThrowAssist>();
                assist.aimSource = camera.transform;
            }
            HandballHands.Attach(rigGo);

            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<XRUIInputModule>();
            return origin;
        }

        // ------------------------------------------------------------------ audio

        static AudioClip Clip(string name)
        {
            var guid = AssetDatabase.FindAssets(name + " t:AudioClip", new[] { "Assets/Handball/Audio" })
                .FirstOrDefault(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
            if (guid == null)
            {
                Debug.LogWarning("[HandballSceneBuilder] Missing audio clip " + name);
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
        }

        static SfxPlayer BuildAudio()
        {
            var go = new GameObject("Audio");
            var sfx = go.AddComponent<SfxPlayer>();
            void Add(SfxPlayer.Cue cue, float volume, float spatial, Vector2 pitch, params string[] clips)
            {
                sfx.entries.Add(new SfxPlayer.Entry { cue = cue, clips = clips.Select(Clip).Where(c => c).ToArray(), volume = volume, spatialBlend = spatial, pitch = pitch });
            }
            var jitter = new Vector2(0.92f, 1.08f);
            var none = new Vector2(1f, 1f);
            Add(SfxPlayer.Cue.Grab, 0.7f, 0.6f, jitter, "pluck_001", "pluck_002");
            Add(SfxPlayer.Cue.Whoosh, 0.8f, 0.7f, jitter, "whoosh");
            Add(SfxPlayer.Cue.Bounce, 0.9f, 0.9f, jitter, "impactSoft_medium_000", "impactSoft_medium_001", "impactSoft_medium_002", "impactSoft_medium_003");
            Add(SfxPlayer.Cue.Post, 1f, 0.9f, jitter, "impactMetal_light_000", "impactMetal_light_001", "impactMetal_light_002", "impactMetal_medium_000");
            Add(SfxPlayer.Cue.NetHit, 1f, 0.9f, jitter, "impactSoft_heavy_000", "impactSoft_heavy_001", "impactSoft_heavy_002");
            Add(SfxPlayer.Cue.KeeperHit, 1f, 0.9f, jitter, "impactPunch_medium_000", "impactPunch_medium_001", "impactPunch_medium_002");
            Add(SfxPlayer.Cue.KeeperLand, 0.9f, 0.9f, jitter, "impactSoft_heavy_000", "impactSoft_heavy_001", "impactSoft_heavy_002");
            Add(SfxPlayer.Cue.KeeperJump, 0.8f, 0.9f, jitter, "footstep_grass_000", "footstep_grass_001", "footstep_grass_002", "footstep_grass_003");
            Add(SfxPlayer.Cue.GoalJingle, 0.8f, 0f, none, "jingles_STEEL02");
            Add(SfxPlayer.Cue.Cheer, 0.6f, 0.3f, new Vector2(0.95f, 1.05f), "crowd_cheer");
            Add(SfxPlayer.Cue.PartyPop, 0.9f, 0.6f, jitter, "party_pop");
            Add(SfxPlayer.Cue.SaveJingle, 0.7f, 0f, none, "jingles_PIZZI14");
            Add(SfxPlayer.Cue.Miss, 0.5f, 0.3f, jitter, "bong_001");
            Add(SfxPlayer.Cue.Respawn, 0.6f, 0.7f, jitter, "drop_001", "maximize_003");
            Add(SfxPlayer.Cue.Boing, 0.8f, 0.8f, jitter, "drop_002");
            Add(SfxPlayer.Cue.Whistle, 0.6f, 0.2f, none, "whistle");
            Add(SfxPlayer.Cue.Celebrate, 0.7f, 0.5f, none, "jingles_STEEL10", "confirmation_001", "confirmation_002");

            var ambience = new GameObject("Ambience (birds)").AddComponent<AudioSource>();
            ambience.transform.SetParent(go.transform, false);
            ambience.clip = Clip("birds_loop");
            ambience.loop = true;
            ambience.playOnAwake = true;
            ambience.volume = 0.22f;
            ambience.spatialBlend = 0f;
            return sfx;
        }

        // ------------------------------------------------------------------ UI

        static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

        static Material OutlinedFontMaterial()
        {
            var path = HandballArt.Root + "/Materials/M_FontOutlined.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.CopyPropertiesFromMaterial(Font.material);
            mat.SetFloat("_FaceDilate", 0.15f);
            mat.SetFloat("_OutlineWidth", 0.22f);
            mat.SetColor("_OutlineColor", new Color(0.25f, 0.12f, 0.3f));
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_UnderlayOffsetX", 0.6f);
            mat.SetFloat("_UnderlayOffsetY", -0.6f);
            mat.SetFloat("_UnderlaySoftness", 0.2f);
            mat.SetColor("_UnderlayColor", new Color(0.2f, 0.05f, 0.25f, 0.45f));
            mat.EnableKeyword("UNDERLAY_ON");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static TextMeshProUGUI UiText(Transform parent, string name, string text, float size, Vector2 pos, Vector2 box, Color color, Material fontMat)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = Font;
            tmp.fontSharedMaterial = fontMat;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        static Image UiImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = s_Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static ScoreboardUI BuildScoreboard(Transform goal)
        {
            var root = Group("Scoreboard", goal).transform;
            root.localPosition = new Vector3(0f, 2.75f, 0.04f);

            // Little stands holding the board on the crossbar.
            Prim(PrimitiveType.Cylinder, "Stand L", root, new Vector3(-0.7f, -0.5f, 0f), new Vector3(0.04f, 0.2f, 0.04f), s_White);
            Prim(PrimitiveType.Cylinder, "Stand R", root, new Vector3(0.7f, -0.5f, 0f), new Vector3(0.04f, 0.2f, 0.04f), s_White);

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(800, 300);
            rt.localScale = Vector3.one * 0.0025f;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

            var fontMat = OutlinedFontMaterial();
            UiImage(rt, "Border", Vector2.zero, new Vector2(800, 300), Color.white);
            UiImage(rt, "Panel", Vector2.zero, new Vector2(776, 276), new Color(1f, 0.5f, 0.68f));
            UiImage(rt, "Saves Panel", new Vector2(215, 0), new Vector2(320, 236), new Color(0.42f, 0.66f, 1f));

            var goalsBox = new GameObject("Goals", typeof(RectTransform)).GetComponent<RectTransform>();
            goalsBox.SetParent(rt, false);
            goalsBox.anchoredPosition = new Vector2(-170, 0);
            goalsBox.sizeDelta = new Vector2(400, 260);
            UiText(goalsBox, "Label", "GOALS", 56, new Vector2(0, 80), new Vector2(400, 70), Color.white, fontMat);
            var goalsValue = UiText(goalsBox, "Value", "0", 160, new Vector2(0, -30), new Vector2(400, 170), Color.white, fontMat);

            var savesBox = new GameObject("Saves", typeof(RectTransform)).GetComponent<RectTransform>();
            savesBox.SetParent(rt, false);
            savesBox.anchoredPosition = new Vector2(215, 0);
            savesBox.sizeDelta = new Vector2(300, 220);
            UiText(savesBox, "Label", "UNITY-CHAN\nSAVES", 34, new Vector2(0, 62), new Vector2(300, 90), Color.white, fontMat);
            var savesValue = UiText(savesBox, "Value", "0", 100, new Vector2(0, -40), new Vector2(300, 120), Color.white, fontMat);

            var popup = UiText(rt, "Popup", "GOAL!", 150, new Vector2(0, 260), new Vector2(1200, 200), Color.yellow, fontMat);

            var board = root.gameObject.AddComponent<ScoreboardUI>();
            board.goalsValue = goalsValue;
            board.savesValue = savesValue;
            board.popup = popup;
            board.goalsPunchTarget = goalsValue.rectTransform;
            board.savesPunchTarget = savesValue.rectTransform;
            return board;
        }

        static void BuildHintSign()
        {
            var root = Group("How To Play Sign").transform;
            root.position = new Vector3(-1.35f, 0f, 2.1f);
            root.rotation = Quaternion.Euler(0f, -28f, 0f);
            Prim(PrimitiveType.Cylinder, "Post", root, new Vector3(0f, 0.56f, 0.14f), new Vector3(0.06f, 0.56f, 0.06f), s_Wood);

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            canvasGo.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            canvasGo.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(720, 480);
            rt.localScale = Vector3.one * 0.0012f;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var fontMat = OutlinedFontMaterial();
            UiImage(rt, "Border", Vector2.zero, new Vector2(720, 480), Color.white);
            UiImage(rt, "Panel", Vector2.zero, new Vector2(696, 456), new Color(0.47f, 0.8f, 0.72f));
            UiText(rt, "Title", "HOW TO PLAY", 54, new Vector2(0, 182), new Vector2(680, 70), Color.white, fontMat);
            var body = UiText(rt, "Body",
                "Grab a ball with GRIP (either hand)\nand throw it past Unity-chan!\n\n<size=23>Simulator: hold Right Mouse to turn, point at a ball,\nhold G to grab, look where you want to shoot,\nthen release G to throw.\nLeft hand: hold Shift too (let go of G before Shift).</size>",
                31, new Vector2(0, -5), new Vector2(680, 300), Color.white, fontMat);
            body.textWrappingMode = TextWrappingModes.Normal;
            UiText(rt, "Credits", "Unity-chan © Unity Technologies Japan/UCL  •  Sounds: Kenney.nl (CC0)", 18, new Vector2(0, -205), new Vector2(680, 28), new Color(0.1f, 0.32f, 0.3f), OutlinedFontMaterial());
        }

        // ------------------------------------------------------------------ effects

        static ConfettiBurst[] BuildConfetti(Transform goal, Transform ground)
        {
            var list = new List<ConfettiBurst>();
            var spots = new[]
            {
                (new Vector3(-1.55f, 2.1f, 0f), Quaternion.Euler(-60f, 0f, 25f)),
                (new Vector3(1.55f, 2.1f, 0f), Quaternion.Euler(-60f, 0f, -25f)),
                (new Vector3(0f, 0.4f, 0.6f), Quaternion.Euler(-80f, 0f, 0f)),
            };
            int i = 0;
            foreach (var (pos, rot) in spots)
            {
                var go = new GameObject("Confetti " + i++);
                go.transform.SetParent(goal, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = s_Confetti;
                renderer.mesh = s_Quad;
                var burst = go.AddComponent<ConfettiBurst>();
                burst.groundPlane = ground;
                burst.count = i == 3 ? 50 : 60;
                list.Add(burst);
            }
            return list.ToArray();
        }

        static ParticleSystem BuildPuff(Transform parent)
        {
            var go = new GameObject("Respawn Puff");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 1f, 0.9f), new Color(1f, 0.8f, 0.9f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = s_Puff;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        // ------------------------------------------------------------------ keeper

        static void BuildKeeper(Camera camera)
        {
            var root = new GameObject("UnityChan Keeper");
            root.transform.SetPositionAndRotation(GoalPosition + new Vector3(0f, 0f, -0.7f), Quaternion.LookRotation(Vector3.back));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HandballAssetSetup.UnityChanModelPath));
            model.name = "unitychan";
            model.transform.SetParent(root.transform, false);
            var animator = model.GetComponent<Animator>();
            animator.runtimeAnimatorController = HandballArt.KeeperController();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ApplyUnityChanMaterials(model);

            var ragdoll = root.AddComponent<KeeperRagdoll>();
            ragdoll.animator = animator;
            ragdoll.material = s_KeeperPhys;

            var keeper = root.AddComponent<GoalkeeperController>();
            model.AddComponent<KeeperIKRelay>().keeper = keeper;
            keeper.animator = animator;
            keeper.ragdoll = ragdoll;
            keeper.lookAtFallback = camera.transform;

            // Speech bubble
            var emoteGo = new GameObject("Keeper Emote");
            emoteGo.AddComponent<Billboard>();
            var emote = emoteGo.AddComponent<KeeperEmote>();
            var bubble = Group("Bubble", emoteGo.transform).transform;
            var bg = MeshObject("Background", bubble, s_Quad, s_Bubble);
            bg.transform.localScale = new Vector3(0.62f, 0.2f, 1f);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(bubble, false);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.font = Font;
            label.fontSize = 1.1f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.6f, 0.2f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = "Hi!";
            emote.label = label;
            emote.bubble = bubble;
            keeper.emote = emote;

            // Dizzy stars
            var dizzyGo = new GameObject("Dizzy Stars");
            var dizzy = dizzyGo.AddComponent<DizzyStars>();
            dizzy.stars = Enumerable.Range(0, 3).Select(i => MeshObject("Star " + i, dizzyGo.transform, s_Star, s_StarMat).transform).ToArray();
            foreach (var star in dizzy.stars)
            {
                star.localScale = Vector3.one * 0.09f;
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                star.gameObject.SetActive(false);
            }
            dizzyGo.transform.position = GoalPosition + new Vector3(0f, 1.6f, -0.7f);
            emoteGo.transform.position = GoalPosition + new Vector3(0f, 1.9f, -0.7f);
            bubble.gameObject.SetActive(false);
            keeper.dizzy = dizzy;
            HandballHands.AttachReactions(keeper);
        }

        static void ApplyUnityChanMaterials(GameObject model)
        {
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/UnityChan/Textures/{n}.tga");
            var outline = new Color(0.3f, 0.18f, 0.22f);
            var cloth = new HandballArt.ToonSettings { texture = Tex("body_01"), shade = new Color(0.74f, 0.7f, 0.88f), outline = 0.0016f, outlineColor = outline, rim = 0.18f, threshold = 0.0f };
            var skin = new HandballArt.ToonSettings { texture = Tex("skin_01"), shade = new Color(1f, 0.82f, 0.8f), outline = 0.0016f, outlineColor = outline, rim = 0.12f, threshold = -0.1f };
            var face = new HandballArt.ToonSettings { texture = Tex("face_00"), shade = new Color(1f, 0.86f, 0.84f), outline = 0f, rim = 0.05f, threshold = -0.45f };
            var hair = new HandballArt.ToonSettings { texture = Tex("hair_01"), shade = new Color(0.76f, 0.72f, 0.9f), outline = 0.0016f, outlineColor = outline, rim = 0.2f, threshold = 0.0f };
            var eyeL = new HandballArt.ToonSettings { texture = Tex("eye_iris_L_00"), outline = 0f, rim = 0f, threshold = -1f, alphaClip = true };
            var eyeR = new HandballArt.ToonSettings { texture = Tex("eye_iris_R_00"), outline = 0f, rim = 0f, threshold = -1f, alphaClip = true };
            var eyeline = new HandballArt.ToonSettings { texture = Tex("eyeline_00"), outline = 0f, rim = 0f, threshold = -1f, alphaClip = true, doubleSided = true };
            var cheek = new HandballArt.ToonSettings { texture = Tex("cheek_00"), outline = 0f, rim = 0f, threshold = -1f, transparent = true };

            var map = new Dictionary<string, Material>
            {
                { "body", HandballArt.ToonMaterial("M_UC_Body", cloth) },
                { "skin1", HandballArt.ToonMaterial("M_UC_Skin", skin) },
                { "face", HandballArt.ToonMaterial("M_UC_Face", face) },
                { "hair", HandballArt.ToonMaterial("M_UC_Hair", hair) },
                { "eyebase", HandballArt.ToonMaterial("M_UC_EyeBase", face) },
                { "eye_L1", HandballArt.ToonMaterial("M_UC_EyeL", eyeL) },
                { "eye_R1", HandballArt.ToonMaterial("M_UC_EyeR", eyeR) },
                { "eyeline", HandballArt.ToonMaterial("M_UC_Eyeline", eyeline) },
                { "mat_cheek", HandballArt.ToonMaterial("M_UC_Cheek", cheek) },
            };
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] && map.TryGetValue(mats[i].name, out var replacement)) mats[i] = replacement;
                }
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
        }

        // ------------------------------------------------------------------ balls

        static void BuildRacksAndBalls(XROrigin rig)
        {
            var center = rig.transform.position;
            int ballIndex = 0;
            foreach (var side in new[] { -1, 1 })
            {
                var rackGo = Group(side < 0 ? "Ball Rack Left" : "Ball Rack Right");
                var rack = rackGo.AddComponent<BallRack>();
                rackGo.transform.position = center;
                var angles = new[] { 62f, 78f, 94f };
                rack.slots = new Transform[angles.Length];
                for (int i = 0; i < angles.Length; i++)
                {
                    float a = angles[i] * side * Mathf.Deg2Rad;
                    var pos = center + new Vector3(Mathf.Sin(a) * RackRadius, RackHeight, Mathf.Cos(a) * RackRadius);
                    var slot = Group("Slot " + i, rackGo.transform).transform;
                    slot.position = pos;
                    rack.slots[i] = slot;

                    // Stand: a candy-striped pole with a glowing dish under the floating ball.
                    var standPos = new Vector3(pos.x, 0f, pos.z);
                    var pole = Prim(PrimitiveType.Cylinder, "Stand " + i, rackGo.transform, standPos + Vector3.up * 0.6f, new Vector3(0.05f, 0.6f, 0.05f), side < 0 ? s_Mint : s_Pink);
                    pole.transform.position = standPos + Vector3.up * 0.6f;
                    var dish = Prim(PrimitiveType.Cylinder, "Dish " + i, rackGo.transform, Vector3.zero, new Vector3(0.2f, 0.012f, 0.2f), s_White);
                    dish.transform.position = pos + Vector3.down * 0.3f;
                    Prim(PrimitiveType.Sphere, "Base " + i, rackGo.transform, Vector3.zero, new Vector3(0.18f, 0.06f, 0.18f), side < 0 ? s_Mint : s_Pink).transform.position = standPos + Vector3.up * 0.03f;

                    CreateBall(rack, i, ballIndex++);
                }
                rack.respawnPuff = BuildPuff(rackGo.transform);
            }
        }

        static void CreateBall(BallRack rack, int slot, int index)
        {
            var ball = Prim(PrimitiveType.Sphere, "Handball " + index, null, rack.SlotPosition(slot), Vector3.one * (BallRadius * 2f), s_Ball, true);
            // Default layer: the starter Near-Far interactors only cast against Default/UI.
            ball.layer = 0;
            ball.GetComponent<SphereCollider>().sharedMaterial = s_BallPhys;
            var rb = ball.AddComponent<Rigidbody>();
            rb.mass = 0.45f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.useGravity = false;
            rb.angularDamping = 0.5f;
            rb.maxAngularVelocity = 40f;

            var grab = ball.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = true;
            grab.throwSmoothingDuration = 0.12f;
            grab.throwVelocityScale = 1f;
            grab.throwAngularVelocityScale = 0.6f;
            grab.forceGravityOnDetach = true;
            grab.useDynamicAttach = false;
            grab.attachEaseInTime = 0.08f;
            grab.farAttachMode = InteractableFarAttachMode.Near;
            // Hold the ball by its upper surface so it hangs under the hand instead of filling the view.
            var gripPoint = new GameObject("Grip Point").transform;
            gripPoint.SetParent(ball.transform, false);
            gripPoint.localPosition = new Vector3(0f, 0.6f, -0.2f);
            grab.attachTransform = gripPoint;

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(ball.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = 0.28f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 0f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.6f, 0.8f), 0f), new GradientColorKey(new Color(0.6f, 0.8f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.sharedMaterial = s_Puff;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;

            var hb = ball.AddComponent<HandballBall>();
            hb.rack = rack;
            hb.slot = slot;
            hb.trail = trail;
        }

        static void BuildTrajectoryPreview(XROrigin rig)
        {
            var go = new GameObject("Throw Trajectory Preview");
            var preview = go.AddComponent<ThrowTrajectoryPreview>();
            preview.assist = rig.GetComponentsInChildren<HandballThrowAssist>(true).Last();
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = s_Trajectory;
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = 0.07f;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.alignment = LineAlignment.View;
            preview.line = line;

            var reticle = MeshObject("Reticle", go.transform, s_Quad, s_Reticle);
            reticle.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            preview.reticle = reticle.transform;
            preview.reticleRenderer = reticle.GetComponent<MeshRenderer>();
        }

        // ------------------------------------------------------------------ scenery

        static bool InPlayArea(Vector3 p)
        {
            // Keep the shooting lane, the goal surroundings and the player's spot clear.
            return (Mathf.Abs(p.x) < 9.5f && p.z > -3f && p.z < 12f) || p.magnitude < 3f;
        }

        static Vector3 RandomRing(float min, float max)
        {
            float a = Random.value * Mathf.PI * 2f;
            float r = Mathf.Lerp(min, max, Mathf.Sqrt(Random.value));
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r + 4f);
        }

        static void BuildScenery(Transform goal)
        {
            var root = Group("Scenery").transform;

            // Trees: round "lollipop" trees, pines and blossom trees.
            var trees = Group("Trees", root).transform;
            for (int i = 0; i < 46; i++)
            {
                var p = RandomRing(17f, 42f);
                if (InPlayArea(p)) continue;
                var t = Group("Tree " + i, trees).transform;
                t.position = p;
                t.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
                float s = Random.Range(0.8f, 1.5f);
                t.localScale = Vector3.one * s;
                int kind = Random.Range(0, 3);
                Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, 0.9f, 0f), new Vector3(0.35f, 0.9f, 0.35f), s_Trunk);
                if (kind == 1)
                {
                    for (int k = 0; k < 3; k++)
                        Prim(PrimitiveType.Sphere, "Cone", t, new Vector3(0f, 2.0f + k * 0.9f, 0f), new Vector3(2.4f - k * 0.6f, 1.3f, 2.4f - k * 0.6f), s_LeafA);
                }
                else
                {
                    var leaf = kind == 2 ? s_LeafC : s_LeafB;
                    Prim(PrimitiveType.Sphere, "Crown", t, new Vector3(0f, 2.6f, 0f), Vector3.one * 2.6f, leaf);
                    Prim(PrimitiveType.Sphere, "Crown 2", t, new Vector3(0.7f, 2.2f, 0.3f), Vector3.one * 1.6f, leaf);
                    Prim(PrimitiveType.Sphere, "Crown 3", t, new Vector3(-0.6f, 2.3f, -0.4f), Vector3.one * 1.7f, leaf);
                }
                var sway = t.gameObject.AddComponent<Ambient>();
                sway.bobAmplitude = 0f;
                sway.swayDegrees = 1.2f;
                sway.swaySpeed = 0.8f;
                sway.swayAxis = Vector3.forward;
            }

            // Bushes
            var bushes = Group("Bushes", root).transform;
            for (int i = 0; i < 30; i++)
            {
                var p = RandomRing(11f, 26f);
                if (InPlayArea(p)) continue;
                var b = Group("Bush " + i, bushes).transform;
                b.position = p;
                for (int k = 0; k < 3; k++)
                    Prim(PrimitiveType.Sphere, "Puff", b, new Vector3(Random.Range(-0.5f, 0.5f), 0.3f, Random.Range(-0.5f, 0.5f)), Vector3.one * Random.Range(0.7f, 1.1f), k == 0 ? s_LeafA : s_LeafB);
            }

            // Flowers
            var flowers = Group("Flowers", root).transform;
            for (int i = 0; i < 220; i++)
            {
                var p = RandomRing(3.5f, 22f);
                if ((Mathf.Abs(p.x) < 5f && p.z > -2f && p.z < 10f) || p.magnitude < 2.5f) continue;
                var f = Group("Flower", flowers).transform;
                f.position = p;
                float s = Random.Range(0.8f, 1.3f);
                f.localScale = Vector3.one * s;
                Prim(PrimitiveType.Cylinder, "Stem", f, new Vector3(0f, 0.08f, 0f), new Vector3(0.012f, 0.08f, 0.012f), s_Stem);
                Prim(PrimitiveType.Sphere, "Head", f, new Vector3(0f, 0.17f, 0f), new Vector3(0.12f, 0.06f, 0.12f), s_Flowers[Random.Range(0, s_Flowers.Length)]);
            }

            // Rolling hills on the horizon
            var hills = Group("Hills", root).transform;
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f + Random.value * 0.3f;
                var p = new Vector3(Mathf.Cos(a) * 85f, -6f, Mathf.Sin(a) * 85f);
                Prim(PrimitiveType.Sphere, "Hill " + i, hills, p, new Vector3(Random.Range(45f, 70f), Random.Range(18f, 28f), Random.Range(35f, 50f)), s_Hill);
            }

            // Fluffy clouds drifting slowly
            var clouds = Group("Clouds", root).transform;
            for (int i = 0; i < 12; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var c = Group("Cloud " + i, clouds).transform;
                c.position = new Vector3(Mathf.Cos(a) * Random.Range(35f, 70f), Random.Range(16f, 28f), Mathf.Sin(a) * Random.Range(35f, 70f) + 10f);
                float s = Random.Range(2.5f, 4.5f);
                for (int k = 0; k < 5; k++)
                    Prim(PrimitiveType.Sphere, "Puff", c, new Vector3((k - 2) * 0.9f * s, Random.Range(-0.2f, 0.5f) * s, Random.Range(-0.4f, 0.4f) * s), Vector3.one * Random.Range(1.2f, 2f) * s, s_Cloud)
                        .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var drift = c.gameObject.AddComponent<Ambient>();
                drift.bobAmplitude = 0.4f;
                drift.bobSpeed = 0.2f;
                drift.drift = new Vector3(0.25f, 0f, 0.05f);
                drift.driftRange = 25f;
            }

            BuildBunting(root);
            BuildBalloons(root, goal);
        }

        static void BuildBunting(Transform root)
        {
            var bunting = Group("Bunting", root).transform;
            var a = new Vector3(-6f, 3.6f, 9.5f);
            var b = new Vector3(6f, 3.6f, 9.5f);
            Prim(PrimitiveType.Cylinder, "Pole L", bunting, new Vector3(a.x, 1.8f, a.z), new Vector3(0.1f, 1.8f, 0.1f), s_White);
            Prim(PrimitiveType.Cylinder, "Pole R", bunting, new Vector3(b.x, 1.8f, b.z), new Vector3(0.1f, 1.8f, 0.1f), s_White);
            Prim(PrimitiveType.Sphere, "Knob L", bunting, a + Vector3.up * 0.05f, Vector3.one * 0.2f, s_Pink);
            Prim(PrimitiveType.Sphere, "Knob R", bunting, b + Vector3.up * 0.05f, Vector3.one * 0.2f, s_Pink);
            const int flags = 22;
            Vector3 Rope(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.7f);
            for (int i = 0; i < flags; i++)
            {
                float t = (i + 0.5f) / flags;
                var flag = MeshObject("Flag " + i, bunting, s_Pennant, s_Flags[i % s_Flags.Length]);
                flag.transform.position = Rope(t);
                var dir = (Rope(t + 0.01f) - Rope(t - 0.01f)).normalized;
                // Pennants face the player and tilt with the sagging rope.
                flag.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                flag.transform.localScale = new Vector3(0.38f, 0.42f, 1f);
                var flutter = flag.AddComponent<Ambient>();
                flutter.bobAmplitude = 0f;
                flutter.swayAxis = Vector3.right;
                flutter.swayDegrees = 14f;
                flutter.swaySpeed = 2.4f;
            }
            for (int i = 0; i < 30; i++)
            {
                float t0 = i / 30f, t1 = (i + 1) / 30f;
                var p0 = Rope(t0);
                var p1 = Rope(t1);
                var seg = Prim(PrimitiveType.Cylinder, "Rope", bunting, Vector3.zero, new Vector3(0.015f, Vector3.Distance(p0, p1) * 0.5f, 0.015f), s_String);
                seg.transform.position = (p0 + p1) * 0.5f;
                seg.transform.up = (p1 - p0).normalized;
            }
        }

        static void BuildBalloons(Transform root, Transform goal)
        {
            var group = Group("Balloons", root).transform;
            var anchors = new[]
            {
                goal.TransformPoint(new Vector3(-1.55f, 2.1f, 0.05f)), goal.TransformPoint(new Vector3(-1.55f, 2.1f, 0.05f)),
                goal.TransformPoint(new Vector3(1.55f, 2.1f, 0.05f)), goal.TransformPoint(new Vector3(1.55f, 2.1f, 0.05f)),
                new Vector3(-6f, 3.6f, 9.5f), new Vector3(6f, 3.6f, 9.5f),
            };
            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                var offset = new Vector3((i % 2 == 0 ? -1 : 1) * Random.Range(0.15f, 0.35f), Random.Range(0.9f, 1.3f), Random.Range(-0.1f, 0.15f));
                var b = Group("Balloon " + i, group).transform;
                b.position = anchor + offset;
                var bob = b.gameObject.AddComponent<Ambient>();
                bob.bobAmplitude = 0.06f;
                bob.bobSpeed = Random.Range(1f, 1.6f);
                bob.swayDegrees = 6f;
                bob.swaySpeed = 0.9f;
                Prim(PrimitiveType.Sphere, "Body", b, Vector3.zero, new Vector3(0.34f, 0.42f, 0.34f), s_Balloons[i % s_Balloons.Length]);
                Prim(PrimitiveType.Sphere, "Knot", b, new Vector3(0f, -0.22f, 0f), Vector3.one * 0.05f, s_Balloons[i % s_Balloons.Length]);
                var str = Prim(PrimitiveType.Cylinder, "String", b, Vector3.zero, new Vector3(0.008f, offset.magnitude * 0.5f, 0.008f), s_String);
                str.transform.position = (anchor + b.position + Vector3.down * 0.22f) * 0.5f;
                str.transform.up = (b.position - anchor).normalized;
            }
        }

        static void BuildPostProcessing()
        {
            var go = new GameObject("Global Volume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = HandballArt.PostProfile();
        }
    }
}
