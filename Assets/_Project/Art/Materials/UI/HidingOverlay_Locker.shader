// HidingOverlay_Locker.shader
// What the player sees from inside a metal locker (EHidingSpotType.Locker): the inside of the door a
// hand from their face, and the room through its vent — a block of stamped louvers, in columns.
//
// Not bars across the screen. Each slit is a punched opening with rounded ends in a sheet of steel,
// drawn in flat tones with hard edges (no dither — see HidingOverlayCommon.hlsl):
//   - the blade above hangs into it: the top of the opening is the blade's dark underside, with a lit
//     line along its lower edge, and under it one flat band of shadow. That is the door's thickness;
//   - the rest of the opening is the room, untouched;
//   - the cut edge of the sheet is lit along the bottom of the slit and dark along the top (bevel);
//   - round the slit the sheet is pressed out: a lit face below it and a shaded face above it. Half
//     way between two slits the two faces meet, which reads as the fold of the louver;
//   - the door itself is blotched and streaked in flat patches, and the slit edges are worn uneven.
// Outside the vent block the door is solid: turn the camera and the block slides off to one side.
//
// Everything is one fragment of HidingOverlayCommon.hlsl's grid: see that file for the PSX look, the
// space the shapes are laid out in (screen heights from the middle of the screen) and the globals.
//
// Colours: the shadows are near black and faintly cold, the light is a cold grey. Never red (danger
// only) and never amber (the player's device only).
//
// Tuned on UI_HidingLocker.mat, on the Locker child of HUDCanvas/HidingOverlay. To see it without
// entering Play: HidingOverlayView > Preview In Edit Mode.

