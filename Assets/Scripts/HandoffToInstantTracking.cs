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
    /// gets a one-time rotation correction applied right after handoff - since
    /// ContentWrapper is a CHILD of InstantTarget (that's what "rides along with the
    /// instant tracker's ongoing SLAM tracking" means structurally), this has to be
    /// Inverse(InstantTarget.rotation) * ImageTarget.rotation, the specific quaternion
    /// that makes InstantTarget.rotation * ContentWrapper.localRotation come out equal
    /// to the QR's actual detected rotation. An earlier version had the two operands
    /// the other way around, which - quaternion multiplication doesn't commute -
    /// produced a CONJUGATION of the correct answer instead: the same rotation ANGLE
    /// as the QR's real orientation, but around the WRONG AXIS (content ending up
    /// tipped onto its side), and one whose exact error depends on the specific
    /// relationship between the QR's detected rotation and the instant tracker's fixed
    /// seed rotation at that moment - which is exactly why it came out differently
    /// depending on which direction the QR was scanned from. Fixed; every other aspect
    /// of the handoff (see below) is unaffected by this, only the rotation math was.
    ///
    /// The POSITION handoff had a real bug too, found the same way (real-device
    /// testing: "scan the QR from a different angle, the building ends up in a
    /// different place"): HandoffRoutine was seeding the anchor with
    /// InstantTrackerTransformOrientation.WORLD, an unverified guess. Reading
    /// Zappar's own ZapparInstantTrackingTarget.Update() - the SDK's own reference
    /// usage of this exact function - shows every call it makes uses
    /// MINUS_Z_AWAY_FROM_USER instead, never WORLD. That parameter controls how the
    /// camera-relative offset's axes get interpreted at the moment of seeding, so
    /// the wrong one placed the anchor off by an amount that depended on which way
    /// the camera happened to be facing when the QR was detected - fixed to match
    /// the SDK's own convention.
    ///
    /// STILL OPEN as of this writing, investigated with Unity closed/no device to
    /// test on (this feature is explicitly undocumented/unsupported in Editor
    /// PlayMode per Zappar's own docs, so static code review was the only
    /// available tool): "walking towards the building doesn't get me closer /
    /// walking around it doesn't let me see around it - it stays at the same
    /// distance and screen position no matter how I move." Ruled out this
    /// session, each with a concrete reason, not just re-asserted:
    ///  - AnchorOrigin pointing at the wrong tracking target (there are two
    ///    plausible candidates - the QR ImageTrackingTarget and the Instant
    ///    Tracker - and if it pointed at the QR one, AnchorPoseCameraRelative()
    ///    returns Matrix4x4.identity the instant the QR leaves frame, which
    ///    would exactly explain "camera pose stops updating once you walk away
    ///    from the QR"). Checked the actual serialized scene data:
    ///    AnchorOrigin correctly references the ZapparInstantTrackingTarget
    ///    component, not the QR target. Confirmed correct.
    ///  - A stray ResetTrackerAnchor() call somewhere re-arming the pre-placement
    ///    per-frame reseed loop (which pins the anchor to a fixed camera-relative
    ///    offset every frame - would exactly match "stuck at a fixed distance").
    ///    Grepped the whole project: never called anywhere. Ruled out.
    ///  - HandoffOnce()/OnSeenEvent re-firing continuously and re-seeding the
    ///    anchor every frame while the QR is in view. Confirmed
    ///    ZapparImageTrackingTarget only invokes OnSeenEvent on the
    ///    not-visible-to-visible EDGE, not every frame while visible. Ruled out.
    ///  - com.zappar.uar package or the zappar-cv.js CDN version being stale.
    ///    Checked GitHub commit history (nothing SLAM/tracking-relevant since
    ///    this project's pinned commit) and cross-checked the version string
    ///    against the package's own current WebGLTemplate (identical, 2.1.9).
    ///    Ruled out.
    ///  - A separate/newer "World Tracking" Unity component distinct from
    ///    ZapparInstantTrackingTarget (a 2021 Zappar blog post uses that name).
    ///    Checked: the installed package only ships Face/Image/Instant tracking
    ///    targets - no such separate class exists in this Unity SDK. Ruled out
    ///    as a real option here.
    /// Current best-supported (NOT confirmed) theory: this is a monocular-SLAM
    /// tracking-QUALITY limitation rather than a code bug - Zappar's own docs
    /// note their (non-beta) Instant World Tracking wants "a relatively dense
    /// set of features... on the horizontal placement surface" for reliable
    /// results, and walking straight towards an anchor is a genuinely hard
    /// motion for a single camera to resolve depth from (minimal parallax)
    /// versus moving sideways. Added TrackingDebugOverlay's "Cam moved: X.XXm"
    /// odometer specifically to test this theory on the next real-device
    /// session - see that class's own doc comment for what each outcome means.
    ///
    /// A SECONDARY re-anchor source (a second image target trained on part of the
    /// real building's facade, correcting position drift continuously instead of
    /// relying on one early QR glimpse) was tried and then removed. It surfaced a
    /// chain of real bugs (re-seeding silently re-seeds rotation and scale too,
    /// not just position; a coordinate-frame mismatch between camera-local and
    /// world-space vectors) which all got fixed in turn - see git history around
    /// "secondary re-anchor"/"reanchor" commits if any of that reasoning is needed
    /// again - but it was never actually testable (it requires physically
    /// standing at the real building; a desk test with a printed copy of the
    /// training image inherently produces nonsensical position results,
    /// regardless of code correctness), so it was removed rather than shipped
    /// unverified. Back to QR-only: one accurate lock, then SLAM holds it.
    ///
    /// SEEDING FROM A SETTLED, AVERAGED READING (not the first detection frame):
    /// real-device testing found that rescanning the QR several times in a row -
    /// standing still, same QR - produced a noticeably different position AND
    /// rotation each time. That's not a bug in the correction math above (all of
    /// which only runs AFTER seeding) - it traces to WHEN the original code
    /// sampled the QR's detected pose: HandoffRoutine used to read
    /// ImageTarget.AnchorPoseCameraRelative() on the exact frame OnSeenEvent first
    /// fires, i.e. the instant the detector just barely crossed its confidence
    /// threshold - the single noisiest possible sample. Compounding that, small
    /// flat markers (a QR code is a good example) have a well-known planar-pose
    /// ambiguity: viewed at anything but head-on, the "camera relative to this
    /// square" solve can have two nearly-equally-valid answers that flip between
    /// each other on tiny viewpoint differences - matching "different result each
    /// rescan" exactly, with no code bug required to explain it. Fixed by
    /// HandoffRoutine now sampling several consecutive frames and only seeding
    /// once they agree within a tolerance (see SettleAndSample) - trading a
    /// fraction of a second of extra wait for a genuinely converged reading
    /// instead of the first noisy one. This does NOT fully eliminate the planar-
    /// pose-ambiguity risk (a print scanned at a steep angle can still converge
    /// on the wrong one of the two solutions) - printing the QR larger and
    /// scanning closer to head-on remains the physical-side mitigation for that.
    /// </summary>
    public class HandoffToInstantTracking : MonoBehaviour
    {
        public ZapparImageTrackingTarget ImageTarget;
        public ZapparInstantTrackingTarget InstantTarget;

        [Tooltip("Everything that should stay anchored - hidden until handoff, then carries a one-time rotation correction and rides along with the instant tracker's ongoing SLAM tracking.")]
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
        /// The building's real-world rotation, as last established by the QR (the
        /// only source ever trusted for rotation). Re-applied to ContentWrapper
        /// EVERY FRAME in LateUpdate, not just once right after a re-seed - see
        /// LateUpdate's own doc comment for why a one-shot correction turned out
        /// not to be enough.
        /// </summary>
        private Quaternion _lockedWorldRotation = Quaternion.identity;

        /// <summary>
        /// True from the moment HandoffOnce starts sampling until the settle loop
        /// (see SettleAndSample) either converges and seeds, or aborts because the
        /// QR dropped out of view - exposed so TrackingDebugOverlay can show a
        /// distinct "locking..." state instead of jumping straight to "tracking
        /// active" before content has actually been placed (seeding can now take
        /// up to SettleTimeoutFrames, not just a single frame, since it no longer
        /// trusts the first detection - see this class's own doc comment).
        /// </summary>
        public bool IsSettling { get; private set; }

        /// <summary>
        /// Consecutive frames the sampled QR pose has agreed within tolerance so
        /// far during the current settle attempt (see SettleAndSample) - live
        /// progress toward SettleFramesRequired, for on-screen feedback while
        /// locking. Reset to 0 whenever a fresh sample disagrees with the
        /// previous one, or a new settle attempt starts.
        /// </summary>
        public int SettleProgress { get; private set; }

        /// <summary>Required consecutive agreeing frames before a reading is trusted enough to seed from - see SettleAndSample. Untested constant (no reference usage anywhere to check against, same caveat this project's other real-device-only constants carry) - tune from an actual device if locking feels too slow or too twitchy.</summary>
        public const int SettleFramesRequired = 10;

        /// <summary>Position tolerance (meters) between consecutive samples to count as "agreeing" - see SettleAndSample. Same untested-constant caveat as SettleFramesRequired.</summary>
        public const float SettlePositionTolerance = 0.12f;

        /// <summary>Hard cap on how long one settle attempt waits for convergence before giving up and seeding from the last sample anyway - fail OPEN, not closed: a client demo should never end up with content that never appears just because a reading never fully converged. See SettleAndSample.</summary>
        public const int SettleTimeoutFrames = 90;

        /// <summary>True once content has actually been revealed (RevealContent has run) - distinct from HasHandedOff, which goes true as soon as the QR is first seen, before the settle loop has necessarily finished. See IsSettling's own doc for why TrackingDebugOverlay needs both.</summary>
        public bool ContentRevealed { get; private set; }

        /// <summary>
        /// True once the FIRST handoff has completed (content revealed). Exposed for
        /// TrackingDebugOverlay - "waiting for the QR" and "tracking active" are
        /// genuinely different states worth telling apart on screen.
        /// </summary>
        public bool HasHandedOff => _handedOff;

        /// <summary>
        /// How many times the instant-tracker anchor has been RE-seeded from a fresh
        /// QR detection after the first handoff - see HandoffOnce's own doc for why
        /// this happens at all. Exposed for TrackingDebugOverlay: this, not a raw
        /// "tracking lost" flag, is the actually meaningful number to show, since
        /// losing sight of the QR after the first handoff is normal/expected (the
        /// instant tracker's own SLAM keeps content anchored regardless), not a
        /// failure.
        /// </summary>
        public int ResetCount { get; private set; }

        private void Awake()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(false);
            DisableAllChildScripts();
        }

        /// <summary>
        /// Tracks whether the QR is CURRENTLY visible, independent of the
        /// Inspector-wired OnSeenEvent -> HandoffOnce() call - HandoffRoutine's
        /// settle-and-sample loop (see SettleAndSample) needs to know if the QR
        /// drops out of view mid-settle, so it can abort that attempt instead of
        /// quietly seeding from a stale last-known pose once ZapparImageTrackingTarget
        /// stops updating (its own Update() skips UpdateTargetPose() entirely once
        /// the image is no longer detected - confirmed by reading the SDK source -
        /// so a frozen stale reading would otherwise look identical to a
        /// genuinely-converged one).
        /// </summary>
        private bool _qrVisible;

        private void OnEnable()
        {
            if (ImageTarget == null) return;
            ImageTarget.OnSeenEvent.AddListener(HandleQrSeen);
            ImageTarget.OnNotSeenEvent.AddListener(HandleQrNotSeen);
        }

        private void OnDisable()
        {
            if (ImageTarget == null) return;
            ImageTarget.OnSeenEvent.RemoveListener(HandleQrSeen);
            ImageTarget.OnNotSeenEvent.RemoveListener(HandleQrNotSeen);
        }

        private void HandleQrSeen() => _qrVisible = true;
        private void HandleQrNotSeen() => _qrVisible = false;

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
            ContentRevealed = true;
            RevealContent();
        }

        private Coroutine _handoffCoroutine;
        private int _handoffGeneration;

        /// <summary>
        /// Wire this to ImageTarget's OnSeenEvent - despite the name (kept as-is so
        /// existing OnSeenEvent wiring in the scene doesn't need to be redone),
        /// this now runs every time the QR is (re)detected, not just the first.
        /// The FIRST call reveals content - every call, including that first one,
        /// re-seeds the instant tracker's anchor from THIS fresh detection (once
        /// it settles - see SettleAndSample), correcting any position/rotation
        /// drift the SLAM tracking may have accumulated while the QR was out of
        /// view. Content itself is never re-revealed or re-burst on a
        /// re-detection - only the anchor is refreshed, which is why
        /// RevealContent() is still gated on firstTime. A generation counter
        /// guards against overlapping attempts: if the QR is glimpsed, lost, and
        /// re-seen again before the first attempt finished settling, the stale
        /// coroutine is stopped and a fresh one takes over rather than both
        /// racing to seed the anchor.
        /// </summary>
        public void HandoffOnce()
        {
            bool firstTime = !_handedOff;
            _handedOff = true;
            if (_handoffCoroutine != null) StopCoroutine(_handoffCoroutine);
            int generation = ++_handoffGeneration;
            _handoffCoroutine = StartCoroutine(HandoffRoutine(firstTime, generation));
        }

        private IEnumerator HandoffRoutine(bool firstTime, int generation)
        {
            IsSettling = true;
            SettleProgress = 0;

            Vector3? settled = null;
            yield return SettleAndSample(generation, v => settled = v);

            IsSettling = false;

            // Superseded by a newer HandoffOnce call while this one was still
            // settling, or the QR dropped out of view before ever converging -
            // either way, this attempt just quietly stops.
            if (generation != _handoffGeneration || settled == null) yield break;

            if (!firstTime) ResetCount++;
            SeedAnchorPosition(settled.Value);

            // Wait a frame so InstantTarget's own Update() applies the pose we just
            // seeded before we read its transform below.
            yield return null;

            // The QR is the ONLY source ever trusted for rotation - record its
            // detected world rotation as the standing "correct" value.
            // LateUpdate (not this one-off assignment) is what actually keeps
            // ContentWrapper's rotation pinned to it every frame from here on -
            // see LateUpdate's own doc comment for why a one-shot correction,
            // which is all this used to do, wasn't enough.
            _lockedWorldRotation = ImageTarget.transform.rotation;
            ApplyLockedTransform();

            // Logged every lock (not just #if UNITY_EDITOR) so a real-device
            // console/remote-log during testing shows exactly what got seeded and
            // how convinced the settle loop was (SettleProgress vs
            // SettleFramesRequired) - the concrete ask behind this: measure
            // lock-to-lock drift/error across repeated scans, not just eyeball it.
            Debug.Log($"[HandoffToInstantTracking] Locked anchor - seeded position {settled.Value}, rotation {_lockedWorldRotation.eulerAngles} (settle reached {SettleProgress}/{SettleFramesRequired} agreeing frames).");

            if (firstTime)
            {
                ContentRevealed = true;
                RevealContent();
            }
        }

        /// <summary>
        /// Samples ImageTarget.AnchorPoseCameraRelative()'s position every frame
        /// and waits for SettleFramesRequired CONSECUTIVE samples to all agree
        /// within SettlePositionTolerance of each other before calling onSettled
        /// with the average of that agreeing run - see this class's own doc
        /// comment ("SEEDING FROM A SETTLED, AVERAGED READING") for why the very
        /// first detection frame isn't trusted directly. Aborts (never calls
        /// onSettled) if the QR drops out of view before converging (_qrVisible,
        /// tracked via HandleQrSeen/HandleQrNotSeen) or a newer HandoffOnce call
        /// supersedes this one (generation check). Falls back to seeding from
        /// whatever the last sample was after SettleTimeoutFrames rather than
        /// risking content that never appears at all - see that constant's own
        /// doc for why (fail open, not closed).
        /// </summary>
        private IEnumerator SettleAndSample(int generation, System.Action<Vector3> onSettled)
        {
            Vector3 sum = Vector3.zero;
            Vector3 lastSample = Vector3.zero;
            bool haveLastSample = false;

            for (int frame = 0; frame < SettleTimeoutFrames; frame++)
            {
                if (generation != _handoffGeneration || !_qrVisible) yield break;

                Vector3 sample = Z.GetPosition(ImageTarget.AnchorPoseCameraRelative());
                if (!IsFinite(sample))
                {
                    // Same reasoning as ApplyLockedTransform's NaN guard elsewhere
                    // in this class - skip a degenerate frame rather than let it
                    // poison the running agreement check.
                    haveLastSample = false;
                    SettleProgress = 0;
                    sum = Vector3.zero;
                    yield return null;
                    continue;
                }

                if (haveLastSample && Vector3.Distance(sample, lastSample) <= SettlePositionTolerance)
                {
                    SettleProgress++;
                    sum += sample;
                }
                else
                {
                    SettleProgress = 1;
                    sum = sample;
                }
                lastSample = sample;
                haveLastSample = true;

                if (SettleProgress >= SettleFramesRequired)
                {
                    onSettled(sum / SettleProgress);
                    yield break;
                }

                yield return null;
            }

            // Timed out without converging - seed from the last sample anyway,
            // see SettleTimeoutFrames's own doc comment for why.
            if (haveLastSample) onSettled(lastSample);
        }

        /// <summary>
        /// Re-applies the standing-correct rotation (_lockedWorldRotation, last
        /// set by the QR) and forces world scale back to (1,1,1), EVERY FRAME,
        /// not just once right after a re-seed. Why continuous, not one-shot:
        /// ZapparInstantTrackingTarget.UpdateTargetPose() re-reads the native
        /// anchor's full pose - position, rotation, AND scale - fresh from the
        /// tracking engine on EVERY SINGLE FRAME, not just at explicit re-seed
        /// moments (Z.InstantWorldTrackerAnchorPose(tracker, cameraPose, ...) is
        /// called unconditionally in its Update()). A one-shot correction right
        /// after HandoffRoutine looked right for exactly one frame, then silently
        /// drifted wrong again as ordinary
        /// per-frame tracking noise/uncertainty (which is what the anchor's
        /// rotation and scale actually represent - see NormalizeContentScale's
        /// own doc on why scale in particular is inherently unstable, especially
        /// in the first few frames after a fresh seed) kept overwriting
        /// InstantTarget's transform underneath it - exactly matching a
        /// real-device report of content going invisible/sideways/tiny again
        /// despite the earlier one-shot fixes seeming to work at first. Position
        /// is deliberately NOT touched here - that's meant to update continuously
        /// as the camera moves relative to the anchor, that's the entire point of
        /// world tracking; only rotation and scale, which should never change for
        /// a real static building, get continuously re-locked.
        /// </summary>
        private void LateUpdate()
        {
            if (!_handedOff) return;
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
            // its last known-good rotation/scale for one frame, imperceptible in
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
            NormalizeContentScale();
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
        /// InstantTarget.lossyScale - the native anchor pose's own scale
        /// component, which is monocular SLAM's inherently uncertain internal
        /// estimate of the ratio between its tracking units and real metres (the
        /// same scale-ambiguity problem behind the still-open "walks meters,
        /// registers as decimetres" bug), NOT a meaningful real-world quantity
        /// worth preserving or trusting - especially unstable in the first few
        /// frames after a fresh seed, before tracking has had time to settle.
        /// Called every frame from ApplyLockedTransform/LateUpdate, not just
        /// once after a re-seed - see LateUpdate's own doc for why that matters.
        /// Assumes uniform, non-sheared scale throughout (true here - InstantTarget
        /// sits at scene root with no scaled parent above it).
        /// </summary>
        private void NormalizeContentScale()
        {
            if (ContentWrapper == null || InstantTarget == null) return;
            Vector3 s = InstantTarget.transform.lossyScale;

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
        /// settled, averaged reading from SettleAndSample). MINUS_Z_AWAY_FROM_USER,
        /// not WORLD - see this class's own doc comment for why (confirmed
        /// against Zappar's own reference usage).
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
