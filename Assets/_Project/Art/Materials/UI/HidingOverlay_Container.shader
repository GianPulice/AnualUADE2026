// HidingOverlay_Container.shader
// What the player sees from inside a sealed cargo container (EHidingSpotType.Container): two steel
// doors shut in front of them and the gap between the two — the only place the world gets in, and
// the only place they can watch it from. The most closed of the three looks, but the gap is wide
// enough to follow what crosses the room: _SeamWidth 0.18 is a tenth of a 16:9 screen (the first
// version was a sixth of that and the player could see next to nothing).
//
// What makes it a container and not a black screen with a strip cut out of it, all in flat tones
// with hard edges (no dither — see HidingOverlayCommon.hlsl):
//   - the gap is clean: the room, untouched, between two hard edges that wander a little up the
//     doors, like a worn gasket;
//   - either side of it, a band of black rubber with one lit line on its inner edge;
//   - the doors are corrugated steel lit only from the gap's side: every rib shows a flank turned to
//     the light, a crest it grazes, and a flank and a trough it never reaches, each one flat tone.
//     The light dies away rib by rib, so the doors sink into the dark towards the sides;
//   - the frame the doors shut against closes the top and the bottom, with a line of light leaking
//     along the sill.
//
// Everything is one fragment of HidingOverlayCommon.hlsl's grid: see that file for the PSX look, the
// space the shapes are laid out in (screen heights from the middle of the screen) and the globals.
//
// Colours: the shadows are near black and faintly cold, the light is a cold grey. Never red (danger
// only) and never amber (the player's device only).
//
// Tuned on UI_HidingContainer.mat, on the Container child of HUDCanvas/HidingOverlay. To see it
// without entering Play: HidingOverlayView > Preview In Edit Mode.

