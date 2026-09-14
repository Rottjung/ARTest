using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Keeps a set of content roots (proxy + wall-hole + tentacle, typically - one per
    /// burst point) fully hidden until this ZapparImageTrackingTarget actually finds
    /// the real-world image, then reveals them and kicks off the burst. Wire
    /// ZapparImageTrackingTarget's OnSeenEvent (Inspector) to call Reveal() on this
    /// component.
    ///
    /// Without this, content just sits visible wherever the tracker's transform
    /// happens to be before tracking locks on (origin, or a stale last-known pose),
    /// and anything using TentacleGrowOnStart fires on a scene-load timer completely
    /// independent of whether the real building has actually been found yet - which is
    /// why it can look "already grown" the moment tracking catches up. Remove
    /// TentacleGrowOnStart from tracked-scene tentacle instances and trigger Grow()
    /// from here (directly, or via a BurstSequencer) instead.
    ///
    /// ContentRoots is a list rather than a single object because burst points are
    /// often direct siblings under the tracker with no single common wrapper to
    /// toggle - list each one that should stay hidden until tracking is found.
    /// </summary>
    public class RevealOnTrackingFound : MonoBehaviour
    {
        [Tooltip("Hidden (SetActive(false)) until tracking is found. Typically each burst point's root (e.g. each Tentacle_01 instance, which already carries its WallHoleDemo/DebrisRing as children).")]
        public GameObject[] ContentRoots;

        [Tooltip("Optional - if set, PlaySequence() is called once content is revealed. Leave empty if you're triggering Grow()/Open() some other way (e.g. each ContentRoot's own TentacleGrowOnStart, once re-enabled/re-added).")]
        public BurstSequencer BurstSequencer;

        [Tooltip("Once revealed, stays revealed even if tracking is later lost and re-found - the burst is a one-time event, not something that should reset every time the camera shakes. Untick if you actually want it to reset on OnNotSeenEvent.")]
        public bool RevealOnlyOnce = true;

        [Header("Testing / capture only - leave off for the real build")]
        [Tooltip("Skips waiting for real tracking and calls Reveal() automatically shortly after Play starts - for recording/screenshotting the burst in-editor without needing a working camera/tracking pipeline. Never enable this on a build meant for actual use.")]
        public bool DebugAutoReveal = false;
        public float DebugAutoRevealDelay = 1f;

        private bool _revealed;

        /// <summary>
        /// True once Reveal() has actually run at least once - the
        /// direct-image-tracking equivalent of
        /// HandoffToInstantTracking.HasHandedOff, so ARShareController can
        /// gate its own UI (the calibration screen, Call To Action timing)
        /// the same way regardless of which of the two reveal mechanisms a
        /// given scene actually uses.
        /// </summary>
        public bool HasRevealed => _revealed;

        private void Awake()
        {
            SetContentActive(false);
        }

        private void Start()
        {
            if (DebugAutoReveal) Invoke(nameof(Reveal), DebugAutoRevealDelay);
        }

        /// <summary>Wire this to the tracker's OnSeenEvent.</summary>
        public void Reveal()
        {
            if (RevealOnlyOnce && _revealed) return;
            _revealed = true;

            SetContentActive(true);
            if (BurstSequencer != null) BurstSequencer.PlaySequence();
        }

        /// <summary>
        /// The direct-image-tracking equivalent of
        /// HandoffToInstantTracking.RestartRevealSequence() - replays the
        /// burst in place via BurstSequencer's own Hide-everything-then-
        /// replay (ResetAll + PlaySequence), for ARShareController's Restart
        /// button to call in a scene that uses this simpler reveal mechanism
        /// instead of the QR/SLAM handoff. No-ops if nothing has been
        /// revealed yet, or if no BurstSequencer is wired.
        /// </summary>
        public void RestartRevealSequence()
        {
            if (!_revealed || BurstSequencer == null) return;
            BurstSequencer.ResetAll();
            BurstSequencer.PlaySequence();
        }

        /// <summary>Wire this to the tracker's OnNotSeenEvent only if you want it to hide again when tracking is lost.</summary>
        public void Hide()
        {
            if (RevealOnlyOnce) return;
            _revealed = false;
            SetContentActive(false);
        }

        /// <summary>
        /// The direct-image-tracking equivalent of
        /// HandoffToInstantTracking.RequestRescan() - the "Rescan" button's
        /// own action (ARShareController.Rescan()) for a scene using this
        /// simpler reveal mechanism instead of the QR/SLAM handoff, per
        /// direct request ("we need a rescan button... reset with the same
        /// flow as the first time"). Unlike Hide() above (a no-op whenever
        /// RevealOnlyOnce is set, which it normally is - the burst is meant
        /// to be a one-time event, not something ordinary tracking loss
        /// should undo), this unconditionally hides content and clears
        /// _revealed regardless of RevealOnlyOnce, so the very next
        /// OnSeenEvent runs Reveal() completely fresh - the exact same code
        /// path the first-ever detection used, since Reveal()'s own
        /// early-out only ever checks RevealOnlyOnce && _revealed, and
        /// _revealed is now false.
        /// </summary>
        public void RequestRescan()
        {
            _revealed = false;
            SetContentActive(false);
            if (BurstSequencer != null) BurstSequencer.ResetAll();
        }

        private void SetContentActive(bool active)
        {
            if (ContentRoots == null) return;
            foreach (var root in ContentRoots)
                if (root != null) root.SetActive(active);
        }
    }
}
