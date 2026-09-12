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
    /// The moment the QR image target is (re)seen, seeds a ZapparInstantTrackingTarget's
    /// anchor at that same real-world position (via Zappar's own persistent
    /// 6DOF/SLAM world tracking) and hands off to it IMMEDIATELY - content is
    /// driven by SLAM from the very first detection onward, re-seeded on every
    /// subsequent (re)detection too (see HandoffOnce), not just once the QR
    /// later leaves view. This is back to the very first version of this handoff
    /// that was confirmed working well on a real device, after a detour (see git
    /// history around "Directly follow the QR live"/"REDESIGNED" commits) into
    /// instead directly parenting ContentWrapper under the QR's own live
    /// transform whenever visible - that had a real bug of its own: the QR's own
    /// detected SCALE isn't reliably 1:1 with real-world metres, so trusting it
    /// directly made the whole building render at a wildly wrong size (real-
    /// device test: content invisible, or filling the entire screen with one
    /// giant flat surface - consistent with being scaled up far too large and
    /// standing millimetres from it). Reverting to "SLAM drives everything,
    /// seeded from the QR" avoids that risk entirely: the Instant Tracker's own
    /// scale was already confirmed reliable (see NormalizeContentScale), and the
    /// QR is only ever trusted for POSITION (AnchorPoseCameraRelative) and
    /// ROTATION (ImageTarget.transform.rotation) - never scale.
    ///
    /// The instant tracker only supports seeding position precisely, plus one of a few
    /// coarse fixed rotation modes (WORLD, MINUS_Z_AWAY_FROM_USER, etc.) - not the QR's
    /// exact detected rotation. So ContentWrapper (everything that should be anchored)
    /// gets a rotation correction applied EVERY FRAME (see LateUpdate/ApplyLockedTransform),
    /// not just once right after handoff - since ContentWrapper is a CHILD of InstantTarget
    /// (that's what "rides along with the instant tracker's ongoing SLAM tracking" means
    /// structurally), this has to be Inverse(InstantTarget.rotation) * _lockedWorldRotation,
    /// the specific quaternion that makes InstantTarget.rotation * ContentWrapper.localRotation
    /// come out equal to the QR's actual detected rotation. An earlier version had the two
    /// operands the other way around, which - quaternion multiplication doesn't commute -
    /// produced a CONJUGATION of the correct answer instead: the same rotation ANGLE
    /// as the QR's real orientation, but around the WRONG AXIS (content ending up
    /// tipped onto its side). A one-shot correction (rather than every frame) isn't
    /// enough either: ZapparInstantTrackingTarget.Update() re-reads the native anchor's
    /// full pose - position, rotation, AND scale - fresh from the tracking engine every
    /// single frame, silently drifting the one-shot result wrong again within moments.
    ///
    /// Re-seeded on EVERY (re)detection of the QR, not just the first - in normal
    /// use the QR only gets glimpsed briefly (someone scans it, then looks up at
    /// the building), so re-seeding whenever it happens to be re-detected
    /// corrects whatever position/rotation drift SLAM accumulated while it was
    /// out of view - this exact mechanism is what was originally "confirmed
    /// working amazingly well" on a real device, before any of this session's
    /// investigation began.
    ///
    /// SYNC CHECK (SyncPositionDelta/SyncRotationDeltaDegrees, see Update): while
    /// the QR remains (or becomes) visible, continuously compares its own live
    /// detected world transform against wherever the SLAM anchor (InstantTarget)
    /// currently is - both sit at the exact same authored world position at rest
    /// (world origin, confirmed in the serialized scene), so in a well-tracked
    /// session these should stay close to identical; a growing gap is a direct,
    /// quantified measurement of how far SLAM has drifted from the real QR since
    /// it was last seeded - a much more concrete on-site diagnostic than
    /// guessing from odometers alone. Purely a diagnostic (shown on
    /// TrackingDebugOverlay, logged) - it never feeds back into ContentWrapper's
    /// actual transform, at least for now.
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
        /// The building's real-world rotation, as last established by the QR
        /// (the only source ever trusted for rotation). Re-applied to
        /// ContentWrapper EVERY FRAME in LateUpdate, not just once right after a
        /// re-seed - see LateUpdate's own doc comment for why a one-shot
        /// correction turned out not to be enough.
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
        /// fresh QR detection after the first handoff - see HandoffOnce's own
        /// doc for why this happens on every (re)detection, not just the first.
        /// Exposed for TrackingDebugOverlay: this, not raw QR visibility, is the
        /// actually meaningful diagnostic for judging tracking quality on-site -
        /// losing sight of the QR after the first handoff is normal/expected
        /// (SLAM keeps content anchored regardless), and a climbing count here
        /// just means it keeps getting re-found and re-corrected, not that
        /// anything is failing.
        /// </summary>
        public int ResetCount { get; private set; }

        /// <summary>
        /// Live distance (meters) between the QR's own currently-detected world
        /// position and wherever the SLAM anchor (InstantTarget) currently sits
        /// - see this class's own "SYNC CHECK" doc comment. Only updated while
        /// the QR is actually visible; check SyncCheckValid before trusting it
        /// (stays at its last value, not reset to 0, while the QR is out of
        /// view - so TrackingDebugOverlay can still show "last measured drift"
        /// rather than a misleading fresh-looking 0).
        /// </summary>
        public float SyncPositionDelta { get; private set; }

        /// <summary>Angle (degrees) between the QR's own currently-detected rotation and the SLAM anchor's current rotation - see SyncPositionDelta's own doc, same validity caveat (check SyncCheckValid).</summary>
        public float SyncRotationDeltaDegrees { get; private set; }

        /// <summary>True once SyncPositionDelta/SyncRotationDeltaDegrees have been computed from at least one genuinely-visible frame - guards TrackingDebugOverlay from showing 0/0 as if it were a real (and suspiciously perfect) measurement before the QR has ever been visible long enough to check.</summary>
        public bool SyncCheckValid { get; private set; }

        private void Awake()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(false);
            DisableAllChildScripts();
        }

        /// <summary>Tracks whether the QR is CURRENTLY visible - used only by the sync check (Update) to know when a fresh comparison is meaningful; HandoffOnce itself doesn't depend on this (it's only ever invoked as a direct result of the QR having just been seen, so it needs no separate visibility tracking of its own).</summary>
        private bool _qrVisible;

        private void OnEnable()
        {
            if (ImageTarget == null) return;
            // OnSeenEvent is deliberately NOT subscribed here - it stays
            // Inspector-wired directly to HandoffOnce, so there's exactly one
            // listener on it (HandoffOnce sets _qrVisible itself - see its own
            // doc - rather than depending on a second listener here, avoiding
            // any dependence on UnityEvent listener ordering).
            ImageTarget.OnNotSeenEvent.AddListener(HandleQrNotSeen);
        }

        private void OnDisable()
        {
            if (ImageTarget == null) return;
            ImageTarget.OnNotSeenEvent.RemoveListener(HandleQrNotSeen);
        }

        private void HandleQrNotSeen() => _qrVisible = false;

        /// <summary>
        /// SYNC CHECK - see this class's own doc comment. Only meaningful while
        /// the QR is genuinely visible (ImageTarget freezes its own transform
        /// otherwise, which would make a stale comparison look like a real,
        /// possibly-misleading measurement) and only once handed off (nothing
        /// to compare against before InstantTarget has ever been seeded).
        /// </summary>
        private void Update()
        {
            if (!_handedOff || !_qrVisible || ImageTarget == null || InstantTarget == null) return;

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
        /// Wire this to ImageTarget's OnSeenEvent - runs every time the QR is
        /// (re)detected, not just the first. The FIRST call reveals content
        /// (unchanged) - every call, including that first one, re-seeds the
        /// instant tracker's anchor from THIS fresh detection, correcting any
        /// position/rotation drift the SLAM tracking may have accumulated while
        /// the QR was out of view. Content itself is never re-revealed or
        /// re-burst on a re-detection - only the anchor is refreshed, which is
        /// why RevealContent() is still gated on firstTime.
        /// </summary>
        public void HandoffOnce()
        {
            bool firstTime = !_handedOff;
            _handedOff = true;
            _qrVisible = true;
            if (!firstTime) ResetCount++;

            // Camera-relative offset of the QR at the exact moment of detection
            // - this is what places the instant anchor at the same real-world
            // spot. The QR is the ONLY source ever trusted for rotation - record
            // its detected world rotation as the standing "correct" value.
            // LateUpdate (not a one-off assignment here) is what actually keeps
            // ContentWrapper's rotation pinned to it every frame from here on -
            // see LateUpdate's own doc comment for why a one-shot correction
            // wasn't enough.
            Vector3 cameraRelativeOffset = Z.GetPosition(ImageTarget.AnchorPoseCameraRelative());
            _lockedWorldRotation = ImageTarget.transform.rotation;
            SeedAnchorPosition(cameraRelativeOffset);

            Debug.Log($"[HandoffToInstantTracking] {(firstTime ? "Initial lock" : "Re-seeded")} - offset {cameraRelativeOffset}, rotation {_lockedWorldRotation.eulerAngles}.");

            if (firstTime) RevealContent();
        }

        /// <summary>
        /// Re-applies the standing-correct rotation (_lockedWorldRotation, last
        /// set by the QR) EVERY FRAME, not just once right after a re-seed. Why
        /// continuous, not one-shot: ZapparInstantTrackingTarget.UpdateTargetPose()
        /// re-reads the native anchor's full pose - position, rotation, AND scale
        /// - fresh from the tracking engine on EVERY SINGLE FRAME, not just at
        /// explicit re-seed moments (Z.InstantWorldTrackerAnchorPose(tracker,
        /// cameraPose, ...) is called unconditionally in its Update()). A
        /// one-shot correction right after HandoffOnce looked right for exactly
        /// one frame, then silently drifted wrong again as ordinary per-frame
        /// tracking noise/uncertainty kept overwriting InstantTarget's transform
        /// underneath it. Position is deliberately NOT touched here - that's
        /// meant to update continuously as the camera moves relative to the
        /// anchor, that's the entire point of world tracking; only rotation and
        /// scale, which should never change for a real static building, get
        /// continuously re-locked.
        /// </summary>
        private void LateUpdate()
        {
            if (!_handedOff) return;
            ApplyLockedTransform();
            NormalizeContentScale();
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
        /// InstantTarget.lossyScale - the native anchor pose's own scale
        /// component, which is monocular SLAM's inherently uncertain internal
        /// estimate of the ratio between its tracking units and real metres (the
        /// same scale-ambiguity problem behind the still-open "walks meters,
        /// registers as decimetres" bug), NOT a meaningful real-world quantity
        /// worth preserving or trusting - especially unstable in the first few
        /// frames after a fresh seed, before tracking has had time to settle.
        /// (The QR's OWN scale is never used for anything, on purpose - a real-
        /// device test during a brief detour into trusting it directly found it
        /// unreliable too, see this class's own doc comment - InstantTarget's
        /// scale is the only one ever read here.) Called every frame from
        /// LateUpdate, not just once after a re-seed - see that method's own doc
        /// for why that matters. Assumes uniform, non-sheared scale throughout
        /// (true here - InstantTarget sits at scene root with no scaled parent
        /// above it).
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
        /// QR's current camera-relative position, captured by HandoffOnce).
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
