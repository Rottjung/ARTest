using System.Collections.Generic;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Scatters a dense, irregular cluster of small procedurally-jittered concrete
    /// flakes around a WallHoleEffect's opening, so the hole reads as punched-through
    /// material rather than a flat cutout - lots of small chips clustered tight to the
    /// rim plus a handful of bigger slabs, mostly flush with the wall plane but a few
    /// canted outward as if peeling off, matching real broken concrete/plaster.
    ///
    /// Chunks pop with a staggered burst, then settle into a static resting pose - no
    /// physics/rigidbodies, cheap and deterministic, fine for a handful of AR burst
    /// points on WebGL.
    ///
    /// Place as a child of (or sibling to, same local origin as) the WallHoleEffect
    /// decal quad, and call Open() at the same time - ideally a little after, so the
    /// debris reads as being pushed out by the tentacle rather than announcing it.
    /// InnerRadius/OuterRadius are in this transform's local units, so match them to
    /// roughly the same world size as the hole shader's _MaxHoleRadius on that quad.
    /// </summary>
    public class DebrisRing : MonoBehaviour
    {
        [Header("Chunks")]
        public int ChunkCount = 26;
        [Tooltip("Where the densest cluster of chips sits - roughly the hole's edge.")]
        public float InnerRadius = 0.32f;
        [Tooltip("Furthest a stray chip can land from the center.")]
        public float OuterRadius = 0.6f;
        [Tooltip("Higher = chips crowd tighter against InnerRadius; lower = more spread out toward OuterRadius.")]
        public float DensityBias = 2.2f;

        [Header("Chip size mix")]
        public float ChunkSize = 0.05f;
        [Range(0f, 1f)] public float LargeChunkChance = 0.25f;
        public Vector2 SmallSizeRange = new Vector2(0.4f, 0.9f);
        public Vector2 LargeSizeRange = new Vector2(1.0f, 2.0f);
        [Range(0f, 1f)] public float Irregularity = 0.4f;
        public Vector2 DepthScaleRange = new Vector2(0.2f, 0.5f);

        [Header("Orientation - mostly flush with the wall, a few peeling outward")]
        [Tooltip("Max degrees a chip cants away from lying flush against the wall plane.")]
        public float MaxTiltDegrees = 55f;

        [Header("Look")]
        public Material ChunkMaterial;
        [Range(0f, 0.5f)] public float ColorJitter = 0.12f;

        [Header("Burst timing")]
        public float BurstDuration = 0.5f;
        [Range(0f, 1f)] public float PerChunkDelayJitter = 0.4f;
        public float OutwardPush = 0.02f;
        public AnimationCurve BurstCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("Auto-trigger Open() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        private struct Chunk
        {
            public Transform T;
            public Vector3 RestLocalPos;
            public Vector3 RestLocalScale;
            public Vector3 OutwardDir;
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
                : new Color(0.55f, 0.53f, 0.5f);

            var mpb = new MaterialPropertyBlock();

            for (int i = 0; i < ChunkCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radiusT = Mathf.Pow((float)rng.NextDouble(), DensityBias);
                float radius = Mathf.Lerp(InnerRadius, OuterRadius, radiusT);
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 restPos = dir * radius;

                bool large = rng.NextDouble() < LargeChunkChance;
                Vector2 sizeRange = large ? LargeSizeRange : SmallSizeRange;
                float size = ChunkSize * Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
                float depthScale = Mathf.Lerp(DepthScaleRange.x, DepthScaleRange.y, (float)rng.NextDouble());

                var go = new GameObject("Chunk_" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = restPos;

                // Mostly flush with the wall plane (identity = facing same way as the
                // quad), canted by a random tilt so a few pieces read as peeling off
                // rather than every chip tumbling in a random direction.
                float tiltAngle = (float)rng.NextDouble() * MaxTiltDegrees;
                var tiltAxis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, 0f).normalized;
                Quaternion tilt = Quaternion.AngleAxis(tiltAngle, tiltAxis);
                Quaternion spin = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.forward);
                go.transform.localRotation = tilt * spin;

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
                    RestLocalPos = restPos,
                    RestLocalScale = go.transform.localScale, // captured before we zero it below
                    OutwardDir = dir,
                    StartDelay = (float)rng.NextDouble() * PerChunkDelayJitter
                };
                _chunks.Add(chunk);

                go.transform.localScale = Vector3.zero; // hidden until the burst
            }
        }

        private void Start()
        {
            // A coroutine, not Invoke(nameof(Open), ...) - Invoke() only works with
            // parameterless methods (even a C# default-valued parameter breaks it,
            // which is exactly what happened when Open() gained forceRestart).
            if (OpenOnStart) StartCoroutine(OpenAfterDelay());
        }

        private System.Collections.IEnumerator OpenAfterDelay()
        {
            yield return new WaitForSeconds(OpenOnStartDelay);
            Open();
        }

        /// <summary>
        /// Starts the debris burst. Call alongside/after WallHoleEffect.Open(). A
        /// second call while already open/mid-burst is a no-op, not a restart - without
        /// this, anything that accidentally triggers the reveal twice (e.g. a
        /// tracking-found event firing more than once) would reset _openStartTime and
        /// make already-settled chunks visibly shrink back down and re-burst. Pass
        /// forceRestart if you actually want that (e.g. deliberately re-triggering a
        /// burst point from scratch).
        /// </summary>
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

            float elapsed = Time.time - _openStartTime;
            foreach (var c in _chunks)
            {
                float t = Mathf.Clamp01((elapsed - c.StartDelay) / Mathf.Max(0.0001f, BurstDuration - c.StartDelay));
                float eased = BurstCurve.Evaluate(t);
                c.T.localScale = c.RestLocalScale * eased;
                c.T.localPosition = c.RestLocalPos + c.OutwardDir * OutwardPush * eased;
            }
        }
    }
}
