// Paint that only shows under the player's module light (Central Puzzle 2 — SP2, the arrow wall).
// _LightReveal 0 = completely invisible, 1 = fully visible; LightRevealWall fades it per renderer
// through a MaterialPropertyBlock, so the material asset stays shared.
//
// Unlit and self-lit on purpose: the arrows must read the same under any level lighting once they
// are revealed, and stay invisible under any level lighting until then. The texture's alpha is the
// shape (white arrow on transparent), _BaseColor tints it.
Shader "WIRED/Puzzles/Light Reveal"
{
    Properties
    {
        _BaseMap ("Shape (alpha)", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 0.78, 0.31, 1)
        _Emission ("Brightness", Range(0, 4)) = 1.5
        _LightReveal ("Light Reveal", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Emission;
                half _LightReveal;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 shape = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half alpha = shape.a * _BaseColor.a * saturate(_LightReveal);
                clip(alpha - 0.001h);
                return half4(shape.rgb * _BaseColor.rgb * _Emission, alpha);
            }
            ENDHLSL
        }
    }
}
