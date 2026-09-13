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
        private const string BuildTag = "build-25";
        private const string BuildTagColorHex = "#FF6347"; // tomato - change alongside BuildTag above

        [Tooltip("Off hides the on-screen label entirely - still tracks everything underneath, just doesn't draw. Flip this off for the real build/client demo.")]
        public bool ShowOverlay = true;
        [Tooltip("Where the reset count and handoff state actually come from. Auto-found (this GameObject, its parents, or anywhere in the scene, in that order) if left blank - the two components don't have to be on the same object.")]
        public HandoffToInstantTracking Handoff;

        private bool _qrVisible;
        private ZapparImageTrackingTarget _target;

        // --- Camera/anchor-movement odometers + live distance (diagnostic only) -
        //
        // "Cam moved"/"Anchor moved" are SINCE-THE-LAST-LOCK odometers, not
        // since-app-start cumulative totals anymore. A real-device report found
        // the old cumulative version climbing to "ridiculous 2-3000m+" over a
        // long troubleshooting session with the phone barely moving - a plain
        // running sum of frame-to-frame distance CANNOT distinguish "genuinely
        // walked around" from "ordinary tracking jitter, summed over tens of
        // thousands of frames with no reset point ever" - even sub-centimeter
        // noise per frame adds up to kilometers eventually, since back-and-forth
        // jitter never cancels out in a pure running total, and a noise-floor
        // filter (tried first) only slows that down, it doesn't fix the actual
        // problem (no reset point). Resetting both odometers every time
        // Handoff.TotalLocksCompleted changes (see Update) makes them answer a
        // bounded, always-meaningful question - "how much has this moved since
        // the most recent lock/reset" - instead of an ever-growing total that's
        // guaranteed to look broken given enough real time.
        //
        // "Anchor" here means InstantTarget specifically - the internal SLAM
        // helper this class seeds with a throwaway fixed offset (see
        // HandoffToInstantTracking.DefaultAnchorSeedOffset's own doc) - NOT
        // where content actually is (that's ContentRoot, driven directly from
        // the QR's own reading). This odometer is about SLAM tracking quality/
        // stability in general, not content position specifically.
        private Transform _zCamTransform;
        private Vector3 _lastCamPos;
        private float _totalCamMovement;
        private Vector3 _lastAnchorPos;
        private float _totalAnchorMovement;
        private bool _posInitialized;
        private int _lastSeenLockCount = -1;
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

            // Reset both odometers every time a NEW lock/reset completes - see
            // this class's own doc comment on _totalCamMovement/_totalAnchorMovement
            // for why "since the last lock" replaced "since app start".
            if (Handoff.TotalLocksCompleted != _lastSeenLockCount)
            {
                _lastSeenLockCount = Handoff.TotalLocksCompleted;
                _totalCamMovement = 0f;
                _totalAnchorMovement = 0f;
                _anchorNaNFrames = 0;
                _posInitialized = false;
            }

            if (!_posInitialized)
            {
                _lastCamPos = _zCamTransform.position;
                if (anchorTransform != null) _lastAnchorPos = anchorTransform.position;
                _posInitialized = true;
                return;
            }

            // Same NaN guard on the camera side, for the same reason - cheap
            // insurance even though it hasn't actually been observed there yet.
            //
            // Frame-to-frame deltas below this floor are treated as noise and
            // not added, so ordinary sub-millimeter tracking jitter doesn't
            // slowly creep the total up even within a single lock's window -
            // genuine movement (centimeters or more per frame) is unaffected.
            const float noiseFloorMeters = 0.01f;

            Vector3 camPos = _zCamTransform.position;
            if (IsFinite(camPos))
            {
                float camDelta = Vector3.Distance(camPos, _lastCamPos);
                if (camDelta >= noiseFloorMeters) _totalCamMovement += camDelta;
                _lastCamPos = camPos;
            }

            if (anchorTransform != null)
            {
                Vector3 anchorPos = anchorTransform.position;
                if (IsFinite(anchorPos))
                {
                    float anchorDelta = Vector3.Distance(anchorPos, _lastAnchorPos);
                    if (anchorDelta >= noiseFloorMeters) _totalAnchorMovement += anchorDelta;
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
                // Genuinely still waiting - the QR has never been seen (or was seen
                // but the handoff hasn't finished this frame yet) - this is the ONE
                // state that should read as "not there yet", since content really
                // isn't live.
                text = "WAITING FOR QR...";
                color = Color.yellow;
            }
            else if (Handoff != null && !Handoff.HasSeededAtLeastOnce)
            {
                // Handoff has started (the QR was seen) but content hasn't
                // actually been placed yet - it's mid-settle (see
                // HandoffToInstantTracking.SettleAndSample), waiting for
                // several consecutive frames to agree before trusting a
                // reading enough to seed the anchor from it. Shown as its own
                // state so this isn't misread as "TRACKING ACTIVE" before the
                // anchor has actually been seeded.
                text = $"LOCKING ANCHOR...\n{Handoff.SettleProgress}/{Handoff.CurrentSettleFramesRequired} agreeing frames" +
                    (_qrVisible ? "\n(QR in view)" : "\n(QR out of view - waiting)");
                color = Color.yellow;
            }
            else
            {
                // Content is live and anchored via the instant tracker's own SLAM
                // tracking regardless of whether the QR is currently in view - so
                // this always reads as active, never "lost". The QR-visible note is
                // a quiet aside, not an alarm.
                //
                // "QR dist" measures camera-to-QR distance directly from
                // ImageTarget.transform - NOT InstantTarget. A real-device
                // report correctly caught this reading a fixed ~3-4m regardless
                // of true scanning distance: it used to measure distance to
                // InstantTarget, which is now seeded with a throwaway fixed
                // offset (see HandoffToInstantTracking.DefaultAnchorSeedOffset's
                // own doc) since content no longer comes from that anchor's
                // absolute position at all - wrong thing being measured, not a
                // tracking bug. Only updates while the QR is genuinely visible
                // (ImageTarget freezes its own transform otherwise, same as
                // everywhere else in this project) - shows the last known value
                // rather than n/a while out of view, same spirit as "Resets"
                // not resetting just because the QR left frame.
                bool haveQr = Handoff != null && Handoff.ImageTarget != null && _zCamTransform != null;
                Vector3 qrPosNow = haveQr ? Handoff.ImageTarget.transform.position : Vector3.zero;
                bool distValid = haveQr && IsFinite(qrPosNow) && IsFinite(_zCamTransform.position);
                float liveDist = distValid ? Vector3.Distance(_zCamTransform.position, qrPosNow) : -1f;

                // See HandoffToInstantTracking's own "SYNC CHECK" doc comment -
                // how far the QR's own live detection currently disagrees with
                // where SLAM has the anchor, whenever the QR happens to be in
                // view to compare against. A big/growing number here is a
                // direct measurement of SLAM drift, not just a guess from the
                // odometers below.
                bool syncValid = Handoff != null && Handoff.SyncCheckValid;
                string syncLine = syncValid
                    ? $"\nQR/SLAM sync: {Handoff.SyncPositionDelta:F2}m, {Handoff.SyncRotationDeltaDegrees:F1}deg"
                    : "";

                // Requested directly - print the settled position for EVERY
                // lock/reset, and whether that particular one was the true
                // "reveal moment" (the only one that actually calls
                // RevealContent()) - so a bad-but-invisible lock can be
                // compared against a good-and-visible one without needing a
                // console.
                string lockLine = Handoff != null
                    ? $"\nLock #{Handoff.TotalLocksCompleted}{(Handoff.LastLockWasReveal ? " (REVEAL)" : "")}: {Handoff.LastLockedPosition:F2}"
                    : "";

                text = "TRACKING ACTIVE" + $"\nResets: {resets}" + (_qrVisible ? "\n(QR in view)" : "") + lockLine +
                    $"\nCam moved (since lock): {_totalCamMovement:F2}m" +
                    $"\nAnchor moved (since lock): {_totalAnchorMovement:F2}m" +
                    (_anchorNaNFrames > 0 ? $" ({_anchorNaNFrames} bad frames)" : "") +
                    (distValid ? $"\nQR dist: {liveDist:F2}m" : "\nQR dist: n/a (QR never seen)") +
                    syncLine;
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
