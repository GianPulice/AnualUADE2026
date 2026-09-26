// PlayerCameraFeed_HLSL.shader
// Fullscreen post-process for URP: the gameplay camera reads as a camera — the one that follows the
// player. Drawn by PlayerFeedRendererFeature (PC_Renderer) only while the player's own camera rig is
// live, and fed every frame by PlayerCameraFeed (on that rig), which publishes the globals below.
//
// Unlike SecurityCamera_HLSL.shader the picture keeps its colour: no black and white, no monitor
// tint. What makes it a camera is the lens, the signal and the overlay it burns in.
//
// Sits BEFORE PSXEffect (same injection point, earlier in the list), so the whole feed — overlay
// included — goes through the game's PSX filter like the rest of the image.
//
// In order:
//   1) Lens barrel: a light, permanent one. Corners stay in the corners; the centre comes closer.
//   2) Fisheye: the wake-up's uncalibrated lens. The camera renders with a wider field of view
//      (PlayerCameraFeed widens the Cinemachine lens) and this remaps it to a fisheye projection,
//      the centre at the normal lens's scale times the fisheye's zoom: zoomed out, the picture is a
//      circle with the whole room curving round the player. Where the render has no picture, the
//      frame goes black with a soft edge.
//   3) Signal: rows jitter, rare glitch bands, and a tear while there is static.
//   4) Focus: a disc blur while the lens hunts for focus.
//   5) Exposure (the sensor adapting) and saturation (1 = the game's colours, untouched).
//   6) Grain, rolling band and vignette, in perceptual space like the security feed.
//   7) Static: analogue snow over the picture, when the signal comes in and on cuts.
//   8) Overlay burnt in by the camera (not through the lens): label, a blinking recording dot, the
//      readout (recording counter or device module, in the small 3x5 font, its time amber then red
//      as the module runs out), a status line and corner brackets — or, while it boots, the boot
//      screen with its bar, over the picture on a darkened plate.
//
// _OverlayGridRows has to equal PS1Effect.mat's _PixelSize (256): each overlay cell then lands on
// exactly one PSX block and the text comes out whole.
//
// Red: the recording dot is the danger red (#CC1A1A), asked for on 26/09 against the red rule —
// a camera recording the subject. _RecColor. The module's time in its last stretch (and once it
// went off) uses it too, as the old module HUD did: _TimerCriticalColor.

