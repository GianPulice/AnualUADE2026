using UnityEngine;

/// <summary>
/// Whether the Nemesis suspects a hiding spot, from what the possibility map holds inside it and how
/// much the player has used it (plan §19.5, E1/E2; D38 and D41).
///
/// The map is where it thinks the player might be, and a spot is a place on it. The habit is how
/// often the player has hidden there. Two cases, and they are not symmetric on purpose:
///
///   - A spot the player HAS used opens by value × habit: the habit chance (the Fase 3 meter,
///     meter × 0.25 capped at 0.85) scaled by how much of the map is in the spot, up to the full
///     chance once the spot holds <c>habitFullShare</c> of it. A spot the map has cleared opens by
///     nothing, whatever the player did there before: it just looked and nobody was in.
///   - A spot NEVER used opens only when the map puts a lot of value in it (D41): at least
///     <c>neverUsedMinShare</c>, and then with <c>neverUsedChance</c>. The map alone can make it look
///     in a locker nobody has hidden in, but only when the search is closing in on it.
///
/// Pure, so the numbers are tested without a scene.
/// </summary>
public static class SpotOpenRules
{
    /// <param name="spotShare">The share (0..1) of the map's value inside the spot.</param>
    /// <param name="habitChance">The spot's open chance from the habit ledger; 0 if never used.</param>
    /// <returns>The chance, 0..1, that this spot becomes suspected on a roll.</returns>
    public static float Chance(float spotShare, float habitChance, float habitFullShare,
                               float neverUsedMinShare, float neverUsedChance)
    {
        if (float.IsNaN(spotShare) || spotShare <= 0f) return 0f;

        if (habitChance > 0f)
        {
            float scale = habitFullShare > 0f ? Mathf.Clamp01(spotShare / habitFullShare) : 1f;
            return Mathf.Clamp01(habitChance * scale);
        }

        return spotShare >= neverUsedMinShare ? Mathf.Clamp01(neverUsedChance) : 0f;
    }
}
