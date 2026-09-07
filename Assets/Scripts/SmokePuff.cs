using System.Collections;
using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// A short burst of dust/smoke puffing out from a breach point - configured
    /// entirely in code (no particle asset to hand-author/import), so it's a drop-in
    /// add to any burst point, same self-contained philosophy as DebrisRing/
    /// FallingRubble. Grey, billowing, rising and dispersing - reads as dust kicked up
    /// by the tentacle punching through, not fire/explosion smoke.
    ///
    /// Uses the built-in Particle System component (not VFX Graph, which needs compute
    /// shaders that WebGL doesn't reliably support) with a URP transparent-unlit
    /// material. If ParticleMaterial is left unassigned, a soft round dot texture is
    /// generated in code and shared across every SmokePuff that doesn't override it -
    /// no texture/material asset to import. Assign ParticleMaterial in the Inspector
    /// for a nicer/more detailed smoke look later.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class SmokePuff : MonoBehaviour
    {
        [Header("Burst")]
        public int ParticleCount = 18;
        public float EmitDuration = 0.4f;

        [Header("Look")]
        public Material ParticleMaterial;
        public Color StartColor = new Color(0.55f, 0.53f, 0.5f, 0.55f);
        [Range(0f, 0.5f)] public float ColorJitter = 0.1f;
        public Vector2 StartSizeRange = new Vector2(0.15f, 0.35f);
        public Vector2 LifetimeRange = new Vector2(1.2f, 2f);

        [Header("Motion")]
        [Tooltip("Initial outward speed range, along the emission cone.")]
        public Vector2 StartSpeedRange = new Vector2(0.2f, 0.6f);
        [Tooltip("Half-angle (degrees) of the emission cone - wider reads as more of a puff, narrower as more of a jet.")]
        [Range(0f, 90f)] public float ConeAngle = 35f;
        [Tooltip("Radius of the cone's base - how spread out across the breach the puffs start from, not how far they travel.")]
        public float ConeRadius = 0.05f;
        public float RiseSpeed = 0.3f;
        [Tooltip("Higher slows particles down faster as they age, reading as smoke losing momentum and dispersing rather than flying off in a straight line.")]
        public float Drag = 1.2f;

        [Header("Overall size")]
        [Tooltip("Uniform scale of this GameObject - sizes the whole effect up or down (particle sizes/speeds/distances all scale with it, since the Particle System's Scaling Mode is Local). Source of truth for the Transform's scale - Configure() applies this every Awake(), so scaling the object by hand only sticks if it's copied back in here (Copy From Particle does this too).")]
        public float EffectScale = 1f;

        [Tooltip("Auto-trigger Open() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        private ParticleSystem _ps;
        private bool _fired;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            Configure();
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

        private void Configure()
        {
            // Source of truth for this object's own scale, same as every other field
            // here - sizes the whole effect (Scaling Mode below makes the Particle
            // System honour it) rather than something set once by hand and never
            // touched again.
            transform.localScale = Vector3.one * EffectScale;

            var main = _ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.duration = Mathf.Max(0.1f, EmitDuration);
            main.startLifetime = new ParticleSystem.MinMaxCurve(LifetimeRange.x, LifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(StartSpeedRange.x, StartSpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(StartSizeRange.x, StartSizeRange.y);
            main.startColor = StartColor;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(main.maxParticles, ParticleCount + 4);

            var emission = _ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ParticleCount) });

            // Left at the default Cone shape (set on AddComponent) - only the cone's
            // own angle/radius are tuned, rather than touching the deprecated
            // shapeType setter.
            var shape = _ps.shape;
            shape.angle = ConeAngle;
            shape.radius = ConeRadius;

            var velocityOverLifetime = _ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(RiseSpeed);

            var colorOverLifetime = _ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(StartColor, 0f), new GradientColorKey(StartColor, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(StartColor.a, 0.2f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = _ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

            var limitVelocityOverLifetime = _ps.limitVelocityOverLifetime;
            limitVelocityOverLifetime.enabled = true;
            limitVelocityOverLifetime.drag = Drag;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ParticleMaterial != null ? ParticleMaterial : GetOrBuildDefaultMaterial();
        }

        private static Material _defaultMaterial;

        /// <summary>
        /// Built once and shared by every SmokePuff that doesn't have its own
        /// ParticleMaterial assigned - a soft round texture (generated below) on a
        /// URP transparent-unlit material, so puffs actually read as soft dispersing
        /// smoke instead of Unity's flat default particle square. No texture/material
        /// asset to import - assign ParticleMaterial in the Inspector to override this
        /// with something better-looking later.
        /// </summary>
        private static Material GetOrBuildDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit"); // fallback if the particle-specific variant isn't present in this URP version

            var mat = new Material(shader) { name = "SmokePuff_DefaultMaterial" };
            mat.SetTexture("_BaseMap", BuildSoftDotTexture());
            // URP's standard Opaque/Transparent toggle (_Surface: 0 = Opaque, 1 =
            // Transparent) plus the matching blend/ZWrite state - both set explicitly
            // since just assigning a texture with alpha does nothing on a shader still
            // set to Opaque.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f); // alpha blend
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);

            _defaultMaterial = mat;
            return mat;
        }

        /// <summary>A small white dot with a soft (roughly Gaussian) falloff to fully transparent at the edge, rather than a hard-edged circle or Unity's default flat square.</summary>
        private static Texture2D BuildSoftDotTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SmokePuff_SoftDot", wrapMode = TextureWrapMode.Clamp };

            var center = new Vector2(size * 0.5f, size * 0.5f);
            float maxDist = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                    float alpha = dist >= 1f ? 0f : Mathf.Pow(Mathf.Cos(dist * Mathf.PI * 0.5f), 1.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Starts the smoke burst. A second call while mid-burst is a no-op, not a restart, matching every other burst-point trigger's idempotency (WallHoleEffect.Open, TentacleController.Grow, etc). Pass forceRestart to actually restart.</summary>
        public void Open(bool forceRestart = false)
        {
            if (_fired && !forceRestart) return;
            _fired = true;
            _ps.Play();
        }

        /// <summary>Stops and clears all particles, resetting for a future trigger.</summary>
        public void Hide()
        {
            _fired = false;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
