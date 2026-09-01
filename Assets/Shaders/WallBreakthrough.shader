Shader "ARReveal/WallBreakthrough"
{
    // Decal quad spawned flush against the (already real-world-aligned) proxy mesh,
    // at the same point a tentacle emerges from. Two things happen at once:
    //   1. The whole decal fades to fully transparent before it reaches its own quad
    //      edge, so there's never a visible rectangle - it blends straight into the
    //      real wall.
    //   2. A jagged, noise-distorted hole grows outward from the center (driven by
    //      _Progress, 0-1), with a glowing crack ring right at its growing edge
    //      (driven by _GlowIntensity, which the controller script pulses up then
    //      fades down as the break settles).
    // Where the hole is fully open (alpha 0) whatever was drawn before this decal -
    // typically the tentacle mesh itself - shows through, since this renders in the
    // Transparent queue after opaque geometry.
    Properties
    {
        _BaseColor("Crack/Debris Color", Color) = (0.35, 0.32, 0.29, 1)
        _EmissionColor("Glow Color", Color) = (1.0, 0.55, 0.2, 1)
        _EmissionStrength("Glow Strength", Range(0,8)) = 2.5
        _Progress("Progress (0-1, script driven)", Range(0,1)) = 0
        _GlowIntensity("Glow Intensity (0-1, script driven)", Range(0,1)) = 0
        _MaxHoleRadius("Max Hole Radius", Range(0,0.7)) = 0.4
        _CrackWidth("Crack Ring Width", Range(0.01,0.3)) = 0.07
        _NoiseScale("Edge Noise Scale", Range(1,20)) = 7
        _NoiseStrength("Edge Noise Strength", Range(0,0.4)) = 0.15
        _EdgeFadeStart("Outer Fade Start", Range(0,1)) = 0.72
        _EdgeFadeEnd("Outer Fade End", Range(0,1.5)) = 1.0
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
                float _Progress;
                float _GlowIntensity;
                float _MaxHoleRadius;
                float _CrackWidth;
                float _NoiseScale;
                float _NoiseStrength;
                float _EdgeFadeStart;
                float _EdgeFadeEnd;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float2 centered = (IN.uv - 0.5) * 2.0; // -1..1
                float d = length(centered);

                // Jagged, organic edge instead of a perfect circle.
                float n = valueNoise(IN.uv * _NoiseScale) - 0.5;
                float dJagged = d + n * _NoiseStrength;

                float holeRadius = _Progress * _MaxHoleRadius;

                // 0 inside the hole (fully transparent), 1 past the crack ring (opaque).
                float ringAlpha = smoothstep(holeRadius, holeRadius + _CrackWidth, dJagged);

                // Fades the whole decal to nothing before it reaches the quad's hard edge.
                float outerFade = 1.0 - smoothstep(_EdgeFadeStart, _EdgeFadeEnd, d);

                float alpha = ringAlpha * outerFade;

                // Glow concentrated right at the leading edge of the crack ring.
                float edgeDist = abs(dJagged - holeRadius);
                float glow = saturate(1.0 - edgeDist / max(_CrackWidth, 0.0001));
                glow *= _GlowIntensity * outerFade;

                half3 color = _BaseColor.rgb + _EmissionColor.rgb * _EmissionStrength * glow;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
