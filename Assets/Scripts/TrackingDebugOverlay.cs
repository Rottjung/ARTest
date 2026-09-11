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

            _totalCamMovement += Vector3.Distance(_zCamTransform.position, _lastCamPos);
            _lastCamPos = _zCamTransform.position;

            if (anchorTransform != null)
            {
                _totalAnchorMovement += Vector3.Distance(anchorTransform.position, _lastAnchorPos);
                _lastAnchorPos = anchorTransform.position;
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
            else
            {
                // Content is live and anchored via the instant tracker's own SLAM
                // tracking regardless of whether the QR is currently in view - so
                // this always reads as active, never "lost". The QR-visible note is
                // a quiet aside, not an alarm.
                float liveDist = (Handoff != null && Handoff.InstantTarget != null && _zCamTransform != null)
                    ? Vector3.Distance(_zCamTransform.position, Handoff.InstantTarget.transform.position)
                    : -1f;
                text = "TRACKING ACTIVE" + $"\nResets: {resets}" + (_qrVisible ? "\n(QR in view)" : "") +
                    $"\nCam moved: {_totalCamMovement:F2}m" +
                    $"\nAnchor moved: {_totalAnchorMovement:F2}m" +
                    (liveDist >= 0f ? $"\nDist: {liveDist:F2}m" : "");
                color = Color.green;
            }

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
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
