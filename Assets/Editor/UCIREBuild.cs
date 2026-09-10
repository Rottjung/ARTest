using UnityEditor;

namespace ARReveal.EditorTools
{
    public static class UCIREBuild
    {
        private const string OutputDir = "Builds/WebGL_UCIRE";
        private const string PagesOutputDir = "docs/uci-re";
        private const string PagesDevOutputDir = "docs/uci-re-dev";
        private const string PagesMarkerOutputDir = "docs/uci-re-marker";
        private const string PagesArOutputDir = "docs/uci-re-ar";
        private const string ArVersion1OutputDir = "Builds/UCI-RE-AR_Version1";

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

        /// <summary>
        /// Builds UCI-RE-AR.unity (the client's own Zappar Pro workspace deliverable,
        /// wired to the RE_AR_QR ground marker via SwapToReArQr) at default/Brotli
        /// compression, same as the plain UCI-RE Zappar build above - Zappar's own
        /// hosting handles Brotli fine, unlike GitHub Pages. Zip Builds/UCI-RE-AR_Version1
        /// (index.html at the zip root) and upload it under the project's Experience tab.
        /// </summary>
        [MenuItem("ARReveal/UCI-RE-AR/Build WebGL for Zappar (Version 1, Brotli)")]
        public static void RunArVersion1()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE-AR.unity" },
                locationPathName = ArVersion1OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            Log(report);
        }

        /// <summary>
        /// Same UCI-RE-AR.unity scene as above, but uncompressed into docs/uci-re-ar/
        /// for GitHub Pages - a fast, self-serve way to test the QR/instant-tracking
        /// handoff, tentacle timing, and Selfie/Share buttons on a real phone WITHOUT
        /// going through ZapWorks' own upload/publish pipeline at all. This works for
        /// that testing purpose because QR tracking itself doesn't depend on ZapWorks
        /// hosting in any way - the trained RE_AR_QR.zpt target ships inside
        /// StreamingAssets as part of the build itself (see the Image Tracking doc in
        /// TentacleController/HandoffToInstantTracking's own comments), so it tracks
        /// identically regardless of which server is serving the files. What's
        /// DIFFERENT here vs the real Zappar-hosted deliverable: no ZapWorks
        /// interstitial/branding screen at all (Pages has none to begin with, so this
        /// isn't representative of that), and a larger uncompressed download (GitHub
        /// Pages can't set Content-Encoding: br) - fine for a quick test on wifi, not
        /// a stand-in for judging real-world load time on the client's own hosting.
        ///
        /// Requires a `git push` after this to actually go live - GitHub Pages serves
        /// straight from the repo's docs/ folder, not a local build output.
        /// </summary>
        [MenuItem("ARReveal/UCI-RE-AR/Build WebGL for GitHub Pages (uncompressed, testing only)")]
        public static void RunForPagesAr()
        {
            var previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UCI-RE-AR.unity" },
                locationPathName = PagesArOutputDir,
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
