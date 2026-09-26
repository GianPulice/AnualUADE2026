// CameraFeedFont.hlsl
// The font the camera feeds burn into the picture, shared by SecurityCamera_HLSL.shader (Cam_Slam,
// Cam_4_Gate) and PlayerCameraFeed_HLSL.shader (the camera that follows the player).
//
// 5x7 bitmap. x = rows 0..3 (5 bits each, bit 4 = left column), y = rows 4..6.
// Plus a 3x5 one in the same order, for small text (the player feed's readout).
// The glyph order has to match CameraFeedFont.GlyphOf (C#). New glyphs ALWAYS go at the end, in both
// tables: the feeds publish their text as glyph indices.

#ifndef WIRED_CAMERA_FEED_FONT_INCLUDED
#define WIRED_CAMERA_FEED_FONT_INCLUDED

// Order: space, 0-9, A-Z, : - / . # >, then % _ ! + [ ] = |
#define GLYPH_COUNT 51
static const uint2 kGlyphs[GLYPH_COUNT] =
{
    uint2(0x00000, 0x0000), //  0 ' '
    uint2(0x74675, 0x662E), //  1 '0'
    uint2(0x23084, 0x108E), //  2 '1'
    uint2(0x74422, 0x111F), //  3 '2'
    uint2(0xF8882, 0x062E), //  4 '3'
    uint2(0x11952, 0x7C42), //  5 '4'
    uint2(0xFC3C1, 0x062E), //  6 '5'
    uint2(0x3221E, 0x462E), //  7 '6'
    uint2(0xF8444, 0x2108), //  8 '7'
    uint2(0x7462E, 0x462E), //  9 '8'
    uint2(0x7462F, 0x044C), // 10 '9'
    uint2(0x7463F, 0x4631), // 11 'A'
    uint2(0xF463E, 0x463E), // 12 'B'
    uint2(0x74610, 0x422E), // 13 'C'
    uint2(0xE4A31, 0x465C), // 14 'D'
    uint2(0xFC21E, 0x421F), // 15 'E'
    uint2(0xFC21E, 0x4210), // 16 'F'
    uint2(0x74617, 0x462F), // 17 'G'
    uint2(0x8C63F, 0x4631), // 18 'H'
    uint2(0x71084, 0x108E), // 19 'I'
    uint2(0x38842, 0x0A4C), // 20 'J'
    uint2(0x8CA98, 0x5251), // 21 'K'
    uint2(0x84210, 0x421F), // 22 'L'
    uint2(0x8EEB5, 0x4631), // 23 'M'
    uint2(0x8C735, 0x4E31), // 24 'N'
    uint2(0x74631, 0x462E), // 25 'O'
    uint2(0xF463E, 0x4210), // 26 'P'
    uint2(0x74631, 0x564D), // 27 'Q'
    uint2(0xF463E, 0x5251), // 28 'R'
    uint2(0x7C20E, 0x043E), // 29 'S'
    uint2(0xF9084, 0x1084), // 30 'T'
    uint2(0x8C631, 0x462E), // 31 'U'
    uint2(0x8C631, 0x4544), // 32 'V'
    uint2(0x8C635, 0x56AA), // 33 'W'
    uint2(0x8C544, 0x2A31), // 34 'X'
    uint2(0x8C544, 0x1084), // 35 'Y'
    uint2(0xF8444, 0x221F), // 36 'Z'
    uint2(0x03180, 0x3180), // 37 ':'
    uint2(0x0001F, 0x0000), // 38 '-'
    uint2(0x00444, 0x2200), // 39 '/'
    uint2(0x00000, 0x018C), // 40 '.'
    uint2(0x52BEA, 0x7D4A), // 41 '#'
    uint2(0x41041, 0x0888), // 42 '>'
    uint2(0xCE844, 0x2173), // 43 '%'
    uint2(0x00000, 0x001F), // 44 '_'
    uint2(0x21084, 0x1004), // 45 '!'
    uint2(0x0109F, 0x1080), // 46 '+'
    uint2(0x72108, 0x210E), // 47 '['
    uint2(0x70842, 0x084E), // 48 ']'
    uint2(0x003E0, 0x7C00), // 49 '='
    uint2(0x21084, 0x1084), // 50 '|'
};

// pixel.x = column 0..4 (left to right), pixel.y = row 0..6 (top to bottom).
float GlyphBit(uint glyph, int2 pixel)
{
    if (glyph >= GLYPH_COUNT) return 0.0;
    uint2 g = kGlyphs[glyph];
    uint row = pixel.y < 4 ? (g.x >> (uint)((3 - pixel.y) * 5))
                           : (g.y >> (uint)((6 - pixel.y) * 5));
    return (float)((row >> (uint)(4 - pixel.x)) & 1u);
}

// 3x5 bitmap, same order: 5 rows of 3 bits, row 0 in bits 14..12, bit 2 = left column.
static const uint kSmallGlyphs[GLYPH_COUNT] =
{
    0x0000, //  0 ' '
    0x7B6F, //  1 '0'
    0x2C97, //  2 '1'
    0x73E7, //  3 '2'
    0x72CF, //  4 '3'
    0x5BC9, //  5 '4'
    0x79CF, //  6 '5'
    0x79EF, //  7 '6'
    0x7292, //  8 '7'
    0x7BEF, //  9 '8'
    0x7BCF, // 10 '9'
    0x2BED, // 11 'A'
    0x6BAE, // 12 'B'
    0x3923, // 13 'C'
    0x6B6E, // 14 'D'
    0x79A7, // 15 'E'
    0x79A4, // 16 'F'
    0x396B, // 17 'G'
    0x5BED, // 18 'H'
    0x7497, // 19 'I'
    0x126A, // 20 'J'
    0x5BAD, // 21 'K'
    0x4927, // 22 'L'
    0x5FED, // 23 'M'
    0x6B6D, // 24 'N'
    0x2B6A, // 25 'O'
    0x6BA4, // 26 'P'
    0x2B73, // 27 'Q'
    0x6BAD, // 28 'R'
    0x388E, // 29 'S'
    0x7492, // 30 'T'
    0x5B6F, // 31 'U'
    0x5B6A, // 32 'V'
    0x5BFD, // 33 'W'
    0x5AAD, // 34 'X'
    0x5A92, // 35 'Y'
    0x72A7, // 36 'Z'
    0x0410, // 37 ':'
    0x01C0, // 38 '-'
    0x12A4, // 39 '/'
    0x0002, // 40 '.'
    0x5F7D, // 41 '#'
    0x4454, // 42 '>'
    0x52A5, // 43 '%'
    0x0007, // 44 '_'
    0x2482, // 45 '!'
    0x05D0, // 46 '+'
    0x6926, // 47 '['
    0x324B, // 48 ']'
    0x0E38, // 49 '='
    0x2492, // 50 '|'
};

// pixel.x = column 0..2 (left to right), pixel.y = row 0..4 (top to bottom).
float SmallGlyphBit(uint glyph, int2 pixel)
{
    if (glyph >= GLYPH_COUNT) return 0.0;
    uint row = kSmallGlyphs[glyph] >> (uint)((4 - pixel.y) * 3);
    return (float)((row >> (uint)(2 - pixel.x)) & 1u);
}

float Hash21(float2 p)
{
    p = frac(p * float2(233.34, 851.73));
    p += dot(p, p + 23.45);
    return frac(p.x * p.y);
}

#endif // WIRED_CAMERA_FEED_FONT_INCLUDED
