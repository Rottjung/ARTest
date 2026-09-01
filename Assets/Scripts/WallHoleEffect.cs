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
    /// No device plane detection involved - the "wall" is the proxy mesh already
    /// anchored to the real facade via image tracking, so this decal is just placed at
    /// a known point/orientation on that mesh, same trick as the tentacle burst points.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WallHoleEffect : MonoBehaviour
    {
        public enum State { Closed, Opening, Open }

        [Header("Timing")]
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

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int GlowId = Shader.PropertyToID("_GlowIntensity");

        public State CurrentState { get; private set; } = State.Closed;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _props = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (OpenOnStart) Invoke(nameof(Open), OpenOnStartDelay);
        }

        /// <summary>Starts the crack-spread + hole-opening. Call alongside TentacleController.Grow().</summary>
        public void Open()
        {
            CurrentState = State.Opening;
            _openStartTime = Time.time;
        }

        /// <summary>Resets to fully closed/invisible (e.g. before re-triggering a burst point).</summary>
        public void Hide()
        {
            CurrentState = State.Closed;
            SetProps(0f, 0f);
        }

        private void Update()
        {
            if (CurrentState == State.Closed) return;

            float elapsed = Time.time - _openStartTime;
            float progress = Mathf.Clamp01(elapsed / OpenDuration);
            float curvedProgress = ProgressCurve.Evaluate(progress);

            float glow = elapsed < GlowRiseDuration
                ? Mathf.Clamp01(elapsed / GlowRiseDuration)
                : Mathf.Clamp01(1f - (elapsed - GlowRiseDuration) / GlowFadeDuration);

            SetProps(curvedProgress, glow);

            if (progress >= 1f && elapsed >= GlowRiseDuration + GlowFadeDuration)
                CurrentState = State.Open;
        }

        private void SetProps(float progress, float glow)
        {
            _renderer.GetPropertyBlock(_props);
            _props.SetFloat(ProgressId, progress);
            _props.SetFloat(GlowId, glow);
            _renderer.SetPropertyBlock(_props);
        }
    }
}
