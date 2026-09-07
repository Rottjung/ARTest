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
            if (GUILayout.Button("Copy From Particle", GUILayout.Height(28)))
            {
                var smoke = (SmokePuff)target;
                Undo.RecordObject(smoke, "Copy From Particle");
                CopyFromParticle(smoke);
                EditorUtility.SetDirty(smoke);
            }
            EditorGUILayout.HelpBox(
                "Tune the Particle System component below directly (or live in Play mode), then click this to pull those values back into the fields above.",
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
