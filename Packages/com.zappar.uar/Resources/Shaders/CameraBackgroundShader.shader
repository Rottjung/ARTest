Shader "Zappar/CameraBackgroundShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        // Camera feed color grading, added per direct request ("the client
        // keep asking to adjust the contrast of the camera feed") - graded
        // right here in the same draw call that already paints the feed to
        // the screen (see frag() below), so this costs a few extra ALU ops
        // per pixel and nothing else: no second render pass, no extra
        // camera, no texture copy. Defaults (1, 0, 1) are all no-ops, so
        // leaving these untouched reproduces the original ungraded look
        // exactly.
        _Exposure ("Exposure (stops)", Range(-3, 3)) = 0
        _Contrast ("Contrast", Range(0, 3)) = 1
        _Brightness ("Brightness", Range(-1, 1)) = 0
        _Saturation ("Saturation", Range(0, 3)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4x4 _nativeTextureMatrix;
            float _Exposure;
            float _Contrast;
            float _Brightness;
            float _Saturation;
            fixed4 _Tint;

            v2f vert (appdata v)
            {
                v2f o;
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = mul(_nativeTextureMatrix, float4(v.uv,0,1)).xy;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Exposure (multiplicative, in stops - camera-style, applied
                // first as if it happened at capture) -> brightness
                // (additive) -> contrast (pivoted around mid-gray) ->
                // saturation (lerp toward the pixel's own luminance) -> tint
                // (color multiply, applied last like a white-balance cast) -
                // standard order, each one a no-op at its default value.
                col.rgb *= exp2(_Exposure);
                col.rgb += _Brightness;
                col.rgb = (col.rgb - 0.5) * _Contrast + 0.5;
                float luminance = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(luminance.xxx, col.rgb, _Saturation);
                col.rgb *= _Tint.rgb;

                col.rgb = saturate(col.rgb);
                return col;
            }
            ENDCG
        }
    }
}
