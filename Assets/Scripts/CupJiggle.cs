using UnityEngine;
using Zappar;

namespace ARReveal
{
    /// <summary>
    /// Pops in (scaled from zero) when the tracked image is found, hides again when
    /// lost, and idles with a layered-noise wobble while visible. Scale pops toward
    /// whatever scale you set in the Editor (captured at Awake), and rotation wobbles
    /// relative to whatever rotation you set - neither is overwritten with a fixed
    /// value, so placement/scale you did by hand is respected.
    /// </summary>
    [RequireComponent(typeof(ZapparImageTrackingTarget))]
    public class CupJiggle : MonoBehaviour
    {
        [Header("Target content")]
        [Tooltip("The object that pops/jiggles - usually a child holding the mesh.")]
        public Transform Content;

        [Header("Pop-in")]
        public float PopDuration = 0.4f;
        public AnimationCurve PopCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Idle jiggle")]
        public float RotationAmplitudeDegrees = 8f;
        public float RotationSpeed = 1.5f;
        public float ScaleWobbleAmount = 0.06f;
        public float ScaleWobbleSpeed = 1.9f;

        private Vector3 _baseScale;
        private Quaternion _baseRotation;
        private float _seenTime = -1f;
        private float _seedX, _seedY, _seedZ;
        private ZapparImageTrackingTarget _target;

        private void Awake()
        {
            if (Content == null) Content = transform;
            _baseScale = Content.localScale;
            _baseRotation = Content.localRotation;
            _seedX = Random.Range(0f, 100f);
            _seedY = Random.Range(0f, 100f);
            _seedZ = Random.Range(0f, 100f);
            _target = GetComponent<ZapparImageTrackingTarget>();
            Content.localScale = Vector3.zero;
        }

        private void OnEnable()
        {
            if (_target != null)
            {
                _target.OnSeenEvent.AddListener(HandleSeen);
                _target.OnNotSeenEvent.AddListener(HandleNotSeen);
            }
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.OnSeenEvent.RemoveListener(HandleSeen);
                _target.OnNotSeenEvent.RemoveListener(HandleNotSeen);
            }
        }

        private void HandleSeen()
        {
            _seenTime = Time.time;
        }

        private void HandleNotSeen()
        {
            _seenTime = -1f;
            Content.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (_seenTime < 0f) return;

            float elapsed = Time.time - _seenTime;

            if (elapsed < PopDuration)
            {
                float t = PopCurve.Evaluate(elapsed / PopDuration);
                Content.localScale = _baseScale * t;
                Content.localRotation = _baseRotation;
                return;
            }

            Content.localScale = _baseScale * (1f + ScaleWobbleAmount *
                (Mathf.PerlinNoise(_seedX, Time.time * ScaleWobbleSpeed) - 0.5f) * 2f);

            float rx = (Mathf.PerlinNoise(_seedX, Time.time * RotationSpeed) - 0.5f) * 2f;
            float ry = (Mathf.PerlinNoise(_seedY, Time.time * RotationSpeed) - 0.5f) * 2f;
            float rz = (Mathf.PerlinNoise(_seedZ, Time.time * RotationSpeed) - 0.5f) * 2f;
            Content.localRotation = _baseRotation * Quaternion.Euler(
                rx * RotationAmplitudeDegrees,
                ry * RotationAmplitudeDegrees,
                rz * RotationAmplitudeDegrees);
        }
    }
}
