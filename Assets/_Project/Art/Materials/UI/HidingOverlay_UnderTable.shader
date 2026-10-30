// HidingOverlay_UnderTable.shader
// What the player sees from under a work table (EHidingSpotType.UnderTable): the room, framed by the
// table they are crouched beneath. The most open of the three looks — it is also the only spot that
// does not blind the Nemesis.
//
// Three things, front to back, all in flat tones with hard edges (no dither — see
// HidingOverlayCommon.hlsl):
//   - the NEAR legs, right beside the player: wide dark masses with one lit line down the inner edge,
//     mostly off screen until the camera turns towards one;
//   - the table TOP across the top of the screen: its front apron (the board the player looks out
//     under) with the lower edge the room's light catches, and above it the underside running back
//     over their head, darker in a few steps the closer it comes. Beneath the edge, one flat band of
//     the shadow the table throws; below that the room is untouched;
//   - the FAR legs, at the front of the table: square section seen from between them, so each shows
//     the face turned to the middle of the table (catching the room) beside the one turned to the
//     player (in shadow), with a lit corner between the two. Their top, under the table, is darker.
//     That is their volume.
//
// The legs stand in the look's space, so with the under-table camera's wide pan they slide across the
// screen and the near ones come into view: the table stays put and the player's head turns.
//
// Everything is one fragment of HidingOverlayCommon.hlsl's grid: see that file for the PSX look, the
// space the shapes are laid out in (screen heights from the middle of the screen) and the globals.
//
// Colours: the shadows are near black and faintly cold, the light is a cold grey. Never red (danger
// only) and never amber (the player's device only).
//
// Tuned on UI_HidingUnderTable.mat, on the UnderTable child of HUDCanvas/HidingOverlay. To see it
// without entering Play: HidingOverlayView > Preview In Edit Mode.

