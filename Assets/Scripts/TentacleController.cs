using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Procedural bone-chain tentacle: drives a chain of bones directly (no Blender IK
    /// needed at runtime) through three blended layers -
    ///   1. Grow/unfold - bones start curled tight near the base and unwind outward
    ///      bone-by-bone as growth progresses (not a uniform scale-up).
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

        [Header("Rig")]
        [Tooltip("First (base) bone in the chain - stays fixed. Children are auto-discovered by walking single-child hierarchy.")]
        public Transform RootBone;

        [Header("Grow / unfold")]
        public float GrowDuration = 1.2f;
        public AnimationCurve GrowCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("How tightly curled the tentacle is before it starts growing (degrees, scaled by base->tip weight).")]
        public float CurledAngle = 70f;
        [Tooltip("How much of the grow duration each successive bone waits before it starts unwinding - creates the outward ripple.")]
        [Range(0f, 1f)] public float PerBoneGrowDelay = 0.08f;

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

        /// <summary>Starts the unfurl. Call this when the burst point activates.</summary>
        public void Grow()
        {
            CurrentState = State.Growing;
            _growStartTime = Time.time;
        }

        public void Hide()
        {
            CurrentState = State.Hidden;
        }

        private void Update()
        {
            if (_bones == null || _bones.Length == 0) return;

            if (CurrentState == State.Growing)
            {
                float elapsed = Time.time - _growStartTime;
                float overallT = Mathf.Clamp01(elapsed / GrowDuration);
                ApplyGrowPose(overallT);
                if (overallT >= 1f) CurrentState = State.Idle;
                return;
            }

            if (CurrentState == State.Idle)
            {
                ApplyIdlePose();
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
