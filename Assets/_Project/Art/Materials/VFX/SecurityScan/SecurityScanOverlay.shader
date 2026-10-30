// The security cameras' scan, drawn over the player's body while a camera is scanning them
// (SecurityScanOverlay). The scan FILLS the body from the head down: above _FillY the body shows
// crawling horizontal scanlines, a lit silhouette and a faint tint; below it, nothing yet. A bright
// band (_ScanY) rides the edge of the fill, and keeps running up and down once the body is full.
//
// Same technique as WIRED/Highlight Overlay: an extra material on the body renderer, same vertex
// transform as the body's own shader, drawn after it at LEqual with a small depth offset, so it
// lands exactly on the mesh without z-fighting. Additive, so black is invisible.
//
// The look is tuned here, on the material. Where the band and the fill edge are (_ScanY, _FillY,
// world heights) and how much of it all is showing (_Strength, the fade) are written per renderer
// by SecurityScanOverlay through a MaterialPropertyBlock - the beam each camera draws aims at that
// same height, which is why the scan's movement lives in C# and not in _Time.
Shader "WIRED/Security Scan Overlay"
{
    Properties
    {
        [HDR] _ScanColor ("Scan Color", Color) = (2.5, 0.18, 0.12, 1)

        [Header(Band)]
        _BandWidth ("Band Half-Height (m)", Range(0.01, 0.5)) = 0.07
        _BandIntensity ("Band Intensity", Range(0, 4)) = 1.6
        _BandGlow ("Band Glow (around the band)", Range(0, 1)) = 0.3

        [Header(Scanned Area)]
        _FillSoftness ("Fill Edge Softness (m)", Range(0.001, 0.3)) = 0.03
        _FillTint ("Fill Tint", Range(0, 1)) = 0.08

        [Header(Scanlines)]
        _LineDensity ("Scanlines per Metre", Range(1, 100)) = 30
        _LineThickness ("Scanline Thickness", Range(0.05, 0.9)) = 0.3
        _LineScroll ("Scanline Scroll (m per s)", Range(-2, 2)) = 0.25
        _LineIntensity ("Scanline Intensity", Range(0, 1)) = 0.22

        [Header(Silhouette)]
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.5

        [Header(Signal)]
        _Flicker ("Flicker", Range(0, 1)) = 0.2

        // Written per renderer by SecurityScanOverlay. Not for hand-tuning.
        [HideInInspector] _ScanY ("Scan Height (world)", Float) = 0
        [HideInInspector] _FillY ("Fill Edge Height (world)", Float) = 100
        [HideInInspector] _Strength ("Strength", Float) = 0
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
                half4 _ScanColor;
                half _BandWidth;
                half _BandIntensity;
                half _BandGlow;
                half _FillSoftness;
                half _FillTint;
                half _LineDensity;
                half _LineThickness;
                half _LineScroll;
                half _LineIntensity;
                half _RimPower;
                half _RimIntensity;
                half _Flicker;
                float _ScanY;
                float _FillY;
                half _Strength;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.positionWS = TransformObjectToWorld(v.vertex.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normal);
                o.pos = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // The band: bright where the body crosses the scan height, with a softer glow
                // around it so the line reads as light rather than paint.
                float distance = abs(i.positionWS.y - _ScanY);
                half band = saturate(1.0 - distance / _BandWidth);
                band *= band;
                half glow = saturate(1.0 - distance / (_BandWidth * 5.0)) * _BandGlow;

                // Scanlines in world height, crawling slowly so the body looks sampled, not lit.
                half lines = step(frac((i.positionWS.y - _Time.y * _LineScroll) * _LineDensity), _LineThickness);

                // Silhouette: the edges of the body facing away from the viewer light up.
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0 - saturate(dot(normalize(i.normalWS), viewDir)), _RimPower);

                // A stepped flicker, ~24 times a second: a video signal, not a smooth pulse.
                half noise = frac(sin(floor(_Time.y * 24.0) * 12.9898) * 43758.5453);
                half flicker = 1.0 - _Flicker * noise;

                // The scanned part of the body: everything above the fill edge, softened.
                half filled = smoothstep(_FillY - _FillSoftness, _FillY + _FillSoftness, i.positionWS.y);

                half amount = band * _BandIntensity + glow +
                              filled * (lines * _LineIntensity + rim * _RimIntensity + _FillTint);
                return half4(_ScanColor.rgb * amount * flicker * _Strength, 0);
            }
            ENDHLSL
        }
    }
}