Shader "Hidden/Custom/PlayerCameraFeed"
{
    Properties
    {
        [Header(Lens)]
        _LensDistortion     ("Lens Distortion (barrel, always on)", Range(0, 0.5)) = 0.06
        _Vignette           ("Vignette", Range(0, 1)) = 0.35
        _FisheyeEdge        ("Fisheye Edge Softness (screen heights)", Range(0.001, 0.2)) = 0.04
        _FisheyeRing        ("Fisheye Ring (darkens towards the edge)", Range(0, 1)) = 0.55
        _FocusBlurRadius    ("Focus Blur Radius (screen heights)", Range(0, 0.03)) = 0.012

        [Header(Image)]
        _Saturation         ("Saturation (1 = untouched)", Range(0, 1.5)) = 1

        [Header(Signal)]
        _Noise              ("Noise (grain)", Range(0, 0.3)) = 0.03
        _NoiseFps           ("Noise FPS", Range(1, 60)) = 12
        _LineJitter         ("Line Jitter (UV)", Range(0, 0.01)) = 0.0003
        _GlitchChance       ("Glitch Chance (per quarter second)", Range(0, 1)) = 0.02
        _GlitchShift        ("Glitch Shift (UV)", Range(0, 0.05)) = 0.006
        _RollStrength       ("Rolling Band", Range(0, 0.5)) = 0.02
        _RollSpeed          ("Rolling Band Speed", Range(-1, 1)) = 0.08
        _RollWidth          ("Rolling Band Width", Range(0.01, 0.5)) = 0.07
        _StaticTear         ("Static Tear (UV, with the static)", Range(0, 0.1)) = 0.04
        _StaticFps          ("Static FPS", Range(1, 60)) = 30

        [Header(Overlay)]
        [ToggleUI] _EnableOverlay   ("Enable Overlay (text, REC, brackets, boot screen)", Float) = 1
        _OverlayColor       ("Overlay Color", Color) = (0.9, 0.93, 0.95, 1)
        _RecColor           ("Recording Dot Color", Color) = (0.8, 0.1, 0.1, 1)
        _RecDotRadius       ("Recording Dot Radius (cells)", Range(1, 8)) = 3.5
        _TimerWarningColor  ("Timer Warning Color (module time, a quarter left)", Color) = (1, 0.6, 0, 1)
        _TimerCriticalColor ("Timer Critical Color (a tenth left, last seconds)", Color) = (0.8, 0.1, 0.1, 1)
        [ToggleUI] _SmallReadout    ("Small Readout (3x5 font, bottom left)", Float) = 1
        _OverlayGridRows    ("Overlay Grid Rows (= PS1Effect _PixelSize)", Float) = 256
        _OverlayMargin      ("Overlay Margin (cells)", Range(0, 40)) = 12
        _OverlayShadow      ("Overlay Shadow", Range(0, 1)) = 0.7
        [ToggleUI] _EnableBrackets  ("Enable Corner Brackets", Float) = 1
        _BracketLength      ("Bracket Length (cells)", Range(2, 60)) = 16

        [Header(Boot screen)]
        _BootBarWidth       ("Boot Bar Width (cells)", Range(20, 200)) = 96
        _BootBlockY         ("Boot Block Height (0 = bottom, 1 = top)", Range(0.1, 0.9)) = 0.32
        _BootPlate          ("Boot Plate (darkens the picture behind it)", Range(0, 1)) = 0.65
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float  _LensDistortion;
        float  _Vignette;
        float  _FisheyeEdge;
        float  _FisheyeRing;
        float  _FocusBlurRadius;

        float  _Saturation;

        float  _Noise;
        float  _NoiseFps;
        float  _LineJitter;
        float  _GlitchChance;
        float  _GlitchShift;
        float  _RollStrength;
        float  _RollSpeed;
        float  _RollWidth;
        float  _StaticTear;
        float  _StaticFps;

        float  _EnableOverlay;
        float4 _OverlayColor;
        float4 _RecColor;
        float  _RecDotRadius;
        float4 _TimerWarningColor;
        float4 _TimerCriticalColor;
        float  _SmallReadout;
        float  _OverlayGridRows;
        float  _OverlayMargin;
        float  _OverlayShadow;
        float  _EnableBrackets;
        float  _BracketLength;

        float  _BootBarWidth;
        float  _BootBlockY;
        float  _BootPlate;
    CBUFFER_END

    // Globals from PlayerCameraFeed. Outside Properties on purpose: a material property would hide
    // them.
    //   _PlayerFeedText    glyph indices, 5 lines of 32: [0] label (top left), [1] readout (bottom
    //                      left), [2] status (under the label), [3] boot title, [4] boot
    //                      percentage.
    //   _PlayerFeedInfo    x = label length, y = readout length, z = status length,
    //                      w = live overlay (0..1: label, recording dot, readout, status, brackets).
    //   _PlayerFeedTimer   the module's time in the readout: x = its first character, y = one past
    //                      its last, z = color stage (0 = overlay, 1 = warning, 2 = critical),
    //                      w = 1 shown, 0 while its blink hides it.
    //   _PlayerFeedBoot    x = boot screen (0..1), y = bar fill (0..1), z = title length,
    //                      w = percentage length.
    //   _PlayerFeedLens    x = focal length the frame was rendered with, y = focal length of the
    //                      output (both 1 / tan(vertical FOV / 2)), z = projection (1 = ordinary
    //                      lens, towards 0 = equidistant fisheye), w = fisheye amount (0 = off).
    //   _PlayerFeedSignal  x = power (0 = black), y = static (0..1), z = focus blur (0..1),
    //                      w = exposure (1 = as rendered).
    #define FEED_LINE 32
    float  _PlayerFeedText[160];
    float4 _PlayerFeedInfo;
    float4 _PlayerFeedBoot;
    float4 _PlayerFeedLens;
    float4 _PlayerFeedSignal;
    float4 _PlayerFeedTimer;

    #include "CameraFeedFont.hlsl"

    // Disc for the focus blur: an inner ring of 4 and an outer ring of 8, unit radius.
    static const float2 kFocusTaps[12] =
    {
        float2( 0.50,  0.00), float2( 0.00,  0.50), float2(-0.50,  0.00), float2( 0.00, -0.50),
        float2( 0.92,  0.38), float2( 0.38,  0.92), float2(-0.38,  0.92), float2(-0.92,  0.38),
        float2(-0.92, -0.38), float2(-0.38, -0.92), float2( 0.38, -0.92), float2( 0.92, -0.38),
    };

    // A line of text with its bottom-left corner on cell `origin`: 1 where a letter paints the cell.
    // Cells are 6 wide (5 of letter + 1 of air).
    float LineMask(int2 cell, int2 origin, int textLine, int length)
    {
        int2 p = cell - origin;
        if (p.x < 0 || p.y < 0 || p.y > 6) return 0.0;

        uint index = (uint)p.x / 6u;
        if (index >= (uint)length) return 0.0;

        uint column = (uint)p.x - index * 6u;
        if (column > 4u) return 0.0;

        uint glyph = (uint)_PlayerFeedText[textLine * FEED_LINE + (int)index];
        return GlyphBit(glyph, int2(column, 6 - p.y));
    }

    // The same in the 3x5 font: cells 4 wide (3 of letter + 1 of air), 5 tall.
    float SmallLineMask(int2 cell, int2 origin, int textLine, int length)
    {
        int2 p = cell - origin;
        if (p.x < 0 || p.y < 0 || p.y > 4) return 0.0;

        uint index = (uint)p.x / 4u;
        if (index >= (uint)length) return 0.0;

        uint column = (uint)p.x - index * 4u;
        if (column > 2u) return 0.0;

        uint glyph = (uint)_PlayerFeedText[textLine * FEED_LINE + (int)index];
        return SmallGlyphBit(glyph, int2(column, 4 - p.y));
    }

    // The same line, centred on column `centreX`. Unsigned halving: signed divisions make the
    // compiler warn that they are slow.
    float CentredLineMask(int2 cell, int centreX, int bottom, int textLine, int length)
    {
        uint width = (uint)max(length * 6 - 1, 0);
        return LineMask(cell, int2(centreX - (int)(width / 2u), bottom), textLine, length);
    }

    // The recording overlay: label and status top left, the recording dot top right, the readout
    // bottom left, viewfinder brackets. `lastCell` is the grid's top-right cell. The module's time in
    // the readout comes out in `timer`, not `ink`: it has its own color and its own blink.
    void LiveOverlay(int2 cell, int2 lastCell, out float ink, out float rec, out float timer)
    {
        int margin = (int)_OverlayMargin;
        int topRow = lastCell.y - margin - 6;

        ink = LineMask(cell, int2(margin, topRow), 0, (int)_PlayerFeedInfo.x);
        ink = max(ink, LineMask(cell, int2(margin, topRow - 10), 2, (int)_PlayerFeedInfo.z));

        bool smallFont = _SmallReadout > 0.5;
        float readout = smallFont ? SmallLineMask(cell, int2(margin, margin), 1, (int)_PlayerFeedInfo.y)
                                  : LineMask(cell, int2(margin, margin), 1, (int)_PlayerFeedInfo.y);
        uint readoutChar = (uint)max(cell.x - margin, 0) / (smallFont ? 4u : 6u);
        bool isTime = readoutChar >= (uint)_PlayerFeedTimer.x && readoutChar < (uint)_PlayerFeedTimer.y;
        timer = isTime ? readout * step(0.5, _PlayerFeedTimer.w) : 0.0;
        ink = max(ink, isTime ? 0.0 : readout);

        // The recording dot, top right on the label's line, blinking at 1 Hz like a camera's.
        float radius = max(_RecDotRadius, 1.0);
        float2 toDot = float2(cell) - float2(lastCell.x - margin - radius, topRow + 3);
        rec = step(dot(toDot, toDot), radius * radius) * step(frac(_Time.y), 0.5);

        // Viewfinder brackets, half a margin in from the edge.
        if (_EnableBrackets > 0.5)
        {
            int inset = max((int)(_OverlayMargin * 0.5), 1);
            int2 fromEdge = min(cell - inset, (lastCell - inset) - cell);
            int arm = (int)_BracketLength;
            if (fromEdge.x >= 0 && fromEdge.y >= 0 &&
                ((fromEdge.x == 0 && fromEdge.y < arm) || (fromEdge.y == 0 && fromEdge.x < arm)))
            {
                ink = 1.0;
            }
        }
    }

    // Centre of the boot block, in cells: centred across, at _BootBlockY of the height.
    int2 BootCentre(int2 lastCell)
    {
        return int2((int)((uint)lastCell.x / 2u), (int)(lastCell.y * _BootBlockY));
    }

    // 1 inside the plate behind the boot block (title, bar and percentage, with a margin).
    float BootPlateMask(int2 cell, int2 lastCell)
    {
        int2 centre = BootCentre(lastCell);
        int halfWidth = (int)((uint)max((int)_BootBarWidth, 8) / 2u) + 8;
        int2 d = abs(cell - centre);
        return (d.x <= halfWidth && d.y <= 18) ? 1.0 : 0.0;
    }

    // The boot screen: title, a bar with a one-cell outline and a one-cell gap round the fill, and
    // the percentage under it, all centred across at _BootBlockY.
    float BootOverlay(int2 cell, int2 lastCell)
    {
        int2 centre = BootCentre(lastCell);

        float ink = CentredLineMask(cell, centre.x, centre.y + 8, 3, (int)_PlayerFeedBoot.z);
        ink = max(ink, CentredLineMask(cell, centre.x, centre.y - 14, 4, (int)_PlayerFeedBoot.w));

        int width = max((int)_BootBarWidth, 8);
        int2 b = cell - int2(centre.x - (int)((uint)width / 2u), centre.y - 3);
        if (b.x >= 0 && b.x <= width && b.y >= 0 && b.y <= 6)
        {
            bool outline = b.x == 0 || b.x == width || b.y == 0 || b.y == 6;
            int inner = width - 3;
            int filled = (int)floor(saturate(_PlayerFeedBoot.y) * (inner + 1) + 0.0001);
            bool fill = b.x >= 2 && b.x <= width - 2 && b.y >= 2 && b.y <= 4 && (b.x - 2) < filled;
            if (outline || fill) ink = 1.0;
        }
        return ink;
    }

    // The module's time: the overlay's color, warning, then critical (_PlayerFeedTimer.z 0, 1, 2).
    float4 TimerColor()
    {
        float stage = _PlayerFeedTimer.z;
        return stage <= 1.0 ? lerp(_OverlayColor, _TimerWarningColor, saturate(stage))
                            : lerp(_TimerWarningColor, _TimerCriticalColor, saturate(stage - 1.0));
    }

    void OverlayAt(int2 cell, int2 lastCell, out float ink, out float rec, out float timer)
    {
        ink = 0.0;
        rec = 0.0;
        timer = 0.0;

        float live = saturate(_PlayerFeedInfo.w);
        if (live > 0.001)
        {
            float liveInk, liveRec, liveTimer;
            LiveOverlay(cell, lastCell, liveInk, liveRec, liveTimer);
            ink = liveInk * live;
            rec = liveRec * live;
            timer = liveTimer * live;
        }

        float boot = saturate(_PlayerFeedBoot.x);
        if (boot > 0.001) ink = max(ink, BootOverlay(cell, lastCell) * boot);
    }

    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;

        float aspect = _ScreenParams.x / _ScreenParams.y;
        float rows = max(_OverlayGridRows, 16.0);
        float2 gridSize = float2(rows * aspect, rows);
        float frame = fmod(floor(_Time.y * _NoiseFps), 4096.0);

        float power = saturate(_PlayerFeedSignal.x);
        float staticAmount = saturate(_PlayerFeedSignal.y);
        float blur = saturate(_PlayerFeedSignal.z);
        float exposure = max(_PlayerFeedSignal.w, 0.0);

        half3 color = 0.0;
        if (power > 0.0001)
        {
            // 1) Lens barrel, normalized so the corners sample the corners.
            float2 d = uv - 0.5;
            float2 da = float2(d.x * aspect, d.y);
            float r2 = dot(da, da);
            float maxR2 = 0.25 * (aspect * aspect + 1.0);
            float2 suv = 0.5 + d * (1.0 + _LensDistortion * r2) / (1.0 + _LensDistortion * maxR2);

            // 2) Fisheye. In units of half the screen height: the output pixel's radius gives the
            //    angle of its ray through the fisheye projection r = (F / k) tan(k a), and the
            //    rendered (ordinary) frame has that ray at r = F' tan(a).
            float coverage = 1.0;
            float ring = 1.0;
            float fisheye = saturate(_PlayerFeedLens.w);
            if (fisheye > 0.0001)
            {
                float srcF = max(_PlayerFeedLens.x, 0.0001);
                float outF = max(_PlayerFeedLens.y, 0.0001);
                float k = clamp(_PlayerFeedLens.z, 0.001, 1.0);

                float2 q = (suv - 0.5) * float2(2.0 * aspect, 2.0);
                float rOut = length(q);
                float2 dir = rOut > 1e-5 ? q / rOut : float2(1.0, 0.0);

                // 89 degrees at most: tan stays finite, and nothing past it is ever rendered.
                float angle = min(atan(k * rOut / outF) / k, 1.5533);
                float rSrc = srcF * tan(angle);
                float2 p = dir * rSrc;
                suv = 0.5 + p / float2(2.0 * aspect, 2.0);

                // Where along this direction the rendered frame ends, back in output radius: past it
                // the lens sees nothing. Soft edge, narrowing to nothing as the fisheye goes.
                float rFrame = min(aspect / max(abs(dir.x), 1e-5), 1.0 / max(abs(dir.y), 1e-5));
                float edgeAngle = min(atan(rFrame / srcF), 1.5533);
                float rEdge = (outF / k) * tan(min(k * edgeAngle, 1.5533));
                coverage = saturate((rEdge - rOut) / max(_FisheyeEdge * 2.0 * fisheye, 1e-4));

                // The lens darkening towards its edge.
                float towardsEdge = saturate((rEdge - rOut) / max(rEdge * 0.35, 1e-3));
                ring = 1.0 - _FisheyeRing * fisheye * (1.0 - towardsEdge * towardsEdge * (3.0 - 2.0 * towardsEdge));
            }

            // 3) Signal: every row shifts a little; now and then a glitch breaks a band of rows; the
            //    static tears the picture sideways.
            float row = floor(suv.y * rows);
            float shift = (Hash21(float2(row, frame)) - 0.5) * _LineJitter;

            float slot = floor(_Time.y * 4.0);
            float burst = step(1.0 - _GlitchChance, Hash21(float2(slot, 17.0)));
            float inBand = 1.0 - step(0.05, abs(suv.y - Hash21(float2(slot, 29.0))));
            shift += burst * inBand * (Hash21(float2(row, slot)) - 0.5) * 2.0 * _GlitchShift;
            shift += staticAmount * (Hash21(float2(row, frame + 7.0)) - 0.5) * _StaticTear;
            suv.x += shift;

            // 4) Focus: a disc of 12 taps round the centre while the lens hunts.
            half3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, suv).rgb;
            if (blur > 0.001)
            {
                float2 radius = float2(_FocusBlurRadius / aspect, _FocusBlurRadius) * blur;
                half3 sum = c;
                [unroll] for (int i = 0; i < 12; i++)
                    sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, suv + kFocusTaps[i] * radius).rgb;
                c = sum / 13.0;
            }

            // 5) Exposure (the sensor adapting to the light), then saturation.
            c *= exposure;
            half luma = dot(c, half3(0.2126, 0.7152, 0.0722));
            c = lerp(luma.xxx, c, _Saturation);

            // 6) Grain (one value per PSX block, _NoiseFps times a second), the light band rolling
            //    down and the vignette, in perceptual space (gamma 2.2).
            half3 pc = pow(max(c, 0.0), 1.0 / 2.2);
            half grain = Hash21(floor(suv * gridSize) + frame * 1.618) - 0.5;
            pc += grain * _Noise;

            float roll = (frac(uv.y + _Time.y * _RollSpeed) - 0.5) / max(_RollWidth, 0.001);
            pc += exp(-roll * roll) * _RollStrength;

            pc *= saturate(1.0 - _Vignette * pow(saturate(r2 / maxR2), 1.25));

            color = pow(saturate(pc), 2.2) * (coverage * ring * power);
        }

        // 7) Static: snow, one value per PSX block, faster than the grain. Replaces the picture as
        //    it rises.
        if (staticAmount > 0.001)
        {
            float snowFrame = fmod(floor(_Time.y * _StaticFps), 4096.0);
            half snow = Hash21(floor(uv * gridSize) + snowFrame * 3.17);
            color = lerp(color, pow(snow, 2.2).xxx * 0.85, staticAmount);
        }

        // 8) Overlay. On PS1Effect's grid shifted half a block: the PSX samples each block at its
        //    corner, and this way that corner falls in the centre of one of these cells.
        if (_EnableOverlay > 0.5 && (_PlayerFeedInfo.w > 0.001 || _PlayerFeedBoot.x > 0.001))
        {
            int2 cell = int2(floor(uv * gridSize + 0.5));
            int2 lastCell = int2(floor(gridSize + 0.5));

            // The boot screen sits over the picture: darken a plate behind it so it reads.
            float bootScreen = saturate(_PlayerFeedBoot.x);
            if (bootScreen > 0.001)
                color *= 1.0 - _BootPlate * bootScreen * BootPlateMask(cell, lastCell);

            float ink, rec, timer, inkShadow, recShadow, timerShadow;
            OverlayAt(cell, lastCell, ink, rec, timer);
            OverlayAt(cell + int2(-1, 1), lastCell, inkShadow, recShadow, timerShadow);

            float shadow = max(max(inkShadow, recShadow), timerShadow) * _OverlayShadow *
                           (1.0 - max(max(ink, rec), timer));
            float4 timerColor = TimerColor();
            color = lerp(color, 0.0, shadow);
            color = lerp(color, _OverlayColor.rgb, ink * _OverlayColor.a);
            color = lerp(color, timerColor.rgb, timer * timerColor.a);
            color = lerp(color, _RecColor.rgb, rec * _RecColor.a);
        }

        return half4(color, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PlayerCameraFeed"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
