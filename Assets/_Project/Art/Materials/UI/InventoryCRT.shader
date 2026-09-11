// InventoryCRT.shader
// The inventory's CRT tube. CanvasCRTPresenter renders the inventory canvas into a screen-sized
// texture with a camera of its own, and puts that texture back on screen through a full-screen
// RawImage drawn with this shader. Because it READS the rendered UI, it can do what an overlay never
// could: bend it, split its colours, quantise them.
//
// Order of operations, each one optional through its own amount:
//   1 curvature  2 wobble  3 glitch bands  4 chromatic aberration  5 PS1 colour (15-bit + dither)
//   6 scanlines  7 refresh bar  8 grain + flicker  9 vignette
//
// Kept in step with C#:
//   CRTWarpUV  <->  CRTWarp.Warp            (clicks are unwarped with it — change both or neither)
//   _WarpStrength                            (read by CanvasCRTPresenter for the click mapping)
//   _EnableScanlines, _EnableDither          (driven from the player's Settings by UIPSXSettingsApplier)
//   _ScanlineIntensity, _ScanlineCount       (same formula and defaults as the world's PS1 pass)
//
// The texture is PREMULTIPLIED: the UI camera clears to transparent and the UI blends over it, so
// every channel already carries its alpha. Hence Blend One OneMinusSrcAlpha, and every brightening
// step clamps colour to alpha.

