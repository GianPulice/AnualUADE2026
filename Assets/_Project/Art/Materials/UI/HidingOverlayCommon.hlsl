// HidingOverlayCommon.hlsl
// Shared by the three looks of the hiding overlay — HidingOverlay_Locker, HidingOverlay_UnderTable and
// HidingOverlay_Container — which draw what the player sees of a hiding spot from inside it. One
// full-screen UI quad each, on HUDCanvas, faded by HidingOverlayView.
//
// WHY THE OVERLAY CARRIES ITS OWN PSX LOOK. HUDCanvas is Screen Space - Overlay: it is drawn after
// PC_Renderer's PSXEffect pass, so nothing pixelates it. A smooth shape over that picture reads as a
// sticker on the lens. So every look is evaluated once per cell of the same grid the PSX pass uses
// (_GridRows = PS1Effect.mat's _PixelSize, same aspect-corrected formula) and shaded in flat tones.
//
// NO DITHER, ANYWHERE. The first version turned every soft transition into ordered dither, and over
// a bright room that read as a dot screen printed on the view. The rule now:
//   - what blocks the view is opaque, what is open is clean. A cell is the spot, the room, or one
//     FLAT shadow band of a single opacity — never a pattern of both;
//   - volume comes from flat-shaded bands with hard edges on the grid (a lit lip, a dark underside),
//     and a gradient is cut into a few steps with HidingBands();
//   - the vignette darkens the solid parts only; it never covers what a look leaves open;
//   - the fade is the whole layer's alpha, in a few steps (_FadeSteps): stepped like the rest of the
//     picture, and no cell-by-cell dissolve.
//
// A look only has to answer, for one cell: what colour is the thing in front of the player here, and
// how much of the cell does it cover (0 = the room shows, 1 = solid, in between = a flat shadow band).
// HidingOverlayOutput() does the rest — vignette, colour levels, grain, the fade.
//
// Space. `HidingCell.p` is the centre of the cell in SCREEN HEIGHTS from the middle of the screen
// (x right, y up; a 16:9 screen is 1.78 wide), with the look offset already taken off: the shapes are
// laid out in that space, so they slide when the player looks around inside the spot.
//
// Fed from C#, never from material properties (a property of the same name would hide the global):
//   _HidingOverlayLook  xy = where the middle of the look sits on screen this frame, in screen heights.
//                       Published by HidingOverlayView: the camera's look off the spot's forward,
//                       scaled by the layer's parallax, plus the idle sway. (0, 0) = at rest.
//   _UnscaledTime       the UI clock from UnscaledShaderTime. The grain keeps running under a pause.
//
// The fade is the vertex alpha: HidingOverlayView writes CanvasRenderer.SetAlpha, so no material is
// ever written at runtime and the three .mat assets stay exactly as the designer left them.
//
// A look declares its own material properties through HIDING_OVERLAY_LOOK_PROPERTIES, #defined before
// this file is included, so they land in the same UnityPerMaterial buffer as the shared ones.

#ifndef WIRED_HIDING_OVERLAY_COMMON_INCLUDED
#define WIRED_HIDING_OVERLAY_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#ifndef HIDING_OVERLAY_LOOK_PROPERTIES
    #define HIDING_OVERLAY_LOOK_PROPERTIES
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _ShadeColor;
    float4 _LightColor;
    float  _Opacity;

    float  _Grime;
    float  _GrimeScale;

    float  _GridRows;
    float  _ColorLevels;
    float  _Grain;
    float  _GrainFps;
    float  _FadeSteps;

    float  _Vignette;
    float  _VignetteStart;

    HIDING_OVERLAY_LOOK_PROPERTIES
CBUFFER_END

// Set by the canvas for a RectMask2D.
float4 _ClipRect;

// Globals — see the header.
float4 _HidingOverlayLook;
float  _UnscaledTime;

struct HidingAttributes
{
    float4 positionOS : POSITION;
    float4 color      : COLOR;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct HidingVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float4 positionOS : TEXCOORD1;
    float  fade       : TEXCOORD2;
    UNITY_VERTEX_OUTPUT_STEREO
};

// One cell of the PSX grid.
struct HidingCell
{
    float2 index;   // which cell, counted from the bottom left of the screen
    float2 screen;  // its centre, in screen heights from the middle of the screen
    float2 p;       // the same with the look offset taken off: the space the looks are laid out in
    float  size;    // one cell, in screen heights
    float  aspect;  // screen width over height
};