Shader "WIRED/UI/Hiding Overlay/Under Table"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Colours)]
        _SurfaceColor ("Table Steel", Color) = (0.09, 0.096, 0.11, 1)
        _ShadeColor ("Deep Shadow (near black, faintly cold)", Color) = (0.015, 0.015, 0.02, 1)
        _LightColor ("Room Light On The Edges (cold grey)", Color) = (0.4, 0.44, 0.48, 1)
        _Opacity ("Solid Opacity (1 = nothing shows through the table)", Range(0, 1)) = 1

        [Header(Table top)]
        _TopReach ("Reach Down The Screen (fraction of its height)", Range(0, 0.8)) = 0.3
        _ApronHeight ("Front Apron Height (screen heights, 0 = none)", Range(0, 0.3)) = 0.05
        _ApronLight ("Apron Brightness (0 = as dark as the shadow)", Range(0, 1)) = 0.7
        _UndersideFalloff ("Underside Darkens Over (screen heights)", Range(0.01, 1)) = 0.22
        [IntRange] _UndersideBands ("Underside Bands (flat steps)", Range(1, 8)) = 3
        _EdgeWidth ("Lit Edge Width (cells)", Range(0, 4)) = 1
        _EdgeLight ("Lit Edge Strength", Range(0, 1)) = 0.55
        _TableShadow ("Shadow Band Under The Edge (opacity, 0 = none)", Range(0, 1)) = 0.35
        _TableShadowReach ("Shadow Band Height (screen heights)", Range(0, 0.3)) = 0.03

        [Header(Far legs)]
        _LegX ("Distance From The Middle (screen heights)", Range(0, 3)) = 0.62
        _LegWidth ("Width (screen heights, 0 = no legs)", Range(0, 0.5)) = 0.075
        _LegSide ("Side Face (fraction of the width)", Range(0, 1)) = 0.4
        _LegSideLight ("Side Face Light", Range(0, 1)) = 0.14
        _LegShade ("Top In The Table's Shadow (screen heights)", Range(0, 1)) = 0.12

        [Header(Near legs)]
        _NearLegX ("Distance From The Middle (screen heights)", Range(0, 4)) = 1
        _NearLegWidth ("Width (screen heights, 0 = no legs)", Range(0, 2)) = 0.36
        _NearLegEdge ("Inner Edge Light", Range(0, 1)) = 0.2

        [Header(Grime)]
        _Grime ("Grime (flat blotches)", Range(0, 1)) = 0.3
        _GrimeScale ("Grime Scale (blotches per screen height)", Range(1, 40)) = 6

        [Header(PSX)]
        _GridRows ("Grid Rows (= PS1Effect _PixelSize)", Float) = 256
        _ColorLevels ("Brightness Levels (flat tones)", Range(2, 64)) = 32
        _Grain ("Grain (solid parts only)", Range(0, 0.1)) = 0.008
        _GrainFps ("Grain FPS", Range(1, 60)) = 12
        [IntRange] _FadeSteps ("Fade Steps (1 = smooth fade)", Range(1, 12)) = 5

        [Header(Vignette)]
        _Vignette ("Vignette (darkens the table, never the room)", Range(0, 1)) = 0.6
        _VignetteStart ("Vignette Start (0 = centre, 1 = corner)", Range(0, 0.99)) = 0.55

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
                float4 _SurfaceColor; \
                float  _TopReach; \
                float  _ApronHeight; \
                float  _ApronLight; \
                float  _UndersideFalloff; \
                float  _UndersideBands; \
                float  _EdgeWidth; \
                float  _EdgeLight; \
                float  _TableShadow; \
                float  _TableShadowReach; \
                float  _LegX; \
                float  _LegWidth; \
                float  _LegSide; \
                float  _LegSideLight; \
                float  _LegShade; \
                float  _NearLegX; \
                float  _NearLegWidth; \
                float  _NearLegEdge;

            #include "HidingOverlayCommon.hlsl"

            half4 Frag(HidingVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                HidingCell cell = HidingOverlayCell(input.uv);
                float2 p = cell.p;
                float dirt = HidingMottle(p) * _Grime;

                half3 color = _ShadeColor.rgb;
                half coverage = 0.0;

                // -- Table top. `above` is how far this cell is over the table's front edge.
                float above = p.y + 0.5 - (1.0 - _TopReach);

                if (above >= 0.0)
                {
                    coverage = 1.0;

                    // The apron's inner face; over it the underside, running back over the player's
                    // head and darker, a step at a time, the closer it comes.
                    float back = HidingBands((above - _ApronHeight) / max(_UndersideFalloff, 1e-5), floor(_UndersideBands + 0.5));
                    half3 underside = lerp(_SurfaceColor.rgb, _ShadeColor.rgb, back);
                    half3 apron = lerp(_ShadeColor.rgb, _SurfaceColor.rgb, _ApronLight);
                    color = above < _ApronHeight ? apron : underside;
                    color = lerp(color, _ShadeColor.rgb, dirt);

                    // The joint where the apron meets the top, and the lower edge the room's light catches.
                    if (abs(above - _ApronHeight) < 0.5 * cell.size) color = _ShadeColor.rgb;
                    if (above < _EdgeWidth * cell.size) color = lerp(color, _LightColor.rgb, _EdgeLight);
                }
                else if (-above < _TableShadowReach)
                {
                    // One flat band of what the table shades just beneath its edge. Below it, the room.
                    coverage = _TableShadow;
                }

                // Mirrored: both legs of a pair are the same distance from the middle.
                float ax = abs(p.x);

                // -- Far legs, under the top. Square section seen from between them.
                if (above < 0.0 && _LegWidth > 0.0)
                {
                    float inner = _LegX - 0.5 * _LegWidth;      // the edge facing the middle of the table
                    float outer = _LegX + 0.5 * _LegWidth;

                    if (ax >= inner && ax <= outer)
                    {
                        // The face turned to the middle catches the room; the one turned to the player
                        // is in shadow.
                        float corner = inner + _LegSide * _LegWidth;
                        half3 side = lerp(_SurfaceColor.rgb, _LightColor.rgb, _LegSideLight);
                        half3 front = lerp(_SurfaceColor.rgb, _ShadeColor.rgb, 0.6);
                        half3 leg = ax < corner ? side : front;
                        leg = lerp(leg, _ShadeColor.rgb, dirt);

                        if (above > -_LegShade)
                        {
                            // The top of the leg, in the table's shadow: one darker band, no highlight.
                            leg = lerp(leg, _ShadeColor.rgb, 0.7);
                        }
                        else if (abs(ax - corner) < 0.5 * _EdgeWidth * cell.size)
                        {
                            // The corner between the two faces.
                            leg = lerp(leg, _LightColor.rgb, _EdgeLight);
                        }

                        color = leg;
                        coverage = 1.0;
                    }
                }

                // -- Near legs, beside the player and in front of everything: a dark mass, with the
                //    room's light along its inner edge.
                if (_NearLegWidth > 0.0)
                {
                    float nearInner = _NearLegX - 0.5 * _NearLegWidth;

                    if (ax >= nearInner && ax <= nearInner + _NearLegWidth)
                    {
                        color = ax - nearInner < cell.size ? lerp(_ShadeColor.rgb, _LightColor.rgb, _NearLegEdge) : _ShadeColor.rgb;
                        coverage = 1.0;
                    }
                }

                return HidingOverlayOutput(input, cell, color, coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
