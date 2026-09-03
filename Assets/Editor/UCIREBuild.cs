using UnityEditor;

namespace ARReveal.EditorTools
{
    public static class UCIREBuild
    {
        private const string OutputDir = "Builds/WebGL_UCIRE";
        private const string PagesOutputDir = "docs/uci-re";
        private const string PagesDevOutputDir = "docs/uci-re-dev";
        private const string PagesMarkerOutputDir = "docs/uci-re-marker";

        [MenuItem("ARReveal/UCI-RE/Build WebGL (Brotli)")]
        public static void Run()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE.unity" },
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            Log(report);
        }

        /// <summary>
        /// Uncompressed build into docs/uci-re/, alongside the existing BuildingTest
        /// site at docs/ root - GitHub Pages can't set Content-Encoding: br.
        /// </summary>
        [MenuItem("ARReveal/UCI-RE/Build WebGL for GitHub Pages (uncompressed)")]
        public static void RunForPages()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE.unity" },
                locationPathName = PagesOutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            PlayerSettings.WebGL.compressionFormat = previousCompression;

            Log(report);
        }

        /// <summary>
        /// Same as above but into docs/uci-re-dev/, a second GitHub Pages URL that's
        /// separate from the live docs/uci-re/ site - use this to publish in-progress
        /// changes for review without touching what's currently live.
        /// </summary>
        [MenuItem("ARReveal/UCI-RE/Build WebGL for GitHub Pages (dev, uncompressed)")]
        public static void RunForPagesDev()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE.unity" },
                locationPathName = PagesDevOutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            PlayerSettings.WebGL.compressionFormat = previousCompression;

            Log(report);
        }

        /// <summary>
        /// Builds UCI-RE-Marker.unity (the standalone ground-marker anchoring test)
        /// into its own docs/uci-re-marker/ URL - separate from both the live site and
        /// uci-re-dev, since this is a different scene testing a different anchoring
        /// mechanism entirely, not an iteration on the building-photo tracking.
        /// </summary>
        [MenuItem("ARReveal/UCI-RE/Build WebGL for GitHub Pages (marker test, uncompressed)")]
        public static void RunForPagesMarker()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE-Marker.unity" },
                locationPathName = PagesMarkerOutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            PlayerSettings.WebGL.compressionFormat = previousCompression;

            Log(report);
        }

        private static void Log(UnityEditor.Build.Reporting.BuildReport report)
        {
            var summary = report.summary;
            UnityEngine.Debug.Log("[UCIREBuild] Result: " + summary.result +
                " | Errors: " + summary.totalErrors +
                " | Warnings: " + summary.totalWarnings +
                " | Size: " + summary.totalSize + " bytes" +
                " | Time: " + summary.totalTime);
        }
    }
}
