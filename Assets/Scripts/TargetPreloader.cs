using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// Prefetches a WebGL StreamingAssets .zpt file as early as this
    /// project's own code can run (Awake) - well before
    /// ZapparImageTrackingTarget's own internal load even starts (that one
    /// only kicks off once the camera pipeline finishes initializing, via
    /// OnZapparInitialized, always somewhat later).
    ///
    /// REAL BUG THIS ADDRESSES - confirmed by reading Zappar's own SDK
    /// source (Library/PackageCache/com.zappar.uar, not editable here) and
    /// matching it against a direct on-site report ("clear browser cache,
    /// first few tries show nothing, reload a few times and it works"):
    /// ZapparImageTrackingTarget.Start() -> OnZapparInitialized() calls
    /// Z.LoadZPTTarget(), which on WebGL is a genuine async
    /// UnityWebRequest.Get() fetch of the .zpt from StreamingAssets - with
    /// ZERO error handling (a failed/empty response is still handed to the
    /// native tracker as if it succeeded) and ZERO exposed "is it actually
    /// loaded yet" signal. Critically, ZapparImageTrackingTarget.Update()
    /// only ever fires OnSeenEvent once the native tracker already has
    /// target data to match against (Z.ImageTrackerAnchorCount() > 0) - so
    /// if the real-world QR/poster is scanned before that fetch completes,
    /// it will NEVER be detected, no matter how well it's framed, until the
    /// page is reloaded (a previous attempt's now-cached HTTP response
    /// makes the next fetch effectively instant).
    ///
    /// This can't be fixed INSIDE Zappar's own script (package code) -
    /// instead, this fires an independent, parallel fetch of the EXACT
    /// SAME URL as early as possible, which warms the browser's HTTP cache
    /// for that exact request - by the time Zappar's own internal fetch
    /// actually starts, it very likely hits that now-warm cache instead of
    /// a cold one, completing near-instantly instead of racing a real scan
    /// attempt. IsReady also gives ARShareController's calibration screen
    /// something concrete to show ("Preparing..." vs "Scan the QR") instead
    /// of assuming the target is available the instant the camera appears.
    /// </summary>
    public class TargetPreloader : MonoBehaviour
    {
        [Tooltip("Auto-found in the scene if left blank - reads its Target field directly (the .zpt filename), so this never needs to be kept in sync by hand whenever that filename changes.")]
        public ZapparImageTrackingTarget ImageTarget;

        /// <summary>True once the .zpt has been successfully prefetched (browser cache warmed for Zappar's own subsequent load).</summary>
        public bool IsReady { get; private set; }

        /// <summary>True if the prefetch itself failed (network error, 404, etc.) - logged loudly either way, since Zappar's own equivalent load would otherwise fail completely silently.</summary>
        public bool FailedToLoad { get; private set; }

        private void Awake()
        {
            if (ImageTarget == null) ImageTarget = FindFirstObjectByType<ZapparImageTrackingTarget>();
            StartCoroutine(Preload());
        }

        private IEnumerator Preload()
        {
            if (ImageTarget == null || string.IsNullOrEmpty(ImageTarget.Target))
            {
                Debug.LogError("[TargetPreloader] No ZapparImageTrackingTarget (or its Target filename is empty) - nothing to preload.");
                yield break;
            }

            string url = Path.Combine(Application.streamingAssetsPath, ImageTarget.Target);
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                bool ok = !request.isNetworkError && !request.isHttpError;
#endif
                if (ok && request.downloadHandler.data != null && request.downloadHandler.data.Length > 0)
                {
                    IsReady = true;
                    Debug.Log("[TargetPreloader] '" + ImageTarget.Target + "' preloaded (" +
                        request.downloadHandler.data.Length + " bytes) - browser cache warmed for Zappar's own load.");
                }
                else
                {
                    FailedToLoad = true;
                    Debug.LogError("[TargetPreloader] Failed to preload '" + ImageTarget.Target + "': " + request.error +
                        " - Zappar's own tracking load will likely fail the same way, with no error of its own.");
                }
            }
        }
    }
}
