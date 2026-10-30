/// <summary>
/// When a search is over (plan §18.5 B, Fase 2B part 3): it cools down, it does not expire.
///
/// It used to be a fixed clock — fifteen seconds in the state, whatever happened in them. A footstep
/// at second fourteen was ignored at second fifteen; a search with no evidence at all still stood
/// around for the full fifteen; and nothing about how good the last evidence was, how much ground it
/// had covered or how the Director wanted the encounter paced ever entered the answer.
///
/// Now the one number is the SILENCE: seconds since the last evidence about the player (leads do not
/// count, and neither does anything heard from inside the Hub). The search goes on while that is
/// shorter than a window scaled by how good that evidence was, within a floor and a cap:
///
///   - under the minimum time in the state, always warm: it always looks a little;
///   - at the cap, cold, however much it still hears — ONLY IF THERE IS ONE: a cap of 0 or less is no
///     cap, and that is how it ships since 03/10. The search lasts for as long as evidence of the
///     player keeps coming; a duration that cuts it off while it still hears you is a budget, not a
///     reason (the designer's rule: "hasta que sienta que no hay más evidencias nuevas");
///   - having searched everything, cold: "I have looked everywhere here". Since the possibility map
///     decides where the search goes (Plan-Busqueda-Nemesis §3.4, Fase 2b) that is
///     <see cref="NothingWorthTheWalk"/>: what is left of the value is so spread out, or so out of
///     reach, that no place is worth walking to;
///   - otherwise, warm while the silence is under window × quality.
///
/// The Director scales the window and the cap through a loan on SO_NemesisData (persistence), so
/// this never needs to know about pacing.
///
/// PURE: plain numbers in, a verdict out, in WIRED.Nemesis.Logic so EditMode tests can reach it.
/// </summary>
public static class SearchCooling
{
    /// <summary>
    /// How much of the value has to be in the Hub's sink for the search to take it that the player
    /// went in there: most of it. A rule and not a tunable, like the Hub's clearance: lowering what
    /// "most" means only makes the search give the Hub up sooner, and raising it brings back a search
    /// that hangs around the one place the game promises is safe (C5).
    /// </summary>
    public const float SinkMajority = 0.5f;

    /// <summary>
    /// How much the silence window is stretched or shrunk by the last evidence: a sighting says more
    /// than a noise, and a noise that came through a wall, a floor or a hiding spot says less.
    /// </summary>
    public static float Quality(bool fromSight, bool muffled, float sightFactor, float muffledFactor)
    {
        if (fromSight) return sightFactor;
        return muffled ? muffledFactor : 1f;
    }

    /// <summary>
    /// "I have looked everywhere here", as the possibility map says it (Plan-Busqueda-Nemesis §3.4).
    ///
    /// It used to be "the disc around the belief is fully swept at its widest", which was a statement
    /// about the search's own bookkeeping. This one is about the player: where they can still be is
    /// either nowhere in particular (the value has spread so thin that the best place holds a sliver
    /// of it), nowhere it can get to on foot (another floor through the lift, an island of NavMesh),
    /// inside hiding spots, or in the Hub. In every one of those there is no place worth the walk.
    ///
    /// <paramref name="bestWorth"/> is the best place's value ÷ (1 + seconds to walk there) — the same
    /// number the pick rolls by (SearchPickRules.Worth), 0 when there is no place at all.
    /// <paramref name="worthThreshold"/> 0 switches the rule off: then only the Hub ends a search this
    /// way.
    ///
    /// THE HUB IS ITS OWN CASE (C5, plan case 67). With most of the value in the sink, the player
    /// went into the Hub — and the sliver still on the floor is right outside its door, the last
    /// stretch of the way in. Walking to it is camping that door, however much it is "worth".
    /// </summary>
    public static bool NothingWorthTheWalk(float bestWorth, float worthThreshold, float sinkShare)
    {
        if (sinkShare >= SinkMajority) return true;

        return bestWorth < worthThreshold;
    }

    /// <summary>Whether the search should go on. See the class comment for the order of the rules.
    /// </summary>
    public static bool IsWarm(float timeInState, float silence, float minTime, float window,
                              float quality, float cap, bool searchedEverything)
    {
        if (timeInState < minTime) return true;
        if (cap > 0f && timeInState >= cap) return false;
        if (searchedEverything) return false;

        return silence < window * quality;
    }
}
