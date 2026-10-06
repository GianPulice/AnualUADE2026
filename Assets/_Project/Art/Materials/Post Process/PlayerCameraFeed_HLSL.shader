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
//      readout (the device's module, all of it amber then red as the module runs out, blinking
//      with the dot, faster as it gets worse), the bomb banner that sweeps across the middle when a
//      module starts, a status line and corner brackets — or, while it boots, the boot screen with its bar,
//      over the picture on a darkened plate. With the Nemesis closing in its letters scramble
//      (never the readout's time).
//
// The Nemesis closing in also corrupts the picture in blocks (ThreatBlock), like a digital signal
// dropping data: before 1) a corrupted block changes where it reads the picture from, and after 6)
// some go to rows of black and grey. The blocks are whole cells of the overlay grid, so they
// land on the PSX's own blocks; everything outside a corrupted block is the plain picture.
//
// _OverlayGridRows has to equal PS1Effect.mat's _PixelSize (256): each overlay cell then lands on
// exactly one PSX block and the text comes out whole.
//
// Red: the recording dot is the danger red (#CC1A1A), asked for on 26/09 against the red rule —
// a camera recording the subject. _RecColor. The bomb banner uses it too. The module's readout in
// its last stretch (and once it went off) uses the same red, as the old module HUD did:
// _TimerCriticalColor.

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

        [Header(Nemesis closing in)]
        _ThreatBlockCells   ("Corrupt Block Size (cells)", Range(4, 64)) = 16
        _ThreatBlockRun     ("Corrupt Run (blocks side by side)", Range(1, 8)) = 3
        _ThreatBlockFps     ("Corrupt Block FPS (a new draw this often)", Range(1, 30)) = 8
        _ThreatBlockTint    ("Grey Block Tint (the grey of the black and grey blocks)", Color) = (0.6, 0.6, 0.6, 1)
        _ThreatScrambleFps  ("Overlay Scramble FPS", Range(1, 30)) = 8

        [Header(Overlay)]
        [ToggleUI] _EnableOverlay   ("Enable Overlay (text, REC, brackets, boot screen)", Float) = 1
        _OverlayColor       ("Overlay Color", Color) = (0.9, 0.93, 0.95, 1)
        _RecColor           ("Recording Dot Color", Color) = (0.8, 0.1, 0.1, 1)
        _RecDotRadius       ("Recording Dot Radius (cells)", Range(1, 8)) = 3.5
        _TimerWarningColor  ("Timer Warning Color (module time, a quarter left)", Color) = (1, 0.6, 0, 1)
        _TimerCriticalColor ("Timer Critical Color (a tenth left, last seconds)", Color) = (0.8, 0.1, 0.1, 1)
        _OverlayGridRows    ("Overlay Grid Rows (= PS1Effect _PixelSize)", Float) = 256
        _OverlayMargin      ("Overlay Margin (cells)", Range(0, 40)) = 12
        _OverlayShadow      ("Overlay Shadow", Range(0, 1)) = 0.7
        [ToggleUI] _EnableBrackets  ("Enable Corner Brackets", Float) = 1
        _BracketLength      ("Bracket Length (cells)", Range(2, 60)) = 16
        _BannerScale        ("Bomb Banner Scale (cells per font cell)", Range(1, 8)) = 3
        _ReadoutCentreScale ("Readout Scale in the Middle (cells per font cell)", Range(1, 8)) = 2

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

        float  _ThreatBlockCells;
        float  _ThreatBlockRun;
        float  _ThreatBlockFps;
        float4 _ThreatBlockTint;
        float  _ThreatScrambleFps;

        float  _EnableOverlay;
        float4 _OverlayColor;
        float4 _RecColor;
        float  _RecDotRadius;
        float4 _TimerWarningColor;
        float4 _TimerCriticalColor;
        float  _OverlayGridRows;
        float  _OverlayMargin;
        float  _OverlayShadow;
        float  _EnableBrackets;
        float  _BracketLength;
        float  _BannerScale;
        float  _ReadoutCentreScale;

        float  _BootBarWidth;
        float  _BootBlockY;
        float  _BootPlate;
    CBUFFER_END

    // Globals from PlayerCameraFeed. Outside Properties on purpose: a material property would hide
    // them.
    //   _PlayerFeedText    glyph indices, 6 lines of 32: [0] label (top left), [1] readout (bottom
    //                      left, or travelling there from the middle), [2] status (under the
    //                      label), [3] boot title, [4] boot percentage, [5] bomb banner.
    //   _PlayerFeedInfo    x = label length, y = readout length, z = status length,
    //                      w = live overlay (0..1: label, recording dot, readout, status, brackets).
    //   _PlayerFeedReadout the readout of the device's module: x = travel (0 = middle of the
    //                      screen, 1 = bottom left), y = its length once typed (it centres on
    //                      that), z = color stage (0 = overlay, 1 = warning, 2 = critical),
    //                      w = blink phase (0..1, lit while under half; -1 = steady).
    //   _PlayerFeedReadoutTime  the time inside the readout's text: x = index of its first
    //                      glyph, y = how many glyphs (the digits that blink, see ReadoutLit).
    //   _PlayerFeedBanner  the bomb banner, in the middle in the danger red: x = how far it has
    //                      swept in, y = how far it has swept out (0..1, left to right),
    //                      z = length (0 = not up), w = blinks per second.
    //   _PlayerFeedRecBlink  blinks per second of the recording dot.
    //   _PlayerFeedBoot    x = boot screen (0..1), y = bar fill (0..1), z = title length,
    //                      w = percentage length.
    //   _PlayerFeedLens    x = focal length the frame was rendered with, y = focal length of the
    //                      output (both 1 / tan(vertical FOV / 2)), z = projection (1 = ordinary
    //                      lens, towards 0 = equidistant fisheye), w = fisheye amount (0 = off).
    //   _PlayerFeedSignal  x = power (0 = black), y = static (0..1), z = focus blur (0..1),
    //                      w = exposure (1 = as rendered).
    //   _PlayerFeedThreat  the Nemesis closing in: x = part of the picture's blocks corrupted
    //                      (0..1), y = part of the overlay's letters scrambled (0..1).
    #define FEED_LINE 32
    float  _PlayerFeedText[192];
    float4 _PlayerFeedInfo;
    float4 _PlayerFeedBoot;
    float4 _PlayerFeedLens;
    float4 _PlayerFeedSignal;
    float4 _PlayerFeedThreat;
    float4 _PlayerFeedReadout;
    float4 _PlayerFeedReadoutTime;
    float4 _PlayerFeedBanner;
    float  _PlayerFeedRecBlink;

    #include "CameraFeedFont.hlsl"

    // Disc for the focus blur: an inner ring of 4 and an outer ring of 8, unit radius.
    static const float2 kFocusTaps[12] =
    {
        float2( 0.50,  0.00), float2( 0.00,  0.50), float2(-0.50,  0.00), float2( 0.00, -0.50),
        float2( 0.92,  0.38), float2( 0.38,  0.92), float2(-0.38,  0.92), float2(-0.92,  0.38),
        float2(-0.92, -0.38), float2(-0.38, -0.92), float2( 0.38, -0.92), float2( 0.92, -0.38),
    };

    // A hash of three whole numbers that holds whatever their size. Hash21 (the font's) spends its
    // float precision on large arguments: with a frame counter in the thousands its y is gone, and
    // snow drawn with it comes out as vertical stripes for most of every cycle of the counter.
    float HashCell(float x, float y, float z)
    {
        uint h = (uint)(int)x * 1597334677u ^ (uint)(int)y * 3812015801u ^ (uint)(int)z * 2798796415u;
        h ^= h >> 16;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return (float)(h >> 8) * (1.0 / 16777216.0);
    }

    // The Nemesis closing in scrambles the overlay's text: _PlayerFeedThreat.y of its letters come
    // out as some other glyph, drawn again _ThreatScrambleFps times a second. A space stays a
    // space, so a line keeps its outline, and the readout's time is spared: the countdown has to
    // stay readable through a chase.
    uint ScrambledGlyph(uint glyph, int textLine, uint index)
    {
        float amount = saturate(_PlayerFeedThreat.y);
        if (amount <= 0.001 || glyph == 0u) return glyph;

        float place = (float)index;
        if (textLine == 1 && place >= _PlayerFeedReadoutTime.x &&
            place < _PlayerFeedReadoutTime.x + _PlayerFeedReadoutTime.y) return glyph;

        float slot = fmod(floor(_Time.y * _ThreatScrambleFps), 4096.0);
        if (HashCell(place, (float)textLine, slot) >= amount) return glyph;

        return 1u + (uint)(HashCell(place, (float)textLine + 64.0, slot) * ((float)GLYPH_COUNT - 1.001));
    }

    // What the Nemesis closing in made of the block of the picture a pixel is in.
    #define BLOCK_CLEAN  0
    #define BLOCK_MOSAIC 1  // The whole block takes the colour at its centre.
    #define BLOCK_SLIP   2  // It shows the picture from up to a block away.
    #define BLOCK_GREY   3  // A mosaic in rows of black and grey (applied in Frag, after the picture).

    // The Nemesis closing in corrupts the picture in blocks: _PlayerFeedThreat.x of them, in runs of
    // _ThreatBlockRun side by side, drawn again _ThreatBlockFps times a second. A block is
    // _ThreatBlockCells cells of the overlay grid, shifted half a cell like the overlay's, so its
    // edges fall between the PSX's blocks. `pictureUv` comes out as where the pixel reads the
    // picture from: `uv` itself in a clean block.
    int ThreatBlock(float2 uv, float2 gridSize, out float2 pictureUv)
    {
        pictureUv = uv;

        float amount = saturate(_PlayerFeedThreat.x);
        if (amount <= 0.001) return BLOCK_CLEAN;

        float size = max(floor(_ThreatBlockCells), 1.0);
        float2 blockIndex = floor(floor(uv * gridSize + 0.5) / size);
        float slot = fmod(floor(_Time.y * _ThreatBlockFps), 4096.0);

        float run = floor(blockIndex.x / max(floor(_ThreatBlockRun), 1.0));
        if (HashCell(run, blockIndex.y, slot) >= amount) return BLOCK_CLEAN;

        float2 centre = saturate((blockIndex * size + size * 0.5 - 0.5) / gridSize);
        float kind = HashCell(blockIndex.x, blockIndex.y, slot + 4096.0);
        if (kind < 0.4)
        {
            pictureUv = centre;
            return BLOCK_MOSAIC;
        }
        if (kind < 0.75)
        {
            // Half a block or a whole one to either side, and up to half a block up or down.
            float across = floor(HashCell(blockIndex.x, blockIndex.y, slot + 8192.0) * 4.0);
            float up = floor(HashCell(blockIndex.x, blockIndex.y, slot + 12288.0) * 3.0);
            float2 slip = float2(across < 2.0 ? across - 2.0 : across - 1.0, up - 1.0) * (size * 0.5);
            pictureUv = saturate(uv + slip / gridSize);
            return BLOCK_SLIP;
        }

        pictureUv = centre;
        return BLOCK_GREY;
    }

    // A line of text with its bottom-left corner on cell `origin`: 1 where a letter paints the cell.
    // Cells are 6 wide (5 of letter + 1 of air), each font cell `scale` cells of the grid. `glyph`
    // is the index of the letter the cell belongs to (meaningless where the mask is 0).
    float LineMaskGlyph(int2 cell, int2 origin, int textLine, int length, int scale, out uint glyphIndex)
    {
        glyphIndex = 0u;

        int2 p = cell - origin;
        if (p.x < 0 || p.y < 0) return 0.0;

        p = int2((uint2)p / (uint)max(scale, 1));
        if (p.y > 6) return 0.0;

        uint index = (uint)p.x / 6u;
        if (index >= (uint)length) return 0.0;

        uint column = (uint)p.x - index * 6u;
        if (column > 4u) return 0.0;

        glyphIndex = index;
        uint glyph = (uint)_PlayerFeedText[textLine * FEED_LINE + (int)index];
        return GlyphBit(ScrambledGlyph(glyph, textLine, index), int2(column, 6 - p.y));
    }

    float LineMask(int2 cell, int2 origin, int textLine, int length, int scale)
    {
        uint glyphIndex;
        return LineMaskGlyph(cell, origin, textLine, length, scale, glyphIndex);
    }

    // The same line, centred on column `centreX`. Unsigned halving: signed divisions make the
    // compiler warn that they are slow.
    float CentredLineMask(int2 cell, int centreX, int bottom, int textLine, int length)
    {
        uint width = (uint)max(length * 6 - 1, 0);
        return LineMask(cell, int2(centreX - (int)(width / 2u), bottom), textLine, length, 1);
    }

    // The middle of the screen as the origin of a line `length` characters long at `scale`: centred
    // across and up.
    int2 MiddleOrigin(int2 lastCell, int length, int scale)
    {
        int span = max(length * 6 - 1, 0) * scale;
        return int2((int)((uint)lastCell.x / 2u) - (int)((uint)span / 2u),
                    (int)((uint)lastCell.y / 2u) - (int)((uint)(7 * scale) / 2u));
    }

    // The blink of the readout's time: 1 while its digits are drawn, 0 in the dark half of a blink,
    // when they are not drawn at all and the picture shows through (_PlayerFeedReadout.w is the
    // phase, -1 = steady).
    float ReadoutLit()
    {
        return _PlayerFeedReadout.w < 0.0 ? 1.0 : step(_PlayerFeedReadout.w, 0.4999);
    }

    // The recording overlay: label and status top left, the recording dot top right, the readout
    // bottom left (or on its way there from the middle), viewfinder brackets, and the bomb banner
    // in the middle. `lastCell` is the grid's top-right cell. The readout comes out in `readout`,
    // not `ink`: it has its own color (amber, red) and its time blinks (ReadoutLit); the banner goes
    // with the recording dot, in the same red.
    void LiveOverlay(int2 cell, int2 lastCell, out float ink, out float rec, out float readout)
    {
        int margin = (int)_OverlayMargin;
        int topRow = lastCell.y - margin - 6;

        ink = LineMask(cell, int2(margin, topRow), 0, (int)_PlayerFeedInfo.x, 1);
        ink = max(ink, LineMask(cell, int2(margin, topRow - 10), 2, (int)_PlayerFeedInfo.z, 1));

        // The readout shrinks from the middle's scale to 1 as it travels; the scale steps, so the
        // letters stay on the grid.
        float travel = saturate(_PlayerFeedReadout.x);
        int readoutScale = max((int)floor(lerp(_ReadoutCentreScale, 1.0, travel) + 0.5), 1);
        int readoutLength = (int)_PlayerFeedInfo.y;
        int2 middle = MiddleOrigin(lastCell, max(readoutLength, (int)_PlayerFeedReadout.y), readoutScale);
        int2 readoutOrigin = int2(round(lerp(float2(middle), float2(margin, margin), travel)));

        // The text stays; only the time's digits go out and come back (ReadoutLit).
        uint glyphIndex;
        readout = LineMaskGlyph(cell, readoutOrigin, 1, readoutLength, readoutScale, glyphIndex);
        float timeFrom = _PlayerFeedReadoutTime.x;
        if (glyphIndex >= timeFrom && glyphIndex < timeFrom + _PlayerFeedReadoutTime.y) readout *= ReadoutLit();

        // The recording dot, top right on the label's line, blinking like a camera's.
        float radius = max(_RecDotRadius, 1.0);
        float2 toDot = float2(cell) - float2(lastCell.x - margin - radius, topRow + 3);
        rec = step(dot(toDot, toDot), radius * radius) * step(frac(_Time.y * _PlayerFeedRecBlink), 0.5);

        // The bomb banner: the letters show between the sweep's two edges, left to right.
        int bannerLength = (int)_PlayerFeedBanner.z;
        if (bannerLength > 0)
        {
            int scale = max((int)_BannerScale, 1);
            int2 origin = MiddleOrigin(lastCell, bannerLength, scale);
            float width = (float)(max(bannerLength * 6 - 1, 1) * scale);
            float x = (float)(cell.x - origin.x);
            float shown = step(_PlayerFeedBanner.y * width, x) * step(x, _PlayerFeedBanner.x * width - 1.0);
            float blink = step(frac(_Time.y * _PlayerFeedBanner.w), 0.5);
            rec = max(rec, LineMask(cell, origin, 5, bannerLength, scale) * shown * blink);
        }

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

    // The module's readout: the overlay's color, warning, then critical (_PlayerFeedReadout.z 0, 1, 2).
    float4 TimerColor()
    {
        float stage = _PlayerFeedReadout.z;
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

        // The Nemesis closing in: a corrupted block reads the picture from somewhere else.
        float2 pictureUv;
        int blockKind = ThreatBlock(uv, gridSize, pictureUv);

        half3 color = 0.0;
        if (power > 0.0001)
        {
            // 1) Lens barrel, normalized so the corners sample the corners.
            float2 d = pictureUv - 0.5;
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

            // A block gone to rows of black and grey: no colour left, the picture's brightness
            // (lifted a little, so it reads over a dark picture too) in the grey of
            // _ThreatBlockTint on every other row of cells, black on the rows between.
            if (blockKind == BLOCK_GREY)
            {
                half oddRow = fmod(floor(uv.y * rows + 0.5), 2.0);
                half grey = lerp(dot(color, half3(0.2126, 0.7152, 0.0722)), 1.0, 0.15);
                color = grey * _ThreatBlockTint.rgb * (1.0 - oddRow);
            }
        }

        // 7) Static: snow, one value per PSX block, faster than the grain. Replaces the picture as
        //    it rises.
        if (staticAmount > 0.001)
        {
            float snowFrame = fmod(floor(_Time.y * _StaticFps), 4096.0);
            float2 snowCell = floor(uv * gridSize);
            half snow = HashCell(snowCell.x, snowCell.y, snowFrame);
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

            // The readout has no shadow: 1-cell letters with an outline came out as a smear.
            float shadow = max(inkShadow, recShadow) * _OverlayShadow *
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
