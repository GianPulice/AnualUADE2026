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
///   - at the cap, cold, however much it still hears (at the cap, if it hears the player, the ladder
///     sends it to investigate — the same as the old expiry);
///   - having searched everything it can reach at its widest, cold: "I have looked everywhere here";
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
    /// How much the silence window is stretched or shrunk by the last evidence: a sighting says more
    /// than a noise, and a noise that came through a wall, a floor or a hiding spot says less.
    /// </summary>
    public static float Quality(bool fromSight, bool muffled, float sightFactor, float muffledFactor)
    {
        if (fromSight) return sightFactor;
        return muffled ? muffledFactor : 1f;
    }

    /// <summary>Whether the search should go on. See the class comment for the order of the rules.
    /// </summary>
    public static bool IsWarm(float timeInState, float silence, float minTime, float window,
                              float quality, float cap, bool searchedEverything)
    {
        if (timeInState < minTime) return true;
        if (timeInState >= cap) return false;
        if (searchedEverything) return false;

        return silence < window * quality;
    }
}
