using System.Text;

/// <summary>
/// Text formatting shared by the inventory views, so every screen speaks the same
/// "grey 1995 application" language instead of each view inventing its own.
///
/// Two primitives:
///   - <see cref="MachineName"/>  "Mechanical Core" -> "MECHANICAL_CORE"
///   - <see cref="DotLeader"/>    "03 MECHANICAL_CORE ......[CMP]"
///
/// IMPORTANT: DotLeader pads by CHARACTER COUNT, so the columns only line up under a
/// monospaced font. The inventory uses ShareTechMono-Regular SDF everywhere; swapping any
/// of those labels to a proportional font silently breaks the alignment.
/// </summary>
public static class InventoryTextFormat
{
    public const char LeaderChar = '.';

    /// <summary>Uppercases and turns separators into underscores, for a filename-ish look.</summary>
    public static string MachineName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
            sb.Append(c == ' ' || c == '-' ? '_' : char.ToUpperInvariant(c));

        return sb.ToString();
    }

    /// <summary>
    /// Builds a row of exactly <paramref name="totalWidth"/> characters:
    /// left, a space, a run of dots, then right flush to the edge.
    ///
    /// The LEFT side is truncated before the leader is allowed to shrink, so the right side
    /// (the category tag, the count) is always readable no matter how long an item name is.
    /// </summary>
    public static string DotLeader(string left, string right, int totalWidth, int minDots = 3)
    {
        left ??= string.Empty;
        right ??= string.Empty;

        // 1 = the space that separates the label from the leader.
        int maxLeft = totalWidth - right.Length - minDots - 1;
        if (maxLeft < 1) return left + " " + right;   // too narrow to format; degrade gracefully

        if (left.Length > maxLeft) left = left.Substring(0, maxLeft);

        int dots = totalWidth - left.Length - right.Length - 1;
        if (dots < minDots) dots = minDots;

        var sb = new StringBuilder(totalWidth);
        sb.Append(left).Append(' ').Append(LeaderChar, dots).Append(right);
        return sb.ToString();
    }
}
