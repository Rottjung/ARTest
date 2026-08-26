using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Phase 1 goal: confirm a placeholder-content WebGL/WebAR build actually exports.
    /// Run headless via:
    ///   Unity.exe -batchmode -quit -projectPath <path>
    ///     -executeMethod ARReveal.EditorTools.Phase1BuildVerify.Run -logFile <path>
    /// </summary>
    public static class Phase1BuildVerify
    {
        private const string OutputDir = "Builds/WebGL_Phase1Test";
        private const string LocalTestOutputDir = "Builds/WebGL_LocalTest";

        [MenuItem("ARReveal/Build/WebGL Publish Build (Brotli)")]
        public static void Run()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            LogResult(report);
        }

        /// <summary>
        /// Same build, but with compression disabled so it can be served by any plain
        /// static file server (no special Content-Encoding/MIME handling needed) —
        /// for quick local sanity checks only. Does not touch the publish-ready
        /// (Brotli) settings used by Run().
        /// </summary>
        [MenuItem("ARReveal/Build/WebGL Local Test Build (uncompressed)")]
        public static void RunLocalTestBuild()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = LocalTestOutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            PlayerSettings.WebGL.compressionFormat = previousCompression;

            LogResult(report);
        }

        public static void LogResult(UnityEditor.Build.Reporting.BuildReport report)
        {
            var summary = report.summary;
            Debug.Log("[Phase1BuildVerify] Result: " + summary.result +
                " | Errors: " + summary.totalErrors +
                " | Warnings: " + summary.totalWarnings +
                " | Size: " + summary.totalSize + " bytes" +
                " | Time: " + summary.totalTime);
        }
    }
}
