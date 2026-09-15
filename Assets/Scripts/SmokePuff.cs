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
        [Tooltip("Denser than the original soft-puff defaults, per direct request for a punchier 'the hole spits out debris dust' look rather than a gentle billow.")]
        public int ParticleCount = 26;
        public float EmitDuration = 0.25f;

        [Header("Look")]
        public Material ParticleMaterial;
        public Color StartColor = new Color(0.55f, 0.53f, 0.5f, 0.55f);
        [Range(0f, 0.5f)] public float ColorJitter = 0.1f;
        public Vector2 StartSizeRange = new Vector2(0.18f, 0.4f);
        [Tooltip("Shortened from the original soft-puff defaults (1.2-2s) - a sharp burst that's mostly gone within a second or so reads as an ejection, not a lingering drift.")]
        public Vector2 LifetimeRange = new Vector2(0.7f, 1.3f);

        [Header("Motion")]
        [Tooltip("Initial outward speed range, along the emission cone - raised sharply from the original soft-puff defaults (0.2-0.6, which read as a gentle drift) per direct request: 'an ejaculation of smoke bursting in the direction the tentacle breaks through, like the hole spits out debris dust.'")]
        public Vector2 StartSpeedRange = new Vector2(2.5f, 4.5f);
        [Tooltip("Half-angle (degrees) of the emission cone - wider reads as more of a puff, narrower as more of a jet. Narrowed from the original 35 degrees so the burst reads as shooting out in ONE direction (the tentacle's own breach direction, along this object's local +Z - orient the GameObject itself if that's wrong) rather than dispersing outward as a wide dome.")]
        [Range(0f, 90f)] public float ConeAngle = 14f;
        [Tooltip("Radius of the cone's base - how spread out across the breach the puffs start from, not how far they travel.")]
        public float ConeRadius = 0.04f;
        [Tooltip("Lowered from the original soft-puff default (0.3) - a fast directional ejection shouldn't also lazily float upward like campfire smoke; it should read as debris dust that was shot out and is now falling/scattering under its own drag.")]
        public float RiseSpeed = 0.05f;
        [Tooltip("Higher slows particles down faster as they age, reading as smoke losing momentum and dispersing rather than flying off in a straight line. Raised from the original 1.2 to sell the 'burst' - the high StartSpeedRange above needs strong drag right after to read as a sharp ejection that quickly loses momentum, rather than dust flying an unrealistically long distance at that speed.")]
        public float Drag = 3.5f;

        [Header("Overall size")]
        [Tooltip("Uniform scale of this GameObject - sizes the whole effect up or down (particle sizes/speeds/distances all scale with it, since the Particle System's Scaling Mode is Local). Source of truth for the Transform's scale - Configure() applies this every Awake(), so scaling the object by hand only sticks if it's copied back in here (Copy From Particle does this too).")]
        public float EffectScale = 1f;

        [Tooltip("Auto-trigger Open() shortly after Start(), for standalone testing.")]
        public bool OpenOnStart = false;
        public float OpenOnStartDelay = 0.5f;

        [Tooltip("Whether Awake() rebuilds the ParticleSystem from the fields above at all. ON (default) is right for a fresh, never-tuned SmokePuff - it's what makes this a self-configuring drop-in with no particle asset to hand-author. Once you've hand-tuned the ParticleSystem component directly to a look you like, UNCHECK this - Configure() then never touches the ParticleSystem again, so EVERY module (including ones Copy From Particle can't read back, like the Color/Size-Over-Lifetime curves and Velocity-Over-Lifetime X/Z) stays exactly as authored, in both Edit and Play mode. Copy From Particle still works as a one-time snapshot of the fields it does understand, but doesn't need to be perfectly complete once this is off, since Configure() simply won't run to discard anything.")]
        public bool AutoConfigure = true;

        private ParticleSystem _ps;
        private bool _fired;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (AutoConfigure) Configure();

            // Safety net for a real-device-observed race: ParticleSystem's own
            // native "Play On Awake" is evaluated by Unity against the
            // GameObject's OWN activation, independently of - and not
            // reliably ordered against - this script's Awake() (which is what
            // sets main.playOnAwake=false, but only when AutoConfigure runs
            // Configure() above). Since every hole's SmokePuff sits inactive
            // under ContentWrapper until HandoffToInstantTracking reveals it
            // by flipping ContentWrapper active all at once
            // (ContentWrapper.gameObject.SetActive(true) in RevealContent()),
            // a lost race there auto-plays EVERY puff simultaneously the
            // instant content reveals - well before each burst point's own
            // staggered Open() call - exactly the "smoke from every hole all
            // together at the very start" symptom seen on-site. An explicit
            // Stop+Clear here, unconditionally and regardless of
            // AutoConfigure, runs synchronously within this same Awake() -
            // guaranteed to complete before this frame's rendering - so
            // whichever side of that race actually won, nothing is ever left
            // playing/visible until a real Open() call.
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (!AutoConfigure && _ps.main.playOnAwake)
                Debug.LogWarning("[SmokePuff] '" + name + "' has AutoConfigure off AND Play On Awake still checked on its ParticleSystem - the Stop() safety net above prevents it bursting early, but consider unchecking Play On Awake directly on the ParticleSystem too, since it serves no purpose here (Open() always drives playback).", this);

            // Same reasoning as the Play On Awake check above - with
            // AutoConfigure off, Configure()'s own main.startDelay = 0f fix
            // never runs, so a nonzero Start Delay left on the Main module
            // (from hand-tuning, or just easy to miss) silently makes Open()
            // visually do nothing until it elapses - the exact "smoke starts
            // a second late" bug reported on-site.
            if (!AutoConfigure && _ps.main.startDelay.constantMax > 0f)
                Debug.LogWarning("[SmokePuff] '" + name + "' has AutoConfigure off AND a nonzero Start Delay on its ParticleSystem's Main module - Open() fires on time, but nothing will actually appear until that delay elapses. Set Start Delay to 0 directly on the ParticleSystem (Open() should be the only thing controlling when this puff starts).", this);
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
            // Explicitly zeroed - Configure() never used to touch this at all,
            // so whatever Start Delay was already sitting on the Main module
            // (left over from hand-tuning, or just never noticed since it's
            // easy to miss/scroll past) silently persisted and made Open()'s
            // _ps.Play() call visually do nothing for however long that delay
            // was - reading exactly as "the smoke starts a second late," per
            // direct report, even though Open() itself fires at the correct
            // instant. Open() is the only thing that should ever decide when
            // this puff actually starts.
            main.startDelay = 0f;
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
            // Render Mode alone ("Billboard" vs Mesh/Stretched/etc.) does NOT
            // guarantee particles actually face the camera - that's a SEPARATE
            // setting, Render Alignment (View/World/Local/Facing/Velocity),
            // which Configure() never used to touch at all. Left at whatever
            // the ParticleSystemRenderer happened to default/carry over to
            // (World or Local, if this component was ever copy-pasted from
            // another particle system, or just Unity's own factory default),
            // Billboard mode still orients the quad relative to THAT space
            // instead of the camera - reading as "doesn't billboard" even
            // though Render Mode itself is correctly set to Billboard. View is
            // the one alignment that means "always face the camera."
            renderer.alignment = ParticleSystemRenderSpace.View;
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

        /// <summary>
        /// Runs Configure() directly, regardless of AutoConfigure or whether
        /// Awake() has ever run - lets SmokePuffEditor's "Reconfigure Now"
        /// button preview field changes live in EDIT MODE, without needing to
        /// enter Play mode (which is when Configure() normally runs, via
        /// Awake()) for every single tuning iteration. Editor-preview
        /// convenience only - nothing at runtime calls this directly (Awake()
        /// still owns the real AutoConfigure gate).
        /// </summary>
        public void ForceReconfigure()
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            Configure();
        }
    }
}
