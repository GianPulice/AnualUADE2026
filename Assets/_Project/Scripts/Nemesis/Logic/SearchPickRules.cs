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

    /// <summary>
    /// How far a place lies along the way the player was last seen going: 1 dead ahead of that
    /// heading from the spot they were last seen at, 0 off to one side, -1 straight back. Flat: a
    /// place one floor up is not "ahead" for being above. 0, no opinion, when there is no heading to
    /// speak of or the place is that spot itself.
    /// </summary>
    public static float HeadingAlignment(Vector3 lastSeen, Vector3 heading, Vector3 place)
    {
        heading.y = 0f;

        Vector3 toPlace = place - lastSeen;
        toPlace.y = 0f;

        if (heading.sqrMagnitude < 0.0001f || toPlace.sqrMagnitude < 0.0001f) return 0f;

        return Vector3.Dot(heading.normalized, toPlace.normalized);
    }

    /// <summary>
    /// What the pick a chase hands over with multiplies a place's worth by, for lying where the
    /// player was heading: <paramref name="aheadBoost"/> dead ahead, 1 off to the side, one over the
    /// boost straight back, and smoothly in between.
    ///
    /// WHY THE WORTH ALONE IS NOT ENOUGH (playtest 05/10: "cuando doblás la esquina no predice que
    /// vas a seguir para adelante"). The map does spread faster along the heading, but worth divides
    /// by the walk, and at the corner where it lost them the places a step away — beside it, behind
    /// it — are the cheapest walks on the list: in three handovers out of seven it set off sideways,
    /// with the player a few metres down the corridor it was not looking along.
    ///
    /// STILL A ROLL, NEVER AN ARGMAX, for the reason <see cref="Worth"/> gives: this leans the roll
    /// towards "they kept going", it does not make the Nemesis know they did. A boost of 1 or less
    /// leaves the worth as it is.
    /// </summary>
    public static float HeadingWeight(float alignment, float aheadBoost)
    {
        if (float.IsNaN(alignment) || aheadBoost <= 1f) return 1f;

        return Mathf.Pow(aheadBoost, Mathf.Clamp(alignment, -1f, 1f));
    }
}
