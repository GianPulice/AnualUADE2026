using UnityEngine;

/// <summary>
/// The C# half of the bitmap font the camera feeds burn into the picture
/// (<c>CameraFeedFont.hlsl</c>, shared by <c>SecurityCamera_HLSL.shader</c> and
/// <c>PlayerCameraFeed_HLSL.shader</c>). Text reaches the shaders as glyph indices in a float
/// array, one float per character; this is where a string becomes those indices.
/// </summary>
public static class CameraFeedFont
{
    /// <summary>Glyph indices from <paramref name="text"/> into <paramref name="buffer"/>, starting
    /// at <paramref name="start"/>; the rest of the line up to <paramref name="capacity"/> is blanked.</summary>
    /// <returns>How many characters were written (the line's length on screen).</returns>
    public static int WriteLine(float[] buffer, int start, int capacity, string text)
    {
        int length = text != null ? text.Length : 0;
        if (length > capacity) length = capacity;

        for (int i = 0; i < capacity; i++)
            buffer[start + i] = i < length ? GlyphOf(text[i]) : 0f;
        return length;
    }

    /// <summary>
    /// The first <paramref name="count"/> characters of <paramref name="text"/>, followed by a cursor
    /// ("_") when <paramref name="cursor"/> is set: a line being typed or erased.
    /// </summary>
    /// <returns>How many glyphs were written, cursor included.</returns>
    public static int WriteTyped(float[] buffer, int start, int capacity, string text, int count, bool cursor)
    {
        int length = Mathf.Clamp(count, 0, text != null ? text.Length : 0);
        if (length > capacity) length = capacity;
        int total = cursor && length < capacity ? length + 1 : length;

        for (int i = 0; i < capacity; i++)
        {
            buffer[start + i] = i < length ? GlyphOf(text[i])
                              : i < total ? GlyphOf('_')
                              : 0f;
        }
        return total;
    }

    /// <summary>Index of <paramref name="c"/> in the shader's font (kGlyphs in
    /// CameraFeedFont.hlsl) — the two orders have to match. 0 is the space, and so is any character
    /// the font does not have. Accented letters fall back to the plain one.</summary>
    public static float GlyphOf(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c >= '0' && c <= '9') return 1 + (c - '0');
        if (c >= 'A' && c <= 'Z') return 11 + (c - 'A');

        switch (c)
        {
            case ':': return 37;
            case '-': return 38;
            case '/': return 39;
            case '.': return 40;
            case '#': return 41;
            case '>': return 42;
            case '%': return 43;
            case '_': return 44;
            case '!': return 45;
            case '+': return 46;
            case '[': return 47;
            case ']': return 48;
            case '=': return 49;
            case '|': return 50;
            case 'Á': case 'À': case 'Â': case 'Ä': return GlyphOf('A');
            case 'É': case 'È': case 'Ê': case 'Ë': return GlyphOf('E');
            case 'Í': case 'Ì': case 'Î': case 'Ï': return GlyphOf('I');
            case 'Ó': case 'Ò': case 'Ô': case 'Ö': return GlyphOf('O');
            case 'Ú': case 'Ù': case 'Û': case 'Ü': return GlyphOf('U');
            case 'Ñ': return GlyphOf('N');
            case 'Ç': return GlyphOf('C');
            default: return 0;
        }
    }
}
