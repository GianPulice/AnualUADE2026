// Shader URP para outline en elementos uGUI (Image, RawImage).
// Asignar este material al componente Image. El outline se dibuja
// muestreando los 4 vecinos del texel y detectando donde la imagen
// tiene alpha pero el pixel actual no.
//
// Uso: crear Material con este shader, arrastrarlo al campo Material
// de un componente Image. Ajustar _OutlineColor y _OutlineWidth.

Shader "Custom/UI/Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color          ("Tint",          Color) = (1,1,1,1)
        _OutlineColor   ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth   ("Outline Width", Range(0, 10)) = 2

        // Propiedades requeridas por el sistema de UI de Unity
        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        _ColorMask        ("Color Mask",         Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UI_OUTLINE"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS   : SV_POSITION;
                half4  color         : COLOR;
                float2 uv            : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // _ClipRect es un uniform global seteado por el Canvas renderer por draw call
            float4 _ClipRect;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4  _Color;
                half4  _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = IN.positionOS;
                OUT.positionHCS   = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv            = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color         = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                // Muestreo en 4 direcciones para detectar el borde
                float2 ts = _MainTex_TexelSize.xy * _OutlineWidth;
                half a0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2( ts.x,    0)).a;
                half a1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2(-ts.x,    0)).a;
                half a2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2(    0,  ts.y)).a;
                half a3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2(    0, -ts.y)).a;
                half maxNeighbor = max(max(a0, a1), max(a2, a3));

                // Outline = vecino con alpha, pixel actual sin alpha
                half outline = maxNeighbor * (1.0h - color.a);
                color.rgb    = lerp(color.rgb, _OutlineColor.rgb, outline);
                color.a      = max(color.a, outline * _OutlineColor.a);

                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, IN.worldPosition.xy)
                              * step(IN.worldPosition.xy, _ClipRect.zw);
                color.a *= inside.x * inside.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
}
