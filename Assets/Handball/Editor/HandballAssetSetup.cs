using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Handball.EditorTools
{
    /// <summary>
    /// Import settings for third-party assets used by the handball scene.
    /// </summary>
    public static class HandballAssetSetup
    {
        public const string UnityChanModelPath = "Assets/UnityChan/Models/unitychan.fbx";
        public const string IdleAnimPath = "Assets/UnityChan/Animations/FUCM05_0000_Idle.fbx";
        public const string RisingAnimPath = "Assets/UnityChan/Animations/FUCM05_0022_M_RISING_P.fbx";
        public const string JabAnimPath = "Assets/UnityChan/Animations/FUCM05_0001_M_CMN_LJAB.fbx";
        public const string KickAnimPath = "Assets/UnityChan/Animations/FUCM_04_0001_RHiKick.fbx";

        public static readonly string[] UnityChanAnimPaths = { IdleAnimPath, RisingAnimPath, JabAnimPath, KickAnimPath };

        /// <summary>Switches Unity-chan and her clips to the Humanoid rig so IK and bone lookups work.</summary>
        public static void ConfigureUnityChanImport()
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(UnityChanModelPath);
            bool changed = false;
            if (model.animationType != ModelImporterAnimationType.Human || model.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                model.animationType = ModelImporterAnimationType.Human;
                model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                model.importAnimation = false;
                model.SaveAndReimport();
                changed = true;
            }

            // The auto-mapper binds hair joints as eyes/jaw, which makes look-at IK twist her hair.
            var desc = model.humanDescription;
            if (desc.human != null && desc.human.Any(h => h.humanName == "LeftEye" || h.humanName == "RightEye" || h.humanName == "Jaw"))
            {
                desc.human = desc.human.Where(h => h.humanName != "LeftEye" && h.humanName != "RightEye" && h.humanName != "Jaw").ToArray();
                model.humanDescription = desc;
                model.SaveAndReimport();
                changed = true;
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(UnityChanModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
            {
                Debug.LogError("[HandballAssetSetup] Unity-chan avatar is not a valid humanoid.");
                return;
            }

            foreach (var path in UnityChanAnimPaths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool dirty = false;
                // These clips must share the model's unit scale (metres) or the baked root height is off by 100x.
                if (!Mathf.Approximately(importer.globalScale, 1f) || !importer.useFileScale)
                {
                    importer.globalScale = 1f;
                    importer.useFileScale = true;
                    dirty = true;
                }
                if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != avatar)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = avatar;
                    dirty = true;
                }

                var clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                bool loop = path == IdleAnimPath;
                foreach (var c in clips)
                {
                    if (c.loopTime != loop || c.lockRootRotation != true || c.lockRootHeightY != true || c.keepOriginalPositionY != true || c.lockRootPositionXZ)
                    {
                        c.loopTime = loop;
                        c.loopPose = loop;
                        // Root motion is driven by script: bake rotation and height into the pose, and drop
                        // horizontal travel (applyRootMotion is off) so punches and idles stay on the spot.
                        c.lockRootRotation = true;
                        c.keepOriginalOrientation = true;
                        c.lockRootPositionXZ = false;
                        c.keepOriginalPositionXZ = false;
                        c.lockRootHeightY = true;
                        c.keepOriginalPositionY = true;
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                    changed = true;
                }
            }

            if (changed) Debug.Log("[HandballAssetSetup] Unity-chan import configured as Humanoid.");
        }
    }
}
