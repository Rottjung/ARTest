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
    /// WHY THE QR/GROUND-PLANE ALWAYS LOOKS RIGHT BUT A ONE-SHOT SLAM SEED
    /// DIDN'T: a real-device test found the QR (and anything just directly
    /// reflecting its live transform) always lands accurately and CONSISTENTLY,
    /// scan after scan - while content seeded once from a single detection
    /// frame landed somewhere different almost every scan. Not a mystery: the
    /// QR's own transform is recalculated LIVE, continuously, every single
    /// frame it's visible - one noisy frame gets silently corrected by the next
    /// good one. A one-shot seed instead commits PERMANENTLY to whatever ONE
    /// frame's detection happened to read, and a single frame's planar-marker
    /// pose estimate is inherently noisier than a continuous stream (small flat
    /// markers like a QR code have a well-known pose ambiguity at non-head-on
    /// viewing angles).
    ///
    /// The tempting fix - just keep content permanently live-parented to the QR
    /// like the ground-plane - doesn't work for THIS project's actual physical
    /// setup: the QR sits on the ground, the content is on the building, and
    /// nobody ever has the QR in frame at the same time as the content they'd
    /// need to see. So content can only ever be shown once handed off to SLAM
    /// (which keeps working after the QR leaves view) - which means it's
    /// unavoidably a one-shot seed. The fix, then, is to give that ONE seed the
    /// SAME precision the continuously-live QR/ground-plane gets "for free": by
    /// requiring several CONSECUTIVE frames' detections to agree with each
    /// other before ever committing to a seed (see SettleAndSample) - this is
    /// literally "use image tracking's own precision to place the anchor, then
    /// hand that exact result to SLAM and never touch it again," just spread
    /// over a handful of frames instead of one, since a single frame can't be
    /// distinguished from a lucky-or-unlucky outlier on its own. A brief detour
    /// into permanently live-parenting content under the QR (see git history
    /// around "Directly follow the QR live"/"REDESIGNED"/"same mechanism the
    /// root QR/plane already uses" commits) solved consistency in principle but
    /// was pointless in practice for exactly the reason above, and reverted.
    ///
    /// SLAM (the Instant Tracker) is used for exactly the one thing image
    /// tracking genuinely can't do on its own: ZapparImageTrackingTarget only
    /// updates its transform while the image is CURRENTLY, continuously visible
    /// (confirmed by reading the SDK source), so content would freeze mid-air
    /// the instant someone tilts up from the QR to look at the building.
    /// HandoffOnce seeds it once a settled reading is available, then never
    /// reparents ContentWrapper again - it stays a child of InstantTarget for
    /// the rest of the session (re-seeding again on a later re-detection just
    /// corrects the same anchor in place, not a structural change).
    ///
    /// The instant tracker only supports seeding position precisely, plus one
    /// of a few coarse fixed rotation modes (WORLD, MINUS_Z_AWAY_FROM_USER,
    /// etc.) - not the QR's exact detected rotation, which is why ContentWrapper
    /// still needs a continuous rotation correction (see LateUpdate/
    /// ApplyLockedTransform): an earlier version got the correction's
    /// quaternion order backwards (Inverse(InstantTarget.rotation) *
    /// _lockedWorldRotation is correct - quaternion multiplication doesn't
    /// commute, so the other order produces a CONJUGATION instead: same angle,
    /// wrong axis, content tipped onto its side), and a ONE-SHOT correction
    /// (rather than every frame) isn't enough either - ZapparInstantTrackingTarget.Update()
    /// re-reads the native anchor's full pose fresh from the tracking engine
    /// every single frame, silently drifting a one-shot result wrong again
    /// within moments. Scale is handled the same way (NormalizeContentScale) -
    /// InstantTarget's own SLAM-derived scale is never trusted directly either,
    /// same reasoning, same fix. Position ALSO turned out to need this same
    /// continuous treatment (see _lockedLocalOffset's own doc comment for the
    /// concrete real-device bug this fixed) - NOT by locking position itself
    /// (InstantTarget's own position/rotation still drive it every frame,
    /// exactly as real camera movement should), but by continuously
    /// cancelling InstantTarget's scale OUT of the position calculation, the
    /// same way its scale is cancelled out of the visual size.
    ///
    /// NO MORE NATIVE CAMERA-RELATIVE MATH FOR POSITION AT ALL - the previous
    /// version of this class fed ImageTarget.AnchorPoseCameraRelative() (the
    /// QR's pose expressed via Zappar's own native camera-relative computation)
    /// into InstantWorldTrackerAnchorPoseSetFromCameraOffset, trusting that
    /// conversion to reproduce the same real-world position ImageTarget.transform
    /// already shows correctly. Investigated this properly (read Zappar's own
    /// SDK source directly - Library/PackageCache/com.zappar.uar/Runtime/*.cs -
    /// rather than guessing again) after a real-device report of "mirrored,
    /// behind me" first locks: traced Z.ConvertToUnityPose's actual matrix math
    /// (F*pose*F, F=diag(1,1,-1,1), a pure Z-axis flip converting Zappar's
    /// native camera-space convention to Unity's) and confirmed the specific
    /// pairing used WAS self-consistent (both ends skip that conversion
    /// identically) - so that particular conversion wasn't a sign-flip bug.
    /// But it was still an unnecessary detour: ImageTarget.transform.position
    /// is ALREADY the correct, Unity-composed world position (that's what
    /// makes the QR/ground-plane always line up right in the first place) -
    /// there's no reason to re-derive a separate camera-relative quantity and
    /// feed it through a completely different native subsystem (the Instant
    /// Tracker's own seeding API) when the already-correct value is sitting
    /// right there. So: ContentWrapper's world position is now set DIRECTLY
    /// from a settled ImageTarget.transform.position reading (see
    /// HandoffRoutine) - the Instant Tracker's own native seed (SeedAnchorPosition)
    /// is only used to give SLAM some starting point to track FROM, and no
    /// longer needs to be numerically accurate at all, since ContentWrapper's
    /// displayed position doesn't come from it anymore.
    ///
    /// A separate, genuine risk remains, NOT solved by any of the above: a
    /// flat marker like a QR code has a well-known SECOND, mirrored solution
    /// to "where is the camera relative to this square" that can be just as
    /// frame-to-frame stable as the correct one from certain viewing angles -
    /// consistency checking alone (SettleAndSample's check) cannot distinguish
    /// the two, since the wrong solution can hold just as steady as the right
    /// one. A gravity-up sanity check was tried (reject a reading whose "up"
    /// direction disagrees with real world-up) and immediately PULLED - a
    /// real-device test got stuck at "0/10 agreeing frames" forever right
    /// after adding it, meaning the assumption behind it (which local axis
    /// ends up "up" after Zappar's own Flat-orientation correction) was wrong,
    /// not verified against a real device before shipping. See
    /// GravityUpToleranceDegrees's own doc comment - now logged only
    /// (HandoffRoutine's lock-completed log line), not enforced, so a future
    /// session can see the real number and fix this properly instead of
    /// guessing again. Until then: scanning closer to head-on (not a steep,
    /// grazing angle) remains the only mitigation for this specific ambiguity,
    /// same conclusion reached earlier this session for the same phenomenon.
    ///
    /// SYNC CHECK (SyncPositionDelta/SyncRotationDeltaDegrees, see Update):
    /// whenever the QR happens to be visible again after the initial handoff,
    /// this compares its own live detected world transform against wherever the
    /// SLAM anchor (InstantTarget) currently is - both sit at the exact same
    /// authored world position at rest (world origin, confirmed in the
    /// serialized scene), so in a well-tracked session these should stay close
    /// together; a growing gap is a direct, quantified measurement of how far
    /// SLAM has drifted since the last seed - a much more concrete on-site
    /// diagnostic than guessing from odometers alone. Purely a diagnostic
    /// (shown on TrackingDebugOverlay) - it never feeds back into
    /// ContentWrapper's actual transform, at least for now.
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

        [Tooltip("Everything that should stay anchored - hidden until handoff, then rides along with the instant tracker's ongoing SLAM tracking, with rotation/scale continuously re-locked to the QR's last known values (see this class's own doc comment).")]
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
        /// The building's real-world rotation, as last established by a
        /// settled QR reading (see SettleAndSample) - the only source ever
        /// trusted for rotation. Re-applied to ContentWrapper EVERY FRAME in
        /// LateUpdate, not just once right after a re-seed - see that method's
        /// own doc comment for why a one-shot correction turned out not to be
        /// enough.
        /// </summary>
        private Quaternion _lockedWorldRotation = Quaternion.identity;

        /// <summary>
        /// True once the FIRST handoff has completed (content revealed).
        /// Exposed for TrackingDebugOverlay - "waiting for the QR" and "tracking
        /// active" are genuinely different states worth telling apart on screen.
        /// </summary>
        public bool HasHandedOff => _handedOff;

        /// <summary>
        /// How many times the instant-tracker anchor has been RE-seeded from a
        /// fresh, settled QR reading after the first handoff - see HandoffOnce's
        /// own doc for why this happens on every (re)detection, not just the
        /// first. Exposed for TrackingDebugOverlay: this, not raw QR visibility,
        /// is the actually meaningful diagnostic for judging tracking quality
        /// on-site - losing sight of the QR after the first handoff is
        /// normal/expected (SLAM keeps content anchored regardless), and a
        /// climbing count here just means it keeps getting re-found and
        /// re-corrected, not that anything is failing.
        /// </summary>
        public int ResetCount { get; private set; }

        /// <summary>Live progress (consecutive agreeing frames so far) toward CurrentSettleFramesRequired during the current settle attempt (see SettleAndSample) - for on-screen feedback while locking.</summary>
        public int SettleProgress { get; private set; }

        /// <summary>The ACTUAL required-frames target for whichever settle attempt is currently running (SettleFramesRequired normally, or +FirstLockExtraSettleFrames for the very first lock) - overlay should show progress against this, not the bare SettleFramesRequired constant, since the first lock's real target is larger.</summary>
        public int CurrentSettleFramesRequired { get; private set; } = SettleFramesRequired;

        /// <summary>Required consecutive agreeing frames (both position AND rotation within tolerance of the previous sample) before a reading is trusted enough to seed from - see SettleAndSample. Untested constant - tune from an actual device if locking feels too slow or too twitchy.</summary>
        public const int SettleFramesRequired = 10;

        /// <summary>Position tolerance (meters) between consecutive samples to count as "agreeing" - see SettleAndSample. Same untested-constant caveat as SettleFramesRequired.</summary>
        public const float SettlePositionTolerance = 0.12f;

        /// <summary>Rotation tolerance (degrees) between consecutive samples to count as "agreeing" - see SettleAndSample. Same untested-constant caveat as SettleFramesRequired.</summary>
        public const float SettleRotationToleranceDegrees = 3f;

        /// <summary>Closest plausible camera-to-QR distance (meters) for a settle sample to be trusted at all - see SettleAndSample's own "DISTANCE SANITY CHECK" comment. Generous on purpose (a few centimeters would mean the QR is essentially against the lens) - this exists to catch a genuinely degenerate reading, not to enforce a specific scanning distance.</summary>
        public const float MinPlausibleDistanceMeters = 0.05f;

        /// <summary>Farthest plausible camera-to-QR distance (meters) for a settle sample to be trusted at all - see SettleAndSample's own "DISTANCE SANITY CHECK" comment. Generous on purpose (well beyond any distance a QR could realistically still be read at) - this exists to catch a genuinely degenerate reading, not to enforce a specific scanning distance.</summary>
        public const float MaxPlausibleDistanceMeters = 15f;

        /// <summary>Hard cap on how long one settle attempt waits for convergence before giving up and seeding from the last sample anyway - fail OPEN, not closed: a client demo should never end up with content that never appears just because a reading never fully converged. See SettleAndSample.</summary>
        public const int SettleTimeoutFrames = 90;

        /// <summary>Extra consecutive agreeing frames required ONLY for the very first lock (added on top of SettleFramesRequired), as a hedge against a real-device finding: the first lock was sometimes mirrored/placed behind the user, "mostly" only that first time - see HandoffRoutine's own doc comment for the working theory (Zappar's underlying camera/world-tracking pipeline not yet fully stabilised in the first moment or two). Untested constant, same caveat as the other Settle* ones - cheap to widen further if the first lock is still occasionally wrong.</summary>
        public const int FirstLockExtraSettleFrames = 15;

        /// <summary>Consecutive not-visible frames tolerated as a brief detection blip DURING a settle attempt, without aborting it - see SettleAndSample.</summary>
        public const int VisibilityGraceFrames = 8;

        /// <summary>
        /// NOT ENFORCED - diagnostic only (see HandoffRoutine's lock-completed
        /// log line, "up-vector X degrees from world-up"). Was briefly a hard
        /// gate rejecting any settle reading whose "up" direction disagreed
        /// with real gravity-up by more than this, on the theory that a
        /// mirrored/flipped misread of the flat-lying QR would show up as a
        /// wrong up-vector. Pulled after a real-device test got stuck at
        /// "0/10 agreeing frames" FOREVER, immediately after this was added -
        /// consistent with the assumption behind it being wrong (which of
        /// Zappar's own post-correction local axes actually ends up "up" for
        /// a Flat-oriented target was never actually verified on a device,
        /// only reasoned about) rather than the mirrored-lock theory itself
        /// being wrong. Left as a constant + logged number so a future
        /// real-device session can read the ACTUAL angle a known-correct lock
        /// reports, and figure out the right check (probably a different axis
        /// or a very different tolerance) with real data instead of guessing
        /// again under deadline pressure.
        /// </summary>
        public const float GravityUpToleranceDegrees = 25f;

        /// <summary>
        /// Live distance (meters) between the QR's own currently-detected world
        /// position and wherever the SLAM anchor (InstantTarget) currently sits
        /// - see this class's own "SYNC CHECK" doc comment. Only updated while
        /// the QR is visible AND InstantTarget has been seeded at least once;
        /// check SyncCheckValid before trusting it (stays at its last value
        /// rather than resetting to 0, so TrackingDebugOverlay can still show
        /// "last measured drift" rather than a misleading fresh-looking 0).
        /// </summary>
        public float SyncPositionDelta { get; private set; }

        /// <summary>Angle (degrees) between the QR's own currently-detected rotation and the SLAM anchor's current rotation - see SyncPositionDelta's own doc, same validity caveat (check SyncCheckValid).</summary>
        public float SyncRotationDeltaDegrees { get; private set; }

        /// <summary>True once SyncPositionDelta/SyncRotationDeltaDegrees have been computed from at least one genuinely meaningful comparison (QR visible AND InstantTarget seeded at least once) - guards TrackingDebugOverlay from showing 0/0 as if it were a real (and suspiciously perfect) measurement before there's anything real to compare.</summary>
        public bool SyncCheckValid { get; private set; }

        /// <summary>
        /// OPTIONAL - a child of ContentWrapper holding the actual content
        /// (cube/building/tentacles/plane), one level deeper than
        /// ContentWrapper itself. If assigned, the settled position is set on
        /// THIS instead of being computed per-frame on ContentWrapper. Leave
        /// blank to fall back to the (correct, but less complete) per-frame
        /// correction in ApplyLockedTransform.
        ///
        /// WHY THIS IS THE MORE ROBUST OPTION: a real-device test found
        /// content invisible despite sane tracking numbers, traced to
        /// InstantTarget's own SCALE (monocular SLAM's known-unstable
        /// internal estimate) multiplying directly into ContentWrapper's
        /// WORLD POSITION once it had a real, non-zero local offset from
        /// InstantTarget (a child's world position in Unity is parentPos +
        /// parentRotation * (parentScale * childLocalPos) - scale genuinely
        /// affects position, not just size). The fallback re-derives
        /// ContentWrapper's local position every frame, dividing out
        /// InstantTarget's current scale so it cancels - correct, but
        /// incomplete: that same formula still multiplies by InstantTarget's
        /// CURRENT ROTATION every frame, so any rotation jitter (the exact
        /// kind ApplyLockedTransform's own rotation-lock exists to correct
        /// for CONTENT's rotation) makes the position term wobble/orbit
        /// around InstantTarget's origin too.
        ///
        /// Assigning this field fixes that completely: ContentWrapper's own
        /// world rotation and scale are ALREADY forced stable every frame
        /// (ApplyLockedTransform/NormalizeContentScale) - a child's position,
        /// set once relative to an already-stable parent, composes into a
        /// stable world position automatically, with no per-frame correction
        /// needed and no dependence on InstantTarget's raw rotation OR scale -
        /// only on ContentWrapper's own locked values, plus InstantTarget's
        /// POSITION (which really is meant to update freely as the camera
        /// moves - the entire point of SLAM). Set up in the Editor (move
        /// every existing child of ContentWrapper one level deeper, under a
        /// new empty child GameObject, and assign that GameObject here)
        /// rather than created dynamically in code, so nothing about it
        /// depends on script execution timing.
        /// </summary>
        [Tooltip("Optional - see this field's own doc comment. A child of ContentWrapper holding the actual content one level deeper; if assigned, position placement is simpler and more robust than the ApplyLockedTransform fallback used when this is left blank.")]
        public Transform ContentRoot;

        private void Awake()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(false);
            // Explicit, not just relied on via ContentWrapper's own cascading
            // active-state - a real-device test found content nested under
            // ContentRoot (itself nested under ContentWrapper) simply never
            // appearing, while the exact same content temporarily moved OUT
            // to the scene root (bypassing ContentWrapper's hide/reveal
            // entirely) showed up fine the moment its position was set. That
            // points at ContentWrapper's own SetActive(true)/RevealContent
            // path not reliably reaching a nested ContentRoot in this
            // project's actual final hierarchy - rather than chase exactly
            // why, this makes ContentRoot's own visibility independently,
            // explicitly controlled here, so it doesn't matter whether
            // cascading through ContentWrapper alone would have worked.
            if (ContentRoot != null) ContentRoot.gameObject.SetActive(false);
            DisableAllChildScripts();
        }

        /// <summary>Tracks whether the QR is CURRENTLY visible - SettleAndSample watches this to tell a brief detection blip from a genuine loss, and the sync check (Update) uses it too.</summary>
        private bool _qrVisible;

        /// <summary>Whether InstantTarget has ever been seeded - guards the sync check from comparing against a meaningless default/never-placed anchor pose before the first real handoff has happened, and lets TrackingDebugOverlay show a distinct "locking..." state while the first settle attempt is still in progress.</summary>
        public bool HasSeededAtLeastOnce => _slamSeededAtLeastOnce;
        private bool _slamSeededAtLeastOnce;

        /// <summary>The settled world position from the MOST RECENT completed lock (first or reset) - see LastLockWasReveal's own doc. Requested directly, to show on TrackingDebugOverlay for each reset, since content sometimes locking onto a plausible-but-invisible position (not a "wrong place", just never appearing at all) is otherwise impossible to diagnose from the overlay alone.</summary>
        public Vector3 LastLockedPosition { get; private set; }

        /// <summary>True if the MOST RECENT completed lock was the one that actually called RevealContent() (i.e. it was the true first lock) - false for every reset after that, since RevealContent() only ever runs once. Shown alongside LastLockedPosition so it's clear which lock, if any, was "the reveal moment".</summary>
        public bool LastLockWasReveal { get; private set; }

        /// <summary>How many completed locks (first + resets) have happened total - distinct from ResetCount (which only counts RE-locks, not the first) - shown on the overlay so "reveal never happened yet, 3 locks so far" is distinguishable from "reveal happened on lock 1".</summary>
        public int TotalLocksCompleted { get; private set; }

        private Coroutine _handoffCoroutine;
        private int _handoffGeneration;

        private void OnEnable()
        {
            if (ImageTarget == null) return;
            // OnSeenEvent is deliberately NOT subscribed here - it stays
            // Inspector-wired directly to HandoffOnce, so there's exactly one
            // listener on it (HandoffOnce sets _qrVisible itself - see its own
            // doc - rather than depending on a second listener here, avoiding
            // any dependence on UnityEvent listener ordering, which caused a
            // real bug once before - see git history around "listener-order
            // race" if that reasoning is needed again).
            ImageTarget.OnNotSeenEvent.AddListener(HandleQrNotSeen);
        }

        private void OnDisable()
        {
            if (ImageTarget == null) return;
            ImageTarget.OnNotSeenEvent.RemoveListener(HandleQrNotSeen);
        }

        /// <summary>
        /// Set the instant the QR is confirmed not-visible - used by
        /// HandoffOnce to require a genuine "left, then came back" cycle before
        /// allowing a re-lock, rather than recalibrating on every re-detection.
        /// Requested directly after real-device testing showed repeated
        /// re-locks (from marginal/flickery detection, not a deliberate
        /// look-away) each risking a fresh, independently-noisy result -
        /// recalibrating only on a deliberate return is both what was asked for
        /// and a cheap way to cut down how often the anchor gets touched at
        /// all once it's already good.
        /// </summary>
        private bool _hasLeftSinceLastLock;

        private void HandleQrNotSeen()
        {
            _qrVisible = false;
            _hasLeftSinceLastLock = true;
        }

        /// <summary>
        /// <summary>
        /// True once InstantTarget has been placed (PlaceTrackerAnchor called)
        /// for the FIRST time, regardless of whether that happened via a real
        /// QR lock or the early, QR-independent placement below. Distinct from
        /// _slamSeededAtLeastOnce, which specifically means "a real, QR-based
        /// lock has completed" - kept separate so the diagnostics/sync-check
        /// still mean what their names say.
        /// </summary>
        private bool _earlyAnchorPlaced;

        /// <summary>
        /// SYNC CHECK - see this class's own doc comment. Only meaningful while
        /// the QR is genuinely visible (ImageTarget freezes its own transform
        /// otherwise, which would make a stale comparison look like a real,
        /// possibly-misleading measurement) and only once InstantTarget has
        /// been seeded at least once (nothing to compare against before that).
        /// </summary>
        private void Update()
        {
            // EARLY ANCHOR PLACEMENT - fixes the actual root cause of "first
            // lock specifically comes out wrong". Verified by reading Zappar's
            // own SDK source directly (ZapparBaseCamera.UpdatePose): this
            // scene's ZapparCamera.AnchorOrigin is InstantTarget (correctly -
            // see this class's own doc comment for why it must NOT be the QR
            // image target instead), which means the CAMERA's own Unity pose
            // is computed EVERY FRAME from
            // Z.PipelineCameraPoseWithOrigin(pipeline, InstantTarget.AnchorPoseCameraRelative()) -
            // i.e. the camera's pose is derived FROM the anchor, not the other
            // way around. Before InstantTarget is ever placed
            // (PlaceTrackerAnchor), ZapparInstantTrackingTarget's own
            // pre-placement loop continuously re-pins it to "3m in front of
            // wherever the camera currently is" (confirmed in its own source)
            // - a purely circular relationship (anchor = f(camera), camera =
            // f(anchor)) that isn't tracking anything real. Since
            // ImageTarget.transform (what SettleAndSample samples) is ITSELF
            // computed from that same camera pose, EVERY reading taken before
            // InstantTarget's first-ever placement is measured against a
            // meaningless reference frame - and because that circular
            // relationship is very self-consistent frame to frame (nothing is
            // really being tracked, so there's no jitter), SettleAndSample's
            // own consistency check actually converges on it FASTER than a
            // genuine reading, not slower - explaining "first lock specifically
            // wrong" as a near-structural certainty, not bad luck. The instant
            // any lock calls PlaceTrackerAnchor (however wrong its position
            // turned out), the anchor switches to real SLAM tracking, the
            // camera's pose starts reflecting genuine device movement, and
            // every SUBSEQUENT reading is real - matching "works after at
            // least one reset" exactly.
            //
            // Fix: place InstantTarget immediately, independent of the QR
            // ever being scanned, so this dead window happens before anyone
            // ever scans anything rather than during the one scan that
            // matters most. The seed value is irrelevant (see
            // SeedAnchorPosition's own doc - never used for content placement)
            // - this exists ONLY to break the circular dependency as early as
            // technically possible.
            if (!_earlyAnchorPlaced && InstantTarget != null && InstantTarget.InstantTracker.HasValue)
            {
                SeedAnchorPosition(DefaultAnchorSeedOffset);
                _earlyAnchorPlaced = true;
            }

            if (!_qrVisible || !_slamSeededAtLeastOnce || ImageTarget == null || InstantTarget == null) return;

            Vector3 qrPos = ImageTarget.transform.position;
            Quaternion qrRot = ImageTarget.transform.rotation;
            Vector3 anchorPos = InstantTarget.transform.position;
            Quaternion anchorRot = InstantTarget.transform.rotation;
            if (!IsFinite(qrPos) || !IsFinite(anchorPos) || !IsFinite(qrRot) || !IsFinite(anchorRot))
                return;

            SyncPositionDelta = Vector3.Distance(qrPos, anchorPos);
            SyncRotationDeltaDegrees = Quaternion.Angle(qrRot, anchorRot);
            SyncCheckValid = true;
        }

        /// <summary>
        /// Structural guarantee, not a checkbox to remember: every script under
        /// ContentWrapper starts disabled, so nothing with its own Start()-based
        /// self-trigger (WallHoleEffect/DebrisRing's OpenOnStart, or any leftover or
        /// future test-bootstrap script like TentacleGrowOnStart) can possibly run
        /// before its own pair's timer elapses - Start() simply never fires until this
        /// re-enables that exact component. Only TentacleController/WallHoleEffect/
        /// DebrisRing get re-enabled, exactly when RevealContent() is about to trigger
        /// them (immediately if unpaired, or via TriggerPairAfterDelay if part of a
        /// Pairs entry) - anything else stays
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
        /// Wire this to ImageTarget's OnSeenEvent - the FIRST call always
        /// reveals content and locks the anchor. After that, per direct
        /// request, a re-lock only happens if the QR has genuinely gone
        /// not-visible and come back since the last lock (_hasLeftSinceLastLock)
        /// - repeated OnSeenEvent firings from the SAME continuous viewing
        /// (marginal/flickery detection re-triggering it, not a deliberate
        /// look-away) are ignored instead of recalibrating from scratch each
        /// time, which only risked introducing a fresh, independently-noisy
        /// result for no reason. A generation counter still guards against
        /// overlapping attempts: if the QR is glimpsed, lost, and re-seen again
        /// before the first attempt finished settling, the stale coroutine is
        /// stopped and a fresh one takes over rather than both racing to seed
        /// the anchor.
        /// </summary>
        public void HandoffOnce()
        {
            bool firstTime = !_handedOff;
            if (!firstTime && !_hasLeftSinceLastLock) return;

            _handedOff = true;
            _qrVisible = true;
            if (_handoffCoroutine != null) StopCoroutine(_handoffCoroutine);
            int generation = ++_handoffGeneration;
            _handoffCoroutine = StartCoroutine(HandoffRoutine(firstTime, generation));
        }

        private IEnumerator HandoffRoutine(bool firstTime, int generation)
        {
            // Real-device testing found the FIRST-EVER lock specifically was
            // sometimes mirrored/placed behind the user - the working theory
            // (not fully confirmed, but the only thing that explains "mostly
            // just the first lock") is that Zappar's own underlying camera/
            // world-tracking pipeline hasn't fully stabilised in the first
            // moment or two after the page loads, which a QR reading can look
            // internally consistent against (agreeing with itself frame to
            // frame) while still being wrong relative to a not-yet-settled
            // world frame. Cheap, low-risk hedge: require extra settle frames
            // for the first lock only, giving that pipeline a bit more time to
            // stabilise before anything is ever trusted. If a first lock still
            // looks wrong on-site, the fix is simply to move the QR out of
            // frame and rescan - the new _hasLeftSinceLastLock gate above
            // means that will now properly trigger a full recalibration.
            int requiredFrames = firstTime ? SettleFramesRequired + FirstLockExtraSettleFrames : SettleFramesRequired;
            CurrentSettleFramesRequired = requiredFrames;

            Vector3? settledPos = null;
            Quaternion settledRot = Quaternion.identity;
            yield return SettleAndSample(generation, requiredFrames, (p, r) => { settledPos = p; settledRot = r; });

            // Superseded by a newer HandoffOnce call while this one was still
            // settling, or the QR dropped out of view before ever converging -
            // either way, this attempt just quietly stops.
            if (generation != _handoffGeneration || settledPos == null) yield break;

            if (!firstTime) ResetCount++;
            _lockedWorldRotation = settledRot;

            // Give the Instant Tracker SOME starting point so its SLAM tracking
            // begins - the exact value no longer matters for where content
            // ends up displayed (see below), only that it's a sane, bounded
            // placement (matching the SDK's own pre-placement default) rather
            // than depending on whatever arbitrary offset happened to be
            // active. See SeedAnchorPosition's own doc comment for why this
            // changed from "the seed IS the placement" to "the seed is just a
            // SLAM starting point."
            SeedAnchorPosition(DefaultAnchorSeedOffset);
            _slamSeededAtLeastOnce = true;
            _hasLeftSinceLastLock = false;

            // Wait a frame so InstantTarget's own Update() applies the pose we
            // just seeded before we read its transform below.
            yield return null;

            // THE ACTUAL PLACEMENT - two ways, see ContentRoot's own doc
            // comment for why the first is more robust:
            //  - If ContentRoot is assigned (a child of ContentWrapper holding
            //    the real content, one level deeper): stabilise ContentWrapper
            //    first (rotation/scale, below), THEN set ContentRoot's WORLD
            //    position once. Since ContentWrapper's own rotation/scale are
            //    already locked stable by that point, this one-time
            //    assignment composes into a permanently-stable world position
            //    with no further correction ever needed.
            //  - Otherwise (fallback): capture the real metric offset from
            //    InstantTarget to the settled position, in InstantTarget's
            //    rotation frame, UNSCALED - ApplyLockedTransform re-derives
            //    ContentWrapper's own local position from this every frame,
            //    cancelling InstantTarget's scale (but not its rotation
            //    jitter - see ContentRoot's own doc for the gap this leaves).
            if (ContentRoot == null && InstantTarget != null)
            {
                _lockedLocalOffset = Quaternion.Inverse(InstantTarget.transform.rotation) *
                    (settledPos.Value - InstantTarget.transform.position);
            }

            ApplyLockedTransform();
            NormalizeContentScale();

            if (ContentRoot != null)
            {
                ContentRoot.position = settledPos.Value;
                // Explicit, every lock (not just firstTime) - see Awake's own
                // comment on ContentRoot's SetActive(false) for why this isn't
                // just left to ContentWrapper's own cascading active-state.
                // Harmless no-op if already active.
                ContentRoot.gameObject.SetActive(true);
            }

            // Exposed for TrackingDebugOverlay - see each property's own doc
            // comment. Requested directly so a live on-site session can watch
            // the position for each reset without needing a console.
            LastLockedPosition = settledPos.Value;
            LastLockWasReveal = firstTime;
            TotalLocksCompleted++;

            // Diagnostic only, never gates anything - see GravityUpToleranceDegrees's
            // own doc comment for why this is logged instead of enforced.
            float upAngle = Vector3.Angle(settledRot * Vector3.up, Vector3.up);
            Debug.Log($"[HandoffToInstantTracking] {(firstTime ? "Initial lock" : "Re-seeded")} - world position {settledPos.Value}, rotation {settledRot.eulerAngles} (settled after {SettleProgress}/{requiredFrames} agreeing frames, up-vector {upAngle:F0} degrees from world-up).");

            if (firstTime) RevealContent();
        }

        /// <summary>
        /// Samples ImageTarget.transform.position AND .rotation - the QR's
        /// already-correct, Unity-composed world transform, the same values
        /// that make the ground marker/plane always line up right - every
        /// frame the QR is visible, and
        /// waits for requiredFrames CONSECUTIVE samples to all agree with
        /// the previous one (position within SettlePositionTolerance, rotation
        /// within SettleRotationToleranceDegrees) before calling onSettled with
        /// the averaged position and the latest agreeing rotation - see this
        /// class's own doc comment for why the very first detection frame isn't
        /// trusted directly: this is what gives a one-shot SLAM seed the same
        /// precision the continuously-live QR/ground-plane gets for free, by
        /// requiring several independent readings to actually agree before
        /// ever committing. requiredFrames is normally SettleFramesRequired,
        /// but HandoffRoutine passes a larger value for the very first lock
        /// (see FirstLockExtraSettleFrames's own doc).
        ///
        /// A brief not-visible blip (up to VisibilityGraceFrames) is tolerated
        /// WITHOUT resetting progress or sampling that frame - ImageTarget only
        /// updates its transform while currently detected, so a frame where
        /// it's momentarily not-seen would otherwise either have to be skipped
        /// (safe, what this does) or would silently re-sample the same frozen
        /// stale pose (NOT safe - would falsely count as "agreeing" with
        /// itself). Only a SUSTAINED loss (longer than VisibilityGraceFrames)
        /// actually aborts (yield break) - along with a newer HandoffOnce call
        /// superseding this one (generation check). Falls back to seeding from
        /// whatever the last sample was after SettleTimeoutFrames (scaled up
        /// proportionally to requiredFrames, so the first lock's larger
        /// requirement gets a correspondingly longer timeout) rather than
        /// risking content that never appears at all (fail open, not closed).
        /// </summary>
        private IEnumerator SettleAndSample(int generation, int requiredFrames, System.Action<Vector3, Quaternion> onSettled)
        {
            Vector3 sum = Vector3.zero;
            Vector3 lastPos = Vector3.zero;
            Quaternion lastRot = Quaternion.identity;
            bool haveLast = false;
            int notVisibleStreak = 0;
            SettleProgress = 0;
            int timeoutFrames = SettleTimeoutFrames * requiredFrames / SettleFramesRequired;

            for (int frame = 0; frame < timeoutFrames; frame++)
            {
                if (generation != _handoffGeneration) yield break;

                if (!_qrVisible)
                {
                    notVisibleStreak++;
                    if (notVisibleStreak > VisibilityGraceFrames) yield break; // genuinely lost, not just a blip
                    yield return null;
                    continue; // don't sample or touch progress during a brief blip
                }
                notVisibleStreak = 0;

                Vector3 pos = ImageTarget.transform.position;
                Quaternion rot = ImageTarget.transform.rotation;
                if (!IsFinite(pos) || !IsFinite(rot))
                {
                    // Same reasoning as ApplyLockedTransform's NaN guard - skip
                    // a degenerate frame rather than let it poison the running
                    // agreement check.
                    haveLast = false;
                    SettleProgress = 0;
                    sum = Vector3.zero;
                    yield return null;
                    continue;
                }

                // DISTANCE SANITY CHECK: real-device testing found content
                // sometimes simply invisible after a lock (or a reset),
                // regardless of how still the camera was held - traced to
                // ContentRoot's own reveal (RevealContent) only ever running
                // ONCE, on the true first lock, while its POSITION gets reset
                // on every lock/reset independently, meaning each attempt is
                // an independent chance for a bad reading to lock in. Frame-
                // to-frame agreement alone can't catch a stable-but-wrong
                // reading (the known planar-marker pose ambiguity - see this
                // class's own doc comment). Unlike the earlier gravity-up
                // check (removed after it broke everything - it depended on
                // an unverified assumption about which local axis is "up"),
                // this check depends on nothing but plain Euclidean distance:
                // ContentRoot's position is always set to wherever the QR
                // itself is (the building's own real-world offset is baked
                // into its mesh data, not into this position), so the camera-
                // to-QR distance should always be a normal, close-range
                // scanning distance - never a couple centimeters (content
                // effectively on top of the camera) or dozens of meters
                // (content far outside where a QR could plausibly be read at
                // all). Rejecting readings outside that range - never letting
                // them count toward SettleProgress - stops a degenerate
                // solution from ever being locked onto in the first place.
                Vector3 camPos = ZapparCamera.Instance != null ? ZapparCamera.Instance.transform.position : Vector3.zero;
                float distFromCamera = Vector3.Distance(pos, camPos);
                if (distFromCamera < MinPlausibleDistanceMeters || distFromCamera > MaxPlausibleDistanceMeters)
                {
                    haveLast = false;
                    SettleProgress = 0;
                    sum = Vector3.zero;
                    yield return null;
                    continue;
                }

                bool agrees = haveLast &&
                    Vector3.Distance(pos, lastPos) <= SettlePositionTolerance &&
                    Quaternion.Angle(rot, lastRot) <= SettleRotationToleranceDegrees;

                if (agrees)
                {
                    SettleProgress++;
                    sum += pos;
                }
                else
                {
                    SettleProgress = 1;
                    sum = pos;
                }
                lastPos = pos;
                lastRot = rot;
                haveLast = true;

                if (SettleProgress >= requiredFrames)
                {
                    onSettled(sum / SettleProgress, rot);
                    yield break;
                }

                yield return null;
            }

            // Timed out without converging - seed from the last sample anyway,
            // see SettleTimeoutFrames's own doc comment for why.
            if (haveLast) onSettled(lastPos, lastRot);
        }

        /// <summary>
        /// Re-applies the standing-correct rotation (_lockedWorldRotation, last
        /// set by a settled QR reading) and re-derives ContentWrapper's local
        /// position from the locked REAL metric offset (_lockedLocalOffset),
        /// EVERY FRAME, not just once right after a re-seed. Why continuous,
        /// not one-shot: ZapparInstantTrackingTarget.UpdateTargetPose()
        /// re-reads the native anchor's full pose - position, rotation, AND
        /// scale - fresh from the tracking engine on EVERY SINGLE FRAME, not
        /// just at explicit re-seed moments. A one-shot correction right after
        /// the handoff looked right for exactly one frame, then silently
        /// drifted wrong again as ordinary per-frame tracking noise/uncertainty
        /// kept overwriting InstantTarget's transform underneath it - true for
        /// rotation from early on, and (found later, via a real-device test)
        /// just as true for position once it stopped being authored as an
        /// exact-zero local offset (see _lockedLocalOffset's own doc comment
        /// for the concrete bug this caused). Real camera-relative movement
        /// still comes through correctly - InstantTarget's own POSITION and
        /// ROTATION (as opposed to its scale) are trusted every frame, exactly
        /// as before; only the scale term is cancelled out of the position
        /// calculation, not the position tracking itself.
        /// </summary>
        private void LateUpdate()
        {
            if (!_handedOff) return;
            ApplyLockedTransform();
            NormalizeContentScale();
        }

        /// <summary>
        /// The REAL metric offset (meters) from InstantTarget's origin to
        /// ContentWrapper's settled world position, captured once at lock time
        /// in InstantTarget's rotation frame AT THAT MOMENT, deliberately NOT
        /// scaled by InstantTarget's lossyScale at all. Re-divided by
        /// InstantTarget's CURRENT lossyScale every frame in ApplyLockedTransform
        /// to produce ContentWrapper.localPosition - this cancels out
        /// InstantTarget's scale from the position calculation the same way
        /// NormalizeContentScale already cancels it from the visual size.
        ///
        /// REAL BUG FOUND AND FIXED HERE: the previous version set
        /// ContentWrapper.position directly, ONCE, via a plain Transform
        /// assignment - Unity computed SOME localPosition from that, given
        /// InstantTarget's scale AT THAT INSTANT, and left it fixed from then
        /// on. But a child's WORLD position in Unity is parentPos +
        /// parentRotation * (parentScale * childLocalPos) - so on every LATER
        /// frame, as InstantTarget's own scale (monocular SLAM's inherently
        /// unstable internal estimate - the same ambiguity NormalizeContentScale
        /// exists to cancel for visual size) drifted to some DIFFERENT value,
        /// ContentWrapper's actual WORLD POSITION drifted right along with it -
        /// not just its size. This was invisible with the ORIGINAL authored
        /// setup (ContentWrapper's local offset was exactly zero, and
        /// anything times zero is still zero, so scale drift never mattered
        /// for position) - but the moment content got a real, non-zero
        /// position set directly (this class's whole redesign, per direct
        /// request, away from native camera-relative seeding), that
        /// protection disappeared, and a real-device test found exactly this:
        /// tracking reported sane numbers (a believable Dist/Cam-moved on the
        /// overlay) while content was simply nowhere to be seen once the
        /// scale had drifted far enough. Fixed by never leaving
        /// ContentWrapper's local position as a fixed, stale value - it's
        /// re-derived from the locked REAL offset every frame instead,
        /// exactly like rotation already was.
        /// </summary>
        private Vector3 _lockedLocalOffset = Vector3.zero;

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
            // its last known-good rotation/position for one frame, imperceptible
            // in practice, instead of permanently breaking it.
            Quaternion instantRotation = InstantTarget.transform.rotation;
            Vector3 instantScale = InstantTarget.transform.lossyScale;
            if (!IsFinite(instantRotation) || !IsFinite(instantScale))
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

            // Only needed as a FALLBACK when ContentRoot isn't assigned - see
            // that field's own doc comment. When it IS assigned, the settled
            // position lives there instead (set once in HandoffRoutine,
            // composing correctly on its own against ContentWrapper's
            // already-stable rotation/scale), and this block would just be
            // redundant, less-complete work.
            if (ContentRoot == null)
            {
                // Dividing the locked REAL metric offset by InstantTarget's
                // CURRENT scale here exactly cancels the multiplication Unity
                // applies when computing world position, so the scale term
                // drops out entirely and ContentWrapper's world position
                // tracks ONLY InstantTarget's position/rotation (real camera-
                // relative movement) - never its scale noise. Same minimum-
                // magnitude clamp as NormalizeContentScale, same reason:
                // bounds how extreme a single near-zero-scale frame's
                // compensation can be. Does NOT protect against InstantTarget's
                // own ROTATION jitter the same way - see ContentRoot's own
                // doc comment for why assigning that field is more complete.
                const float minMagnitude = 0.05f;
                ContentWrapper.localPosition = new Vector3(
                    _lockedLocalOffset.x / (Mathf.Sign(instantScale.x == 0f ? 1f : instantScale.x) * Mathf.Max(Mathf.Abs(instantScale.x), minMagnitude)),
                    _lockedLocalOffset.y / (Mathf.Sign(instantScale.y == 0f ? 1f : instantScale.y) * Mathf.Max(Mathf.Abs(instantScale.y), minMagnitude)),
                    _lockedLocalOffset.z / (Mathf.Sign(instantScale.z == 0f ? 1f : instantScale.z) * Mathf.Max(Mathf.Abs(instantScale.z), minMagnitude)));
            }
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
        /// (The QR's OWN scale is never used for anything, on purpose - a
        /// real-device test during a brief detour into trusting it directly
        /// found it unreliable too, see this class's own doc comment -
        /// InstantTarget's scale is the only one ever read here.) Called every
        /// frame from LateUpdate, not just once after a re-seed - see that
        /// method's own doc for why that matters. Assumes uniform, non-sheared
        /// scale throughout (true here - InstantTarget sits at scene root with
        /// no scaled parent above it).
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
        /// Zappar's default pre-placement offset for ZapparInstantTrackingTarget
        /// (confirmed by reading the SDK source directly -
        /// m_anchorOffsetFromCamera's own default) - reused here deliberately,
        /// since HandoffRoutine no longer needs this offset to be numerically
        /// ACCURATE (see SeedAnchorPosition's own doc comment for why), only a
        /// sane, bounded starting point for SLAM to begin tracking from.
        /// </summary>
        private static readonly Vector3 DefaultAnchorSeedOffset = new Vector3(0f, 0f, -3f);

        /// <summary>
        /// Places the instant tracker's anchor at cameraRelativeOffsetToQR,
        /// giving SLAM a starting point to track from. MINUS_Z_AWAY_FROM_USER,
        /// not WORLD - see this class's own doc comment for why (confirmed
        /// against Zappar's own reference usage).
        ///
        /// This USED to be where the actual content placement came from -
        /// feeding a settled QR reading through Zappar's native camera-relative
        /// seeding API. That depended on this class's own conversion math
        /// exactly matching whatever convention that native API expects, which
        /// - even after tracing through Zappar's ConvertToUnityPose to confirm
        /// the two ends were self-consistent (see this class's own "MIRRORED/
        /// BEHIND-THE-USER" doc) - was still an unnecessary detour: content's
        /// actual displayed position is now set directly, via a plain Unity
        /// world-position assignment on ContentWrapper (see HandoffRoutine),
        /// using the SAME already-guaranteed-correct math that makes the QR/
        /// ground-plane itself always line up right - no native conversion
        /// needed at all for that. This method now ONLY needs to give the
        /// Instant Tracker SOME starting point so its own SLAM tracking begins;
        /// the exact value no longer affects where content ends up.
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
        /// tentacle+hole+debris+smoke+rubble together on one shared random delay).
        /// Anything else found under ContentWrapper - a TentacleController/
        /// WallHoleEffect/DebrisRing/SmokePuff/FallingRubble that isn't part of
        /// any Pairs entry - fires IMMEDIATELY instead, no delay at all: staggering
        /// is only ever meant for the deliberately choreographed burst points
        /// listed in Pairs, not for anything left unwired. (Previously,
        /// unpaired items ALSO got their own independent random delay, on the
        /// reasoning that nothing should be silently skipped - per direct
        /// request, changed so "not in Pairs" now means "show immediately the
        /// moment tracking locks", not "show on some other random delay".)
        /// SetActive(true) alone isn't enough for any of these - each one waits
        /// for its own Grow()/Open() call (Unfold-style tentacles otherwise
        /// just sit static in their curled rest pose; Punch-style ones stay
        /// invisible, since they zero their own scale in Awake() until told to
        /// grow) - "immediately" here means that call happens right in this
        /// method, not via a delayed coroutine.
        ///
        /// Every non-hero Pairs entry's random delay is drawn together as ONE
        /// batch (see GenerateSpacedDelays/MinDelayBetweenBursts) rather than
        /// each one calling Random.Range independently the instant its own
        /// StartCoroutine runs - independent draws could land close enough by
        /// pure chance to read as a simultaneous double-burst, which a shared
        /// minimum-gap batch avoids. The hero Pairs entry (if any) is excluded
        /// from that batch entirely - it always gets the separate guaranteed-
        /// FIRST delay from HeroStartDelay(), unaffected by this spacing.
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
            // batch below - ONLY Pairs entries go through this now (see this
            // method's own doc comment) - the hero pair fires immediately with
            // its own guaranteed delay instead of being added here.
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

            // Not part of any Pairs entry - show immediately, no delay, no
            // stagger, hero or not (see this method's own doc comment).
            foreach (var tentacle in ContentWrapper.GetComponentsInChildren<TentacleController>(true))
                if (!pairedTentacles.Contains(tentacle)) { tentacle.enabled = true; tentacle.Grow(); }
            foreach (var hole in ContentWrapper.GetComponentsInChildren<WallHoleEffect>(true))
                if (!pairedHoles.Contains(hole)) { hole.enabled = true; hole.Open(); }
            foreach (var debris in ContentWrapper.GetComponentsInChildren<DebrisRing>(true))
                if (!pairedDebris.Contains(debris)) { debris.enabled = true; debris.Open(); }
            foreach (var smoke in ContentWrapper.GetComponentsInChildren<SmokePuff>(true))
                if (!pairedSmoke.Contains(smoke)) { smoke.enabled = true; smoke.Open(); }
            foreach (var rubble in ContentWrapper.GetComponentsInChildren<FallingRubble>(true))
                if (!pairedRubble.Contains(rubble)) { rubble.enabled = true; rubble.Open(); }

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

    }
}