Shader "WIRED/UI/Hiding Overlay/Locker"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Colours)]
        _MetalColor ("Door Metal", Color) = (0.06, 0.066, 0.08, 1)
        _ShadeColor ("Deep Shadow (near black, faintly cold)", Color) = (0.015, 0.015, 0.02, 1)
        _LightColor ("Light Through The Slits (cold grey)", Color) = (0.45, 0.49, 0.53, 1)
        _Opacity ("Solid Opacity (1 = nothing shows through the door)", Range(0, 1)) = 1

        [Header(Vent block)]
        _VentCentre ("Centre Height (0 = bottom of the screen, 1 = top)", Range(0, 1)) = 0.52
        _VentHeight ("Height (screen heights)", Range(0.1, 2)) = 0.8
        _VentWidth ("Width (screen heights, 16 by 9 is 1.78 wide)", Range(0.2, 4)) = 1.6
        [IntRange] _Louvers ("Louvers (rows of slits)", Range(1, 24)) = 6
        [IntRange] _Columns ("Columns", Range(1, 6)) = 3
        _ColumnGap ("Gap Between Columns (screen heights)", Range(0, 0.5)) = 0.09

        [Header(Slits)]
        _SlitOpen ("Open Fraction Of Each Louver (what the player sees through)", Range(0.05, 0.9)) = 0.55
        _SlitRound ("Rounded Ends", Range(0, 1)) = 1
        _EdgeWear ("Edge Wear (uneven slit edges)", Range(0, 1)) = 0.15

        [Header(Blade over the slit)]
        _BladeDepth ("Blade Overhang (fraction of the slit it covers from above)", Range(0, 0.8)) = 0.16
        _BladeLight ("Blade Lower Edge Light", Range(0, 1)) = 0.3
        _BladeShadow ("Shadow Band Under The Blade (opacity, 0 = none)", Range(0, 1)) = 0.4
        _BladeShadowReach ("Shadow Band Height (fraction of the slit)", Range(0, 1)) = 0.16

        [Header(Pressed sheet round the slit)]
        _LipWidth ("Cut Edge Width (cells)", Range(0, 4)) = 1
        _LipStrength ("Cut Edge Highlight (lower edge)", Range(0, 1)) = 0.6
        _FaceLight ("Lit Face Below A Slit", Range(0, 1)) = 0.16
        _FaceShade ("Shaded Face Above A Slit", Range(0, 1)) = 0.6
        _FaceReach ("Face Width (1 = the faces of two slits meet in a fold)", Range(0, 1)) = 1

        [Header(Grime)]
        _Grime ("Grime (flat blotches)", Range(0, 1)) = 0.4
        _GrimeScale ("Grime Scale (blotches per screen height)", Range(1, 40)) = 7
        _Streaks ("Dirt Streaks", Range(0, 1)) = 0.25

        [Header(PSX)]
        _GridRows ("Grid Rows (= PS1Effect _PixelSize)", Float) = 256
        _ColorLevels ("Brightness Levels (flat tones)", Range(2, 64)) = 32
        _Grain ("Grain (solid parts only)", Range(0, 0.1)) = 0.008
        _GrainFps ("Grain FPS", Range(1, 60)) = 12
        [IntRange] _FadeSteps ("Fade Steps (1 = smooth fade)", Range(1, 12)) = 5

        [Header(Vignette)]
        _Vignette ("Vignette (darkens the door, never the openings)", Range(0, 1)) = 0.7
        _VignetteStart ("Vignette Start (0 = centre, 1 = corner)", Range(0, 0.99)) = 0.5

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
            "RenderPipeline" = "UniversalPipeline"
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
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            HLSLPROGRAM
            #pragma vertex HidingOverlayVert
            #pragma fragment Frag
            #pragma target 3.0

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #define HIDING_OVERLAY_LOOK_PROPERTIES \
                float4 _MetalColor; \
                float  _VentCentre; \
                float  _VentHeight; \
                float  _VentWidth; \
                float  _Louvers; \
                float  _Columns; \
                float  _ColumnGap; \
                float  _SlitOpen; \
                float  _SlitRound; \
                float  _EdgeWear; \
                float  _BladeDepth; \
                float  _BladeLight; \
                float  _BladeShadow; \
                float  _BladeShadowReach; \
                float  _LipWidth; \
                float  _LipStrength; \
                float  _FaceLight; \
                float  _FaceShade; \
                float  _FaceReach; \
                float  _Streaks;

            #include "HidingOverlayCommon.hlsl"

            half4 Frag(HidingVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                HidingCell cell = HidingOverlayCell(input.uv);
                float2 p = cell.p;

                // The door: one sheet of painted steel, blotched, with dirt run down it. Flat patches:
                // the blotches come in three tones and a streak is either there or not.
                float streak = HidingNoise(float2(p.x * _GrimeScale * 9.0, p.y * _GrimeScale * 0.5 + 11.0));
                float dirt = saturate(HidingMottle(p) * _Grime + step(0.72, streak) * _Streaks);
                half3 metal = lerp(_MetalColor.rgb, _ShadeColor.rgb, dirt);
                half3 color = metal;
                half coverage = 1.0;

                // Which louver and which column this cell belongs to. Clamped, so a cell outside the
                // block still measures to the nearest slit.
                float louvers = max(floor(_Louvers + 0.5), 1.0);
                float columns = max(floor(_Columns + 0.5), 1.0);

                float pitch = _VentHeight / louvers;                                   // one louver, in screen heights
                float v = (p.y + 0.5 - _VentCentre) / pitch + 0.5 * louvers;           // louvers up from the block's lower edge
                float row = clamp(floor(v), 0.0, louvers - 1.0);
                float ly = (v - row - 0.5) * pitch;                                    // from the middle of this louver

                // Columns are cut half way across the gap between two slits, so each side of the gap
                // measures to its own slit.
                float slitWidth = max((_VentWidth - (columns - 1.0) * _ColumnGap) / columns, 1e-4);
                float columnPitch = slitWidth + _ColumnGap;
                float h = (p.x + 0.5 * (_VentWidth + _ColumnGap)) / columnPitch;
                float column = clamp(floor(h), 0.0, columns - 1.0);
                float lx = (h - column - 0.5) * columnPitch;                           // from the middle of this slit

                // Signed distance to the slit, a rounded box: negative inside the opening.
                float halfH = 0.5 * _SlitOpen * pitch;
                float halfW = 0.5 * slitWidth;
                float radius = min(halfH, halfW) * _SlitRound;
                float2 q = abs(float2(lx, ly)) - (float2(halfW, halfH) - radius);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                // Worn: the edge wanders by a cell or so along the slit, differently on each one.
                d += (HidingNoise(float2(p.x * 70.0, row * 13.7 + column * 5.1)) - 0.5) * _EdgeWear * 3.0 * cell.size;

                if (d >= 0.0)
                {
                    // The sheet round the slit. The room's light comes down through the opening, so
                    // everything below its middle is lit and everything above it is in shade.
                    bool below = ly < 0.0;
                    float metalGap = max(0.5 * pitch - halfH, 0.0);     // from a slit to the fold between two

                    if (d <= _LipWidth * cell.size)
                    {
                        // The cut edge of the sheet.
                        color = below ? lerp(_ShadeColor.rgb, _LightColor.rgb, _LipStrength) : _ShadeColor.rgb;
                    }
                    else if (d <= _FaceReach * metalGap)
                    {
                        // The pressed face: lit below the slit, shaded above it.
                        color = below ? lerp(metal, _LightColor.rgb, _FaceLight) : lerp(metal, _ShadeColor.rgb, _FaceShade);
                    }
                }
                else
                {
                    float open = 2.0 * halfH;
                    float clear = (halfH - ly) - _BladeDepth * open;    // > 0 once below the blade's lower edge

                    if (clear < 0.0)
                    {
                        // The underside of the blade above, its lower edge catching the light.
                        color = clear > -cell.size ? lerp(_ShadeColor.rgb, _LightColor.rgb, _BladeLight) : _ShadeColor.rgb;
                        coverage = 1.0;
                    }
                    else
                    {
                        // Open: one flat band of the blade's shadow, then the room, untouched.
                        color = _ShadeColor.rgb;
                        coverage = clear < _BladeShadowReach * open ? _BladeShadow : 0.0;
                    }
                }

                return HidingOverlayOutput(input, cell, color, coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
