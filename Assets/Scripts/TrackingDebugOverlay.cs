using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// On-screen tracking status label. Uses legacy OnGUI so it needs no Canvas setup.
    ///
    /// Deliberately does NOT say "TRACKING LOST" when the QR leaves the camera's
    /// view (an earlier version did) - that's a normal, expected state in this
    /// project's setup, not a failure: once HandoffToInstantTracking has handed off,
    /// content rides along on the instant tracker's own SLAM tracking, which keeps
    /// working with or without the QR still in view (that's the whole point of the
    /// handoff - see that class's own doc comment). Flagging every ordinary "QR out
    /// of frame" moment as "LOST" in red reads as something being broken when
    /// nothing is. Instead this shows three genuinely distinct, honestly-labelled
    /// states: waiting for the QR to be seen at all, tracking active (content is
    /// live and anchored), and how many times the anchor has been RE-seeded from a
    /// fresh QR detection since - that reset count, not raw QR visibility, is the
    /// actually meaningful diagnostic for judging tracking quality on-site.
    /// </summary>
    [RequireComponent(typeof(ZapparImageTrackingTarget))]
    public class TrackingDebugOverlay : MonoBehaviour
    {
        /// <summary>
        /// BUMP BOTH OF THESE TOGETHER in every commit meant to be tested as a new
        /// build - shown as a colored tag line in the overlay so a glance confirms
        /// a genuinely fresh build/deploy loaded (not a stale cached one), without
        /// having to read or compare any text. Requested after a debugging round
        /// where it wasn't obvious whether a real-device test was actually running
        /// the latest fix or a leftover cached build. Cycle through visually
        /// distinct colors (not just a version number bump) since the whole point
        /// is to be readable as "different" at a glance, from across a room.
        /// </summary>
        private const string BuildTag = "build-5";
        private const string BuildTagColorHex = "#B0FF00"; // lime - change alongside BuildTag above

        [Tooltip("Off hides the on-screen label entirely - still tracks everything underneath, just doesn't draw. Flip this off for the real build/client demo.")]
        public bool ShowOverlay = true;
        [Tooltip("Where the reset count and handoff state actually come from. Auto-found (this GameObject, its parents, or anywhere in the scene, in that order) if left blank - the two components don't have to be on the same object.")]
        public HandoffToInstantTracking Handoff;

        private bool _qrVisible;
        private ZapparImageTrackingTarget _target;

        // --- Camera/anchor-movement odometers + live distance (diagnostic only) -
        // Added while chasing the "building stays at the same distance no matter
        // how far I walk" bug with Unity closed/no device to test on. First pass
        // was just "Cam moved" - real-device testing then showed that number
        // climbing even while genuinely standing still, which "Cam moved" alone
        // can't explain: it could be ordinary handheld jitter (a real phone is
        // never perfectly still - a few cm of drift from natural hand shake is
        // normal and NOT itself a bug), or it could mean something odder. Two
        // more numbers added to actually tell those apart:
        //   - "Anchor moved" - the SAME odometer, but for the InstantTarget
        //     (the content's own anchor) instead of the camera. In Zappar's
        //     "origin mode" (AnchorOrigin set, which this project uses) the
        //     anchor is SUPPOSED to stay close to fixed in world space while the
        //     CAMERA moves realistically around it - that's the whole mechanism
        //     that's meant to make walking around content work. If this number
        //     climbs in step with "Cam moved" (rather than staying near 0), the
        //     content is drifting right along with the camera instead of staying
        //     put - which would explain "stays at the same distance/screen
        //     position" even though the camera really is moving: both ends of
        //     the measurement are moving together, canceling out the parallax
        //     that should otherwise be visible.
        //   - "Dist" - the LIVE (not cumulative) straight-line distance from
        //     camera to anchor right now. This is the single most direct number
        //     for the actual bug: if it doesn't change while someone deliberately
        //     walks toward/away from the anchor, the building genuinely isn't
        //     getting closer/further, regardless of what either odometer above
        //     is doing individually - this is the number to watch during a real
        //     walk test, the odometers are just there to explain WHY if it
        //     doesn't move.
        private Transform _zCamTransform;
        private Vector3 _lastCamPos;
        private float _totalCamMovement;
        private Vector3 _lastAnchorPos;
        private float _totalAnchorMovement;
        private bool _posInitialized;
        // A real-device test showed "Anchor moved" latching permanently to NaN -
        // Vector3.Distance returns NaN if EITHER position it's given has any NaN
        // component, and float NaN poisons every += after it forever, so a single
        // bad frame (most likely right at/just after placement, before the native
        // anchor pose has fully stabilised - Z.GetRotation/Z.GetScale decomposing
        // a not-yet-valid matrix is the likely source) would explain the whole
        // odometer going permanently unreadable from that point on, even though
        // the underlying anchor pose may well have recovered to sane values on
        // every later frame. Guarded below so one bad frame is skipped/counted
        // instead of poisoning the running total - the count itself is still
        // shown (only when >0) since a NaN frame happening at all is itself a
        // real, worth-knowing signal about the native anchor pose's stability,
        // not just noise to hide.
        private int _anchorNaNFrames;

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        private void Awake()
        {
            _target = GetComponent<ZapparImageTrackingTarget>();
            if (Handoff == null) Handoff = GetComponentInParent<HandoffToInstantTracking>();
            if (Handoff == null) Handoff = FindFirstObjectByType<HandoffToInstantTracking>();

            var zCam = ZapparCamera.Instance != null ? ZapparCamera.Instance : FindFirstObjectByType<ZapparCamera>();
            if (zCam != null) _zCamTransform = zCam.transform;
        }

        private void Update()
        {
            // Only start accumulating once handed off - the camera (and anchor,
            // pre-placement) can jump around freely before the anchor is placed
            // (that's expected/normal, see ZapparInstantTrackingTarget's own
            // pre-placement re-seed loop), so counting that would just add noise
            // to the numbers this exists to make readable.
            var anchorTransform = Handoff != null && Handoff.InstantTarget != null ? Handoff.InstantTarget.transform : null;
            if (_zCamTransform == null || Handoff == null || !Handoff.HasHandedOff) return;

            if (!_posInitialized)
            {
                _lastCamPos = _zCamTransform.position;
                if (anchorTransform != null) _lastAnchorPos = anchorTransform.position;
                _posInitialized = true;
                return;
            }

            // Same NaN guard on the camera side, for the same reason - cheap
            // insurance even though it hasn't actually been observed there yet.
            Vector3 camPos = _zCamTransform.position;
            if (IsFinite(camPos))
            {
                _totalCamMovement += Vector3.Distance(camPos, _lastCamPos);
                _lastCamPos = camPos;
            }

            if (anchorTransform != null)
            {
                Vector3 anchorPos = anchorTransform.position;
                if (IsFinite(anchorPos))
                {
                    _totalAnchorMovement += Vector3.Distance(anchorPos, _lastAnchorPos);
                    _lastAnchorPos = anchorPos;
                }
                else
                {
                    _anchorNaNFrames++;
                    // Don't fold the bad value into _lastAnchorPos - next good
                    // frame should compare against the last KNOWN-GOOD position,
                    // not a garbage one, or it'd just move the poisoning by one
                    // frame instead of fixing it.
                }
            }
        }

        private void OnEnable()
        {
            if (_target == null) return;
            _target.OnSeenEvent.AddListener(HandleSeen);
            _target.OnNotSeenEvent.AddListener(HandleNotSeen);
        }

        private void OnDisable()
        {
            if (_target == null) return;
            _target.OnSeenEvent.RemoveListener(HandleSeen);
            _target.OnNotSeenEvent.RemoveListener(HandleNotSeen);
        }

        private void HandleSeen() => _qrVisible = true;
        private void HandleNotSeen() => _qrVisible = false;

        private void OnGUI()
        {
            if (!ShowOverlay) return;

            bool handedOff = Handoff != null && Handoff.HasHandedOff;
            int resets = Handoff != null ? Handoff.ResetCount : 0;

            string text;
            Color color;
            if (!handedOff)
            {
                // Genuinely still waiting - the QR has never been seen - this is
                // the ONE state that should read as "not there yet", since
                // content really isn't live. Reveal is now immediate on first
                // sighting (see HandoffToInstantTracking.HandoffOnce), so there's
                // no separate "locking..." delay state to show anymore.
                text = "WAITING FOR QR...";
                color = Color.yellow;
            }
            else if (Handoff != null && Handoff.IsFollowingQrLive)
            {
                // ContentWrapper is a live child of the QR's own transform right
                // now - the most accurate state, straight from Zappar's image
                // tracking with nothing in between.
                text = "TRACKING ACTIVE\n(LIVE on QR)" + $"\nSLAM handoffs: {resets}";
                color = Color.green;
            }
            else
            {
                // QR is out of view - content is riding on the Instant Tracker's
                // own SLAM tracking instead, with rotation/scale continuously
                // re-locked to the QR's last known values (see
                // HandoffToInstantTracking.ApplyLockedTransform). Still reads as
                // active, never "lost" - this is the expected fallback state
                // whenever someone tilts up from the QR to look at the building.
                bool haveAnchor = Handoff != null && Handoff.InstantTarget != null && _zCamTransform != null;
                Vector3 anchorPosNow = haveAnchor ? Handoff.InstantTarget.transform.position : Vector3.zero;
                bool distValid = haveAnchor && IsFinite(anchorPosNow) && IsFinite(_zCamTransform.position);
                float liveDist = distValid ? Vector3.Distance(_zCamTransform.position, anchorPosNow) : -1f;

                text = "TRACKING ACTIVE\n(SLAM fallback)" + $"\nSLAM handoffs: {resets}" +
                    $"\nCam moved: {_totalCamMovement:F2}m" +
                    $"\nAnchor moved: {_totalAnchorMovement:F2}m" +
                    (_anchorNaNFrames > 0 ? $" ({_anchorNaNFrames} bad frames)" : "") +
                    (distValid ? $"\nDist: {liveDist:F2}m" : "\nDist: n/a (bad anchor pose)");
                color = Color.green;
            }

            // Build-tag line always shown first, in its own color via rich text -
            // see BuildTag/BuildTagColorHex's own doc comment for why. Kept
            // separate from `color` above (which still means waiting/locking/
            // active) rather than overriding the whole box's color, so that
            // meaningful state coloring isn't lost.
            text = $"<color={BuildTagColorHex}>[{BuildTag}]</color>\n" + text;

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
                richText = true,
                normal = { textColor = color }
            };
            // Box height sized to the actual number of lines (up to 6 once handed
            // off, the QR is in view, and all the diagnostic lines are showing) -
            // a fixed height tuned for the old 2-line text was still in place when
            // the "(QR in view)" 3rd line was added, which is exactly why it was
            // rendering cut off - computing it from the real line count instead
            // means adding more lines later (like the diagnostics above) can't
            // reintroduce that same bug.
            int lineCount = text.Split('\n').Length;
            float boxHeight = 20f + lineCount * 34f;
            GUI.Box(new Rect(10, 10, 360, boxHeight), text, style);
        }
    }
}
