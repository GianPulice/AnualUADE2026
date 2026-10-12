using System.Text;

/// <summary>
/// A puzzle whose solution a note gives away, and that can fill that note's placeholders with this
/// run's values. Set on a note as its <c>SO_InventoryItem</c> Clue Source.
/// </summary>
public interface IClueSource
{
    /// <summary>
    /// The text for the placeholder <c>{key}</c> or <c>{key:format}</c> (format is null without the
    /// colon), or null to leave the placeholder as it was written.
    /// </summary>
    string ResolveClue(string key, string format);
}

/// <summary>
/// Fills the <c>{placeholders}</c> of a clue note with what its <see cref="IClueSource"/> answers,
/// so the note always matches the solution of the current run. Also the wording helpers number
/// clues need ("1 worker" / "3 workers", "2nd floor").
/// </summary>
public static class ClueText
{
    public static string Fill(string text, IClueSource source)
    {
        if (source == null || string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;

        var result = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i);
            int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(text, i, text.Length - i);
                break;
            }

            result.Append(text, i, open - i);

            string body = text.Substring(open + 1, close - open - 1);
            int colon = body.IndexOf(':');
            string key = (colon < 0 ? body : body.Substring(0, colon)).Trim();
            string format = colon < 0 ? null : body.Substring(colon + 1).Trim();

            // Unknown placeholders stay visible, so a typo in a note shows up in the game.
            result.Append(source.ResolveClue(key, format) ?? text.Substring(open, close - open + 1));
            i = close + 1;
        }
        return result.ToString();
    }

    /// <summary>"word|words": the first for exactly one, the second otherwise.</summary>
    public static string Plural(int count, string forms)
    {
        int bar = forms.IndexOf('|');
        if (bar < 0) return forms;
        return count == 1 ? forms.Substring(0, bar) : forms.Substring(bar + 1);
    }

    /// <summary>1st, 2nd, 3rd, 4th… 11th, 12th, 13th, 21st.</summary>
    public static string Ordinal(int number)
    {
        int lastTwo = number % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return number + "th";
        switch (number % 10)
        {
            case 1: return number + "st";
            case 2: return number + "nd";
            case 3: return number + "rd";
            default: return number + "th";
        }
    }
}