Shader "WIRED/UI/Inventory CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "black" {}
        _Color ("Tint (alpha fades the whole tube)", Color) = (1, 1, 1, 1)

        [Header(Curvature)]
        _WarpStrength ("Warp Strength", Range(0, 0.3)) = 0.06

        [Header(Wobble)]
        _WobbleAmount ("Wobble Amount (px)", Range(0, 4)) = 0.8
        _WobbleFrequency ("Wobble Frequency", Float) = 90
        _WobbleSpeed ("Wobble Speed", Float) = 2

        [Header(Glitch)]
        _GlitchChance ("Band Chance (per band, per step)", Range(0, 0.2)) = 0.015
        _GlitchAmount ("Band Offset (px)", Range(0, 40)) = 8
        _GlitchRate ("Steps per Second", Range(1, 30)) = 10

        [Header(Chromatic Aberration)]
        _ChromaticOffset ("Offset at the Edge (px)", Range(0, 6)) = 1.5

        [Header(PS1 Colour)]
        [ToggleUI] _EnableDither ("Enable Dither", Float) = 1
        // 32 levels per channel is the PS1's own 15-bit colour.
        _DitherLevels ("Colour Levels per Channel", Range(2, 64)) = 32
        _PixelRows ("Dither Grid Rows", Float) = 540

        [Header(Scanlines)]
        [ToggleUI] _EnableScanlines ("Enable Scanlines", Float) = 1
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.125
        _ScanlineCount ("Scanline Count", Float) = 150

        [Header(Refresh Bar)]
        _RollIntensity ("Roll Intensity", Range(0, 0.5)) = 0.05
        _RollWidth ("Roll Width", Range(0.01, 0.5)) = 0.12
        _RollSpeed ("Roll Speed (screens per second)", Range(0, 1)) = 0.07

        [Header(Grain and Flicker)]
        _GrainIntensity ("Grain Intensity", Range(0, 0.5)) = 0.04
        _GrainFPS ("Grain FPS", Range(1, 60)) = 12
        _FlickerIntensity ("Flicker Intensity", Range(0, 0.2)) = 0.015

        [Header(Vignette)]
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.3
        _VignetteStart ("Vignette Start", Range(0, 1.5)) = 0.45
        _VignetteEnd ("Vignette End", Range(0, 1.5)) = 1.05

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;

            float _WarpStrength;
            float _WobbleAmount, _WobbleFrequency, _WobbleSpeed;
            float _GlitchChance, _GlitchAmount, _GlitchRate;
            float _ChromaticOffset;
            float _EnableDither, _DitherLevels, _PixelRows;
            float _EnableScanlines, _ScanlineIntensity, _ScanlineCount;
            float _RollIntensity, _RollWidth, _RollSpeed;
            float _GrainIntensity, _GrainFPS, _FlickerIntensity;
            float _VignetteIntensity, _VignetteStart, _VignetteEnd;

            // Bayer 4x4 — the same ordered-dither matrix as the world pass.
            static const float Bayer4x4[16] =
            {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Mirrored by CRTWarp.Warp in C#.
            float2 CRTWarpUV(float2 uv, float strength, float aspect)
            {
                float2 c = uv - 0.5;
                float2 scaled = float2(c.x * aspect, c.y);
                float fit = 1.0 / (1.0 + strength * (0.25 * aspect * aspect + 0.25));
                float bulge = 1.0 + strength * dot(scaled, scaled);
                return 0.5 + c * (bulge * fit);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 screenSize = _ScreenParams.xy;
                float aspect = screenSize.x / screenSize.y;
                float2 texel = 1.0 / screenSize;   // the UI texture is screen-sized

                // 1) Curvature.
                float2 uv = CRTWarpUV(IN.texcoord, _WarpStrength, aspect);

                // 2) Wobble — a slow horizontal ripple; the tube is never quite still.
                uv.x += sin(uv.y * _WobbleFrequency + _Time.y * _WobbleSpeed) * _WobbleAmount * texel.x;

                // 3) Glitch — now and then one horizontal band slips sideways for a step.
                float step = floor(_Time.y * _GlitchRate);
                float band = floor(uv.y * 36.0);
                if (Hash(float2(band, step)) < _GlitchChance)
                    uv.x += (Hash(float2(step, band + 13.0)) - 0.5) * 2.0 * _GlitchAmount * texel.x;

                // Off the tube: nothing, so the world shows through the rounded corners.
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return fixed4(0, 0, 0, 0);

                // 4) Chromatic aberration, growing toward the edges like a tube's convergence error.
                float2 ca = (uv - 0.5) * 2.0 * _ChromaticOffset * texel;
                half4 centre = tex2D(_MainTex, uv);
                half4 col = half4(tex2D(_MainTex, uv + ca).r, centre.g, tex2D(_MainTex, uv - ca).b, centre.a);

                // 5) PS1 colour: each channel snapped to _DitherLevels steps, with an ordered dither to
                //    break the bands. Done in gamma space, where the console did it.
                if (_EnableDither > 0.5)
                {
                    float cell = max(1.0, round(screenSize.y / max(_PixelRows, 1.0)));
                    uint2 p = (uint2)fmod(floor(IN.vertex.xy / cell), 4.0);
                    float threshold = Bayer4x4[p.y * 4 + p.x] - 0.5;
                    float steps = max(_DitherLevels - 1.0, 1.0);

                    half3 c = col.rgb;
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                    c = LinearToGammaSpace(c);
                    #endif
                    c = saturate(floor(c * steps + threshold + 0.5) / steps);
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                    c = GammaToLinearSpace(c);
                    #endif
                    col.rgb = c;
                }

                half shade = 1.0;

                // 6) Scanlines — same formula and count as the world pass, so the UI sits on the same tube.
                if (_EnableScanlines > 0.5)
                    shade *= 1.0 - _ScanlineIntensity * (0.5 - 0.5 * sin(IN.texcoord.y * _ScanlineCount * UNITY_PI));

                // 7) Refresh bar — a soft brighter band rolling down the tube.
                float roll = frac(IN.texcoord.y + _Time.y * _RollSpeed);
                shade *= 1.0 + _RollIntensity * (1.0 - smoothstep(0.0, _RollWidth, abs(roll - 0.5)));

                // 8) Grain, stepped like tape noise rather than fizzing every frame, and a faint flicker.
                float frame = floor(_Time.y * _GrainFPS);
                shade *= 1.0 - _GrainIntensity * Hash(floor(IN.vertex.xy / 2.0) + frame * 17.0);
                shade *= 1.0 - _FlickerIntensity * Hash(float2(frame, 3.7));

                // 9) Vignette, measured with the real aspect so it stays round.
                float2 centred = (IN.texcoord - 0.5) * float2(aspect, 1.0);
                shade *= 1.0 - _VignetteIntensity * smoothstep(_VignetteStart, _VignetteEnd, length(centred));

                col.rgb *= shade;

                // Premultiplied: colour may never exceed its alpha, or the brightening steps would
                // light up the transparent parts of the tube.
                col.rgb = min(col.rgb, col.a);

                col.rgb *= IN.color.rgb;
                col *= IN.color.a;
                return col;
            }
        ENDCG
        }
    }

    Fallback Off
}
