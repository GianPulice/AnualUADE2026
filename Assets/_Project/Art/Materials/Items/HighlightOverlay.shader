// Additive glow drawn on top of an interactable part while the crosshair is on it
// (ItemProximityHighlight). Used on URP/Lit parts whose emission is masked by an _EmissionMap:
// adding the highlight to their _EmissionColor would only light the map's spots, and swapping the
// map made the authored glow blink off and on. This layer leaves the material untouched and fades
// in and out with _OverlayColor (black = invisible), so the change is smooth both ways.
//
// Same vertex transform as URP/Lit, drawn after it at LEqual with a small depth offset, so it
// lands exactly on the part without z-fighting. Colour comes per renderer from a
// MaterialPropertyBlock; the material asset stays shared.
Shader "WIRED/Highlight Overlay"
{
    Properties
    {
        [HDR] _OverlayColor ("Overlay Color", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend One One
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OverlayColor;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                return half4(_OverlayColor.rgb, 0);
            }
            ENDHLSL
        }
    }
}
