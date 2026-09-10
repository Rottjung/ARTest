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

        private void Awake()
        {
            _target = GetComponent<ZapparImageTrackingTarget>();
            if (Handoff == null) Handoff = GetComponentInParent<HandoffToInstantTracking>();
            if (Handoff == null) Handoff = FindFirstObjectByType<HandoffToInstantTracking>();
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
                text = "TRACKING ACTIVE" + $"\nResets: {resets}" + (_qrVisible ? "\n(QR in view)" : "");
                color = Color.green;
            }

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
                normal = { textColor = color }
            };
            // Box height sized to the actual number of lines (up to 3 once handed
            // off and the QR is in view) - a fixed height tuned for the old 2-line
            // text was still in place when the "(QR in view)" third line was added,
            // which is exactly why it was rendering cut off.
            int lineCount = text.Split('\n').Length;
            float boxHeight = 20f + lineCount * 34f;
            GUI.Box(new Rect(10, 10, 360, boxHeight), text, style);
        }
    }
}
