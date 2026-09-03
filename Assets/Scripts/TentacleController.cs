using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Procedural bone-chain tentacle: drives a chain of bones directly (no Blender IK
    /// needed at runtime) through three blended layers -
    ///   1. Grow - reveals the tentacle, either style:
    ///      - Unfold: bones start curled tight near the base and unwind outward
    ///        bone-by-bone as growth progresses. Reads as a slow uncurl - good for
    ///        rooftop tentacles that don't need to break through anything solid first.
    ///      - Punch: a jab-retract-burst sequence, purely through non-uniform scale on
    ///        ExtendAxis (the tentacle's own forward axis), anchored at the base (which
    ///        sits at the wall/hole) - the other two axes stay pinned at 1 throughout,
    ///        so it's always reaching further out, never puffing up in girth:
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

        [Header("Grow / unfold (Style = Unfold)")]
        public float GrowDuration = 1.2f;
        public AnimationCurve GrowCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("How tightly curled the tentacle is before it starts growing (degrees, scaled by base->tip weight).")]
        public float CurledAngle = 70f;
        [Tooltip("How much of the grow duration each successive bone waits before it starts unwinding - creates the outward ripple.")]
        [Range(0f, 1f)] public float PerBoneGrowDelay = 0.08f;

        [Header("Punch through (Style = Punch)")]
        [Tooltip("Which local axis the tentacle points forward along - this is the axis that gets extended. Pick whichever one actually looks right; there's no way to infer it automatically since it depends on how the rig was authored.")]
        public Axis ExtendAxis = Axis.X;

        [Header("1. Jab - short, straight, tip-only")]
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

            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
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

        /// <summary>Starts the reveal (unfurl or punch-through, per Style). Call this when the burst point activates.</summary>
        public void Grow()
        {
            CurrentState = State.Growing;
            _growStartTime = Time.time;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
        }

        public void Hide()
        {
            CurrentState = State.Hidden;
            if (Style == GrowStyle.Punch) transform.localScale = Vector3.zero;
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
                transform.localScale = Vector3.one;
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

        /// <summary>A Vector3 with axisValue on ExtendAxis and 1 on the other two.</summary>
        private Vector3 AxisScale(float axisValue)
        {
            switch (ExtendAxis)
            {
                case Axis.X: return new Vector3(axisValue, 1f, 1f);
                case Axis.Y: return new Vector3(1f, axisValue, 1f);
                default: return new Vector3(1f, 1f, axisValue);
            }
        }

        private void ApplyGrowPose(float overallT)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                float baseToTip = BaseToTipWeight(i);

                // Bones further down the chain wait longer before unwinding.
                float boneStart = i * PerBoneGrowDelay;
                float boneT = Mathf.Clamp01((overallT - boneStart) / Mathf.Max(0.0001f, 1f - boneStart));
                float unwind = GrowCurve.Evaluate(boneT);

                Quaternion curled = Quaternion.AngleAxis(CurledAngle * baseToTip, Vector3.right) * _restLocalRotation[i];
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
