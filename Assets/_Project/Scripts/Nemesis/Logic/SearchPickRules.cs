using UnityEngine;

/// <summary>
/// The two judgements the search makes about a place on the possibility map (Plan-Busqueda-Nemesis
/// §3.4, Fase 2b): what it is worth going to, and whether the place it is already going to still is.
///
/// WHAT THIS REPLACED. SearchSweepRules.Judge asked where new evidence fell relative to a DISC the
/// search was sweeping — inside it (slide the centre) or outside it (re-centre). The disc is gone:
/// evidence re-seeds the possibility map by itself, and the only question left for the search is
/// whether the place it is heading to kept its value. One rule answers it whatever moved the value —
/// its own eyes clearing the place from a distance, or evidence putting the player somewhere else —
/// which is why there is no "inside or outside" left to judge.
///
/// PURE, AND ON PURPOSE. No scene, no state manager, no Assembly-CSharp type: it lives in
/// WIRED.Nemesis.Logic so EditMode tests can reach it. NemesisSearchPicker and NemesisSearchingState
/// feed it plain numbers and act on the answer.
/// </summary>
public static class SearchPickRules
{
    /// <summary>
    /// What a place is worth to a search: its share of the value ÷ (1 + the seconds it takes to walk
    /// there). The number the pick ROLLS by — never an argmax: always walking to the single best
    /// place is indistinguishable from knowing where the player is.
    ///
    /// The +1 is what keeps a place right beside it from dividing by nothing, and what makes the
    /// first seconds of a walk cost less than the later ones: a likely place a little further off
    /// still beats a sliver next door. A place it cannot walk to (infinite time) is worth nothing.
    /// </summary>
    public static float Worth(float share, float seconds)
    {
        if (float.IsNaN(share) || share <= 0f) return 0f;
        if (float.IsNaN(seconds) || float.IsPositiveInfinity(seconds)) return 0f;

        return share / (1f + Mathf.Max(0f, seconds));
    }

    /// <summary>
    /// Whether the place it is heading to is no longer worth finishing the walk to: what the map holds
    /// there now has fallen under <paramref name="keepFraction"/> of what it held when it was picked.
    /// It saw the place from a distance and nobody was there, or new evidence moved the value
    /// somewhere else.
    ///
    /// WHY A FRACTION OF WHAT IT HAD, AND NOT A COMPARISON WITH THE OTHER PLACES. The value flows on
    /// every tick of the map: it spreads, what the Nemesis looks at empties and everything else is
    /// renormalised upwards. "Somewhere else is better now" is true on most ticks, and acting on it is
    /// re-picking on every tick — the Nemesis turning round in the middle of a corridor because a
    /// number moved. Measured against its own starting point, a place has to LOSE most of its value to
    /// be dropped, and the place picked next starts from its own value: the hysteresis is built in.
    ///
    /// A place that was not picked off the map (nothing held when picked) has nothing to lose.
    /// </summary>
    public static bool LostItsValue(float shareNow, float shareWhenPicked, float keepFraction)
    {
        if (shareWhenPicked <= 0f) return false;

        return shareNow < shareWhenPicked * Mathf.Clamp01(keepFraction);
    }
}
