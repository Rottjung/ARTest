using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// A handful of small chunks that spawn at the hole and actually fall - gravity-
    /// approximated (no Rigidbody/physics), tumbling as they drop, fading out once
    /// they've cleared the shot rather than needing to detect a ground plane (this is
    /// an AR scene with no reliable "floor" to land on). Distinct from DebrisRing,
    /// which pops out and settles into a static ring around the hole - this is the
    /// stuff that keeps falling and disappears, for a sense of ongoing collapse rather
    /// than one frozen burst.
    ///
    /// Same no-physics, cheap-and-deterministic philosophy as DebrisRing, reusing its
    /// DebrisChunkFactory for the actual chunk meshes - fine for several burst points
    /// at once on WebGL.
    /// </summary>
    public class FallingRubble : MonoBehaviour
    {
        [Header("Chunks")]
        public int ChunkCount = 10;
        public float ChunkSize = 0.04f;
        [Range(0f, 1f)] public float Irregularity = 0.45f;
        public Vector2 SizeRange = new Vector2(0.5f, 1.4f);
        public Vector2 DepthScaleRange = new Vector2(0.3f, 0.6f);

        [Header("Where they start - roughly at the hole, a little spread")]
        public float StartRadius = 0.15f;

        [Header("Fall")]
        [Tooltip("How far (local units) a chunk drops before it's faded out - there's no ground to land on here, so this just needs to clear the shot.")]
        public float FallDistance = 1.2f;
        public float FallDuration = 1.4f;
        [Tooltip("Curve of downward progress over FallDuration - starting flat and accelerating reads as gravity, not a linear drop.")]
        public AnimationCurve FallCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Sideways drift, as a multiple of Start Radius, applied on top of the fall - a light outward scatter rather than dropping straight down.")]
        public float SidewaysDrift = 0.4f;
        [Range(0f, 1f)] public float PerChunkDelayJitter = 0.5f;
        [Tooltip("Degrees/second range each chunk tumbles at, picked once per chunk.")]
        public Vector2 TumbleSpeedRange = new Vector2(90f, 260f);

        [Header("Fade")]
        [Tooltip("Fraction of Fall Duration, at the end, spent shrinking to nothing rather than popping off abruptly.")]
        [Range(0f, 1f)] public float FadeOutFraction = 0.25f;

        [Header("Look")]
        public Material ChunkMaterial;
        [Range(0f, 0.5f)] public float ColorJitter = 0.12f;

        [Tooltip("Auto-trigger Open() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        private struct Chunk
        {
            public Transform T;
            public Vector3 StartLocalPos;
            public Vector3 RestLocalScale;
            public Vector3 DriftDir;
            public Vector3 TumbleAxis;
            public float TumbleSpeed;
            public float StartDelay;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private bool _open;
        private float _openStartTime;

        private void Awake()
        {
            var rng = new System.Random(GetInstanceID());
            Color baseColor = ChunkMaterial != null && ChunkMaterial.HasProperty(BaseColorId)
                ? ChunkMaterial.GetColor(BaseColorId)
                : new Color(0.5f, 0.48f, 0.45f);

            var mpb = new MaterialPropertyBlock();

            for (int i = 0; i < ChunkCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radius = (float)rng.NextDouble() * StartRadius;
                Vector3 startPos = new Vector3(
                    Mathf.Cos(angle) * radius,
                    (float)rng.NextDouble() * StartRadius * 0.5f,
                    Mathf.Sin(angle) * radius);

                float size = ChunkSize * Mathf.Lerp(SizeRange.x, SizeRange.y, (float)rng.NextDouble());
                float depthScale = Mathf.Lerp(DepthScaleRange.x, DepthScaleRange.y, (float)rng.NextDouble());

                var go = new GameObject("Rubble_" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = startPos;
                go.transform.localRotation = Random.rotation;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = DebrisChunkFactory.Build(size, Irregularity, depthScale, rng);
                var mr = go.AddComponent<MeshRenderer>();
                if (ChunkMaterial != null) mr.sharedMaterial = ChunkMaterial;

                float tint = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * ColorJitter;
                mpb.Clear();
                mpb.SetColor(BaseColorId, baseColor * tint);
                mr.SetPropertyBlock(mpb);

                var chunk = new Chunk
                {
                    T = go.transform,
                    StartLocalPos = startPos,
                    RestLocalScale = go.transform.localScale, // captured before we zero it below
                    DriftDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                    TumbleAxis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized,
                    TumbleSpeed = Mathf.Lerp(TumbleSpeedRange.x, TumbleSpeedRange.y, (float)rng.NextDouble()),
                    StartDelay = (float)rng.NextDouble() * PerChunkDelayJitter
                };
                _chunks.Add(chunk);

                go.transform.localScale = Vector3.zero; // hidden until it starts falling
            }
        }

        private void Start()
        {
            // A coroutine, not Invoke(nameof(Open), ...) - Invoke() only works with
            // parameterless methods (even a C# default-valued parameter breaks it).
            if (OpenOnStart) StartCoroutine(OpenAfterDelay());
        }

        private IEnumerator OpenAfterDelay()
        {
            yield return new WaitForSeconds(OpenOnStartDelay);
            Open();
        }

        /// <summary>Starts the fall. A second call while already falling is a no-op, not a restart - matches WallHoleEffect/DebrisRing's reasoning. Pass forceRestart to actually restart.</summary>
        public void Open(bool forceRestart = false)
        {
            if (_open && !forceRestart) return;
            _open = true;
            _openStartTime = Time.time;
        }

        /// <summary>Resets all chunks to hidden (e.g. before re-triggering a burst point).</summary>
        public void Hide()
        {
            _open = false;
            foreach (var c in _chunks) c.T.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (!_open) return;

            // Fake gravity means "falls toward real (world) down", regardless of
            // which way this spawner itself is facing - a hole on a tilted roof or
            // side wall has a local "up" that points somewhere else entirely in
            // world space, so using Vector3.up directly to build a LOCAL offset
            // (the old approach) made the fall direction follow the quad's own
            // orientation instead of gravity - which is exactly why chunks flew up
            // or sideways depending on where the hole was. Converting world-down
            // into this transform's local space first means the offset, once
            // Unity turns localPosition back into a world position via this
            // transform's actual rotation, always ends up pointing straight down
            // in the real world, however this object itself is oriented. Computed
            // once per frame (not per chunk) since it only depends on this
            // transform's rotation, not on anything per-chunk.
            Vector3 localDown = transform.InverseTransformDirection(Vector3.down);

            float elapsed = Time.time - _openStartTime;
            bool anyStillVisible = false;

            foreach (var c in _chunks)
            {
                float t = Mathf.Clamp01((elapsed - c.StartDelay) / Mathf.Max(0.0001f, FallDuration - c.StartDelay));
                if (elapsed < c.StartDelay) { c.T.localScale = Vector3.zero; anyStillVisible = true; continue; }

                float fallT = FallCurve.Evaluate(t);
                c.T.localPosition = c.StartLocalPos
                    + localDown * FallDistance * fallT
                    + c.DriftDir * SidewaysDrift * StartRadius * fallT;
                c.T.Rotate(c.TumbleAxis, c.TumbleSpeed * Time.deltaTime, Space.Self);

                // Pops in quickly as it starts falling, shrinks away over the last
                // FadeOutFraction instead of popping off abruptly mid-fall.
                float popIn = Mathf.Clamp01(t / 0.08f);
                float fadeStart = 1f - FadeOutFraction;
                float fadeOut = t > fadeStart ? 1f - Mathf.InverseLerp(fadeStart, 1f, t) : 1f;
                float visibility = Mathf.Min(popIn, fadeOut);
                c.T.localScale = c.RestLocalScale * visibility;

                if (t < 1f) anyStillVisible = true;
            }

            if (!anyStillVisible) _open = false;
        }
    }
}
