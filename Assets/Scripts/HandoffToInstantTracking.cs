using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// One burst point: a tentacle paired with whichever of its own hole/debris/smoke/
    /// rubble are present (leave any blank if this point doesn't have one). Everything
    /// in a pair shares a single random start delay and fires together - hole,
    /// tentacle, smoke and falling rubble all together, the static debris ring a
    /// moment after (once the hole's most of the way open) - so it reads as one
    /// coordinated burst instead of independent coin-flips that could land out of
    /// sync with each other.
    /// </summary>
    [System.Serializable]
    public class TentaclePair
    {
        public string Name = "Burst Point";
        public TentacleController Tentacle;
        public WallHoleEffect Hole;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public DebrisRing Debris;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public SmokePuff Smoke;
        [Tooltip("Only needed if this isn't already a child of Hole above - if it is (the usual setup), leave this blank and it's found automatically.")]
        public FallingRubble Rubble;
    }

    /// <summary>
    /// REDESIGNED around a real-device finding (separate UCI-RE-Marker test
    /// scene): raw ZapparImageTrackingTarget tracking of the QR - position AND
    /// rotation, read directly, no Instant Tracker involved at all - lands
    /// accurately on the real QR every single scan, at any rotation. That's
    /// exactly the ground truth to trust, and directly parenting ContentWrapper
    /// under the QR's own live transform while it's actually visible is simpler
    /// and more reliable than the previous approach (seed the Instant Tracker's
    /// anchor from one reading, then fight to re-derive rotation/scale from it
    /// every frame) - which turned out not to reliably reproduce the QR's real
    /// rotation at all (real-device test: rotating the printed QR 90/180 degrees
    /// left the building's rotation completely unchanged - a genuine defect in
    /// that indirection, not something to keep patching further).
    ///
    /// So: WHILE the QR is visible, ContentWrapper is a literal child of
    /// ImageTarget's transform (see HandoffOnce) - not "seeded from" it, not
    /// "corrected toward" it, just parented under it, exactly as if it had
    /// always been authored that way (which, at rest, it effectively was - its
    /// authored local transform relative to its original parent is identity).
    /// Zappar's own tracking does all the work; nothing here fights it.
    ///
    /// The Instant Tracker (SLAM) is used for exactly the one thing it's
    /// actually needed for: ZapparImageTrackingTarget only updates its transform
    /// while the image is CURRENTLY, continuously visible (confirmed by reading
    /// the SDK source - there's no persistence built into image tracking
    /// itself), so content would freeze in place mid-air the instant someone
    /// tilts up to look at the building instead of the QR. The moment the QR is
    /// confirmed genuinely out of view (see HandOffToSlamRoutine and
    /// LossGraceFrames), its LAST known pose is captured and used to seed the
    /// Instant Tracker's anchor, and ContentWrapper is reparented onto THAT -
    /// from there, SLAM keeps it roughly in place while the QR stays out of
    /// frame. If the QR comes back into view later, HandoffOnce re-latches
    /// ContentWrapper directly onto it again, correcting any drift SLAM
    /// accumulated in between - for free, using a source already confirmed
    /// accurate, instead of the removed secondary-facade-target mechanism that
    /// tried to do the same thing with a much less reliable signal (see git
    /// history around "secondary re-anchor"/"reanchor" commits if that
    /// reasoning is ever needed again).
    ///
    /// The instant tracker only supports seeding position precisely, plus one of
    /// a few coarse fixed rotation modes (WORLD, MINUS_Z_AWAY_FROM_USER, etc.) -
    /// not the QR's exact detected rotation, which is why the SLAM-fallback
    /// phase still needs a continuous correction: ContentWrapper's rotation and
    /// scale are re-locked to the QR's own last-known values EVERY FRAME (see
    /// LateUpdate/ApplyLockedTransform) rather than trusting the Instant
    /// Tracker's own coarse/drifting values for either. Position is never
    /// touched there - letting it update freely from SLAM as the camera moves
    /// is the entire reason the Instant Tracker phase exists at all.
    ///
    /// STILL OPEN, investigated with Unity closed/no device to test on (this
    /// feature is explicitly undocumented/unsupported in Editor PlayMode per
    /// Zappar's own docs, so static code review was the only available tool):
    /// "walking towards the building doesn't get me closer / walking around it
    /// doesn't let me see around it - it stays at the same distance and screen
    /// position no matter how I move," while the SLAM fallback is active. Ruled
    /// out previously, each with a concrete reason: AnchorOrigin pointing at the
    /// wrong tracking target (checked the serialized scene - correctly
    /// references InstantTarget); a stray ResetTrackerAnchor() call (grepped the
    /// project - never called); OnSeenEvent re-firing every frame while visible
    /// (confirmed it only fires on the not-visible-to-visible edge); a stale SDK
    /// version (checked - current); a separate "World Tracking" component (the
    /// installed package has no such class). Current best-supported (NOT
    /// confirmed) theory: a monocular-SLAM tracking-quality limitation, not a
    /// code bug - Zappar's own docs want "a relatively dense set of features...
    /// on the horizontal placement surface," and walking straight toward an
    /// anchor is a genuinely hard motion for a single camera to resolve depth
    /// from. TrackingDebugOverlay's "Cam moved"/"Anchor moved"/"Dist" odometers
    /// exist to test this theory on the next real-device session.
    /// </summary>
    public class HandoffToInstantTracking : MonoBehaviour
    {
        public ZapparImageTrackingTarget ImageTarget;
        public ZapparInstantTrackingTarget InstantTarget;

        [Tooltip("Everything that should stay anchored - hidden until first seen. While the QR is visible this is a live child of ImageTarget's transform; while it isn't, a child of InstantTarget's (SLAM), with rotation/scale continuously re-locked to the QR's last known values.")]
        public Transform ContentWrapper;

        [Header("Burst stagger")]
        [Tooltip("Each burst point (see Pairs below) or unpaired tentacle/hole/debris waits its own random delay (seconds) in this range before triggering, instead of all bursting in the same frame - purely a start-time offset, doesn't touch any of their own grow/open timings. Set both to 0 to burst everything at once.")]
        public float MinStartDelay = 0f;
        public float MaxStartDelay = 1.2f;
        [Tooltip("Minimum time (seconds) between any two of the RANDOM (non-hero) burst points' own start delays - without this, each one draws Random.Range(Min Start Delay, Max Start Delay) completely independently (see GenerateSpacedDelays), and pure chance can land two of them close enough together to read as one simultaneous double-burst instead of a staggered sequence. Enforced by drawing all of them together as one batch, sorting, then pushing later ones forward as needed - which one of these components/pairs actually gets which time slot is still randomised afterward, only the GAPS between times are guaranteed. Can push some delays past Max Start Delay if there isn't room for this many burst points at this spacing - widen Max Start Delay, or lower this, if that's not wanted. 0 = old behaviour, no minimum enforced.")]
        public float MinDelayBetweenBursts = 0f;
        [Tooltip("Head start (seconds before Min Start Delay) for whichever TentacleController has IsHero checked - guarantees it starts strictly BEFORE every other burst point's random delay (which can never go below Min Start Delay), instead of just happening to roll an early number. Clamped so the hero's own delay never goes below 0. Works whether the hero is inside a Pairs entry or an unpaired tentacle. Only one tentacle should be marked hero; if more than one is, they'll all fire together at this same delay.")]
        public float HeroExtraDelay = 0.6f;
        [Tooltip("Pair each tentacle with its own hole so they burst together on one shared delay - debris doesn't need its own slot if it's already a child of the hole, that's found automatically. Anything under ContentWrapper NOT listed here (e.g. a tentacle with no hole yet) still fires on its own independent random delay, so nothing is silently skipped.")]
        public TentaclePair[] Pairs;

        [Header("Testing / capture only - leave off for the real build")]
        [Tooltip("Skips waiting for the QR and the real instant-tracker handoff entirely, and just reveals ContentWrapper as-authored shortly after Play starts - for recording/screenshotting the burst in-editor without needing a working camera/tracking pipeline. Never enable this on a build meant for actual use.")]
        public bool DebugAutoReveal = false;
        public float DebugAutoRevealDelay = 1f;

        private bool _handedOff;

        /// <summary>
        /// The QR's last known real-world rotation - captured directly from
        /// ImageTarget.transform.rotation, either continuously (while
        /// _followingQrLive) or once, at the moment visibility was lost, as the
        /// SLAM-fallback phase's standing "correct" value. Re-applied to
        /// ContentWrapper EVERY FRAME during the SLAM phase (see LateUpdate) -
        /// while following the QR live, Unity's own parenting already keeps
        /// ContentWrapper's rotation correct with zero extra code, so this field
        /// only matters once _followingQrLive goes false.
        /// </summary>
        private Quaternion _lockedWorldRotation = Quaternion.identity;

        /// <summary>
        /// True while ContentWrapper is a live child of ImageTarget's transform
        /// (the QR is visible, or was until fewer than LossGraceFrames frames
        /// ago) - false once it's been hand off to the Instant Tracker/SLAM
        /// fallback (see HandOffToSlamRoutine). Exposed for TrackingDebugOverlay.
        /// </summary>
        public bool IsFollowingQrLive { get; private set; }

        /// <summary>
        /// True once the FIRST handoff has completed (content revealed, the
        /// instant the QR is first seen - no settling delay needed anymore,
        /// since directly following the QR's own live transform is trusted
        /// immediately). Exposed for TrackingDebugOverlay - "waiting for the QR"
        /// and "tracking active" are genuinely different states worth telling
        /// apart on screen.
        /// </summary>
        public bool HasHandedOff => _handedOff;

        /// <summary>
        /// How many times ContentWrapper has been handed off to the Instant
        /// Tracker/SLAM fallback (i.e. how many times the QR was lost after
        /// being found) - see HandOffToSlamRoutine. Exposed for
        /// TrackingDebugOverlay: in ordinary use this should be 0 or 1 per
        /// session (found the QR once, walked away once) - a climbing count
        /// means the QR is being lost and re-found repeatedly.
        /// </summary>
        public int ResetCount { get; private set; }

        private void Awake()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(false);
            DisableAllChildScripts();
        }

        /// <summary>
        /// Tracks whether the QR is CURRENTLY visible - Update() watches this to
        /// decide when a loss has lasted long enough (LossGraceFrames) to be
        /// genuine rather than a momentary detection blip, before actually
        /// committing to the SLAM handoff.
        /// </summary>
        private bool _qrVisible;
        private int _notVisibleStreak;
        private Coroutine _slamHandoffCoroutine;

        /// <summary>Consecutive not-visible frames tolerated as a detection blip before actually committing to the SLAM handoff (see Update/HandOffToSlamRoutine) - avoids reacting to a single marginal/borderline not-seen frame as if the QR were genuinely gone. Untested constant - tune from an actual device if the handoff feels too eager or too sluggish.</summary>
        public const int LossGraceFrames = 8;

        private void OnEnable()
        {
            if (ImageTarget == null) return;
            // OnSeenEvent is deliberately NOT subscribed here - it stays
            // Inspector-wired directly to HandoffOnce (see that method's own
            // doc), so there's exactly one listener on it, not two racing for
            // order (an earlier version added a second runtime listener here
            // purely to track _qrVisible, and a real-device test found that
            // could run BEFORE the Inspector-wired one on the very first
            // detection, since UnityEvent doesn't guarantee persistent listeners
            // run after runtime-added ones - HandoffOnce now sets _qrVisible
            // itself instead).
            ImageTarget.OnNotSeenEvent.AddListener(HandleQrNotSeen);
        }

        private void OnDisable()
        {
            if (ImageTarget == null) return;
            ImageTarget.OnNotSeenEvent.RemoveListener(HandleQrNotSeen);
        }

        private void HandleQrNotSeen() => _qrVisible = false;

        /// <summary>
        /// Not-visible streak/grace check - see LossGraceFrames's own doc. Only
        /// does anything while _followingQrLive (nothing to debounce once
        /// already on the SLAM fallback; HandoffOnce is what re-latches).
        /// </summary>
        private void Update()
        {
            if (!IsFollowingQrLive) return;

            if (_qrVisible)
            {
                _notVisibleStreak = 0;
                return;
            }

            _notVisibleStreak++;
            if (_notVisibleStreak > LossGraceFrames && _slamHandoffCoroutine == null)
                _slamHandoffCoroutine = StartCoroutine(HandOffToSlamRoutine());
        }

        /// <summary>
        /// Structural guarantee, not a checkbox to remember: every script under
        /// ContentWrapper starts disabled, so nothing with its own Start()-based
        /// self-trigger (WallHoleEffect/DebrisRing's OpenOnStart, or any leftover or
        /// future test-bootstrap script like TentacleGrowOnStart) can possibly run
        /// before its own pair's timer elapses - Start() simply never fires until this
        /// re-enables that exact component. Only TentacleController/WallHoleEffect/
        /// DebrisRing get re-enabled, exactly when RevealContent() is about to trigger
        /// them (see TriggerPairAfterDelay/TriggerAfterDelay) - anything else stays
        /// disabled forever, which is the point: a forgotten test script can no longer
        /// fire early no matter what its own flags say.
        ///
        /// ARShareController is explicitly exempted - it's persistent UI chrome (the
        /// Selfie/Share buttons), not gated AR content, and was never meant to be
        /// swept up here at all. The real fix is keeping it OUTSIDE ContentWrapper
        /// entirely (it doesn't need to wait for tracking either - ContentWrapper's
        /// own SetActive(false) above would still hide it if it's a child, regardless
        /// of this exclusion) - this is just a safety net in case it, or anything
        /// else meant to be persistent, ends up under ContentWrapper anyway.
        /// </summary>
        private void DisableAllChildScripts()
        {
            foreach (var mb in ContentWrapper.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && !(mb is ARShareController)) mb.enabled = false;
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

        /// <summary>
        /// Wire this to ImageTarget's OnSeenEvent (kept as the same Inspector
        /// wiring/method name this project has always used, so existing scene
        /// setup doesn't need to be redone) - runs every time the QR is
        /// (re)detected, whether that's the very first sighting or a re-latch
        /// after being lost and found again. (Re-)attaches ContentWrapper
        /// directly as a live child of ImageTarget's transform - see this
        /// class's own doc comment for why that's trusted directly rather than
        /// seeded/corrected through the Instant Tracker. Cancels a pending SLAM
        /// handoff if one was mid-grace-period (the QR came back before that
        /// commit actually happened). Only reveals content the very first time;
        /// later re-latches just correct position/rotation/scale for whatever
        /// was already visible.
        /// </summary>
        public void HandoffOnce()
        {
            bool firstTime = !_handedOff;
            _handedOff = true;
            _qrVisible = true;
            _notVisibleStreak = 0;

            if (_slamHandoffCoroutine != null)
            {
                StopCoroutine(_slamHandoffCoroutine);
                _slamHandoffCoroutine = null;
            }

            if (ContentWrapper != null && ImageTarget != null)
            {
                // worldPositionStays: false - this is a deliberate SNAP to
                // wherever the QR is actually detected right now (identity local
                // position/rotation, matching ContentWrapper's authored rest
                // position relative to its original parent), not "keep wherever
                // it currently is" - if the QR was re-found after drifting on
                // SLAM, this is exactly the correction that's wanted. Scale is
                // NOT reset to identity here - see LateUpdate/NormalizeContentScale,
                // which corrects it continuously in BOTH phases instead, since
                // trusting ImageTarget's own scale directly turned out to be a
                // real bug (see this class's own doc comment - real-device
                // testing showed content not rendering AT ALL immediately after
                // switching to this live-follow design, consistent with
                // inheriting a wildly wrong scale factor from the QR target).
                ContentWrapper.SetParent(ImageTarget.transform, false);
                ContentWrapper.localPosition = Vector3.zero;
                ContentWrapper.localRotation = Quaternion.identity;
            }
            IsFollowingQrLive = true;

            if (firstTime) RevealContent();
        }

        /// <summary>
        /// Committed only after the QR has been continuously not-visible for
        /// more than LossGraceFrames (see Update) - captures its LAST known pose
        /// (ImageTarget freezes its own transform the instant it's no longer
        /// detected, confirmed by reading the SDK source, so reading it now,
        /// mid-grace-period, is identical to reading it at the exact instant
        /// visibility was lost) and seeds the Instant Tracker's anchor there,
        /// then reparents ContentWrapper onto it (worldPositionStays: true this
        /// time - the point is NOT to move content at the moment of the switch,
        /// only to change what it's riding on going forward).
        /// </summary>
        private IEnumerator HandOffToSlamRoutine()
        {
            Vector3 cameraRelativeOffset = Z.GetPosition(ImageTarget.AnchorPoseCameraRelative());
            _lockedWorldRotation = ImageTarget.transform.rotation;

            SeedAnchorPosition(cameraRelativeOffset);
            ResetCount++;

            // Wait a frame so InstantTarget's own Update() applies the pose we
            // just seeded before ContentWrapper is reparented onto it below -
            // same reasoning this class has always used after a fresh seed.
            yield return null;

            if (ContentWrapper != null && InstantTarget != null)
                ContentWrapper.SetParent(InstantTarget.transform, true);

            IsFollowingQrLive = false;
            ApplyLockedTransform();

            Debug.Log($"[HandoffToInstantTracking] Handed off to SLAM fallback - QR out of view, locked rotation {_lockedWorldRotation.eulerAngles}.");

            _slamHandoffCoroutine = null;
        }

        /// <summary>
        /// Re-applies the standing-correct rotation (_lockedWorldRotation, last
        /// set by the QR) EVERY FRAME, not just once right after a re-seed. Why
        /// continuous, not one-shot: ZapparInstantTrackingTarget.UpdateTargetPose()
        /// re-reads the native anchor's full pose - position, rotation, AND scale
        /// - fresh from the tracking engine on EVERY SINGLE FRAME, not just at
        /// explicit re-seed moments (Z.InstantWorldTrackerAnchorPose(tracker,
        /// cameraPose, ...) is called unconditionally in its Update()). A
        /// one-shot correction right after the SLAM handoff looked right for
        /// exactly one frame, then silently drifted wrong again as ordinary
        /// per-frame tracking noise/uncertainty kept overwriting InstantTarget's
        /// transform underneath it - exactly matching a real-device report of
        /// content going invisible/sideways again despite an earlier one-shot
        /// fix seeming to work at first. Position is deliberately NOT touched
        /// here - that's meant to update continuously as the camera moves
        /// relative to the anchor, that's the entire point of world tracking;
        /// only rotation, which should never change for a real static building,
        /// gets continuously re-locked. Scale is handled separately, in
        /// LateUpdate below, in BOTH phases - see NormalizeContentScale.
        ///
        /// Skipped entirely while IsFollowingQrLive - ContentWrapper's rotation
        /// is a direct child of ImageTarget's transform then, so Unity's own
        /// parenting already keeps it correctly oriented with zero extra code;
        /// this correction only matters once it's riding on the Instant Tracker.
        /// </summary>
        private void LateUpdate()
        {
            if (!_handedOff) return;

            // Never trust whichever transform ContentWrapper is CURRENTLY
            // parented under for real-world SCALE, in EITHER phase - see
            // NormalizeContentScale's own doc comment. Originally this only ran
            // during the SLAM-fallback phase (InstantTarget's own SLAM-derived
            // scale is a known, previously-fixed source of this exact problem),
            // but a real-device test found content not rendering AT ALL right
            // after the QR was first scanned - i.e. during the LIVE phase, no
            // SLAM involved yet - which is only consistent with the QR's OWN
            // image-tracking target ALSO not supplying a reliable real-world
            // scale (an untrained/incorrect-DPI target produces exactly this,
            // the same underlying bug class already found and fixed once for a
            // different training image this session). The old QR-seeds-SLAM
            // design never had this problem because it never used the QR's
            // scale for anything at all - only its position.
            Transform reference = IsFollowingQrLive
                ? (ImageTarget != null ? ImageTarget.transform : null)
                : (InstantTarget != null ? InstantTarget.transform : null);
            NormalizeContentScale(reference);

            if (IsFollowingQrLive) return;
            ApplyLockedTransform();
        }

        private void ApplyLockedTransform()
        {
            if (ContentWrapper == null || InstantTarget == null) return;

            // Guard against a degenerate InstantTarget pose - most likely in the
            // first frame or two right after a fresh seed, before the native
            // tracker has had any real data to work with yet (a fresh anchor's
            // pose matrix can be poorly conditioned before tracking settles).
            // Was the actual cause of a real-device "no building at all"
            // regression: NaN/Infinity poisons the moment it's divided into or
            // multiplied through, and unlike ordinary bad-but-finite values,
            // NOTHING here would ever recover from it on its own - every later
            // frame's ApplyLockedTransform would keep computing NaN from NaN
            // forever, even long after the underlying tracking became fine
            // again. Skipping a bad frame instead just leaves ContentWrapper at
            // its last known-good rotation for one frame, imperceptible in
            // practice, instead of permanently breaking it.
            Quaternion instantRotation = InstantTarget.transform.rotation;
            if (!IsFinite(instantRotation))
                return;

            // ContentWrapper is a CHILD of InstantTarget (rides along with its
            // ongoing SLAM tracking), so its WORLD rotation is
            // InstantTarget.rotation * ContentWrapper.localRotation - to make
            // that equal _lockedWorldRotation, localRotation needs to be
            // Inverse(InstantTarget.rotation) * _lockedWorldRotation, NOT the
            // other order (quaternion multiplication doesn't commute - the other
            // order produces a CONJUGATION instead: same angle, wrong axis,
            // reading as content tipped onto its side - the original QR bug).
            ContentWrapper.localRotation = Quaternion.Inverse(instantRotation) * _lockedWorldRotation;
        }

        /// <summary>Same finiteness check TrackingDebugOverlay uses for its own NaN guard on this exact tracking data - a quaternion is finite iff all four components are.</summary>
        private static bool IsFinite(Quaternion q) =>
            !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w) &&
            !float.IsInfinity(q.x) && !float.IsInfinity(q.y) && !float.IsInfinity(q.z) && !float.IsInfinity(q.w);

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        /// <summary>
        /// Forces ContentWrapper's WORLD scale to exactly (1,1,1), cancelling out
        /// whichever transform it's CURRENTLY parented under (reference -
        /// InstantTarget during the SLAM fallback, ImageTarget during the live
        /// phase - see LateUpdate) - trusting either one's own scale directly is
        /// a real, previously-confirmed bug, not a hypothetical: InstantTarget's
        /// SLAM-derived scale is monocular SLAM's inherently uncertain internal
        /// estimate of the ratio between its tracking units and real metres (the
        /// same scale-ambiguity problem behind the still-open "walks meters,
        /// registers as decimetres" bug); ImageTarget's own scale depends on the
        /// QR's trained target actually having a correct real-world physical size
        /// baked in, which a real-device test found NOT to hold - content simply
        /// didn't render at all the moment ContentWrapper first inherited it
        /// directly (see this class's own doc comment). Neither is a meaningful
        /// real-world quantity worth preserving from either source - called every
        /// frame in BOTH phases, not just once after a re-seed, since a
        /// tracked transform's scale can drift moment to moment even when its
        /// position/rotation are being trusted directly. Assumes uniform,
        /// non-sheared scale throughout (true here - neither ImageTarget nor
        /// InstantTarget has a scaled parent above it).
        /// </summary>
        private void NormalizeContentScale(Transform reference)
        {
            if (ContentWrapper == null || reference == null) return;
            Vector3 s = reference.lossyScale;

            // Same reasoning as ApplyLockedTransform's rotation guard - skip a
            // degenerate frame entirely rather than let NaN/Infinity poison
            // ContentWrapper.localScale permanently (1/NaN is NaN forever after,
            // with nothing here to ever recover it).
            if (!IsFinite(s)) return;

            // A genuinely tiny-but-nonzero component (not caught by an exact-zero
            // check) is just as dangerous: 1/0.0001 = 10000, still a finite
            // number, still enough to make the building explode to an absurd
            // size (or, inverted, shrink to imperceptible) for a frame. Clamping
            // the minimum magnitude before dividing bounds how extreme a single
            // bad frame's compensation can be, the same spirit as the exact-zero
            // guard this replaced, just wide enough to actually catch what a
            // real device produced.
            const float minMagnitude = 0.05f;
            ContentWrapper.localScale = new Vector3(
                Mathf.Sign(s.x == 0f ? 1f : s.x) / Mathf.Max(Mathf.Abs(s.x), minMagnitude),
                Mathf.Sign(s.y == 0f ? 1f : s.y) / Mathf.Max(Mathf.Abs(s.y), minMagnitude),
                Mathf.Sign(s.z == 0f ? 1f : s.z) / Mathf.Max(Mathf.Abs(s.z), minMagnitude));
        }

        /// <summary>
        /// Re-seeds the instant tracker's anchor at cameraRelativeOffsetToQR (the
        /// QR's last known pose, captured by HandOffToSlamRoutine).
        /// MINUS_Z_AWAY_FROM_USER, not WORLD - see this class's own doc comment
        /// for why (confirmed against Zappar's own reference usage).
        /// </summary>
        private void SeedAnchorPosition(Vector3 cameraRelativeOffsetToQR)
        {
            Z.InstantWorldTrackerAnchorPoseSetFromCameraOffset(
                InstantTarget.InstantTracker.Value,
                cameraRelativeOffsetToQR.x, cameraRelativeOffsetToQR.y, cameraRelativeOffsetToQR.z,
                Z.InstantTrackerTransformOrientation.MINUS_Z_AWAY_FROM_USER);
            InstantTarget.PlaceTrackerAnchor();
        }

        /// <summary>
        /// Activates ContentWrapper, then starts every paired burst point (Pairs -
        /// tentacle+hole+debris+smoke+rubble together on one shared random delay) plus
        /// every remaining TentacleController/WallHoleEffect/DebrisRing/SmokePuff/
        /// FallingRubble found under it that isn't part of a pair (each on its own
        /// independent random delay), so nothing added under ContentWrapper but never
        /// wired into Pairs gets silently skipped. SetActive(true) alone isn't enough -
        /// each one waits for its own Grow()/Open() call (Unfold-style tentacles
        /// otherwise just sit static in their curled rest pose; Punch-style ones stay
        /// invisible, since they zero their own scale in Awake() until told to grow).
        ///
        /// Every non-hero burst point's random delay is drawn together as ONE batch
        /// (see GenerateSpacedDelays/MinDelayBetweenBursts) rather than each one
        /// calling Random.Range independently the instant its own StartCoroutine
        /// runs - independent draws could land close enough by pure chance to read
        /// as a simultaneous double-burst, which a shared minimum-gap batch avoids.
        /// The hero (if any) is excluded from that batch entirely - it always gets
        /// the separate guaranteed-FIRST delay from HeroStartDelay(), unaffected by
        /// this spacing.
        /// </summary>
        private void RevealContent()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(true);

            var pairedTentacles = new HashSet<TentacleController>();
            var pairedHoles = new HashSet<WallHoleEffect>();
            var pairedDebris = new HashSet<DebrisRing>();
            var pairedSmoke = new HashSet<SmokePuff>();
            var pairedRubble = new HashSet<FallingRubble>();

            // Collects every trigger that should draw from the shared spaced-random
            // batch below - hero pairs/tentacles fire immediately with their own
            // guaranteed delay instead of being added here.
            var pendingRandomTriggers = new List<System.Action<float>>();

            if (Pairs != null)
            {
                foreach (var pair in Pairs)
                {
                    DebrisRing debris = ResolveChild(pair.Debris, pair.Hole);
                    SmokePuff smoke = ResolveChild(pair.Smoke, pair.Hole);
                    FallingRubble rubble = ResolveChild(pair.Rubble, pair.Hole);
                    if (pair.Tentacle != null) pairedTentacles.Add(pair.Tentacle);
                    if (pair.Hole != null) pairedHoles.Add(pair.Hole);
                    if (debris != null) pairedDebris.Add(debris);
                    if (smoke != null) pairedSmoke.Add(smoke);
                    if (rubble != null) pairedRubble.Add(rubble);

                    if (pair.Tentacle != null && pair.Tentacle.IsHero)
                        StartCoroutine(TriggerPairAfterDelay(pair, debris, smoke, rubble, HeroStartDelay()));
                    else
                        pendingRandomTriggers.Add(delay => StartCoroutine(TriggerPairAfterDelay(pair, debris, smoke, rubble, delay)));
                }
            }

            foreach (var tentacle in ContentWrapper.GetComponentsInChildren<TentacleController>(true))
            {
                if (pairedTentacles.Contains(tentacle)) continue;
                if (tentacle.IsHero)
                    StartCoroutine(TriggerAfterDelay(() => { tentacle.enabled = true; tentacle.Grow(); }, HeroStartDelay()));
                else
                    pendingRandomTriggers.Add(delay => StartCoroutine(TriggerAfterDelay(() => { tentacle.enabled = true; tentacle.Grow(); }, delay)));
            }
            foreach (var hole in ContentWrapper.GetComponentsInChildren<WallHoleEffect>(true))
                if (!pairedHoles.Contains(hole))
                    pendingRandomTriggers.Add(delay => StartCoroutine(TriggerAfterDelay(() => { hole.enabled = true; hole.Open(); }, delay)));
            foreach (var debris in ContentWrapper.GetComponentsInChildren<DebrisRing>(true))
                if (!pairedDebris.Contains(debris))
                    pendingRandomTriggers.Add(delay => StartCoroutine(TriggerAfterDelay(() => { debris.enabled = true; debris.Open(); }, delay)));
            foreach (var smoke in ContentWrapper.GetComponentsInChildren<SmokePuff>(true))
                if (!pairedSmoke.Contains(smoke))
                    pendingRandomTriggers.Add(delay => StartCoroutine(TriggerAfterDelay(() => { smoke.enabled = true; smoke.Open(); }, delay)));
            foreach (var rubble in ContentWrapper.GetComponentsInChildren<FallingRubble>(true))
                if (!pairedRubble.Contains(rubble))
                    pendingRandomTriggers.Add(delay => StartCoroutine(TriggerAfterDelay(() => { rubble.enabled = true; rubble.Open(); }, delay)));

            float[] delays = GenerateSpacedDelays(pendingRandomTriggers.Count, MinStartDelay, MaxStartDelay, MinDelayBetweenBursts);
            for (int i = 0; i < pendingRandomTriggers.Count; i++)
                pendingRandomTriggers[i](delays[i]);
        }

        /// <summary>
        /// Draws `count` random values in [min, max], then (if minGap > 0) enforces
        /// at least minGap between every pair of them once sorted - pushing later
        /// ones forward as needed, which can exceed max if there isn't room for this
        /// many at this spacing - then shuffles the result (Fisher-Yates) so which
        /// burst point actually gets which time slot stays random; without that
        /// reshuffle, sorting would leave the array's position correlated with its
        /// value, and since callers hand these out in a fixed (hierarchy) order,
        /// that would make the FIRE ORDER deterministic (always top-of-hierarchy
        /// first) even though only the GAPS are meant to be guaranteed, not the order.
        /// </summary>
        private static float[] GenerateSpacedDelays(int count, float min, float max, float minGap)
        {
            var delays = new float[count];
            for (int i = 0; i < count; i++) delays[i] = Random.Range(min, max);
            if (minGap > 0f && count > 1)
            {
                System.Array.Sort(delays);
                for (int i = 1; i < delays.Length; i++)
                    delays[i] = Mathf.Max(delays[i], delays[i - 1] + minGap);
                for (int i = delays.Length - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (delays[i], delays[j]) = (delays[j], delays[i]);
                }
            }
            return delays;
        }

        /// <summary>
        /// The hero's guaranteed delay - MinStartDelay minus HeroExtraDelay, clamped
        /// at 0. Since every non-hero burst point's own delay is drawn from
        /// Random.Range(MinStartDelay, MaxStartDelay) and can therefore never go
        /// below MinStartDelay, subtracting a positive HeroExtraDelay from that floor
        /// guarantees the hero always starts strictly before every other burst point,
        /// rather than just happening to roll an early number. (This used to add
        /// HeroExtraDelay on top of MaxStartDelay instead, guaranteeing the hero went
        /// LAST - flipped per a later request to have the hero spawn first.)
        /// </summary>
        private float HeroStartDelay() => Mathf.Max(0f, MinStartDelay - HeroExtraDelay);

        /// <summary>Debris/Smoke/Rubble are usually authored as children of their hole (matches each script's own doc comment), so they don't need their own Pairs slot - only fall back to an explicit reference if the hole has none of that type as a child.</summary>
        private static T ResolveChild<T>(T explicitRef, WallHoleEffect hole) where T : Component
        {
            if (explicitRef != null) return explicitRef;
            return hole != null ? hole.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>One shared delay for the whole pair, then hole+tentacle+smoke+rubble together and the static debris ring a moment after - same pacing BurstSequencer uses for debris. delay is passed in already resolved by the caller (RevealContent) - either the hero's guaranteed-first HeroStartDelay(), or this pair's slot from the shared spaced-random batch (see GenerateSpacedDelays) - so non-hero pairs don't draw a delay blind to what every other burst point rolled.</summary>
        private IEnumerator TriggerPairAfterDelay(TentaclePair pair, DebrisRing debris, SmokePuff smoke, FallingRubble rubble, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (pair.Hole != null) { pair.Hole.enabled = true; pair.Hole.Open(); }
            if (pair.Tentacle != null) { pair.Tentacle.enabled = true; pair.Tentacle.Grow(); }
            // Smoke and falling rubble read as the moment of impact, so they fire
            // alongside the hole/tentacle rather than waiting like the static debris
            // ring below.
            if (smoke != null) { smoke.enabled = true; smoke.Open(); }
            if (rubble != null) { rubble.enabled = true; rubble.Open(); }

            if (debris != null)
            {
                float debrisDelay = pair.Hole != null ? pair.Hole.OpenDuration * 0.8f : 0.3f;
                yield return new WaitForSeconds(debrisDelay);
                debris.enabled = true;
                debris.Open();
            }
        }

        /// <summary>delay is passed in already resolved by the caller (RevealContent) - either the hero's guaranteed-first HeroStartDelay(), or this trigger's own slot from the shared spaced-random batch (see GenerateSpacedDelays).</summary>
        private IEnumerator TriggerAfterDelay(System.Action trigger, float delay)
        {
            yield return new WaitForSeconds(delay);
            trigger();
        }
    }
}
