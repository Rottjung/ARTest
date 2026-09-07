using System.Collections;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Procedural bone-chain tentacle: drives a chain of bones directly (no Blender IK
    /// needed at runtime) through three blended layers -
    ///   1. Grow - reveals the tentacle, either style:
    ///      - Unfold: purely per-bone rotation, no scale involved at all (scaling the
    ///        whole transform would squash the mesh's actual thickness, not change how
    ///        far it reaches - wrong tool for the job). Bones start rolled tight near
    ///        the base and unwind outward bone-by-bone as growth progresses - roll
    ///        tightly enough (CurledAngle) and the coil's own geometry does the
    ///        "reaching out of the wall" work on its own: a tightly-rolled chain has
    ///        its tip bunched up near the base, and moves a long way as it straightens,
    ///        the same way uncoiling a spring extends it, with no artificial stretching
    ///        needed. Each instance curls around a slightly randomized axis
    ///        (CurlAxisRandomness, fixed per instance) so several growing at once don't
    ///        all curl identically (the "twin" look). Optionally also rises up along
    ///        local Y from below its authored position (RiseDistance) and scales up
    ///        from smaller than its authored size (RiseStartScale), both over the same
    ///        GrowCurve timing, so the whole rig physically climbs up out of the roof
    ///        while it uncurls rather than unrolling already at full height/size. Good
    ///        for rooftop tentacles that don't need to break through anything solid first.
    ///      - Punch: a jab-retract-burst sequence, purely through non-uniform scale on
    ///        ExtendAxis (the tentacle's own forward axis), anchored at the base (which
    ///        sits at the wall/hole) - the other two axes stay pinned at this instance's
    ///        own authored scale throughout (captured once in Awake, so per-burst-point
    ///        sizing survives), so it's always reaching further out, never puffing up
    ///        in girth:
    ///          1. Jab - a short, fast, dead-straight poke to just past the surface
    ///             (JabDistance), like only the tip breaking the glass/wall.
    ///          2. Retract - pulls back partway, still straight - the "coiling" beat.
    ///          3. Burst - rushes out from the retracted position past full size
    ///             (PunchOvershoot) and settles to rest, with idle motion (noise/sine/
    ///             jitter) dialling in from 0 at the start of the burst to 1 exactly as
    ///             it finishes settling - straight through the wind-up, fully alive by
    ///             the time it's fully through.
    ///        Because scale grows from the fixed base anchor, the tip (furthest from
    ///        it) is what visibly leads every phase, with the rest of the chain
    ///        following behind. Use this for tentacles bursting through a
    ///        WallHoleEffect decal.
    ///   2. Idle - layered Perlin noise + a sine wave across all three axes, phase-offset
    ///      per bone so it reads as a wave travelling down the tentacle.
    ///   3. Reach-toward-camera - a baseline ReachStrength bend toward a point out
    ///      along the camera's forward direction (i.e. the centre of the screen, not
    ///      the camera's physical position), computed in local space so it's correct
    ///      regardless of whether the camera or the tracked anchor is the one actually
    ///      moving. Always in effect, never overridden - it's the resting pose
    ///      everything below returns to. On top of it, an opt-in attack - a single
    ///      continuous "Lash" (see Lash()): coil back, one explosive snap onto the
    ///      camera with a whip-crack overshoot right at contact, then a direct
    ///      hand-off into ApplyIdlePose's blend-in for the recoil - fires either once,
    ///      right as this tentacle finishes growing/bursting through
    ///      (SnapAtCameraOnSettle), or repeatedly whenever the viewer walks within
    ///      SnapDistance of it (ReachByDistance, re-striking every AttackRepeatInterval
    ///      for as long as the camera stays within SnapFadeDistance) - same Lash, two
    ///      triggers. Deliberately NOT a sequence of separately-timed wait-for-it-to-
    ///      finish stages (windup, then strike, then settle, then hold) - an earlier
    ///      version worked that way and read as mechanical/waiting rather than a
    ///      single fast, vicious motion (a scorpion sting/cobra strike), which is why
    ///      Lash() instead drives the whole coil+strike off one eased progress curve.
    ///      The aim point is always at least MinStrikeDistance out in front of the
    ///      lens (never the camera's exact position). Every bend, toward or away
    ///      from the camera, is CCD (ApplyDirectionalBend) - deliberately not true
    ///      IK (an earlier version solved the strike with FABRIK, which landed the
    ///      tip exactly on the aim point but did so via a straightened, mechanical-
    ///      looking "IK pulling the chain" shape; CCD's own natural bend looks far
    ///      more organic, at the cost of the tip no longer landing on an exact
    ///      point). That approximation is fine for the repeating Reach By Distance
    ///      lashes, but not for the one-shot Snap At Camera On Settle money-shot
    ///      (see IsHero) - that one alone gets a scoped-down stretch mechanic back
    ///      (ApplyStretchToCloseGap/SnapStretchTipFraction/SnapMaxStretchMultiplier)
    ///      to guarantee it actually touches the camera. SlimeOverlay (optional, on
    ///      TentacleController or auto-found on the camera) gets Splat() the
    ///      instant each strike makes contact.
    ///
    /// The base bone never moves under any of the three layers - motion weight ramps
    /// from 0 at the base to 1 at the tip, so it reads as anchored into the wall with
    /// everything downstream articulating, rather than the whole chain wagging.
    ///
    /// Self-contained per instance (no shared Timeline/Director), so the same prefab
    /// can be placed at many independent burst points with independent timing - per
    /// the "reusable prefab, multiple burst points" requirement in the project brief.
    ///
    /// Auto-discovers the bone chain by walking single-child hierarchy from RootBone,
    /// so it doesn't depend on exact bone names surviving FBX import untouched.
    /// </summary>
    public class TentacleController : MonoBehaviour
    {
        public enum State { Hidden, Growing, Idle }
        public enum GrowStyle { Unfold, Punch }
        public enum Axis { X, Y, Z }

        [Header("Rig")]
        [Tooltip("First (base) bone in the chain - stays fixed. Children are auto-discovered by walking single-child hierarchy.")]
        public Transform RootBone;
        [Tooltip("Marks this as THE hero tentacle - purely a marker read by HandoffToInstantTracking, which delays this one's spawn until after every other burst point has started (see its HeroExtraDelay), and by this component's own SnapAtCameraOnSettle handling, which stretches to guarantee this one actually reaches the camera (see SnapStretchTipFraction/SnapMaxStretchMultiplier) - the repeating Reach By Distance lashes never stretch, hero or not. Mark at most one tentacle this way.")]
        public bool IsHero = false;

        [Header("Grow style")]
        [Tooltip("Unfold = slow uncurl (rooftop). Punch = fast scale-out burst, tip leads (wall breakthrough).")]
        public GrowStyle Style = GrowStyle.Unfold;
        [Tooltip("Which local axis the tentacle points/reaches forward along - used by both styles: Punch scales along it directly, and Unfold derives its curl axis from it (perpendicular to it and world-up) so unrolling sweeps the tip up and out along this direction. Pick whichever one actually looks right; there's no way to infer it automatically since it depends on how the rig was authored.")]
        public Axis ExtendAxis = Axis.X;

        [Header("Grow / unfold (Style = Unfold)")]
        [Tooltip("Longer than it looks like it should need to be, on purpose - with PerBoneGrowDelay staggering the chain, the tip bone doesn't even start unwinding until well into this duration, so its own share of time is a fraction of the total. Too short here reads as 'stuck, then suddenly snaps' right at the end.")]
        public float GrowDuration = 1.8f;
        public AnimationCurve GrowCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("How rolled up the tentacle is before it starts growing (degrees, scaled by base->tip weight - accumulates down the chain like a coiled spring). For 'unrolling up through the roof', this wants to stay fairly modest - too tight and the axis randomness below turns it into chaotic looping instead of a clean upward unroll. Tune by eye; I can't preview the actual mesh.")]
        public float CurledAngle = 130f;
        [Tooltip("How much of the grow duration each successive bone waits before it starts unwinding - creates the outward ripple. Kept fairly small so the tip's own unwind isn't squeezed into a short window at the very end of GrowDuration.")]
        [Range(0f, 1f)] public float PerBoneGrowDelay = 0.05f;
        [Tooltip("Random per-instance tilt (degrees) applied to the curl axis, so multiple Unfold tentacles don't curl in exactly the same plane (the 'twin' look). Kept small on purpose - this is meant to be a subtle per-instance variation, not enough to turn a clean upward unroll into chaotic looping. Captured once in Awake, fixed for this instance's whole lifetime.")]
        public float CurlAxisRandomness = 10f;
        [Tooltip("How far below its authored position (in this transform's local Y) the whole tentacle starts before growing - it rises up into place over GrowDuration (same GrowCurve easing as the uncurl), so it reads as physically emerging up out of the roof rather than uncurling already sitting at final height. 0 = no rise, stays put and just uncurls in place.")]
        public float RiseDistance = 0f;
        [Range(0.05f, 1f)]
        [Tooltip("How much smaller (as a fraction of its authored scale) the tentacle starts before growing, scaling up to full size over the same GrowCurve timing as the rise/uncurl - e.g. 0.2 = starts 5x smaller. 1 = no scale change, starts at full size.")]
        public float RiseStartScale = 1f;

        [Header("Punch through (Style = Punch) - 1. Jab, short/straight/tip-only")]
        [Tooltip("First-pass 'heavier, not spiky' tuning (client reference: UCI-RE.mp4) - a genuinely massive limb takes a moment to get moving (slower jab than the old 0.08), doesn't wind up like a spring (shorter/shallower retract below), and takes real time to shoot out and settle (Burst/Settle both much longer below) rather than snapping through in well under a second. Tune from here rather than treating these as final.")]
        public float JabDuration = 0.18f;
        [Tooltip("How far along ExtendAxis the jab reaches, as a fraction of full length - just enough for the tip to break through.")]
        [Range(0f, 1f)] public float JabDistance = 0.35f;

        [Header("2. Retract - pulls back partway, still straight")]
        public float RetractDuration = 0.16f;
        [Tooltip("Where it pulls back to, as a fraction of full length - should be less than JabDistance so it visibly recoils, but doesn't need to go all the way back to 0. Kept fairly shallow (vs the old 0.15) - a heavy mass doesn't coil back like a loaded spring before committing, it just keeps pushing through.")]
        [Range(0f, 1f)] public float RetractDistance = 0.22f;

        [Header("3. Burst - rushes out past full size, idle wakes up here")]
        [Tooltip("The single biggest lever for 'heavy vs spiky' - this is what was 0.22 (a near-instant snap) and is now much longer, so the mass actually reads as accelerating into frame rather than teleporting.")]
        public float BurstDuration = 0.55f;
        [Tooltip("Scale multiplier along ExtendAxis at the peak of the burst, before settling back to 1 - the 'snap' overshoot. Eased down slightly from the old 1.15 - a heavy mass carries some overshoot from momentum, but not a springy boing.")]
        public float PunchOvershoot = 1.1f;
        [Tooltip("Time easing back down from the overshoot peak to normal size - idle motion reaches full strength exactly when this finishes. Much longer than the old 0.25 - a heavy mass takes real time to stop wobbling from its own momentum, a quick settle is part of what read as light/spiky.")]
        public float SettleDuration = 0.65f;
        [Range(0f, 1f)]
        [Tooltip("How much idle noise/sine/jitter is blended in during jab+retract (ramping 0 up to this value) and held CONSTANT at this value through the whole of Burst - i.e. everything up to Settle. A tentacle with a curled/hooked tip authored into its rest pose reads as visibly rigid/frozen holding that exact curled shape with zero idle motion, and the same dead-straight rest shape is what makes the axis-aligned scale stretch during this window look mechanical. Settle Duration then ramps from this value up to full idle (1) - that leg, and only that leg, is what Settle Duration actually controls, so tune Settle Duration for how gradual that final wake-up reads. Ignored entirely if Idle Active Throughout Punch below is on. 0 = old behaviour (dead straight through jab/retract/burst).")]
        public float PunchIdleDuringWindup = 0.15f;
        [Tooltip("Simpler alternative to tuning Punch Idle During Windup above: idle noise/sine/jitter runs at full strength from the very instant this tentacle spawns, all the way through jab/retract/burst/settle and beyond - never blended/ramped at all. The Punch scale animation (jab out, retract, burst past full size, settle) plays independently on top of that constant idle rotation, rather than idle needing to ease in around it. Turn this on if tuning the windup/ramp values still doesn't fully hide a curled tip's rigid rest shape or the axis-aligned stretch - this guarantees there's never a moment holding the bare, unwobbled rest pose at all.")]
        public bool IdleActiveThroughoutPunch = false;
        [Range(0f, 1f)]
        [Tooltip("Addresses the actual CAUSE of tip distortion on a curled/hooked tentacle, rather than trying to hide it with idle noise: the Punch scale (AxisScale) stretches the mesh along a single fixed axis, and stretching a CURVED shape along one axis is what visibly warps/distorts it - a perfectly straight chain doesn't have this problem. This forcibly straightens the whole chain (every bone's local rotation set toward identity relative to its parent, base bone excepted - so the base's own authored anchor angle into the wall/roof is untouched) for the full jab+retract+burst, so the scale animation plays on an undistorted straight shape, then eases back to the tentacle's own natural curled rest pose over Settle Duration - it whips out straight, then curls into position, like a party favour or a tongue flicking out. 1 = fully straightened during the punch-through, 0 = no straightening at all (old behaviour, whatever curl was authored plays through the whole thing). Lowered from the old default of 1 - fully straightening a curled/hooked tentacle makes it look exactly like a straight spike while it's punching through, which is likely the biggest single reason ours read as 'spiky' against a reference (UCI-RE.mp4) where every tentacle keeps a smooth, heavy-looking curve even mid-punch. This trades back in some of the axis-stretch distortion the full-1 value was fixing - if that becomes visible again on a heavily curled tip, raise this back up on that tentacle specifically rather than everywhere.")]
        public float PunchStraightenTip = 0.4f;

        [Header("Idle - noise (slow, smooth)")]
        public float NoiseAmplitudeDegrees = 22f;
        public float NoiseSpeed = 1.1f;

        [Header("Idle - travelling sine wave, all 3 axes (slow, smooth)")]
        public float SineAmplitudeDegrees = 28f;
        public float SineFrequency = 1.4f;
        public float SineSpeed = 1.6f;

        [Header("Idle - jitter (fast, sharp - the 'alive/menacing' layer on top)")]
        public float JitterAmplitudeDegrees = 10f;
        public float JitterSpeed = 3.5f;

        [Header("Reach toward camera")]
        [Tooltip("Baseline bend toward the aim point, always in effect (0 = none, 1 = fully bent) - a resting floor the attack cycle below returns to, not something it overrides.")]
        [Range(0f, 1f)] public float ReachStrength = 0f;
        public float ReachSmoothing = 3f;
        [Tooltip("Preferred aim distance along the camera's forward direction (matters once the camera is a moving AR phone, not this test rig) - floored by Min Strike Distance below, so this only matters if you want the aim point further out than that.")]
        public float ReachAimDistance = 0f;
        [Tooltip("Every bend-toward-camera (baseline reach and every attack strike) aims at a point at least this far out along the camera's forward axis - never at the camera's exact position. For a standard camera this point projects to screen-centre, so it also keeps the tip centred in view rather than off to one side. Prevents the tip piercing through/past the lens - raise this if strikes still look like they're going through the camera.")]
        public float MinStrikeDistance = 0.3f;
        public Transform CameraOverride; // falls back to Camera.main if unset
        [Tooltip("Optional - a CameraSlimeOverlay to splat the instant a strike reaches its peak (SnapPeakStrength), reading as the tip touching the lens. Left blank, one is auto-found as a component on the resolved camera (CameraOverride, or Camera.main) if present - leave both blank if you don't want this.")]
        public CameraSlimeOverlay SlimeOverlay;

        [Header("Reach - attack when the viewer gets close")]
        [Tooltip("On top of the baseline Reach Strength above, plays a Lash (see Lash()) whenever the camera comes within Snap Distance - it doesn't replace the baseline, it's an extra strike on top of it, recoiling back down to the baseline afterward. Repeats (see Attack Repeat Interval) for as long as the camera stays close, rather than striking once and holding a static pose.")]
        public bool ReachByDistance = false;
        [Tooltip("Camera distance (metres) at or inside which an attack triggers - measured horizontally only (X/Z, ignoring height) from the tentacle's TIP, not its base/root - for a rooftop tentacle the base can sit 10-20m from anywhere a viewer walks, so the tip (what actually hangs down near them) is the only sensible reference point.")]
        public float SnapDistance = 1.5f;
        [Tooltip("Camera distance up to which an already-triggered attack keeps repeating - once the camera goes beyond this (same tip-based horizontal measurement as Snap Distance), it stops. Must be >= Snap Distance (enforced defensively even if left smaller); the gap between the two avoids flicker right at the edge.")]
        public float SnapFadeDistance = 3f;
        [Tooltip("How long the camera has to stay CONTINUOUSLY beyond Snap Fade Distance before an engagement actually ends - a real-world/AR camera's tracked position is never perfectly steady (natural hand sway, tracking jitter), so checking the raw distance every single frame with zero tolerance means one noisy frame right at the boundary ends the whole engagement early; idle motion (and its blend-in) then plays for a frame or few before Reach By Distance re-triggers a fresh engagement - which is what reads as the tentacle visibly lashing, rotating, lashing, rotating, rather than lashing, holding still, lashing. Raise this if that's happening.")]
        public float SnapFadeGraceDuration = 0.4f;
        [Tooltip("Natural pause between repeat strikes while the camera stays close (idle motion keeps playing during this gap, so the tentacle isn't frozen between hits) - a fresh Lash fires again after this, for as long as it's still within Snap Fade Distance.")]
        public float AttackRepeatInterval = 0.6f;

        [Header("Attack shape - ONE continuous lash (coil, explosive strike, recoil)")]
        [Tooltip("Shared by both attack triggers below (Snap At Camera On Settle and Reach By Distance) - total time for one whole Lash, coil through recoil-handoff. Keep this short - a scorpion sting/cobra strike is fast and vicious, not a lingering multi-stage sequence.")]
        public float LashDuration = 0.3f;
        [Range(0.05f, 0.6f)]
        [Tooltip("Fraction of Lash Duration spent coiling back before the strike explodes forward. Keep well under half so the strike, not the wind-up, dominates the motion.")]
        public float LashCoilFraction = 0.35f;
        [Range(0.05f, 0.6f)]
        [Tooltip("Fraction of Lash Duration the explosive strike itself takes, from the coiled position to full peak/touch - the fastest, most violent part of the motion. Whatever's left after Coil Fraction + Strike Fraction is the recoil hand-off into idle.")]
        public float LashStrikeFraction = 0.2f;
        [Range(0f, 1f)]
        [Tooltip("How far it coils away from the camera before striking (0-1 bend strength) - the cobra wind-up before every strike, one-shot or repeating alike.")]
        public float LashCoilStrength = 0.35f;
        [Range(0f, 0.5f)]
        [Tooltip("How far past full peak strength the strike overshoots for an instant right at contact before settling - the 'crack' of the whip. 0 = no overshoot, a plain ease to the peak.")]
        public float LashOvershoot = 0.15f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the chain's segments, counted from the base, whose bend is held back at all - within this base-side fraction, influence ramps from 0 (the very first segment, right at the wall/roof - never bends) up to 1 (full bend, same as everywhere else). EVERY segment beyond this fraction always gets full bend - so raising this only holds MORE of the base-side chain anchored, it never weakens the tip's own ability to bend toward the target. Keep this fairly small (0.2-0.35ish); pushing it too high starves the chain of the free segments it needs to reach far around toward a target (e.g. a roof-mounted tentacle bending down to a camera at ground level) - see Lash Bend Curve below for why an earlier version that ramped across the WHOLE chain caused exactly that problem.")]
        public float LashBaseAnchorFraction = 0.3f;
        [Range(0.5f, 6f)]
        [Tooltip("Shapes the ramp WITHIN Lash Base Anchor Fraction only (1 = linear, higher = exponential, holding the base-most segments stiller for longer before ramping up). Does not affect segments beyond that fraction - those are always full bend regardless of this value, so this only changes how the anchored portion eases in, never how far the rest of the chain can reach.")]
        public float LashBendCurve = 1f;
        [Tooltip("Once a Lash finishes (rotation lands cleanly on the peak pose), idle noise/sine/jitter used to cut in instantly the very next frame - a visible snap, since the strike's last pose and that frame's idle noise rarely agree. This is how long ApplyIdlePose instead blends from wherever the strike actually left the chain into full idle motion (i.e. it's also the recoil time) - see ApplyIdlePose/_idleBlendFromRotation.")]
        public float IdleBlendInDuration = 0.25f;
        [Range(0f, 1f)]
        [Tooltip("Between repeat strikes (still engaged/within Snap Fade Distance, but no Lash actively running), idle noise/sine/jitter is capped at this fraction of full strength instead of either full idle sway (which read as the tentacle randomly rotating/drifting while 'waiting' between hits) or a complete freeze (which reads as a dead, frozen prop rather than a predator holding tension before its next strike). 0 = frozen solid, 1 = full idle motion even while engaged. A small value (0.1-0.2ish) is a light tremor, not a sway.")]
        public float EngagedIdleDamping = 0.15f;

        [Header("Attack trigger 1 - once, right as growth finishes")]
        [Tooltip("If set, this tentacle plays one Lash the instant it finishes growing/bursting through, before easing back to its baseline idle reach. Reads as the tentacle noticing the viewer right as it breaks through. Off by default - opt in per tentacle.")]
        public bool SnapAtCameraOnSettle = false;
        [Range(0f, 1f)]
        [Tooltip("How strongly the strike bends toward the camera at its peak (1 = full bend to the aim point). Shared by both attack triggers.")]
        public float SnapPeakStrength = 1f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the chain's segments, counted from the TIP backward, allowed to stretch to close any gap CCD leaves short of actually touching the camera - the base-side segments never stretch, however large the gap. Applies ONLY to the one-shot Snap At Camera On Settle attack, never to the repeating Reach By Distance lashes (which are fine approximating - see Lash()'s doc on why CCD alone doesn't land exactly on target). This is the hero/money-shot moment, so it's worth guaranteeing actual contact rather than a close approximation.")]
        public float SnapStretchTipFraction = 0.5f;
        [Tooltip("Caps how far any single segment can stretch, as a multiple of its own rest length, so a target genuinely far beyond the chain's reach doesn't turn into an absurd spike.")]
        public float SnapMaxStretchMultiplier = 2f;

        private Transform[] _bones;
        private float[] _boneSegmentLength; // rest-pose world length of each segment (bone i to bone i+1) - used only by ApplyStretchToCloseGap, the hero one-shot's guaranteed-reach stretch
        private Quaternion[] _restLocalRotation;
        private Vector3 _restLocalScale;
        private Vector3 _restLocalPosition;
        private Vector3 _curlAxis = Vector3.right;
        private float _seedX, _seedY, _seedZ;
        private float _jitterSeedX, _jitterSeedY, _jitterSeedZ;
        private float _reachCurrent;
        private float _growStartTime = -1f;
        private Quaternion[] _idleBlendFromRotation; // per-bone pose captured the instant an attack ends, so ApplyIdlePose can ease from it instead of snapping straight to idle
        private Vector3[] _idleBlendFromScale; // per-bone scale captured at the same instant - almost always all Vector3.one (only a stretchToReach hero Lash ever leaves scale != 1), so ApplyIdlePose can ease any leftover stretch back to normal on the same timeline as the rotation blend-in, instead of it staying stretched forever
        private float _idleBlendWeight = 1f; // 1 = fully idle, no blend in progress; reset to 0 whenever an attack finishes

        private enum AttackPhase { None, Windup, Strike, Settle }
        private AttackPhase _attackPhase = AttackPhase.None;
        // True for the WHOLE attack engagement, including the natural gap between
        // repeat strikes (where _attackPhase itself is back to None) - distinct from
        // _attackPhase, which only covers an individual Lash's own internal windup/
        // strike/settle sub-phases. This is what ApplyIdlePose actually gates on, so
        // idle noise/sine/jitter and the baseline reach chase both hold completely
        // still for the whole engagement instead of visibly drifting in the gaps
        // between strikes - and, just as importantly, it also stops ApplyIdlePose's
        // ReachByDistance check from spawning a second, overlapping AttackRoutine
        // while one is already running its repeat loop.
        private bool _isEngaged;
        private float _timeOutsideHoldDistance; // seconds the camera has been continuously beyond SnapFadeDistance during the current engagement - see ShouldStayEngaged/SnapFadeGraceDuration

        public State CurrentState { get; private set; } = State.Hidden;

        private void Awake()
        {
            DiscoverBones();
            _seedX = Random.Range(0f, 1000f);
            _seedY = Random.Range(0f, 1000f);
            _seedZ = Random.Range(0f, 1000f);
            _jitterSeedX = Random.Range(0f, 1000f);
            _jitterSeedY = Random.Range(0f, 1000f);
            _jitterSeedZ = Random.Range(0f, 1000f);

            // Base curl axis is derived from ExtendAxis (perpendicular to it and to
            // world-up), so the coil lies in the vertical plane containing the extend
            // direction - unrolling sweeps the tip up and out along ExtendAxis, rather
            // than an arbitrary/unrelated axis. Then a small per-instance random tilt
            // on top, so multiple Unfold tentacles growing at once don't all curl in
            // exactly the same plane (the "twin" look) - fixed for this instance's
            // whole lifetime, not re-rolled per grow.
            _curlAxis = (Quaternion.Euler(
                Random.Range(-CurlAxisRandomness, CurlAxisRandomness),
                Random.Range(-CurlAxisRandomness, CurlAxisRandomness),
                Random.Range(-CurlAxisRandomness, CurlAxisRandomness)) * BaseCurlAxis()).normalized;

            // Captured BEFORE zeroing for Punch's hidden state, so each instance's own
            // authored size (burst points are often scaled up/down individually) is
            // preserved as "full size" instead of every tentacle being flattened to a
            // hardcoded (1,1,1) once it finishes punching out.
            _restLocalScale = transform.localScale;
            // Captured BEFORE ApplyGrowPose(0f) below moves it down for the rise-in-place
            // start, so each instance's own authored placement in the scene is what it
            // rises up to, not some hardcoded point.
            _restLocalPosition = transform.localPosition;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
            // Unfold's Hidden state previously did nothing at all, leaving bones at
            // their raw straight rest pose - so the instant Grow() fired, every bone
            // snapped straight to the curled pose in one frame (ApplyGrowPose(0) is the
            // fully-curled pose), then visibly held there until each bone's staggered
            // delay came up. Sitting pre-curled here makes Grow() a smooth continuation
            // instead of a snap.
            else if (Style == GrowStyle.Unfold) ApplyGrowPose(0f);
        }

        private void DiscoverBones()
        {
            var chain = new System.Collections.Generic.List<Transform>();
            var current = RootBone;
            while (current != null)
            {
                chain.Add(current);
                current = current.childCount > 0 ? current.GetChild(0) : null;
            }
            _bones = chain.ToArray();
            _restLocalRotation = new Quaternion[_bones.Length];
            _idleBlendFromRotation = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                _restLocalRotation[i] = _bones[i].localRotation;

            // Rest-pose world-space length of each segment - measured here before
            // anything else in Awake() touches rotation, so these are the rig's own
            // authored lengths, never scaled to hit them (only ApplyStretchToCloseGap
            // ever touches scale, and only for the hero one-shot attack).
            _boneSegmentLength = new float[Mathf.Max(0, _bones.Length - 1)];
            for (int i = 0; i < _bones.Length - 1; i++)
                _boneSegmentLength[i] = Vector3.Distance(_bones[i].position, _bones[i + 1].position);
        }

        /// <summary>0 at the base bone, 1 at the tip - every motion layer is scaled by this so the base never moves.</summary>
        private float BaseToTipWeight(int index)
        {
            return _bones.Length <= 1 ? 1f : index / (float)(_bones.Length - 1);
        }

        /// <summary>
        /// 0 at the first segment (base bone never bends), ramping up to 1 by the
        /// END of the base-side LashBaseAnchorFraction portion of the chain - EVERY
        /// segment beyond that fraction always gets full bend, regardless of
        /// curveExponent. This is deliberately NOT a ramp across the whole chain: an
        /// earlier version raised BaseToTipWeight(i) (or a plain segmentIndex/
        /// segmentCount ramp) to curveExponent for every segment, which meant most of
        /// the chain was under-weighted and could barely bend at all toward a target
        /// that needed a large reach (a roof-mounted tentacle bending far down to a
        /// ground-level camera, for instance). Anchoring only the base-side fraction
        /// and leaving the rest of the chain at full, uncapped weight keeps the base
        /// genuinely still without starving the rest of the chain of its own reach.
        ///
        /// The ramp itself is smoothstepped (zero slope at BOTH t=0 and t=1), not a
        /// raw Mathf.Pow(t, curveExponent) - a plain power curve's VALUE starts at 0
        /// like it should, but its RATE of change doesn't: at curveExponent 1
        /// (linear) the slope jumps instantly from 0 (the anchored segments before
        /// it) to a constant nonzero rate the moment a segment starts bending at
        /// all, which is exactly what read as a hard crease right at that joint -
        /// and the same abrupt-slope problem happens again in reverse at the far
        /// end, where the ramp meets the "always full bend" segments beyond it.
        /// Smoothstepping first removes both kinks; curveExponent still biases the
        /// eased curve toward the base (higher = slower start) same as before.
        /// anchorLength is also no longer rounded to a whole number of segments -
        /// that quantization could leave as few as 0-1 actual segments inside the
        /// ramp, which is too little resolution for any easing to look smooth on,
        /// however the curve is shaped.
        /// </summary>
        private float SegmentAttackInfluence(int segmentIndex, float curveExponent)
        {
            int segmentCount = _bones.Length - 1;
            if (segmentCount <= 1) return 1f;
            float anchorLength = segmentCount * LashBaseAnchorFraction;
            if (anchorLength <= 0.0001f) return 1f;
            float t = Mathf.Clamp01(segmentIndex / anchorLength);
            return Mathf.Pow(Smoothstep(t), Mathf.Max(0.0001f, curveExponent));
        }

        /// <summary>
        /// Starts the reveal (unfurl or punch-through, per Style). Call this when the
        /// burst point activates. A second call while already Growing/Idle is a no-op,
        /// not a restart - without this, anything that accidentally triggers the
        /// reveal twice (e.g. a tracking-found event firing more than once) would reset
        /// _growStartTime and snap an already-grown tentacle back to hidden/curled
        /// before regrowing. Pass forceRestart if you actually want that (e.g.
        /// deliberately re-triggering a burst point from scratch).
        /// </summary>
        public void Grow(bool forceRestart = false)
        {
            if (CurrentState != State.Hidden && !forceRestart) return;
            ResetAttackState();
            CurrentState = State.Growing;
            _growStartTime = Time.time;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
        }

        public void Hide()
        {
            ResetAttackState();
            CurrentState = State.Hidden;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
            else if (Style == GrowStyle.Unfold) ApplyGrowPose(0f);
        }

        /// <summary>Stops any in-flight attack coroutine so it can't keep bending bones after a Hide()/regrow - StopAllCoroutines is safe here since attacks are the only coroutines this component runs.</summary>
        private void ResetAttackState()
        {
            StopAllCoroutines();
            _attackPhase = AttackPhase.None;
            _isEngaged = false;
            _timeOutsideHoldDistance = 0f;
            _reachCurrent = 0f;
            _idleBlendWeight = 1f; // no stale blend-in carried across a Hide()/regrow
        }

        private void Update()
        {
            if (_bones == null || _bones.Length == 0) return;

            if (CurrentState == State.Growing)
            {
                if (Style == GrowStyle.Punch)
                {
                    UpdatePunch();
                }
                else
                {
                    float elapsed = Time.time - _growStartTime;
                    float overallT = Mathf.Clamp01(elapsed / GrowDuration);
                    ApplyGrowPose(overallT);
                    if (overallT >= 1f) EnterIdle();
                }
                return;
            }

            if (CurrentState == State.Idle)
            {
                ApplyIdlePose();
            }
        }

        private void UpdatePunch()
        {
            float elapsed = Time.time - _growStartTime;

            float jabEnd = JabDuration;
            float retractEnd = jabEnd + RetractDuration;
            float burstEnd = retractEnd + BurstDuration;
            float totalPunch = burstEnd + SettleDuration;

            float extend;
            if (elapsed < jabEnd)
            {
                // Just the tip - fast, straight poke to break the surface.
                float t = elapsed / Mathf.Max(0.0001f, JabDuration);
                extend = Mathf.Lerp(0f, JabDistance, EaseOutCubic(t));
            }
            else if (elapsed < retractEnd)
            {
                // Pulls back partway - coiling before the real punch.
                float t = (elapsed - jabEnd) / Mathf.Max(0.0001f, RetractDuration);
                extend = Mathf.Lerp(JabDistance, RetractDistance, Smoothstep(t));
            }
            else if (elapsed < burstEnd)
            {
                // The real punch - rushes out from the retracted position past full size.
                float t = (elapsed - retractEnd) / Mathf.Max(0.0001f, BurstDuration);
                extend = Mathf.Lerp(RetractDistance, PunchOvershoot, EaseOutCubic(t));
            }
            else
            {
                float t = Mathf.Clamp01((elapsed - burstEnd) / Mathf.Max(0.0001f, SettleDuration));
                extend = Mathf.Lerp(PunchOvershoot, 1f, Smoothstep(t));
            }
            transform.localScale = AxisScale(extend);

            // Used to be dead straight through the whole jab+retract wind-up (idle
            // weight held at a hard 0), then dialled in from 0 to 1 across
            // retractEnd..totalPunch (burst+settle COMBINED) - which sounds right
            // but wasn't: BurstDuration is short and fixed, so most of that ramp's
            // actual visible "wake up" happened during Burst, before Settle even
            // started - meaning Settle Duration (despite its own tooltip promising
            // "idle motion reaches full strength exactly when this finishes") barely
            // affected anything, since by the time Settle began idleWeight was
            // already most of the way to 1. Now split into three legs instead of
            // two: 0 -> PunchIdleDuringWindup across jab+retract, HELD CONSTANT
            // through the whole of Burst, then PunchIdleDuringWindup -> 1 across
            // Settle specifically and only Settle - so Settle Duration actually
            // controls that final wake-up the way its tooltip already claimed, and
            // straightenWeight below (already Settle-scoped) ramps back on exactly
            // the same timeline instead of a mismatched one.
            float idleWeight;
            if (IdleActiveThroughoutPunch)
            {
                // Simplest option: idle just runs at full strength the entire time,
                // from the moment this tentacle spawns - the Punch scale animation
                // (jab/retract/burst/settle) plays independently on top of it, so
                // there's never a bare, unwobbled rest pose to look rigid or make
                // the scale stretch look mechanical in the first place.
                idleWeight = 1f;
            }
            else if (elapsed < retractEnd)
            {
                float t = retractEnd > 0.0001f ? elapsed / retractEnd : 1f;
                idleWeight = Mathf.Lerp(0f, PunchIdleDuringWindup, Smoothstep(Mathf.Clamp01(t)));
            }
            else if (elapsed < burstEnd)
            {
                idleWeight = PunchIdleDuringWindup;
            }
            else
            {
                float t = Mathf.Clamp01((elapsed - burstEnd) / Mathf.Max(0.0001f, SettleDuration));
                idleWeight = Mathf.Lerp(PunchIdleDuringWindup, 1f, Smoothstep(t));
            }

            // Straightened (undistorted) through jab+retract+burst - the axis-
            // aligned scale above plays on a straight chain instead of warping a
            // curled/hooked rest pose - then eases back to the true authored curl
            // over Settle, once the scale itself has finished changing. See
            // PunchStraightenTip/ApplyPunchIdlePose.
            float straightenWeight = elapsed < burstEnd
                ? PunchStraightenTip
                : Mathf.Lerp(PunchStraightenTip, 0f, Smoothstep(Mathf.Clamp01((elapsed - burstEnd) / Mathf.Max(0.0001f, SettleDuration))));
            ApplyPunchIdlePose(straightenWeight, idleWeight);

            // Baseline reach-toward-camera - _reachCurrent itself keeps chasing
            // ReachStrength unconditionally, same as ever (so it's already wherever
            // it should be by the time Idle takes over - see below), but the bend
            // it's actually APPLIED at is scaled by idleWeight, the exact same
            // curve idle noise blends in on: suppressed through jab/retract/burst
            // (a punch-through is a straight, mechanical action - it shouldn't
            // also be leaning toward the viewer mid-punch) and only fading in
            // during Settle, right alongside idle waking up. Reusing idleWeight
            // directly (rather than a second, separately-tuned ramp) guarantees
            // reach and idle are perfectly in sync without needing to keep two
            // curves lined up by hand.
            _reachCurrent = Mathf.MoveTowards(_reachCurrent, ReachStrength, Time.deltaTime * ReachSmoothing);
            float appliedReach = _reachCurrent * idleWeight;
            if (appliedReach > 0.001f)
            {
                Vector3 reachAimPoint = AimPointWorld();
                ApplyDirectionalBend(reachAimPoint, appliedReach);
                ClampTipFromCamera(reachAimPoint);
            }

            if (elapsed >= totalPunch)
            {
                transform.localScale = _restLocalScale;
                EnterIdle();
            }
        }

        /// <summary>Common Growing -> Idle transition for both styles, so the one-shot settle attack fires the same way regardless of how growth finished.</summary>
        private void EnterIdle()
        {
            CurrentState = State.Idle;
            if (SnapAtCameraOnSettle && !_isEngaged) StartCoroutine(AttackRoutine(holdUntilFar: false));
        }

        /// <summary>
        /// Fires one Lash (see Lash()) and, for the repeating proximity trigger, keeps
        /// firing another after each AttackRepeatInterval gap for as long as the
        /// camera stays within SnapFadeDistance - deliberately just a thin loop around
        /// Lash() rather than owning any of the motion itself, so both triggers play
        /// the exact same strike.
        /// </summary>
        private IEnumerator AttackRoutine(bool holdUntilFar)
        {
            // Only the proximity trigger logs - it's the one whose in-range/out-of-range
            // timing actually needs watching; the one-shot settle attack always fires
            // regardless of distance, so there's nothing to verify there.
            if (holdUntilFar) Debug.Log($"[SnapAttack] '{name}' engaging - camera in range", this);

            _isEngaged = true;
            _timeOutsideHoldDistance = 0f;
            int strikeCount = 0;
            bool keepGoing = true;
            while (keepGoing)
            {
                if (holdUntilFar) Debug.Log($"[SnapAttack] '{name}' strike #{++strikeCount}", this);
                // Only the one-shot Snap At Camera On Settle trigger (holdUntilFar
                // false) stretches to guarantee it actually reaches the camera -
                // the repeating Reach By Distance lashes stay CCD-only/approximate,
                // same as ever. See Lash()'s stretchToReach doc.
                yield return Lash(stretchToReach: !holdUntilFar);

                if (!holdUntilFar) break; // one-shot settle trigger - single Lash, no repeat wait

                // Natural gap between strikes - _attackPhase is back to None during
                // this wait, so ApplyIdlePose runs its damped idle tremor (see
                // EngagedIdleDamping) rather than either full idle sway or a
                // complete freeze. ShouldStayEngaged (not a raw distance check)
                // decides whether to keep waiting for the next strike - see its doc
                // comment for why the raw check alone caused visible lash/rotate/
                // lash/rotate flicker from ordinary tracking jitter.
                float held = 0f;
                while (true)
                {
                    if (!ShouldStayEngaged()) { keepGoing = false; break; }
                    if (held >= AttackRepeatInterval) break; // ready for the next strike
                    held += Time.deltaTime;
                    yield return null;
                }
            }

            if (holdUntilFar) Debug.Log($"[SnapAttack] '{name}' releasing after {strikeCount} strike(s) - camera left range", this);
            // Only now does ApplyIdlePose resume (idle motion + the blend-in from
            // wherever the last Lash's Settle phase left the chain, captured at the
            // end of that Lash - see ApplyIdlePose/_idleBlendFromRotation) - not
            // after every individual strike, which is what used to read as the
            // tentacle idly drifting/rotating in between hits.
            _isEngaged = false;
        }

        /// <summary>
        /// One continuous strike - coil back, then bend toward the camera with a
        /// brief overshoot right at contact (the whip-crack), then a direct hand-off
        /// into ApplyIdlePose's existing blend-in for the recoil back to baseline -
        /// so there's no separate "settle" or "hold" step to visibly pause at. Runs
        /// as a single eased pass over LashDuration.
        ///
        /// Deliberately CCD only (ApplyDirectionalBend) for BOTH the coil and the
        /// strike now - not true IK (FABRIK) for the strike. An earlier version
        /// solved the strike with FABRIK, which landed the tip EXACTLY on the aim
        /// point but did so by pulling the chain into a straightened, mechanical
        /// "IK" shape; CCD's own natural bend (the same mechanism already used for
        /// the coil and the baseline reach, and the one that actually looked right)
        /// reads as a much nicer, more organic curve - at the deliberate cost of the
        /// tip no longer landing on an exact point - CCD just bends as far as its
        /// strength allows and stops there, no exact "how far short did it fall"
        /// measurement the way true IK gave for free. That approximation is left
        /// as-is for the repeating Reach By Distance lashes (stretchToReach false);
        /// only the one-shot hero strike measures the actual leftover gap after the
        /// CCD bend and closes it with a scoped stretch - see stretchToReach/
        /// ApplyStretchToCloseGap below.
        ///
        /// Recomputed LIVE every frame - reset the chain to _restLocalRotation, then
        /// call ApplyDirectionalBend fresh with this frame's eased strength - rather
        /// than baking full poses once and Slerping per-bone between them (an even
        /// earlier version did that, and looked visibly wrong while mid-blend: per-
        /// bone independent Slerp between two very different whole-chain shapes
        /// doesn't preserve the chain's overall curve). Resetting to rest every
        /// frame is also what keeps this immune to the roll-drift/corkscrew bug a
        /// pose-caching approach had. The one Slerp that still happens is a short
        /// hand-off (handoffFraction) from wherever the chain actually is (startPose)
        /// onto rest itself at the very start - safe because rest is a single
        /// simple, well-formed target, not a fully bent shape.
        ///
        /// stretchToReach brings a scoped-down stretch mechanic back for ONE case:
        /// the hero's one-shot Snap At Camera On Settle attack, which needs to
        /// guarantee actual contact rather than CCD's approximation - see
        /// ApplyStretchToCloseGap. The repeating Reach By Distance lashes always
        /// pass false, so they stay exactly as approximate/CCD-only as before.
        /// </summary>
        private IEnumerator Lash(bool stretchToReach)
        {
            if (_bones.Length < 2) yield break;

            Vector3 aimPoint = AimPointWorld();
            Vector3 awayPoint = AwayPointWorld();
            Quaternion[] startPose = CaptureLocalRotations();
            bool touched = false;

            // Reset scale to identity up front, unconditionally - the windup/coil
            // phase below never touches scale at all, so without this, a stale
            // stretch left over from an EARLIER hero (stretchToReach) Lash on this
            // same tentacle would stay visibly stretched through this entire new
            // Lash's coil-back, only getting cleared once the strike phase's own
            // ApplyStretchToCloseGap call runs.
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localScale = Vector3.one;

            float coilEnd = Mathf.Clamp01(LashCoilFraction);
            float strikeEnd = Mathf.Clamp(coilEnd + LashStrikeFraction, coilEnd + 0.01f, 1f);
            const float handoffFraction = 0.3f; // fraction of the coil window spent on the startPose->rest hand-off before the coil bend itself starts
            float handoffEnd = coilEnd * handoffFraction;

            float elapsed = 0f;
            while (elapsed < LashDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, LashDuration));
                // Nothing left to animate once the strike itself finishes (no more
                // stretch to ease out now that CCD replaced FABRIK) - hand off to
                // idle immediately instead of sitting through a now-pointless hold,
                // which is exactly what used to read as a strange pause here.
                if (t >= strikeEnd) break;

                if (t < handoffEnd)
                {
                    _attackPhase = AttackPhase.Windup;
                    float localT = handoffEnd > 0.0001f ? t / handoffEnd : 1f;
                    ApplyBlendedPose(startPose, _restLocalRotation, EaseInQuad(localT));
                }
                else if (t < coilEnd)
                {
                    _attackPhase = AttackPhase.Windup;
                    float localT = (t - handoffEnd) / Mathf.Max(0.0001f, coilEnd - handoffEnd);
                    ApplyLocalRotations(_restLocalRotation);
                    ApplyDirectionalBend(awayPoint, LashCoilStrength * EaseInQuad(localT), LashBendCurve);
                }
                else
                {
                    _attackPhase = AttackPhase.Strike;
                    float localT = (t - coilEnd) / (strikeEnd - coilEnd);
                    float eased = EaseOutBack(localT, LashOvershoot); // can exceed 1 briefly - the crack (ApplyDirectionalBend is unclamped, so this actually overshoots)

                    // Rebuild the fully coiled baseline fresh, then strike live on
                    // top of it - the strike sweeps through a coherent, correctly-
                    // curving shape throughout, not just at its start and end.
                    ApplyLocalRotations(_restLocalRotation);
                    ApplyDirectionalBend(awayPoint, LashCoilStrength, LashBendCurve);
                    ApplyDirectionalBend(aimPoint, SnapPeakStrength * Mathf.Max(0f, eased), LashBendCurve);
                    ClampTipFromCamera(aimPoint);
                    // Eases in alongside the strike's own overshoot weight, rather
                    // than popping to full stretch in one frame - reads as the tip
                    // reaching AND stretching that last bit together, not two
                    // separate motions. Called unconditionally (strength 0 when
                    // stretchToReach is false), not skipped - it resets scale to
                    // identity every time regardless, which matters if THIS same
                    // tentacle also has a hero stretch from an earlier Lash still
                    // lingering (SnapAtCameraOnSettle + ReachByDistance can both be
                    // on at once) - skipping the call entirely would leave that
                    // stale stretch applied forever on every later, non-stretching
                    // repeat lash.
                    ApplyStretchToCloseGap(aimPoint, stretchToReach ? Mathf.Clamp01(eased) : 0f);
                    if (!touched && localT >= 0.8f) { TouchCamera(); touched = true; }
                }

                yield return null;
            }

            ApplyLocalRotations(_restLocalRotation);
            ApplyDirectionalBend(awayPoint, LashCoilStrength, LashBendCurve);
            ApplyDirectionalBend(aimPoint, SnapPeakStrength, LashBendCurve);
            ClampTipFromCamera(aimPoint);
            ApplyStretchToCloseGap(aimPoint, stretchToReach ? 1f : 0f); // unconditional - see the matching call above for why
            if (!touched) TouchCamera();

            // Hand off straight into ApplyIdlePose's blend-in (see ApplyIdlePose/
            // _idleBlendFromRotation) for the recoil back to baseline/idle, instead of
            // animating a separate recoil pass here - one continuous motion into idle,
            // not a second discrete step the viewer has to wait through.
            for (int i = 0; i < _bones.Length; i++)
                _idleBlendFromRotation[i] = _bones[i].localRotation;
            _idleBlendWeight = 0f;
            _attackPhase = AttackPhase.None;
        }

        /// <summary>Splats SlimeOverlay (explicit, or auto-found on the resolved camera) once the strike's overshoot crosses 80% of the way through, timed to read as contact - CCD doesn't guarantee the tip lands exactly on the aim point (see Lash()), so this is a timing approximation, not a measured touch.</summary>
        private void TouchCamera()
        {
            var overlay = SlimeOverlay != null ? SlimeOverlay : ResolveCamera() != null ? ResolveCamera().GetComponentInChildren<CameraSlimeOverlay>() : null;
            if (overlay != null) overlay.Splat();
        }

        /// <summary>
        /// Safety clamp against the tip actually ending up closer to the camera than
        /// MinStrikeDistance. CCD has no built-in concept of the chain's total reach
        /// vs. distance to the target the way true IK/FABRIK did - each segment just
        /// independently rotates toward pointing at the aim point from its own
        /// (moving) position, which for a long chain at full/overshot strength can
        /// converge past a nearby target rather than stopping at it - the tip swings
        /// through/uncomfortably close to the lens instead of stopping in front of
        /// it. Losing that "never overshoots" guarantee was a known trade-off of
        /// dropping FABRIK for a nicer-looking curve (see Lash()) - this is the
        /// targeted fix for the one part of that trade-off that's a hard
        /// requirement (never actually clip the camera), not just a nice-to-have,
        /// without reintroducing full IK for everything else.
        ///
        /// Pass the SAME aimPoint the strike itself is already targeting (already
        /// guaranteed to sit exactly MinStrikeDistance out along the camera's
        /// forward axis - see AimPointWorld()), not a point derived from the tip's
        /// current position - an earlier version did that and it broke down exactly
        /// when it mattered most: with the tip already at/through the camera, "the
        /// direction from the camera to the tip" is undefined/unstable, which is
        /// precisely the worst-case this is meant to catch. Iterates (CCD toward
        /// that same fixed target, checking distance after each pass) rather than
        /// correcting once - a single ApplyDirectionalBend pass isn't precise enough
        /// on a chain built from a handful of large segments, where a small angular
        /// error becomes a large positional one; SolveFABRIK needed multiple
        /// iterations to converge for the same reason.
        /// </summary>
        private void ClampTipFromCamera(Vector3 aimPoint)
        {
            Transform cam = ResolveCamera();
            if (cam == null || _bones.Length == 0) return;

            const int maxIterations = 8;
            for (int i = 0; i < maxIterations; i++)
            {
                float dist = Vector3.Distance(_bones[_bones.Length - 1].position, cam.position);
                if (dist >= MinStrikeDistance) return;
                ApplyDirectionalBend(aimPoint, 1f, LashBendCurve);
            }
        }

        /// <summary>
        /// Stretches the tip-side SnapStretchTipFraction of the chain's segments
        /// (compounding-corrected, capped at SnapMaxStretchMultiplier - same
        /// correction an earlier FABRIK-based version of this class needed: Unity's
        /// localScale compounds down a parent-child chain, so N consecutive
        /// stretched bones each at factor F compound to F^N, not N*F, without
        /// dividing each one's own scale by the running product of everything
        /// upstream) so the tip closes whatever gap is left to targetPoint after
        /// the strike's CCD bend - CCD only approximates the target (see Lash()'s
        /// doc), which is fine for the repeating Reach By Distance lashes but not
        /// for the hero's one-shot Snap At Camera On Settle money-shot, which needs
        /// guaranteed actual contact. strength (0-1) scales the gap being closed,
        /// so this can ease in alongside the strike's own eased weight rather than
        /// popping to full stretch in one frame - pass 1 for the final, settled
        /// amount. Resets every bone's scale to (1,1,1) first and measures the gap
        /// against THAT reset pose, so calling this repeatedly (once per frame)
        /// never compounds against a previous frame's own stretch.
        /// </summary>
        private void ApplyStretchToCloseGap(Vector3 targetPoint, float strength)
        {
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localScale = Vector3.one;

            int segmentCount = _bones.Length - 1;
            if (segmentCount <= 0 || strength <= 0f) return;

            float gap = Vector3.Distance(_bones[_bones.Length - 1].position, targetPoint) * Mathf.Clamp01(strength);
            if (gap <= 0.001f) return;

            int stretchCount = Mathf.Clamp(Mathf.RoundToInt(segmentCount * SnapStretchTipFraction), 1, segmentCount);
            int firstStretchedSegment = segmentCount - stretchCount;
            float perSegment = gap / stretchCount;

            float upstreamScale = 1f;
            for (int i = firstStretchedSegment; i < segmentCount; i++)
            {
                float restLength = _boneSegmentLength[i];
                float desiredRatio = restLength > 0.0001f ? (restLength + perSegment) / restLength : 1f;
                float localScale = upstreamScale > 0.0001f ? desiredRatio / upstreamScale : desiredRatio;
                localScale = Mathf.Min(localScale, SnapMaxStretchMultiplier);
                _bones[i].localScale = new Vector3(1f, localScale, 1f);
                upstreamScale *= localScale;
            }
        }

        private Quaternion[] CaptureLocalRotations()
        {
            var r = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) r[i] = _bones[i].localRotation;
            return r;
        }

        private void ApplyLocalRotations(Quaternion[] rotations)
        {
            for (int i = 0; i < _bones.Length; i++) _bones[i].localRotation = rotations[i];
        }

        /// <summary>SlerpUnclamped, not Slerp - t is allowed (and expected, via EaseOutBack) to briefly exceed 1, which is what actually produces the overshoot-past-target "crack" rather than just clamping it away.</summary>
        private void ApplyBlendedPose(Quaternion[] from, Quaternion[] to, float t)
        {
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = Quaternion.SlerpUnclamped(from[i], to[i], t);
        }

        private Transform ResolveCamera()
        {
            return CameraOverride != null ? CameraOverride : Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>
        /// Horizontal-only distance (X/Z), ignoring height - "the viewer walked up
        /// close" shouldn't depend on how high they're holding the phone relative to
        /// this tentacle. Measured from the TIP bone's current position, not
        /// transform.position (the base/root) - for a rooftop tentacle the base can
        /// sit 10-20m away from anywhere a viewer actually walks, so distance-to-
        /// base never reads as "close" at any sane threshold; the tip is what
        /// physically hangs down near the viewer and is what actually has to reach
        /// them, so it's the only sensible reference point for this check.
        /// </summary>
        private float HorizontalDistanceToCamera(Transform cam)
        {
            Vector3 a = _bones != null && _bones.Length > 0 ? _bones[_bones.Length - 1].position : transform.position;
            Vector3 b = cam.position;
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private bool IsCameraWithinEngageDistance()
        {
            Transform cam = ResolveCamera();
            return cam != null && HorizontalDistanceToCamera(cam) <= SnapDistance;
        }

        private bool IsCameraWithinHoldDistance()
        {
            Transform cam = ResolveCamera();
            // Clamped to at least SnapDistance, regardless of what SnapFadeDistance
            // is set to - if the disengage range ends up SMALLER than the engage
            // range (e.g. left over from testing at a different distance), the
            // engagement ends the moment the camera drifts past this tighter ring,
            // but ApplyIdlePose's own engage check (the wider SnapDistance ring) is
            // immediately satisfied again - firing a brand new Lash within about a
            // second of the last one ending, which reads as a second, unrelated
            // strike snapping in right as the first one settles. Enforcing the
            // documented SnapFadeDistance >= SnapDistance invariant here means that
            // mismatch can't cause this any more, however the sliders get set.
            return cam != null && HorizontalDistanceToCamera(cam) <= Mathf.Max(SnapDistance, SnapFadeDistance);
        }

        /// <summary>
        /// Debounced version of IsCameraWithinHoldDistance for AttackRoutine's own
        /// continuation checks - resets the "how long has it been outside range"
        /// timer every frame the camera IS within range, but only actually reports
        /// "stop engaging" once that timer has exceeded SnapFadeGraceDuration
        /// continuously. Call once per frame (it has side effects on
        /// _timeOutsideHoldDistance) - never call this more than once in the same
        /// frame or the timer accumulates wrong.
        /// </summary>
        private bool ShouldStayEngaged()
        {
            if (IsCameraWithinHoldDistance())
            {
                _timeOutsideHoldDistance = 0f;
                return true;
            }
            _timeOutsideHoldDistance += Time.deltaTime;
            return _timeOutsideHoldDistance < SnapFadeGraceDuration;
        }

        private Vector3 AimPointWorld()
        {
            Transform cam = ResolveCamera();
            if (cam == null) return transform.position;
            // Always at least MinStrikeDistance out along the camera's own forward
            // axis - never at the camera's exact position, which would otherwise let
            // strikes pierce straight through the lens. For a standard perspective
            // camera a point along its own forward axis projects to screen-centre, so
            // this also keeps the tip centred in view rather than off to one side.
            float dist = Mathf.Max(MinStrikeDistance, ReachAimDistance);
            return cam.position + cam.forward * dist;
        }

        /// <summary>The aim point mirrored through this tentacle's base - bending toward this instead reads as coiling away from the camera.</summary>
        private Vector3 AwayPointWorld()
        {
            Vector3 basePos = transform.position;
            return basePos - (AimPointWorld() - basePos);
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float Smoothstep(float t) => t * t * (3f - 2f * t);
        /// <summary>Slow start, accelerating - used for the coil (tension building into the pull-back rather than an even, mechanical lerp).</summary>
        private static float EaseInQuad(float t) => t * t;
        /// <summary>
        /// Standard "ease out back" - rises to 1 while briefly overshooting past it,
        /// then settles to exactly 1 at t=1. overshoot controls how far past 1 the
        /// bump goes (0 = no overshoot, a plain ease). This is the whole "whip-crack"
        /// shape for Lash()'s strike phase in one function - rise, crack past the
        /// target, settle - rather than a separate overshoot-then-correct step.
        /// </summary>
        private static float EaseOutBack(float t, float overshoot)
        {
            float c1 = Mathf.Max(0f, overshoot) * 3f;
            float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        /// <summary>A local-space vector for ExtendAxis (X/Y/Z -> right/up/forward).</summary>
        private Vector3 ExtendAxisVector()
        {
            switch (ExtendAxis)
            {
                case Axis.X: return Vector3.right;
                case Axis.Y: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        /// <summary>
        /// Perpendicular to both ExtendAxis and world-up, so curling around it sweeps
        /// the tip through the vertical plane containing ExtendAxis - reads as
        /// unrolling up and out along that direction. Falls back to Vector3.right if
        /// ExtendAxis is itself (close to) world-up, where that cross product degenerates.
        /// </summary>
        private Vector3 BaseCurlAxis()
        {
            Vector3 perpendicular = Vector3.Cross(ExtendAxisVector(), Vector3.up);
            return perpendicular.sqrMagnitude > 0.01f ? perpendicular.normalized : Vector3.right;
        }

        /// <summary>_restLocalScale with ExtendAxis multiplied by axisValue (1 = that axis's own full/authored size) - the other two axes stay at their full authored size throughout.</summary>
        private Vector3 AxisScale(float axisValue)
        {
            switch (ExtendAxis)
            {
                case Axis.X: return new Vector3(axisValue * _restLocalScale.x, _restLocalScale.y, _restLocalScale.z);
                case Axis.Y: return new Vector3(_restLocalScale.x, axisValue * _restLocalScale.y, _restLocalScale.z);
                default: return new Vector3(_restLocalScale.x, _restLocalScale.y, axisValue * _restLocalScale.z);
            }
        }

        private void ApplyGrowPose(float overallT)
        {
            // Whole-object rise + scale-in: emerges up out of the roof, starting smaller
            // than its authored size, growing to full size and height as it grows.
            // Same GrowCurve easing as the uncurl so all three finish together.
            // RiseDistance = 0 / RiseStartScale = 1 make these no-ops.
            float riseT = GrowCurve.Evaluate(overallT);
            transform.localPosition = Vector3.Lerp(
                _restLocalPosition - Vector3.up * RiseDistance,
                _restLocalPosition,
                riseT);
            transform.localScale = Vector3.Lerp(_restLocalScale * RiseStartScale, _restLocalScale, riseT);

            for (int i = 0; i < _bones.Length; i++)
            {
                float baseToTip = BaseToTipWeight(i);

                // Bones further down the chain wait longer before unwinding.
                float boneStart = i * PerBoneGrowDelay;
                float boneT = Mathf.Clamp01((overallT - boneStart) / Mathf.Max(0.0001f, 1f - boneStart));
                float unwind = GrowCurve.Evaluate(boneT);

                Quaternion curled = Quaternion.AngleAxis(CurledAngle * baseToTip, _curlAxis) * _restLocalRotation[i];
                _bones[i].localRotation = Quaternion.Slerp(curled, IdleRotationForBone(i), unwind);
            }
        }

        private void ApplyIdlePose()
        {
            // While a Lash is actually running it has full, exclusive control of
            // every bone's rotation, so idle noise must not touch them at all: this
            // used to only guard the reach chase, so the idle noise loop was
            // overwriting every bone with a fresh, constantly-shifting pose every
            // single frame regardless of an active attack, and the attack's own
            // rotation math was then computing its delta from that noisy, ever-
            // changing baseline instead of continuing smoothly - a real, chaotic
            // root cause behind erratic/wrapping motion during attacks.
            if (_attackPhase != AttackPhase.None) return;

            // Keeps tracking ReachStrength even DURING an engagement's between-
            // strike gaps (computed here unconditionally; only the guard below
            // decides whether it's actually acted on this frame) - so _reachCurrent
            // is already close to right by the time an engagement fully ends,
            // rather than being frozen at whatever stale value it had from BEFORE
            // the engagement even started and then popping straight to that on
            // release. That pop-to-a-stale-value was the actual bug: the baseline
            // reach lean is a completely separate bend from the Lash itself (no
            // reset-to-rest, no shared easing), so resuming it with zero easing -
            // which happened whenever ReachStrength was non-zero - looked like a
            // second, differently-shaped bend snapping in right as the attack
            // finished, distinct from the Lash's own strike shape.
            _reachCurrent = Mathf.MoveTowards(_reachCurrent, ReachStrength, Time.deltaTime * ReachSmoothing);

            // This frame's full idle target - noise/sine/jitter, THEN the baseline
            // reach lean bent on top - computed ONCE as a single pose, so the reach
            // lean is folded into the SAME blend-in ramp as everything else below
            // instead of being applied separately (with no easing of its own) only
            // once that ramp already finished. Recomputed fresh from rest via
            // IdleRotationForBone every frame (never built on the previous frame's
            // result), so this can't drift/compound the way live-baking the Lash's
            // coil/strike keyframes used to.
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = IdleRotationForBone(i);
            if (_reachCurrent > 0.001f)
            {
                Vector3 reachAimPoint = AimPointWorld();
                ApplyDirectionalBend(reachAimPoint, _reachCurrent);
                ClampTipFromCamera(reachAimPoint); // same clipping risk as the Lash strike (see its doc) - the baseline lean bends toward the camera too
            }
            Quaternion[] idleTarget = CaptureLocalRotations();

            // _isEngaged spans the WHOLE attack engagement, including the gap
            // between repeat strikes (where _attackPhase itself is already back to
            // None) - capping the blend ceiling at EngagedIdleDamping instead of
            // 1 while engaged means that gap gets a light tremor rather than full
            // idle sway (which read as the tentacle randomly rotating/drifting
            // between hits - the original complaint _isEngaged was added for) or a
            // complete freeze (which instead read as a dead, frozen prop, not a
            // predator holding tension before its next strike - too far the other
            // way). It's also what stops the ReachByDistance check below from
            // firing a SECOND, overlapping AttackRoutine every frame of that gap.
            float blendCeiling = _isEngaged ? EngagedIdleDamping : 1f;

            if (_idleBlendWeight < blendCeiling)
            {
                // Ease from the pose the attack just left us in toward idleTarget
                // (which is itself moving every frame - noise/sine/jitter/reach all
                // shift continuously), rather than snapping straight to it - see
                // IdleBlendInDuration/_idleBlendFromRotation (set at the end of
                // Lash()).
                _idleBlendWeight = Mathf.Min(blendCeiling, _idleBlendWeight + Time.deltaTime / Mathf.Max(0.0001f, IdleBlendInDuration));
                for (int i = 0; i < _bones.Length; i++)
                    _bones[i].localRotation = Quaternion.Slerp(_idleBlendFromRotation[i], idleTarget[i], _idleBlendWeight);
            }
            else if (_isEngaged)
            {
                // Held at the damped ceiling - recomputed fresh from the fixed
                // post-strike pose every frame, so this can't drift/compound either.
                for (int i = 0; i < _bones.Length; i++)
                    _bones[i].localRotation = Quaternion.Slerp(_idleBlendFromRotation[i], idleTarget[i], _idleBlendWeight);
            }
            // else: idleTarget (already the full idle+reach pose) is already applied to the bones above - nothing further to do.

            if (_isEngaged) return; // no re-trigger check while an engagement (including its between-strike gaps) is still in progress

            if (ReachByDistance && IsCameraWithinEngageDistance())
                StartCoroutine(AttackRoutine(holdUntilFar: true));
        }

        private Quaternion IdleRotationForBone(int index)
        {
            return _restLocalRotation[index] * IdleOffsetForBone(index);
        }

        /// <summary>
        /// Just the noise/sine/jitter wobble quaternion, split out from
        /// IdleRotationForBone so ApplyPunchIdlePose can layer it onto a
        /// TEMPORARILY straightened basis instead of always the true curled rest
        /// pose - IdleRotationForBone itself is unchanged for every other caller.
        /// </summary>
        private Quaternion IdleOffsetForBone(int index)
        {
            float weight = BaseToTipWeight(index);
            float t = Time.time;
            float chainPhase = index * 0.6f; // spacing between bones for the travelling-wave look

            float noiseX = (Mathf.PerlinNoise(_seedX, t * NoiseSpeed + index * 0.37f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(_seedY, t * NoiseSpeed + index * 0.37f) - 0.5f) * 2f;
            float noiseZ = (Mathf.PerlinNoise(_seedZ, t * NoiseSpeed + index * 0.37f) - 0.5f) * 2f;

            // Different frequency/phase multipliers per axis so the sine layer reads as
            // organic 3D motion rather than a single flat plane of oscillation.
            float sineX = Mathf.Sin(t * SineSpeed * Mathf.PI * 2f - chainPhase * SineFrequency);
            float sineY = Mathf.Sin(t * SineSpeed * 1.3f * Mathf.PI * 2f - chainPhase * SineFrequency * 1.1f + Mathf.PI / 3f);
            float sineZ = Mathf.Sin(t * SineSpeed * 0.8f * Mathf.PI * 2f - chainPhase * SineFrequency * 0.9f + Mathf.PI / 2f);

            // Fast/sharp jitter layer on top - this is what keeps it from reading as a
            // slow gentle sway and makes it feel twitchy/alive/aggressive instead.
            float jitterX = (Mathf.PerlinNoise(_jitterSeedX, t * JitterSpeed + index * 0.53f) - 0.5f) * 2f;
            float jitterY = (Mathf.PerlinNoise(_jitterSeedY, t * JitterSpeed + index * 0.53f) - 0.5f) * 2f;
            float jitterZ = (Mathf.PerlinNoise(_jitterSeedZ, t * JitterSpeed + index * 0.53f) - 0.5f) * 2f;

            float pitch = (noiseX * NoiseAmplitudeDegrees + sineX * SineAmplitudeDegrees + jitterX * JitterAmplitudeDegrees) * weight;
            float yaw = (noiseY * NoiseAmplitudeDegrees + sineY * SineAmplitudeDegrees + jitterY * JitterAmplitudeDegrees) * weight;
            float roll = (noiseZ * NoiseAmplitudeDegrees * 0.6f + sineZ * SineAmplitudeDegrees * 0.6f + jitterZ * JitterAmplitudeDegrees * 0.6f) * weight;

            return Quaternion.Euler(pitch, yaw, roll);
        }

        /// <summary>
        /// Blends each bone from dead-straight rest toward idle noise/sine/jitter,
        /// same as the old ApplyRestToIdleBlend did - except the REST BASIS itself
        /// is also blended, from a forcibly straightened chain (identity local
        /// rotation relative to parent; the base bone's own authored anchor angle
        /// is left untouched) up to the tentacle's true authored curled rest pose,
        /// controlled by straightenWeight. Used by UpdatePunch (see
        /// PunchStraightenTip) so a curled/hooked tip is undistorted while the
        /// axis-aligned Punch scale plays, then curls into its natural resting
        /// shape afterward, with idle noise layered on top of whichever basis is
        /// currently active either way.
        /// </summary>
        private void ApplyPunchIdlePose(float straightenWeight, float idleWeight)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                Quaternion basis = i == 0 || straightenWeight <= 0f
                    ? _restLocalRotation[i]
                    : Quaternion.Slerp(_restLocalRotation[i], Quaternion.identity, straightenWeight);
                Quaternion idlePose = basis * IdleOffsetForBone(i);
                _bones[i].localRotation = Quaternion.Slerp(basis, idlePose, idleWeight);
            }
        }

        /// <summary>
        /// Bends the chain toward (or, given AwayPointWorld, away from) a world point
        /// via CCD (cyclic coordinate descent) - the only bend method in this class
        /// now (an earlier version also had true IK/FABRIK for the strike, dropped
        /// deliberately for a more organic-looking curve - see Lash()). strength is
        /// scaled per-segment by SegmentAttackInfluence (curveExponent 1 = linear
        /// ramp within LashBaseAnchorFraction; higher concentrates the bend toward
        /// the tip, holding the base-side bones stiller for longer). Not clamped to
        /// 1 - Lash()'s strike phase deliberately passes strength slightly above 1
        /// for its whip-crack overshoot, and SlerpUnclamped below is what actually
        /// lets that show up rather than being clamped away. Walks base->tip, at each
        /// bone measuring its *current* world direction toward the next bone and
        /// rotating that toward the target by a weighted slice, so each later bone
        /// sees the already-bent result of the ones before it - the bend accumulates
        /// toward the target instead of a fixed fraction applied identically
        /// everywhere. Never lands exactly on targetPoint (that's the trade-off for
        /// the nicer curve) - close enough that the aim point still reads as "the
        /// tip reaching for the camera."
        /// </summary>
        private void ApplyDirectionalBend(Vector3 targetPoint, float strength, float curveExponent = 1f)
        {
            if (strength <= 0f) return;

            for (int i = 0; i < _bones.Length - 1; i++)
            {
                float weight = SegmentAttackInfluence(i, curveExponent) * strength;
                if (weight <= 0f) continue;

                Vector3 currentDir = (_bones[i + 1].position - _bones[i].position).normalized;
                Vector3 targetDir = (targetPoint - _bones[i].position).normalized;

                Quaternion currentToTarget = Quaternion.FromToRotation(currentDir, targetDir);
                Quaternion stepped = Quaternion.SlerpUnclamped(Quaternion.identity, currentToTarget, weight);

                _bones[i].rotation = stepped * _bones[i].rotation;
            }
        }
    }
}
