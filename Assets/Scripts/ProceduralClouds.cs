using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// A handful of soft, fluffy cloud billboards scattered across the sky, drifting
    /// slowly - built entirely in code (procedural cloud texture + quads), same
    /// self-contained philosophy as SmokePuff/CameraSlimeOverlay, so it's a drop-in
    /// add with nothing to import or configure in a shader graph.
    ///
    /// URP has no built-in sky cloud system (that's an HDRP-only feature - Visual
    /// Environment's Volumetric Clouds override); this project is on URP throughout
    /// (every material here uses "Universal Render Pipeline/..." shaders), so this is
    /// a lightweight stand-in rather than a real volumetric renderer.
    ///
    /// IMPORTANT for this project specifically: this is an AR app, and on-device the
    /// background behind everything is the LIVE CAMERA FEED, not the Unity skybox -
    /// these clouds (and the skybox generally) are only visible in Editor/non-AR
    /// preview testing, not in the real on-site experience. Harmless either way, but
    /// don't expect to see them through the phone.
    ///
    /// Attach to any empty GameObject anywhere in the scene - it builds and places
    /// everything itself on Awake, centred on wherever this object sits.
    /// </summary>
    public class ProceduralClouds : MonoBehaviour
    {
        [Header("Layout")]
        public int CloudCount = 10;
        [Tooltip("How far out (metres) clouds are scattered from this object, on the horizontal XZ plane.")]
        public Vector2 DistanceRange = new Vector2(40f, 90f);
        [Tooltip("Height above this object's own Y position.")]
        public Vector2 HeightRange = new Vector2(15f, 35f);
        public Vector2 SizeRange = new Vector2(12f, 28f);

        [Header("Look")]
        [Range(0f, 1f)] public float Opacity = 0.85f;
        public Color TintTop = new Color(1f, 1f, 1f);
        public Color TintBottom = new Color(0.82f, 0.85f, 0.9f);

        [Header("Drift")]
        [Tooltip("Metres/second each cloud drifts, picked once per cloud within this range - a light breeze, not a storm.")]
        public Vector2 DriftSpeedRange = new Vector2(0.15f, 0.5f);
        [Tooltip("Once a cloud drifts this far past DistanceRange.y from this object, it wraps back around to the opposite side at a fresh random distance/height - keeps a fixed cloud count cycling forever instead of them all drifting away.")]
        public float WrapMargin = 20f;

        [Tooltip("Auto-builds on Awake - leave on unless something else should control when clouds appear.")]
        public bool BuildOnAwake = true;

        private struct Cloud
        {
            public Transform T;
            public Vector3 DriftDir;
            public float DriftSpeed;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Cloud[] _clouds;
        private Transform _cam;
        private static Material _sharedMaterial;

        private void Awake()
        {
            if (BuildOnAwake) Build();
        }

        /// <summary>Builds (or rebuilds, destroying any previous clouds first) the whole cloud field. Safe to call again if you change field values at runtime and want to see the result.</summary>
        public void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            var rng = new System.Random(GetInstanceID());
            var mat = GetOrBuildMaterial();
            _clouds = new Cloud[CloudCount];

            for (int i = 0; i < CloudCount; i++)
                _clouds[i] = BuildCloud(rng, mat);
        }

        private Cloud BuildCloud(System.Random rng, Material mat)
        {
            var go = new GameObject("Cloud_" + rng.Next());
            go.transform.SetParent(transform, false);
            PlaceRandomly(go.transform, rng);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Opacity via a MaterialPropertyBlock, not the (shared, cached-once)
            // material itself - same reasoning as SmokePuff/FallingRubble's per-
            // instance tint: this ProceduralClouds instance's own Opacity should
            // actually take effect even though the texture/material is shared
            // across every cloud (cheap on WebGL), rather than only the first
            // instance built winning.
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, Opacity));
            mr.SetPropertyBlock(mpb);

            float size = Mathf.Lerp(SizeRange.x, SizeRange.y, (float)rng.NextDouble());
            go.transform.localScale = new Vector3(size, size * 0.55f, 1f);

            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            return new Cloud
            {
                T = go.transform,
                DriftDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                DriftSpeed = Mathf.Lerp(DriftSpeedRange.x, DriftSpeedRange.y, (float)rng.NextDouble())
            };
        }

        private void PlaceRandomly(Transform t, System.Random rng)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = Mathf.Lerp(DistanceRange.x, DistanceRange.y, (float)rng.NextDouble());
            float height = Mathf.Lerp(HeightRange.x, HeightRange.y, (float)rng.NextDouble());
            t.localPosition = new Vector3(Mathf.Cos(angle) * dist, height, Mathf.Sin(angle) * dist);
        }

        private void Update()
        {
            if (_clouds == null) return;

            _cam = ResolveCamera();
            float farEnough = DistanceRange.y + WrapMargin;

            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                c.T.localPosition += c.DriftDir * c.DriftSpeed * Time.deltaTime;

                Vector3 fromCentre = c.T.localPosition; fromCentre.y = 0f;
                if (fromCentre.magnitude > farEnough)
                {
                    // Wrap back around to the opposite side, at a fresh random
                    // distance/height - reads as a steady stream of clouds drifting
                    // across the sky rather than a fixed count slowly thinning out.
                    Vector3 reentry = -c.DriftDir * DistanceRange.x;
                    reentry.y = Mathf.Lerp(HeightRange.x, HeightRange.y, Random.value);
                    c.T.localPosition = reentry;
                }

                // Billboard toward the camera so each flat quad always reads as a
                // full, soft cloud shape rather than edge-on/invisible from some
                // angles - yaw only (keep it upright), matching how distant sky
                // cloud cards are conventionally rendered.
                if (_cam != null)
                {
                    Vector3 toCam = _cam.position - c.T.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 0.001f)
                        c.T.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }

                _clouds[i] = c;
            }
        }

        private Transform ResolveCamera()
        {
            return Camera.main != null ? Camera.main.transform : null;
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "CloudQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material GetOrBuildMaterial()
        {
            // Shared across every ProceduralClouds instance in the scene - one
            // texture/material regardless of how many cloud fields exist, since
            // per-cloud variety comes from placement/scale, not a unique texture
            // each (keeps this cheap on WebGL). One consequence: TintTop/
            // TintBottom are baked into this shared texture, so with more than one
            // ProceduralClouds in the scene, whichever instance's Build() runs
            // first is the tint every instance actually gets - Opacity, by
            // contrast, IS genuinely per-instance (see BuildCloud's
            // MaterialPropertyBlock) since it doesn't need its own texture.
            if (_sharedMaterial != null) return _sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            var mat = new Material(shader) { name = "ProceduralCloud_Material" };
            mat.SetTexture("_BaseMap", BuildCloudTexture());
            mat.SetFloat("_Surface", 1f); // URP Opaque/Transparent toggle
            mat.SetFloat("_Blend", 0f); // alpha blend
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetColor("_BaseColor", Color.white); // default full-opacity white - each renderer overrides this via MaterialPropertyBlock for its own Opacity (see BuildCloud); the top/bottom tint itself is already baked into the texture above

            _sharedMaterial = mat;
            return mat;
        }

        /// <summary>
        /// A soft, irregular fluffy blob cluster (several overlapping soft circles,
        /// same technique as CameraSlimeOverlay's splat sprite) with a subtle
        /// top-lit/bottom-shaded tint baked in (TintTop/TintBottom) and a noisy
        /// edge so it reads as a cloud silhouette rather than a perfectly round
        /// splat. Instance method (not static) so it can read this instance's own
        /// TintTop/TintBottom - see GetOrBuildMaterial's doc for what "shared"
        /// means for that in practice.
        /// </summary>
        private Texture2D BuildCloudTexture()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ProceduralCloud_Tex",
                wrapMode = TextureWrapMode.Clamp
            };

            var rng = new System.Random(2024);
            var center = new Vector2(size * 0.5f, size * 0.48f);

            const int blobCount = 9;
            var blobCenters = new Vector2[blobCount];
            var blobRadii = new float[blobCount];
            for (int i = 0; i < blobCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = (float)rng.NextDouble() * size * 0.3f;
                // Flattened horizontally so the cluster reads as a wide, low cloud
                // rather than a round puff.
                blobCenters[i] = center + new Vector2(Mathf.Cos(angle) * 1.4f, Mathf.Sin(angle) * 0.7f) * dist;
                blobRadii[i] = size * (0.16f + (float)rng.NextDouble() * 0.22f);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x, y);

                    // Cheap per-pixel noise to jitter the effective radius per blob,
                    // so the silhouette edge is ragged/fluffy instead of a smooth
                    // circle arc.
                    float edgeNoise = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.35f + 0.85f;

                    float alpha = 0f;
                    for (int i = 0; i < blobCount; i++)
                    {
                        float d = Vector2.Distance(p, blobCenters[i]) / (blobRadii[i] * edgeNoise);
                        float a = d >= 1f ? 0f : Mathf.Pow(Mathf.Cos(d * Mathf.PI * 0.5f), 1.6f);
                        if (a > alpha) alpha = a;
                    }

                    float verticalT = y / (float)size;
                    Color tinted = Color.Lerp(TintBottom, TintTop, verticalT);
                    pixels[y * size + x] = new Color(tinted.r, tinted.g, tinted.b, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