HidingVaryings HidingOverlayVert(HidingAttributes input)
{
    HidingVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionOS = input.positionOS;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv;

    // Only the alpha of the Graphic's colour is used: it is the fade.
    output.fade = input.color.a;
    return output;
}

float HidingHash21(float2 v)
{
    v = frac(v * float2(233.34, 851.73));
    v += dot(v, v + 23.45);
    return frac(v.x * v.y);
}

// Value noise, 0..1.
float HidingNoise(float2 v)
{
    float2 i = floor(v);
    float2 f = frac(v);
    f = f * f * (3.0 - 2.0 * f);

    float a = HidingHash21(i);
    float b = HidingHash21(i + float2(1.0, 0.0));
    float c = HidingHash21(i + float2(0.0, 1.0));
    float d = HidingHash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Cuts a 0..1 amount into `steps` flat bands (steps + 1 levels, 0 and 1 included).
float HidingBands(float amount, float steps)
{
    steps = max(steps, 1.0);
    return floor(saturate(amount) * steps + 0.5) / steps;
}

// Blotches of dirt, 0..1, as three flat tones. In the look's space, so they stay on the surface as it
// slides.
float HidingMottle(float2 p)
{
    float2 q = p * _GrimeScale;
    return HidingBands(HidingNoise(q) * 0.65 + HidingNoise(q * 2.7 + 31.0) * 0.35, 3.0);
}

HidingCell HidingOverlayCell(float2 uv)
{
    HidingCell cell;
    cell.aspect = _ScreenParams.x / _ScreenParams.y;

    // The PSX pass's grid: _GridRows rows, as many columns as keep each block square.
    float rows = max(_GridRows, 16.0);
    float2 grid = float2(rows * cell.aspect, rows);

    cell.index = floor(uv * grid);
    cell.size = 1.0 / rows;
    cell.screen = ((cell.index + 0.5) / grid - 0.5) * float2(cell.aspect, 1.0);
    cell.p = cell.screen - _HidingOverlayLook.xy;
    return cell;
}

// Turns a look's answer for one cell into the pixel. `color` is linear; `coverage` is 0 where the
// room shows, 1 where the spot is solid, and a flat value in between for a shadow band. Premultiplied
// output, for Blend One OneMinusSrcAlpha.
half4 HidingOverlayOutput(HidingVaryings input, HidingCell cell, half3 color, half coverage)
{
    // Vignette: the solid parts sink into the dark towards the corners, in four flat bands. Colour
    // only — it never covers anything the look left open.
    float fromCentre = length(cell.screen) / length(float2(cell.aspect, 1.0) * 0.5);    // 0 centre, 1 corner
    color = lerp(color, _ShadeColor.rgb, _Vignette * HidingBands(smoothstep(_VignetteStart, 1.0, fromCentre), 4.0));

    // Colour levels, in perceptual space (gamma 2.2), where the console did it. The brightness is
    // snapped and the hue kept: snapping each channel by itself tints near-black tones green and
    // purple from one band to the next.
    half3 perceptual = pow(max(color, 0.0), 1.0 / 2.2);
    float steps = max(_ColorLevels - 1.0, 1.0);
    float peak = max(perceptual.r, max(perceptual.g, perceptual.b));
    perceptual *= (floor(peak * steps + 0.5) / steps) / max(peak, 1e-4);

    // Grain, after the levels so it never flips a cell from one band to the next. One value per cell,
    // stepped _GrainFps times a second like tape noise. Kept faint: it only lives on the solid parts.
    float frame = fmod(floor(_UnscaledTime * _GrainFps), 4096.0);
    perceptual += (HidingHash21(cell.index + frame * 1.618) - 0.5) * _Grain;
    color = pow(saturate(perceptual), 2.2);

    // The fade: the whole layer at once, in _FadeSteps steps (1 = a smooth fade).
    float fade = saturate(input.fade);
    float fadeSteps = floor(_FadeSteps + 0.5);
    if (fadeSteps > 1.5) fade = floor(fade * fadeSteps + 0.5) / fadeSteps;

    float alpha = saturate(coverage) * fade * _Opacity;

    #ifdef UNITY_UI_CLIP_RECT
    float2 inside = step(_ClipRect.xy, input.positionOS.xy) * step(input.positionOS.xy, _ClipRect.zw);
    alpha *= inside.x * inside.y;
    #endif

    #ifdef UNITY_UI_ALPHACLIP
    clip(alpha - 0.001);
    #endif

    return half4(color * alpha, alpha);
}

#endif // WIRED_HIDING_OVERLAY_COMMON_INCLUDED
