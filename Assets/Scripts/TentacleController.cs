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
    ///   3. Reach-toward-camera - blends in a bend toward a point out along the camera's
    ///      forward direction (i.e. the centre of the screen, not the camera's physical
    ///      position), computed in local space so it's correct regardless of whether the
    ///      camera or the tracked anchor is the one actually moving.
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
        public float JabDuration = 0.08f;
        [Tooltip("How far along ExtendAxis the jab reaches, as a fraction of full length - just enough for the tip to break through.")]
        [Range(0f, 1f)] public float JabDistance = 0.3f;

        [Header("2. Retract - pulls back partway, still straight")]
        public float RetractDuration = 0.1f;
        [Tooltip("Where it pulls back to, as a fraction of full length - should be less than JabDistance so it visibly recoils, but doesn't need to go all the way back to 0.")]
        [Range(0f, 1f)] public float RetractDistance = 0.15f;

        [Header("3. Burst - rushes out past full size, idle wakes up here")]
        public float BurstDuration = 0.22f;
        [Tooltip("Scale multiplier along ExtendAxis at the peak of the burst, before settling back to 1 - the 'snap' overshoot.")]
        public float PunchOvershoot = 1.15f;
        [Tooltip("Time easing back down from the overshoot peak to normal size - idle motion reaches full strength exactly when this finishes.")]
        public float SettleDuration = 0.25f;

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
        [Tooltip("0 = ignore camera entirely, 1 = fully bend toward the aim point.")]
        [Range(0f, 1f)] public float ReachStrength = 0f;
        public float ReachSmoothing = 3f;
        [Tooltip("0 = aim at the camera's exact position. >0 = aim at a point that far out along the camera's forward direction instead (matters once the camera is a moving AR phone, not this test rig).")]
        public float ReachAimDistance = 0f;
        public Transform CameraOverride; // falls back to Camera.main if unset

        private Transform[] _bones;
        private Quaternion[] _restLocalRotation;
        private Vector3 _restLocalScale;
        private Vector3 _restLocalPosition;
        private Vector3 _curlAxis = Vector3.right;
        private float _seedX, _seedY, _seedZ;
        private float _jitterSeedX, _jitterSeedY, _jitterSeedZ;
        private float _reachCurrent;
        private float _growStartTime = -1f;

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
            for (int i = 0; i < _bones.Length; i++)
                _restLocalRotation[i] = _bones[i].localRotation;
        }

        /// <summary>0 at the base bone, 1 at the tip - every motion layer is scaled by this so the base never moves.</summary>
        private float BaseToTipWeight(int index)
        {
            return _bones.Length <= 1 ? 1f : index / (float)(_bones.Length - 1);
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
            CurrentState = State.Growing;
            _growStartTime = Time.time;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
        }

        public void Hide()
        {
            CurrentState = State.Hidden;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
            else if (Style == GrowStyle.Unfold) ApplyGrowPose(0f);
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
                    if (overallT >= 1f) CurrentState = State.Idle;
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

            // Dead straight through the jab+retract wind-up (no idle at all), then
            // dials in from 0 to 1 across the burst+settle, so it's fully alive exactly
            // as it finishes rather than either frozen the whole time or fighting the
            // wind-up by swinging the tip around before the real punch even happens.
            float idleWeight = Mathf.Clamp01((elapsed - retractEnd) / Mathf.Max(0.0001f, totalPunch - retractEnd));
            ApplyRestToIdleBlend(idleWeight);

            if (elapsed >= totalPunch)
            {
                transform.localScale = _restLocalScale;
                CurrentState = State.Idle;
            }
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float Smoothstep(float t) => t * t * (3f - 2f * t);

        /// <summary>Blends each bone from dead-straight rest (0) to full idle noise/sine/jitter (1).</summary>
        private void ApplyRestToIdleBlend(float idleWeight)
        {
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = Quaternion.Slerp(_restLocalRotation[i], IdleRotationForBone(i), idleWeight);
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
            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = IdleRotationForBone(i);

            if (ReachStrength > 0f || _reachCurrent > 0.001f)
                ApplyReachBlend();
        }

        private Quaternion IdleRotationForBone(int index)
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

            Quaternion offset = Quaternion.Euler(pitch, yaw, roll);
            return _restLocalRotation[index] * offset;
        }

        private void ApplyReachBlend()
        {
            _reachCurrent = Mathf.MoveTowards(_reachCurrent, ReachStrength, Time.deltaTime * ReachSmoothing);
            if (_reachCurrent <= 0.001f) return;

            Transform cam = CameraOverride != null ? CameraOverride : Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;

            // 0 distance = aim at the camera's exact position. >0 = a point projected
            // out along its forward direction instead (for later, on a moving AR camera).
            Vector3 aimPointWorld = cam.position + cam.forward * ReachAimDistance;

            // Proper per-bone bend: walk base->tip, at each bone measure its *current*
            // world direction toward the next bone, rotate that toward the target by a
            // weighted slice, then move on - each later bone sees the already-bent
            // result of the ones before it, so the bend actually accumulates toward
            // the target instead of a fixed fraction applied identically everywhere.
            for (int i = 0; i < _bones.Length - 1; i++)
            {
                float weight = BaseToTipWeight(i) * _reachCurrent;
                if (weight <= 0f) continue;

                Vector3 currentDir = (_bones[i + 1].position - _bones[i].position).normalized;
                Vector3 targetDir = (aimPointWorld - _bones[i].position).normalized;

                Quaternion currentToTarget = Quaternion.FromToRotation(currentDir, targetDir);
                Quaternion stepped = Quaternion.Slerp(Quaternion.identity, currentToTarget, weight);

                _bones[i].rotation = stepped * _bones[i].rotation;
            }
        }
    }
}
