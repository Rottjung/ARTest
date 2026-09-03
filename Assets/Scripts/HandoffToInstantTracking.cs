using System.Collections;
using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// The moment the QR image target is first seen, seeds a ZapparInstantTrackingTarget's
    /// anchor at that same real-world position (via Zappar's own persistent 6DOF/SLAM
    /// world tracking) and hands off to it - so content stays correctly anchored even
    /// after the QR leaves the camera's view, instead of freezing/detaching the moment
    /// image detection is lost (ZapparImageTrackingTarget only updates its transform
    /// while the image is currently, continuously visible - confirmed by reading the
    /// SDK source; there's no persistence built into image tracking itself).
    ///
    /// The instant tracker only supports seeding position precisely, plus one of a few
    /// coarse fixed rotation modes (WORLD, MINUS_Z_AWAY_FROM_USER, etc.) - not the QR's
    /// exact detected rotation. So ContentWrapper (everything that should be anchored)
    /// gets a one-time rotation correction applied right after handoff, computed as the
    /// difference between the QR's actual rotation and whatever the instant tracker's
    /// seeded rotation turned out to be - after that, ContentWrapper just rides along
    /// with the instant tracker's ongoing SLAM tracking.
    ///
    /// Built from the SDK's public API surface, not verified on a real device yet -
    /// the position handoff especially assumes ImageTarget.AnchorPoseCameraRelative()'s
    /// translation is in the same camera-space convention InstantWorldTrackerAnchorPoseSetFromCameraOffset
    /// expects. Needs real on-site testing; may need a small correction if the anchor
    /// doesn't land exactly where the QR was.
    /// </summary>
    public class HandoffToInstantTracking : MonoBehaviour
    {
        public ZapparImageTrackingTarget ImageTarget;
        public ZapparInstantTrackingTarget InstantTarget;

        [Tooltip("Everything that should stay anchored - hidden until handoff, then carries a one-time rotation correction and rides along with the instant tracker's ongoing SLAM tracking.")]
        public Transform ContentWrapper;

        [Header("Testing / capture only - leave off for the real build")]
        [Tooltip("Skips waiting for the QR and the real instant-tracker handoff entirely, and just reveals ContentWrapper as-authored shortly after Play starts - for recording/screenshotting the burst in-editor without needing a working camera/tracking pipeline. Never enable this on a build meant for actual use.")]
        public bool DebugAutoReveal = false;
        public float DebugAutoRevealDelay = 1f;

        private bool _handedOff;

        private void Awake()
        {
            if (ContentWrapper != null) ContentWrapper.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (DebugAutoReveal) Invoke(nameof(DebugReveal), DebugAutoRevealDelay);
        }

        /// <summary>Bypasses the QR + instant-tracker handoff entirely - just shows ContentWrapper where it already sits in the scene.</summary>
        private void DebugReveal()
        {
            if (_handedOff) return;
            _handedOff = true;
            RevealContent();
        }

        /// <summary>Wire this to ImageTarget's OnSeenEvent.</summary>
        public void HandoffOnce()
        {
            if (_handedOff) return;
            _handedOff = true;
            StartCoroutine(HandoffRoutine());
        }

        private IEnumerator HandoffRoutine()
        {
            // Camera-relative offset of the QR at the exact moment of detection - this
            // is what places the instant anchor at the same real-world spot.
            Matrix4x4 cameraRelative = ImageTarget.AnchorPoseCameraRelative();
            Vector3 offset = Z.GetPosition(cameraRelative);

            Z.InstantWorldTrackerAnchorPoseSetFromCameraOffset(
                InstantTarget.InstantTracker.Value, offset.x, offset.y, offset.z,
                Z.InstantTrackerTransformOrientation.WORLD);
            InstantTarget.PlaceTrackerAnchor();

            // Wait a frame so InstantTarget's own Update() applies the pose we just
            // seeded before we read its transform for the rotation correction below.
            yield return null;

            if (ContentWrapper != null)
            {
                Quaternion correction = ImageTarget.transform.rotation * Quaternion.Inverse(InstantTarget.transform.rotation);
                ContentWrapper.localRotation = correction;
            }
            RevealContent();
        }

        /// <summary>
        /// Activates ContentWrapper and starts every TentacleController (plus any
        /// WallHoleEffect/DebrisRing) found under it. SetActive(true) alone isn't
        /// enough - each tentacle waits for its own Grow() call (Unfold-style ones
        /// otherwise just sit static in their curled rest pose; Punch-style ones stay
        /// invisible, since they zero their own scale in Awake() until told to grow).
        /// Auto-discovered rather than hand-wired so it doesn't need updating every
        /// time a burst point is added or removed under ContentWrapper.
        /// </summary>
        private void RevealContent()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(true);

            foreach (var tentacle in ContentWrapper.GetComponentsInChildren<TentacleController>(true))
                tentacle.Grow();
            foreach (var hole in ContentWrapper.GetComponentsInChildren<WallHoleEffect>(true))
                hole.Open();
            foreach (var debris in ContentWrapper.GetComponentsInChildren<DebrisRing>(true))
                debris.Open();
        }
    }
}