Shader "WIRED/UI/Hiding Overlay/Container"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Colours)]
        _WallColor ("Door Steel", Color) = (0.075, 0.08, 0.095, 1)
        _ShadeColor ("Deep Shadow (near black, faintly cold)", Color) = (0.012, 0.012, 0.017, 1)
        _LightColor ("Light Through The Gap (cold grey)", Color) = (0.55, 0.6, 0.64, 1)
        _Opacity ("Solid Opacity (1 = sealed, nothing shows through the doors)", Range(0, 1)) = 1

        [Header(Gap between the doors)]
        _SeamX ("Position (screen heights from the middle)", Range(-1, 1)) = 0
        _SeamWidth ("Width (screen heights) - how much the player sees", Range(0.01, 0.6)) = 0.18
        _SeamWander ("Uneven Edges (worn gasket)", Range(0, 1)) = 0.12
        _Gasket ("Gasket Width (screen heights)", Range(0, 0.2)) = 0.022
        _GasketLight ("Gasket Inner Edge Light", Range(0, 1)) = 0.35

        [Header(Light on the doors)]
        _SeamGlow ("Glow", Range(0, 1)) = 0.3
        _GlowReach ("Glow Reach (screen heights)", Range(0.02, 2)) = 0.5
        _Ambient ("Steel Seen In The Dark", Range(0, 1)) = 0.3
        _RibPitch ("Corrugation Pitch (screen heights)", Range(0.02, 0.5)) = 0.11
        _RibFlank ("Corrugation Flank (fraction of the pitch)", Range(0.05, 0.45)) = 0.2

        [Header(Frame)]
        _Header ("Header (fraction of the screen from the top)", Range(0, 0.5)) = 0.07
        _Sill ("Sill (fraction of the screen from the bottom)", Range(0, 0.5)) = 0.09
        _SillLeak ("Light Leaking Along The Sill", Range(0, 1)) = 0.3

        [Header(Grime)]
        _Grime ("Grime (flat blotches)", Range(0, 1)) = 0.3
        _GrimeScale ("Grime Scale (blotches per screen height)", Range(1, 40)) = 5

        [Header(PSX)]
        _GridRows ("Grid Rows (= PS1Effect _PixelSize)", Float) = 256
        _ColorLevels ("Brightness Levels (flat tones)", Range(2, 64)) = 32
        _Grain ("Grain (solid parts only)", Range(0, 0.1)) = 0.008
        _GrainFps ("Grain FPS", Range(1, 60)) = 12
        [IntRange] _FadeSteps ("Fade Steps (1 = smooth fade)", Range(1, 12)) = 5

        [Header(Vignette)]
        _Vignette ("Vignette (darkens the doors, never the gap)", Range(0, 1)) = 0
        _VignetteStart ("Vignette Start (0 = centre, 1 = corner)", Range(0, 0.99)) = 0.45

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
                float4 _WallColor; \
                float  _SeamX; \
                float  _SeamWidth; \
                float  _SeamWander; \
                float  _Gasket; \
                float  _GasketLight; \
                float  _SeamGlow; \
                float  _GlowReach; \
                float  _Ambient; \
                float  _RibPitch; \
                float  _RibFlank; \
                float  _Header; \
                float  _Sill; \
                float  _SillLeak;

            #include "HidingOverlayCommon.hlsl"

            half4 Frag(HidingVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                HidingCell cell = HidingOverlayCell(input.uv);
                float2 p = cell.p;
                float y = p.y + 0.5;                    // 0 bottom of the screen, 1 top, at rest
                float dx = abs(p.x - _SeamX);           // from the middle of the gap

                // The gap's edges are a worn gasket, not a ruled line: its width wanders up the doors.
                // Only the gap wanders — the ribs below are measured from its nominal edge and stay
                // straight, or the doors would hang like a curtain.
                float wander = lerp(1.0 - _SeamWander, 1.0, HidingNoise(float2(y * 17.0, 3.7)));
                float gap = dx - 0.5 * _SeamWidth * wander;     // < 0 in the opening

                // The room through the gap, untouched; everything else is door. A hard edge.
                half coverage = gap < 0.0 ? 0.0 : 1.0;

                // Corrugated steel lit from the gap's side. Along one rib, going away from the gap:
                // the flank turned to the light, the crest it grazes, the flank turned away from it,
                // and the trough in that flank's shadow. The light is taken once per rib, so each of
                // those is one flat tone.
                float steel = max(dx - 0.5 * _SeamWidth - _Gasket, 0.0);    // along the door, past the gasket
                float ribIndex = floor(steel / _RibPitch);
                float rib = steel / _RibPitch - ribIndex;
                float reach = exp(-ribIndex * _RibPitch / _GlowReach);      // how much of the light gets this far
                float facing = rib < _RibFlank ? 1.0 : (rib < 0.5 ? 0.4 : (rib < 0.5 + _RibFlank ? 0.0 : 0.15));

                // The steel shows a little even where the gap's light does not reach, so the ribs
                // are still there in the dark; the light itself is added on top.
                float seen = _Ambient * lerp(0.5, 1.0, facing) + (1.0 - _Ambient) * facing * reach;
                half3 color = lerp(_ShadeColor.rgb, _WallColor.rgb, saturate(seen));
                color += _LightColor.rgb * (_SeamGlow * reach * facing);
                color = lerp(color, _ShadeColor.rgb, HidingMottle(p) * _Grime);

                if (gap < _Gasket)
                {
                    // Black rubber, with the light that squeezes past it along its inner edge.
                    color = gap < cell.size ? lerp(_ShadeColor.rgb, _LightColor.rgb, _GasketLight) : _ShadeColor.rgb;
                }

                float sill = _Sill - y;                 // > 0 below the sill's upper edge
                float header = y - (1.0 - _Header);     // > 0 over the header's lower edge

                if (max(sill, header) > 0.0)
                {
                    // The frame the doors shut against: nothing gets past it, the gap included.
                    coverage = 1.0;
                    color = _ShadeColor.rgb;

                    // The doors do not quite meet the sill: a line of light along it, strongest under
                    // the gap and dimmer rib by rib.
                    if (sill > 0.0 && sill < cell.size)
                    {
                        float leak = _SillLeak * exp(-floor(dx / _RibPitch) * _RibPitch / (2.0 * _GlowReach));
                        color = lerp(color, _LightColor.rgb, leak);
                    }
                }

                return HidingOverlayOutput(input, cell, color, coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
