using UnityEditor;

namespace ARReveal.EditorTools
{
    public static class BuildingTestBuild
    {
        private const string OutputDir = "Builds/WebGL_BuildingTest";
        private const string PagesOutputDir = "docs";

        [MenuItem("ARReveal/Building Test/Build WebGL (Brotli)")]
        public static void Run()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/BuildingTest.unity" },
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            Log(report);
        }

        /// <summary>
        /// Uncompressed build into /docs at the repo root, for GitHub Pages (which
        /// can't set Content-Encoding: br, so the Brotli build won't load there).
        /// </summary>
        [MenuItem("ARReveal/Building Test/Build WebGL for GitHub Pages (uncompressed)")]
        public static void RunForPages()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/BuildingTest.unity" },
                locationPathName = PagesOutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            PlayerSettings.WebGL.compressionFormat = previousCompression;

            Log(report);
        }

        private static void Log(UnityEditor.Build.Reporting.BuildReport report)
        {
            var summary = report.summary;
            UnityEngine.Debug.Log("[BuildingTestBuild] Result: " + summary.result +
                " | Errors: " + summary.totalErrors +
                " | Warnings: " + summary.totalWarnings +
                " | Size: " + summary.totalSize + " bytes" +
                " | Time: " + summary.totalTime);
        }
    }
}
