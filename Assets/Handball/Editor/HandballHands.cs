using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Handball.EditorTools
{
    /// <summary>
    /// The hands modelled in Blender (Blender/make_hand.py exports Assets/Handball/Hands/Hand.fbx): import settings,
    /// materials, the animator that plays the Grab, Trigger and hand-sign animations, one hand on each controller
    /// of the XR rig, and Unity-chan's reactions to the signs. The model is a left hand; the right hand is the same
    /// model scaled by -1 on X.
    /// </summary>
    public static class HandballHands
    {
        public const string ModelPath = "Assets/Handball/Hands/Hand.fbx";
        public const string TexturePath = "Assets/Handball/Hands/HandSkin.png";
        const string ControllerPath = HandballArt.Root + "/AC_Hand.controller";
        const string IndexMaskPath = HandballArt.Root + "/AM_HandIndexFinger.mask";
        const string InputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/XRI Default Input Actions.inputactions";

        // The hand is held palm-down over the controller, the way a ball hangs under it when grabbed (see the ball's
        // Grip Point). Where the middle of the palm sits in controller space, and how far that is from the wrist.
        static readonly Vector3 s_PalmOnController = new Vector3(0f, -0.012f, 0.035f);
        const float PalmFromWrist = 0.062f;
        const float PalmHalfThickness = 0.014f;

        [MenuItem("Handball/Setup/Attach Hands To Scene")]
        public static void AttachToScene()
        {
            var scene = EditorSceneManager.OpenScene(HandballSceneBuilder.ScenePath);
            var rig = Object.FindFirstObjectByType<XROrigin>();
            if (!rig)
            {
                Debug.LogError("[HandballHands] No XR Origin in " + HandballSceneBuilder.ScenePath);
                return;
            }
            Attach(rig.gameObject);
            var keeper = Object.FindFirstObjectByType<GoalkeeperController>();
            if (keeper) AttachReactions(keeper);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds (or replaces) the hand on both controllers of an XR Origin instance.</summary>
        public static void Attach(GameObject rig)
        {
            ConfigureImport();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var skin = HandballArt.ToonMaterial("M_HandSkin", new HandballArt.ToonSettings
            {
                texture = texture, shade = new Color(0.84f, 0.70f, 0.74f), softness = 0.14f, rim = 0.12f, ambient = 0.4f, instancing = false
            });
            var nail = HandballArt.ToonMaterial("M_HandNail", new HandballArt.ToonSettings
            {
                texture = texture, shade = new Color(0.86f, 0.74f, 0.80f), softness = 0.08f, rim = 0.4f, ambient = 0.4f, instancing = false
            });
            var controller = BuildController(prefab);

            foreach (var side in new[] { "Left", "Right" })
            {
                var parent = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == side + " Controller");
                var old = parent.Find(side + " Hand");
                if (old) Object.DestroyImmediate(old.gameObject);

                var hand = new GameObject(side + " Hand").transform;
                hand.SetParent(parent, false);
                hand.localScale = new Vector3(side == "Left" ? 1f : -1f, 1f, 1f);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hand);
                model.name = "Hand Model";
                PlaceOnController(model.transform);
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = new[] { skin, nail };
                    renderer.updateWhenOffscreen = true;
                }
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var driver = model.AddComponent<HandAnimator>();
                driver.gripValue = new InputActionProperty(Action(side, "Select Value"));
                driver.triggerValue = new InputActionProperty(Action(side, "Activate Value"));
                var gestures = model.AddComponent<HandGestures>();
                gestures.leftHand = side == "Left";
                gestures.controller = parent;
                gestures.thumbBase = model.GetComponentsInChildren<Transform>(true).First(t => t.name == "Thumb2");
                gestures.thumbTip = model.GetComponentsInChildren<Transform>(true).First(t => t.name == "Thumb3");

                // The hand replaces the controller model.
                foreach (var visual in parent.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("Controller Visual")))
                {
                    visual.gameObject.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(visual.gameObject);
                }
            }
            Debug.Log("[HandballHands] Hands attached to both controllers.");
        }

        /// <summary>Gives Unity-chan a face that can change expression and makes her answer the player's hand signs.</summary>
        public static void AttachReactions(GoalkeeperController keeper)
        {
            var face = keeper.GetComponent<KeeperFace>();
            if (!face) face = keeper.gameObject.AddComponent<KeeperFace>();
            var reactions = keeper.GetComponent<KeeperGestureReactions>();
            if (!reactions) reactions = keeper.gameObject.AddComponent<KeeperGestureReactions>();
            reactions.keeper = keeper;
            reactions.face = face;
        }

        public static void ConfigureImport()
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            model.animationType = ModelImporterAnimationType.Generic;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importNormals = ModelImporterNormals.Import;
            model.bakeAxisConversion = true;
            model.importBlendShapes = false;
            model.importCameras = false;
            model.importLights = false;
            model.importAnimation = true;
            // Blender names its takes "HandRig|Grab"; keep just the action name. Only the wave repeats.
            model.clipAnimations = model.defaultClipAnimations.Select(clip =>
            {
                clip.name = clip.takeName.Split('|').Last();
                clip.loopTime = clip.name == nameof(HandGesture.Wave);
                return clip;
            }).ToArray();
            model.SaveAndReimport();

            var texture = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (!texture.sRGBTexture || !texture.mipmapEnabled || texture.anisoLevel != 4 || texture.maxTextureSize != 2048)
            {
                texture.sRGBTexture = true;
                texture.mipmapEnabled = true;
                texture.anisoLevel = 4;
                texture.maxTextureSize = 2048;
                texture.SaveAndReimport();
            }
        }

        /// <summary>
        /// Two layers each scrub one animation with a float parameter (the state's Motion Time): Grab follows the
        /// grip, Trigger follows the trigger and is masked to the index finger so the two can overlap. A third
        /// layer holds one state per hand sign; <see cref="HandGestures"/> picks the state and fades the layer in.
        /// </summary>
        static AnimatorController BuildController(GameObject prefab)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Clip(string name)
            {
                var clip = clips.FirstOrDefault(c => c.name == name);
                if (!clip) Debug.LogError($"[HandballHands] {ModelPath} has no '{name}' animation (found: {string.Join(", ", clips.Select(c => c.name))})");
                return clip;
            }

            AssetDatabase.DeleteAsset(IndexMaskPath);
            var mask = new AvatarMask();
            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            mask.transformCount = transforms.Length;
            for (int i = 0; i < transforms.Length; i++)
            {
                mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(transforms[i], prefab.transform));
                mask.SetTransformActive(i, transforms[i].name.StartsWith("Index"));
            }
            AssetDatabase.CreateAsset(mask, IndexMaskPath);

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(HandAnimator.GripParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(HandAnimator.TriggerParameter, AnimatorControllerParameterType.Float);

            void Scrub(AnimatorStateMachine machine, string name, string parameter)
            {
                var state = machine.AddState(name);
                state.motion = Clip(name);
                state.timeParameterActive = true;
                state.timeParameter = parameter;
            }

            var layers = controller.layers;
            layers[0].name = "Grab";
            Scrub(layers[0].stateMachine, "Grab", HandAnimator.GripParameter);
            controller.layers = layers;

            var machine = new AnimatorStateMachine { name = "Trigger", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            Scrub(machine, "Trigger", HandAnimator.TriggerParameter);
            controller.AddLayer(new AnimatorControllerLayer { name = "Trigger", defaultWeight = 1f, stateMachine = machine, avatarMask = mask });

            var signs = new AnimatorStateMachine { name = HandGestures.LayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(signs, controller);
            foreach (HandGesture gesture in System.Enum.GetValues(typeof(HandGesture)))
            {
                if (gesture != HandGesture.None) signs.AddState(gesture.ToString()).motion = Clip(gesture.ToString());
            }
            controller.AddLayer(new AnimatorControllerLayer { name = HandGestures.LayerName, defaultWeight = 0f, stateMachine = signs });
            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Orients the model from its own bones: fingers along the controller's forward, back of the hand up.</summary>
        static void PlaceOnController(Transform model)
        {
            Vector3 Bone(string name) => model.InverseTransformPoint(model.GetComponentsInChildren<Transform>(true).First(t => t.name == name).position);

            var fingers = ((Bone("Middle1") + Bone("Ring1")) * 0.5f - Bone("Palm")).normalized;
            var towardsThumb = Vector3.ProjectOnPlane(Bone("Index1") - Bone("Pinky1"), fingers).normalized;
            var back = Vector3.Cross(fingers, towardsThumb);
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(fingers, back));
            var palm = fingers * PalmFromWrist - back * PalmHalfThickness;
            model.localRotation = rotation;
            model.localPosition = s_PalmOnController - rotation * palm;
        }

        static InputActionReference Action(string side, string name)
        {
            var path = $"XRI {side} Interaction/{name}";
            var reference = AssetDatabase.LoadAllAssetsAtPath(InputActionsPath).OfType<InputActionReference>().FirstOrDefault(r => r.name == path);
            if (!reference) Debug.LogError($"[HandballHands] Input action '{path}' not found in {InputActionsPath}");
            return reference;
        }
    }
}
