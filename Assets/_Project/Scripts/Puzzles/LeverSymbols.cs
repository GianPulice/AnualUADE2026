using UnityEngine;

/// <summary>
/// The letter of each lever of the boiler room puzzle (Central Puzzle 2 — SP1): lever
/// 1 is A, lever 2 is B… The lever panel writes them on its plates and the boiler's screen shows
/// it while it knocks, so the player can match what the boiler plays with what is on the panel.
/// One place for both, so they can never disagree.
/// </summary>
public static class LeverSymbols
{
    // Green on black, like the boiler's screen: the letter is what tells the levers apart.
    private static readonly Color Green = new Color(0.25f, 1.00f, 0.35f);

    /// <summary>"A" for lever 1, "B" for lever 2…</summary>
    public static string Letter(int lever) => ((char)('A' + Mathf.Max(0, lever - 1))).ToString();

    public static Color ColorOf(int lever) => Green;
}
