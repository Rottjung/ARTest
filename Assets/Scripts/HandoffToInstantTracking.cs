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

        private bool _handedOff;

        private void Awake()
        {
            if (ContentWrapper != null) ContentWrapper.gameObject.SetActive(false);
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
                ContentWrapper.gameObject.SetActive(true);
            }
        }
    }
}
