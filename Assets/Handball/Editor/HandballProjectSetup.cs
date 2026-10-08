using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Handball.EditorTools
{
    /// <summary>
    /// One-click project configuration: OpenXR + interaction profiles, XR Interaction Simulator,
    /// TextMeshPro resources, layers and physics. Safe to run repeatedly.
    /// </summary>
    public static class HandballProjectSetup
    {
        public const int BallLayer = 6;
        public const int KeeperLayer = 7;
        const string XriPackage = "com.unity.xr.interaction.toolkit";

        [MenuItem("Handball/Setup/Configure Project")]
        public static void Run()
        {
            HandballAssetSetup.ConfigureUnityChanImport();
            ImportTmpEssentials();
            ImportSimulatorSample();
            ConfigureOpenXR();
            ConfigureLayers();
            ConfigurePhysicsAndTime();
            AssetDatabase.SaveAssets();
            Debug.Log("[HandballProjectSetup] Project configured.");
        }

        static void ImportTmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro/Resources")) return;
            var path = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
            AssetDatabase.ImportPackage(Path.GetFullPath(path), false);
            AssetDatabase.Refresh();
            Debug.Log("[HandballProjectSetup] Imported TMP Essential Resources.");
        }

        public static string SimulatorPrefabPath
        {
            get
            {
                var guid = AssetDatabase.FindAssets("XR Interaction Simulator t:Prefab", new[] { "Assets/Samples" })
                    .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == "XR Interaction Simulator");
                return guid != null ? AssetDatabase.GUIDToAssetPath(guid) : null;
            }
        }

        static void ImportSimulatorSample()
        {
            if (SimulatorPrefabPath == null)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(XriPackage);
                var sample = Sample.FindByPackage(XriPackage, info.version).FirstOrDefault(s => s.displayName == "XR Interaction Simulator");
                if (string.IsNullOrEmpty(sample.displayName))
                {
                    Debug.LogError("[HandballProjectSetup] XR Interaction Simulator sample not found.");
                    return;
                }
                sample.Import(Sample.ImportOptions.OverridePreviousImports);
                AssetDatabase.Refresh();
                Debug.Log("[HandballProjectSetup] Imported XR Interaction Simulator sample.");
            }

            // Spawn the simulator automatically in the Editor only, so headset builds are unaffected.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset");
            if (settings == null)
            {
                Debug.LogError("[HandballProjectSetup] XRDeviceSimulatorSettings asset missing.");
                return;
            }
            var so = new SerializedObject(settings);
            so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = true;
            so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
            so.FindProperty("m_UseClassic").boolValue = false;
            so.FindProperty("m_SimulatorPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        static void ConfigureOpenXR()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (general == null)
            {
                Debug.LogError("[HandballProjectSetup] No XR General Settings for Standalone.");
                return;
            }
            general.InitManagerOnStart = true;
            var manager = general.Manager;
            if (!manager.activeLoaders.Any(l => l is OpenXRLoader))
                XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Standalone);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);

            var openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (openXR != null)
            {
                foreach (var feature in openXR.GetFeatures())
                {
                    if (feature is OculusTouchControllerProfile || feature is MetaQuestTouchPlusControllerProfile ||
                        feature is ValveIndexControllerProfile || feature is HTCViveControllerProfile ||
                        feature is KHRSimpleControllerProfile)
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                    }
                }
                EditorUtility.SetDirty(openXR);
            }
        }

        static void ConfigureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(BallLayer).stringValue = "Ball";
            layers.GetArrayElementAtIndex(KeeperLayer).stringValue = "Keeper";
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigurePhysicsAndTime()
        {
            // 90 Hz physics keeps a fast ball from tunnelling through the keeper's arms.
            Time.fixedDeltaTime = 1f / 90f;
            var timeManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            var fixedStep = timeManager.FindProperty("Fixed Timestep");
            if (fixedStep != null && fixedStep.propertyType == SerializedPropertyType.Float)
            {
                fixedStep.floatValue = 1f / 90f;
                timeManager.ApplyModifiedPropertiesWithoutUndo();
            }

            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = 1f;
            Physics.IgnoreLayerCollision(BallLayer, BallLayer, false);

            PlayerSettings.companyName = "IMR";
            PlayerSettings.productName = "Handball with Unity-chan";
        }
    }
}
