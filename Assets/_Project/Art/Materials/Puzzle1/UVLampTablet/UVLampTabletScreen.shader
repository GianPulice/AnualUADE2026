// The screen of the UV lamp tablet (Central Puzzle 1 — SP2, the boxes). A small map of the UV lamps
// seen from above: a ring for each place the light can be, a violet light on the lit one. When the
// tablet moves the light, a refresh band sweeps down the screen, the light travels to its new place
// (dimmed: the lamps are off on the way) and a ring spreads where it lands.
//
// Everything but the colours is written by RevealLampTabletScreen through a MaterialPropertyBlock,
// so the material asset stays shared. Positions are in screen units: x from -_Aspect/2 to
// _Aspect/2, y from -0.5 to 0.5, so circles stay round whatever the screen's proportions.
//
// Unlit and self-lit on purpose: it is a screen, it reads the same under any level lighting.
// The idle motion (the slow scan, the light's breathing) runs on _Time, so the pause freezes it.
Shader "WIRED/Puzzles/UV Lamp Tablet Screen"
{
    Properties
    {
        _Background ("Background", Color) = (0.012, 0.004, 0.025, 1)
        _GridColor ("Grid", Color) = (0.09, 0.04, 0.2, 1)
        _SlotColor ("Lamp Places", Color) = (0.35, 0.22, 0.6, 1)
        [HDR] _LightColor ("Lit Lamp", Color) = (1.6, 0.5, 4, 1)
        _OffColor ("Screen Off", Color) = (0.004, 0.004, 0.006, 1)
        _Brightness ("Brightness", Range(0, 4)) = 1
        _GridDensity ("Grid Cells (per screen height)", Range(2, 30)) = 9
        _SlotRadius ("Place Ring Radius", Range(0.01, 0.2)) = 0.065

        [HideInInspector] _Aspect ("Aspect", Float) = 1
        [HideInInspector] _Power ("Power", Range(0, 1)) = 1
        [HideInInspector] _SlotCount ("Slot Count", Float) = 0
        [HideInInspector] _Slots0 ("Slots 0-1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Slots1 ("Slots 2-3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Slots2 ("Slots 4-5", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Slots3 ("Slots 6-7", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Cursor ("Cursor From (xy) To (zw)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Travel ("Travel", Range(0, 1)) = 1
        [HideInInspector] _Lit ("Lit", Range(0, 1)) = 1
        [HideInInspector] _Ping ("Ping", Range(0, 1)) = 1
        [HideInInspector] _Refresh ("Refresh", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Background;
            half4 _GridColor;
            half4 _SlotColor;
            half4 _LightColor;
            half4 _OffColor;
            half _Brightness;
            float _GridDensity;
            float _SlotRadius;
            float _Aspect;
            half _Power;
            float _SlotCount;
            float4 _Slots0;
            float4 _Slots1;
            float4 _Slots2;
            float4 _Slots3;
            float4 _Cursor;
            float _Travel;
            half _Lit;
            half _Ping;
            half _Refresh;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

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
                o.uv = v.uv;
                return o;
            }

            // 1 inside a ring of radius r and half-width w around the centre, 0 outside, antialiased.
            float Ring(float d, float r, float w, float aa)
            {
                return 1.0 - smoothstep(w, w + aa, abs(d - r));
            }

            // 1 inside a disc of radius r, 0 outside, antialiased.
            float Disc(float d, float r, float aa)
            {
                return 1.0 - smoothstep(r, r + aa, d);
            }

            float2 SlotAt(int i)
            {
                float4 pair = i < 2 ? _Slots0 : (i < 4 ? _Slots1 : (i < 6 ? _Slots2 : _Slots3));
                return (i & 1) == 0 ? pair.xy : pair.zw;
            }

            // Distance from p to the segment a-b.
            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
                return length(p - (a + ab * t));
            }

            float Hash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float time = _Time.y;

                // The refresh band: a bright line sweeping top to bottom, with the rows it crosses
                // knocked sideways, like the picture being redrawn.
                float bandY = lerp(1.08, -0.08, _Refresh);
                float band = (1.0 - smoothstep(0.0, 0.06, abs(uv.y - bandY))) * step(_Refresh, 0.999);
                float row = floor(uv.y * 48.0);
                uv.x += band * (Hash(row + floor(time * 30.0)) - 0.5) * 0.06;

                float2 p = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5);
                float aa = max(fwidth(p.y), 1e-4) * 1.5;

                half3 colour = _Background.rgb;

                // Grid, with a slow scan line drifting down it.
                float2 cell = (0.5 - abs(frac(p * _GridDensity) - 0.5)) / _GridDensity; // distance to the nearest line
                float gridLine = 1.0 - smoothstep(0.0, aa, min(cell.x, cell.y) - 0.002);
                float scan = 1.0 - smoothstep(0.0, 0.08, abs(frac(uv.y + time * 0.12) - 0.5));
                colour += _GridColor.rgb * gridLine * (0.6 + 0.6 * scan);

                // The places the light can be.
                float r = _SlotRadius;
                int count = (int)_SlotCount;
                for (int s = 0; s < 8; s++)
                {
                    if (s >= count) break;
                    float d = length(p - SlotAt(s));
                    colour += _SlotColor.rgb * (Ring(d, r, 0.006, aa) + 0.5 * Disc(d, r * 0.2, aa));
                }

                // The light: from where it was to where it is going. Eased, so it sets off and lands softly.
                float2 a = _Cursor.xy;
                float2 b = _Cursor.zw;
                float t = smoothstep(0.0, 1.0, _Travel);
                float2 c = lerp(a, b, t);
                float moving = step(_Travel, 0.999);

                // The trail it leaves along the way, dashes running towards the target.
                float trailD = SegmentDistance(p, a, c);
                float along = dot(p - a, normalize(b - a + 1e-5));
                float dashes = step(0.5, frac(along * 14.0 - time * 6.0));
                colour += _LightColor.rgb * 0.18 * moving * dashes * (1.0 - smoothstep(0.0, 0.012 + aa, trailD));

                float dc = length(p - c);
                half breathe = 0.85 + 0.15 * sin(time * 2.4);
                half strength = lerp(0.35, 1.0, _Lit) * breathe;
                colour += _LightColor.rgb * strength * (Disc(dc, r * 0.55, aa) + 0.35 * exp(-dc * dc / (r * r * 2.5)));

                // The ring that spreads where it lands.
                float pingRadius = lerp(r * 0.6, r * 3.2, _Ping);
                colour += _LightColor.rgb * 0.6 * (1.0 - _Ping) * Ring(length(p - b), pingRadius, 0.008, aa);

                colour += _LightColor.rgb * 0.25 * band;

                // CRT finish: scanlines and darker corners.
                colour *= 0.82 + 0.18 * sin(i.uv.y * 260.0);
                float2 fromCentre = i.uv - 0.5;
                colour *= 1.0 - 0.9 * dot(fromCentre, fromCentre);

                colour = lerp(_OffColor.rgb, colour * _Brightness, _Power);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Depth for the vision fog and SSAO: without it the fog would measure the wall behind.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(vertex.xyz);
            }

            half frag () : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normal);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
