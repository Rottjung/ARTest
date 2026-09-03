Shader "ARReveal/WallBreakthrough"
{
    // Decal quad spawned flush against the (already real-world-aligned) proxy mesh,
    // at the same point a tentacle emerges from. Three things happen, in sequence:
    //   0. At rest (_Active = 0) the decal is fully invisible - nothing is drawn.
    //   1. On trigger, a Voronoi-line "spiderweb crack" flashes across the whole
    //      decal (_PreCrackIntensity, quick rise/fade) - like glass/plaster
    //      fracturing an instant before it gives way.
    //   2. A jagged, noise-distorted hole grows outward from the center
    //      (_Progress, 0-1), with a glowing crack ring right at its growing edge
    //      (_GlowIntensity, pulses up then fades as the break settles).
    // The whole decal also always fades to fully transparent before it reaches its
    // own quad edge, so there's never a visible rectangle - it blends into the real
    // wall. Where the hole is fully open (alpha 0), whatever was drawn before this
    // decal - typically the tentacle mesh - shows through, since this renders in the
    // Transparent queue after opaque geometry.
    Properties
    {
        _BaseColor("Crack/Debris Color", Color) = (0.35, 0.32, 0.29, 1)
        _EmissionColor("Glow Color", Color) = (1.0, 0.55, 0.2, 1)
        _EmissionStrength("Glow Strength", Range(0,8)) = 2.5
        _Active("Active (0/1, script driven)", Range(0,1)) = 0
        _Progress("Progress (0-1, script driven)", Range(0,1)) = 0
        _GlowIntensity("Glow Intensity (0-1, script driven)", Range(0,1)) = 0
        _MaxHoleRadius("Max Hole Radius", Range(0,0.7)) = 0.4
        _CrackWidth("Crack Ring Width", Range(0.01,0.3)) = 0.07
        _NoiseScale("Edge Noise Scale", Range(1,20)) = 7
        _NoiseStrength("Edge Noise Strength", Range(0,0.4)) = 0.15
        _EdgeFadeStart("Outer Fade Start", Range(0,1)) = 0.72
        _EdgeFadeEnd("Outer Fade End", Range(0,1.5)) = 1.0

        [Header(Pre Crack Spiderweb Flash)]
        _PreCrackColor("Pre-Crack Line Color", Color) = (0.9, 0.95, 1.0, 1)
        _PreCrackIntensity("Pre-Crack Intensity (0-1, script driven)", Range(0,1)) = 0
        _PreCrackScale("Pre-Crack Cell Scale", Range(1,20)) = 6
        _PreCrackLineWidth("Pre-Crack Line Width", Range(0.001,0.2)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "WallBreakthroughForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Active;
                float _Progress;
                float _GlowIntensity;
                float _MaxHoleRadius;
                float _CrackWidth;
                float _NoiseScale;
                float _NoiseStrength;
                float _EdgeFadeStart;
                float _EdgeFadeEnd;
                float4 _PreCrackColor;
                float _PreCrackIntensity;
                float _PreCrackScale;
                float _PreCrackLineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // Cheap hash-based value noise - no noise texture needed, WebGL-safe.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Worley/Voronoi distances to the nearest (f1) and second-nearest (f2)
            // feature point - cell boundaries (where f2-f1 is small) read as a
            // spiderweb-crack line pattern once thresholded.
            void voronoi(float2 uv, out float f1, out float f2)
            {
                float2 cell = floor(uv);
                float2 localPos = frac(uv);
                f1 = 8.0;
                f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 point_ = hash22(cell + neighbor);
                        float dist = length(neighbor + point_ - localPos);
                        if (dist < f1) { f2 = f1; f1 = dist; }
                        else if (dist < f2) { f2 = dist; }
                    }
                }
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 centered = (IN.uv - 0.5) * 2.0; // -1..1
                float d = length(centered);

                // Fades the whole decal to nothing before it reaches the quad's hard edge.
                float outerFade = 1.0 - smoothstep(_EdgeFadeStart, _EdgeFadeEnd, d);

                // Jagged, organic edge instead of a perfect circle.
                float n = valueNoise(IN.uv * _NoiseScale) - 0.5;
                float dJagged = d + n * _NoiseStrength;

                float holeRadius = _Progress * _MaxHoleRadius;

                // 0 inside the hole (fully transparent), 1 past the crack ring (opaque).
                float ringAlpha = smoothstep(holeRadius, holeRadius + _CrackWidth, dJagged);

                // Glow concentrated right at the leading edge of the crack ring.
                float edgeDist = abs(dJagged - holeRadius);
                float glow = saturate(1.0 - edgeDist / max(_CrackWidth, 0.0001));
                glow *= _GlowIntensity;

                half3 color = _BaseColor.rgb + _EmissionColor.rgb * _EmissionStrength * glow;
                float alpha = ringAlpha;

                // Spiderweb pre-crack flash - a Voronoi cell-boundary line pattern,
                // faded in/out entirely by script-driven intensity, independent of
                // the hole's own radius so it can flash before the hole starts moving.
                float f1, f2;
                voronoi(IN.uv * _PreCrackScale + 17.0, f1, f2);
                float crackLines = 1.0 - smoothstep(0.0, _PreCrackLineWidth, f2 - f1);
                crackLines *= _PreCrackIntensity;

                color = lerp(color, _PreCrackColor.rgb, saturate(crackLines));
                alpha = max(alpha, crackLines);

                alpha *= outerFade * _Active;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
