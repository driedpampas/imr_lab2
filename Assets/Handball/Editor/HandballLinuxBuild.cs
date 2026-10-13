using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Handball.EditorTools
{
    /// <summary>
    /// Flat-screen test build for Linux. The Unity OpenXR plugin has no Linux runtime, and its build step refuses
    /// any Standalone target other than Windows x64 / macOS while the OpenXR loader is enabled. This build drops the
    /// OpenXR loader and turns on the XR Interaction Simulator for the player, then puts both settings back, so the
    /// committed project settings (OpenXR for Windows / headset, simulator in the Editor only) are left untouched.
    /// </summary>
    public static class HandballLinuxBuild
    {
        public const string OutputPath = "Builds/LinuxTest/Handball.x86_64";
        const string SimulatorSettingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
        const string EditorOnlyProperty = "m_AutomaticallyInstantiateInEditorOnly";

        [MenuItem("Handball/Build/Linux Test Build (simulator, no OpenXR)")]
        public static void BuildFromMenu()
        {
            if (Build() is { } path) EditorUtility.RevealInFinder(path);
        }

        /// <summary>Entry point for batch mode: <c>Unity -batchmode -quit -executeMethod Handball.EditorTools.HandballLinuxBuild.BuildCli</c>.</summary>
        public static void BuildCli()
        {
            EditorApplication.Exit(Build() != null ? 0 : 1);
        }

        static string Build()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var manager = general != null ? general.Manager : null;
            var openXr = manager != null ? manager.activeLoaders.OfType<OpenXRLoader>().FirstOrDefault() : null;
            var simulator = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SimulatorSettingsPath);

            // The build unloads unused assets, so object references taken here are dead by the time it returns.
            // Restore the settings files byte-for-byte instead of undoing the edits through the API.
            var touched = new[] { AssetDatabase.GetAssetPath(general), SimulatorSettingsPath }
                .Where(File.Exists).ToDictionary(path => path, File.ReadAllBytes);
            try
            {
                if (openXr != null) manager.TryRemoveLoader(openXr);
                if (simulator != null) SetEditorOnly(new SerializedObject(simulator), false);
                SaveSettings(general, manager, simulator);

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = OutputPath,
                    target = BuildTarget.StandaloneLinux64,
                    options = BuildOptions.Development,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[HandballLinuxBuild] Build {report.summary.result} ({report.summary.totalErrors} errors).");
                    return null;
                }

                Debug.Log($"[HandballLinuxBuild] Built {OutputPath} ({report.summary.totalSize / (1024 * 1024)} MB).");
                return Path.GetFullPath(OutputPath);
            }
            finally
            {
                foreach (var file in touched)
                {
                    File.WriteAllBytes(file.Key, file.Value);
                    AssetDatabase.ImportAsset(file.Key, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        static void SetEditorOnly(SerializedObject so, bool value)
        {
            so.FindProperty(EditorOnlyProperty).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SaveSettings(params UnityEngine.Object[] objects)
        {
            foreach (var o in objects.Where(o => o != null)) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();
        }
    }
}
