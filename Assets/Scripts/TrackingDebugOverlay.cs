using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// On-screen "TRACKING: FOUND/LOST" label, wired to the same OnSeenEvent/
    /// OnNotSeenEvent as content scripts - lets you confirm on the phone itself
    /// whether the tracker is actually catching/losing the target, independent of
    /// whether any content is visible. Uses legacy OnGUI so it needs no Canvas setup.
    /// </summary>
    [RequireComponent(typeof(ZapparImageTrackingTarget))]
    public class TrackingDebugOverlay : MonoBehaviour
    {
        private bool _seen = false;
        private int _seenCount = 0;
        private int _lostCount = 0;
        private ZapparImageTrackingTarget _target;

        private void Awake()
        {
            _target = GetComponent<ZapparImageTrackingTarget>();
        }

        private void OnEnable()
        {
            _target.OnSeenEvent.AddListener(HandleSeen);
            _target.OnNotSeenEvent.AddListener(HandleNotSeen);
        }

        private void OnDisable()
        {
            _target.OnSeenEvent.RemoveListener(HandleSeen);
            _target.OnNotSeenEvent.RemoveListener(HandleNotSeen);
        }

        private void HandleSeen() { _seen = true; _seenCount++; }
        private void HandleNotSeen() { _seen = false; _lostCount++; }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
                normal = { textColor = _seen ? Color.green : Color.red }
            };
            string text = (_seen ? "TRACKING: FOUND" : "TRACKING: LOST") +
                $"\nfound x{_seenCount}  lost x{_lostCount}";
            GUI.Box(new Rect(10, 10, 340, 80), text, style);
        }
    }
}
