using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Drives the WallBreakthrough shader's animated hole-opening on a quad decal
    /// spawned flush against the (already real-world-aligned) proxy mesh, at the same
    /// point a TentacleController prefab emerges from. Call Open() alongside
    /// TentacleController.Grow() so the crack spreads and the hole opens right as the
    /// tentacle pushes through.
    ///
    /// Sequence on Open(): a spiderweb pre-crack flash across the whole decal, then
    /// the hole itself grows outward with a glowing crack ring at its edge that
    /// settles once fully open.
    ///
    /// No device plane detection involved - the "wall" is the proxy mesh already
    /// anchored to the real facade via image tracking, so this decal is just placed at
    /// a known point/orientation on that mesh, same trick as the tentacle burst points.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WallHoleEffect : MonoBehaviour
    {
        public enum State { Closed, Opening, Open }

        [Header("Pre-crack flash (fires first, on trigger)")]
        public float PreCrackRiseDuration = 0.08f;
        public float PreCrackFadeDuration = 0.25f;

        [Header("Hole opening")]
        public float OpenDuration = 1.0f;
        public AnimationCurve ProgressCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Glow pulse (crack catching light as it spreads, then settling)")]
        public float GlowRiseDuration = 0.35f;
        public float GlowFadeDuration = 1.2f;

        [Tooltip("Auto-trigger Open() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _props;
        private float _openStartTime = -1f;

        private static readonly int ActiveId = Shader.PropertyToID("_Active");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int GlowId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int PreCrackId = Shader.PropertyToID("_PreCrackIntensity");

        public State CurrentState { get; private set; } = State.Closed;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _props = new MaterialPropertyBlock();
            Hide();
        }

        private void Start()
        {
            if (OpenOnStart) Invoke(nameof(Open), OpenOnStartDelay);
        }

        /// <summary>Starts the pre-crack flash + hole-opening. Call alongside TentacleController.Grow().</summary>
        public void Open()
        {
            CurrentState = State.Opening;
            _openStartTime = Time.time;
        }

        /// <summary>Resets to fully closed/invisible (e.g. before re-triggering a burst point).</summary>
        public void Hide()
        {
            CurrentState = State.Closed;
            SetProps(0f, 0f, 0f, 0f);
        }

        private void Update()
        {
            if (CurrentState == State.Closed) return;

            float elapsed = Time.time - _openStartTime;

            float preCrack = elapsed < PreCrackRiseDuration
                ? Mathf.Clamp01(elapsed / PreCrackRiseDuration)
                : Mathf.Clamp01(1f - (elapsed - PreCrackRiseDuration) / PreCrackFadeDuration);

            float progress = Mathf.Clamp01(elapsed / OpenDuration);
            float curvedProgress = ProgressCurve.Evaluate(progress);

            float glow = elapsed < GlowRiseDuration
                ? Mathf.Clamp01(elapsed / GlowRiseDuration)
                : Mathf.Clamp01(1f - (elapsed - GlowRiseDuration) / GlowFadeDuration);

            SetProps(1f, curvedProgress, glow, preCrack);

            if (progress >= 1f && elapsed >= GlowRiseDuration + GlowFadeDuration)
                CurrentState = State.Open;
        }

        private void SetProps(float active, float progress, float glow, float preCrack)
        {
            _renderer.GetPropertyBlock(_props);
            _props.SetFloat(ActiveId, active);
            _props.SetFloat(ProgressId, progress);
            _props.SetFloat(GlowId, glow);
            _props.SetFloat(PreCrackId, preCrack);
            _renderer.SetPropertyBlock(_props);
        }
    }
}
