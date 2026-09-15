using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds a "Copy From Particle" button to SmokePuff's Inspector - reads back
    /// whatever the attached ParticleSystem is actually set to right now (however it
    /// got there: tuned directly on the component, previewed in the Scene view, etc.)
    /// into SmokePuff's own public fields. Without this, tuning the ParticleSystem by
    /// hand is a dead end - SmokePuff.Configure() unconditionally overwrites it from
    /// those same fields every Awake(), so any manual tweak is lost the next time it
    /// runs unless it's captured back into the fields first.
    /// </summary>
    [CustomEditor(typeof(SmokePuff))]
    public class SmokePuffEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Reconfigure Now", GUILayout.Height(24)))
            {
                var smoke = (SmokePuff)target;
                Undo.RecordObject(smoke.GetComponent<ParticleSystem>(), "Reconfigure Now");
                smoke.ForceReconfigure();
                EditorUtility.SetDirty(smoke.GetComponent<ParticleSystem>());
            }
            EditorGUILayout.HelpBox(
                "Applies the fields above to the ParticleSystem right now, in Edit Mode - no need to enter Play mode to preview a field change (this is exactly what Awake() runs automatically when AutoConfigure is on, just callable on demand). Use the ParticleSystem's own Play button below to preview after clicking this.",
                MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("Copy From Particle", GUILayout.Height(28)))
            {
                var smoke = (SmokePuff)target;
                Undo.RecordObject(smoke, "Copy From Particle");
                CopyFromParticle(smoke);
                // Also turns AutoConfigure off - per direct request: this
                // button can only round-trip the specific properties it
                // reads below (see CopyFromParticle), NOT the Color/Size-
                // Over-Lifetime curves or Velocity-Over-Lifetime X/Z, which
                // Configure() would otherwise still silently rebuild from
                // its own fixed logic on the next Awake() even after this
                // copy. Turning AutoConfigure off guarantees the ParticleSystem
                // is never touched again, so it stays EXACTLY as tuned -
                // toggle it back on by hand if this instance should go back
                // to being auto-configured from the fields instead.
                smoke.AutoConfigure = false;
                EditorUtility.SetDirty(smoke);
            }
            EditorGUILayout.HelpBox(
                "Tune the Particle System component below directly (or live in Play mode), then click this to pull the properties it understands back into the fields above, AND turn Auto Configure off - guaranteeing nothing (including properties this button can't read, like the Color/Size-Over-Lifetime curves) gets reset on the next Awake(). Re-check Auto Configure by hand if you want this instance driven from the fields again.",
                MessageType.Info);
        }

        private static void CopyFromParticle(SmokePuff smoke)
        {
            var ps = smoke.GetComponent<ParticleSystem>();
            if (ps == null) return;

            var main = ps.main;
            smoke.EmitDuration = main.duration;
            smoke.LifetimeRange = ReadRange(main.startLifetime, smoke.LifetimeRange);
            smoke.StartSpeedRange = ReadRange(main.startSpeed, smoke.StartSpeedRange);
            smoke.StartSizeRange = ReadRange(main.startSize, smoke.StartSizeRange);
            smoke.StartColor = ReadColor(main.startColor, smoke.StartColor);

            var emission = ps.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            if (bursts.Length > 0)
            {
                if (bursts[0].count.mode == ParticleSystemCurveMode.Constant)
                    smoke.ParticleCount = Mathf.RoundToInt(bursts[0].count.constant);
                else
                    Debug.LogWarning("[SmokePuffEditor] The first burst's Count is a random range, not a single constant - Particle Count left unchanged.", smoke);
            }

            var shape = ps.shape;
            smoke.ConeAngle = shape.angle;
            smoke.ConeRadius = shape.radius;

            // Uniform scale assumed (EffectScale is a single float) - if it's been
            // scaled non-uniformly by hand, only X is captured and a warning explains
            // why the other axes are being ignored.
            Vector3 scale = smoke.transform.localScale;
            if (Mathf.Abs(scale.x - scale.y) > 0.001f || Mathf.Abs(scale.x - scale.z) > 0.001f)
                Debug.LogWarning($"[SmokePuffEditor] '{smoke.name}' has a non-uniform Transform scale {scale} - Effect Scale only supports uniform scale, capturing X ({scale.x}) and the others will be forced to match it once Configure() next runs.", smoke);
            smoke.EffectScale = scale.x;

            var velocityOverLifetime = ps.velocityOverLifetime;
            if (velocityOverLifetime.enabled && velocityOverLifetime.y.mode == ParticleSystemCurveMode.Constant)
                smoke.RiseSpeed = velocityOverLifetime.y.constant;

            var limitVelocityOverLifetime = ps.limitVelocityOverLifetime;
            if (limitVelocityOverLifetime.enabled && limitVelocityOverLifetime.drag.mode == ParticleSystemCurveMode.Constant)
                smoke.Drag = limitVelocityOverLifetime.drag.constant;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // Only capture the material if it's something the user actually assigned -
            // not the auto-generated default SmokePuff builds for itself when
            // ParticleMaterial is left blank, which would otherwise get "captured"
            // right back into that same blank-default state under a real reference.
            if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.name != "SmokePuff_DefaultMaterial")
                smoke.ParticleMaterial = renderer.sharedMaterial;

            Debug.Log($"[SmokePuffEditor] Copied current Particle System settings into '{smoke.name}'.", smoke);
        }

        /// <summary>Constant or TwoConstants map cleanly to a min/max pair; Curve/TwoCurves don't, so those are left unchanged (with a warning) rather than guessed at.</summary>
        private static Vector2 ReadRange(ParticleSystem.MinMaxCurve curve, Vector2 fallback)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.TwoConstants:
                    return new Vector2(curve.constantMin, curve.constantMax);
                case ParticleSystemCurveMode.Constant:
                    return new Vector2(curve.constant, curve.constant);
                default:
                    Debug.LogWarning("[SmokePuffEditor] A range field is in Curve/TwoCurves mode, which doesn't map to a simple min/max - left unchanged.");
                    return fallback;
            }
        }

        private static Color ReadColor(ParticleSystem.MinMaxGradient gradient, Color fallback)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return gradient.color;
                case ParticleSystemGradientMode.Gradient:
                    return gradient.gradient.Evaluate(0f);
                default:
                    Debug.LogWarning("[SmokePuffEditor] Start Color is using a two-color/random mode that doesn't map to a single Color - left unchanged.");
                    return fallback;
            }
        }
    }
}
