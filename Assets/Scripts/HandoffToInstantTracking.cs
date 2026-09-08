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
    /// gets a one-time rotation correction applied right after handoff, computed as the
    /// difference between the QR's actual rotation and whatever the instant tracker's
    /// seeded rotation turned out to be - after that, ContentWrapper just rides along
    /// with the instant tracker's ongoing SLAM tracking.
    ///
    /// Built from the SDK's public API surface, not verified on a real device yet -
    /// the position handoff especially assumes ImageTarget.AnchorPoseCameraRelative()'s
    /// translation is in the same camera-space convention InstantWorldTrackerAnchorPoseSetFromCameraOffset
    /// expects. Needs real on-site testing; may need a small correction if the anchor
    /// doesn't land exactly where the QR was.
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
        [Tooltip("Extra delay (on top of Max Start Delay) for whichever TentacleController has IsHero checked - guarantees it starts strictly after every other burst point's random delay (which can never exceed Max Start Delay), instead of just happening to roll a late number. Works whether the hero is inside a Pairs entry or an unpaired tentacle. Only one tentacle should be marked hero; if more than one is, they'll all fire together at this same delay.")]
        public float HeroExtraDelay = 0.6f;
        [Tooltip("Pair each tentacle with its own hole so they burst together on one shared delay - debris doesn't need its own slot if it's already a child of the hole, that's found automatically. Anything under ContentWrapper NOT listed here (e.g. a tentacle with no hole yet) still fires on its own independent random delay, so nothing is silently skipped.")]
        public TentaclePair[] Pairs;

        [Header("Testing / capture only - leave off for the real build")]
        [Tooltip("Skips waiting for the QR and the real instant-tracker handoff entirely, and just reveals ContentWrapper as-authored shortly after Play starts - for recording/screenshotting the burst in-editor without needing a working camera/tracking pipeline. Never enable this on a build meant for actual use.")]
        public bool DebugAutoReveal = false;
        public float DebugAutoRevealDelay = 1f;

        private bool _handedOff;

        private void Awake()
        {
            if (ContentWrapper == null) return;
            ContentWrapper.gameObject.SetActive(false);
            DisableAllChildScripts();
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
        /// </summary>
        private void DisableAllChildScripts()
        {
            foreach (var mb in ContentWrapper.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;
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

        /// <summary>Wire this to ImageTarget's OnSeenEvent.</summary>
        public void HandoffOnce()
        {
            if (_handedOff) return;
            _handedOff = true;
            StartCoroutine(HandoffRoutine());
        }

        private IEnumerator HandoffRoutine()
        {
            // Camera-relative offset of the QR at the exact moment of detection - this
            // is what places the instant anchor at the same real-world spot.
            Matrix4x4 cameraRelative = ImageTarget.AnchorPoseCameraRelative();
            Vector3 offset = Z.GetPosition(cameraRelative);

            Z.InstantWorldTrackerAnchorPoseSetFromCameraOffset(
                InstantTarget.InstantTracker.Value, offset.x, offset.y, offset.z,
                Z.InstantTrackerTransformOrientation.WORLD);
            InstantTarget.PlaceTrackerAnchor();

            // Wait a frame so InstantTarget's own Update() applies the pose we just
            // seeded before we read its transform for the rotation correction below.
            yield return null;

            if (ContentWrapper != null)
            {
                Quaternion correction = ImageTarget.transform.rotation * Quaternion.Inverse(InstantTarget.transform.rotation);
                ContentWrapper.localRotation = correction;
            }
            RevealContent();
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
        /// the separate guaranteed-last MaxStartDelay+HeroExtraDelay, unaffected by
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
                        StartCoroutine(TriggerPairAfterDelay(pair, debris, smoke, rubble, MaxStartDelay + HeroExtraDelay));
                    else
                        pendingRandomTriggers.Add(delay => StartCoroutine(TriggerPairAfterDelay(pair, debris, smoke, rubble, delay)));
                }
            }

            foreach (var tentacle in ContentWrapper.GetComponentsInChildren<TentacleController>(true))
            {
                if (pairedTentacles.Contains(tentacle)) continue;
                if (tentacle.IsHero)
                    StartCoroutine(TriggerAfterDelay(() => { tentacle.enabled = true; tentacle.Grow(); }, MaxStartDelay + HeroExtraDelay));
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

        /// <summary>Debris/Smoke/Rubble are usually authored as children of their hole (matches each script's own doc comment), so they don't need their own Pairs slot - only fall back to an explicit reference if the hole has none of that type as a child.</summary>
        private static T ResolveChild<T>(T explicitRef, WallHoleEffect hole) where T : Component
        {
            if (explicitRef != null) return explicitRef;
            return hole != null ? hole.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>One shared delay for the whole pair, then hole+tentacle+smoke+rubble together and the static debris ring a moment after - same pacing BurstSequencer uses for debris. delay is passed in already resolved by the caller (RevealContent) - either the hero's guaranteed MaxStartDelay+HeroExtraDelay, or this pair's slot from the shared spaced-random batch (see GenerateSpacedDelays) - so the hero's hole doesn't burst open early while the hero itself is still waiting off to the side, and non-hero pairs don't draw a delay blind to what every other burst point rolled.</summary>
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

        /// <summary>delay is passed in already resolved by the caller (RevealContent) - either the hero's guaranteed MaxStartDelay+HeroExtraDelay, or this trigger's own slot from the shared spaced-random batch (see GenerateSpacedDelays).</summary>
        private IEnumerator TriggerAfterDelay(System.Action trigger, float delay)
        {
            yield return new WaitForSeconds(delay);
            trigger();
        }
    }
}
