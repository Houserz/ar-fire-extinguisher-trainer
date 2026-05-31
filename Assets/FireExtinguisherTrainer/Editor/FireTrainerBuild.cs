#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace FireExtinguisherTrainerEditor
{
    public static class FireTrainerBuild
    {
        private const string DefaultApkPath = "Builds/fire-trainer-platform.apk";

        [MenuItem("Tools/Fire Trainer/Build Platform Android APK")]
        public static void BuildAndroidApk()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultApkPath));

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                scenes = new[] { "Assets/Scenes/FireTrainerWeek1.unity" };
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = DefaultApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Android APK build failed: {report.summary.result}");
            }
        }
    }
}
#endif
