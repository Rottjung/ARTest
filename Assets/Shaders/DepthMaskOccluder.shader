Shader "ARReveal/DepthMaskOccluder"
{
    // Invisible depth-only occluder - the fast/easy fix for "the tentacle's
    // base sticks out behind the wall/hole line": rather than reshaping
    // every tentacle mesh (there are 7 different variants) in an external
    // 3D tool, this is added as a SECOND material slot directly on
    // WallHoleDemo's own existing hole-decal quad (see WallHoleDemo.prefab's
    // MeshRenderer - it has one submesh but two materials now: slot 0 is
    // WallBreakthrough for the visible crack/glow, slot 1 is this) - no new
    // GameObject, no repositioning, since it's literally the same quad
    // that's already sitting flush against the wall right where each
    // tentacle emerges. Unity's documented behavior for a renderer with
    // more materials than submeshes is to render the last submesh once per
    // extra material - so the exact same quad geometry draws twice, once
    // per material, each independently bucketed by its own queue.
    //
    // HOW IT WORKS: this pass writes ONLY to the depth buffer (ColorMask 0
    // - no color output at all, so it's completely invisible on its own)
    // and renders in the OPAQUE queue, BEFORE the tentacle
    // (Queue = Geometry-1, earlier than the tentacle's own default
    // Geometry queue). Once this quad's depth is in the depth buffer, any
    // tentacle geometry BEHIND it - further from the camera at that same
    // screen position - fails the standard depth test and never gets
    // drawn. This is why it has to be a SEPARATE material/queue from
    // WallBreakthrough itself, not literally a second pass merged into
    // that same shader: WallBreakthrough deliberately renders in the
    // Transparent queue (see its own doc comment, for its fade/crack
    // visuals), and Transparent-queue draws always happen AFTER all
    // Opaque-queue draws each frame - by the time it would run, the
    // Opaque-queue tentacle has already been fully rendered and
    // depth-tested, too late to occlude it. A shader's per-pass Tags
    // don't override that opaque/transparent bucketing either - it's
    // decided by the material's own overall queue, not by an individual
    // pass, so only a genuinely separate material assigned to its own
    // slot actually gets scheduled into the earlier Opaque phase.
    //
    // No shader parameters exist since it does nothing but write depth -
    // whatever size/position WallHoleDemo's own quad is already authored
    // at (flush with the real wall) is exactly where this needs to be too.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="UniversalForward" }
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Never actually seen (ColorMask 0 discards this output),
                // but a fragment shader still needs to return something.
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
