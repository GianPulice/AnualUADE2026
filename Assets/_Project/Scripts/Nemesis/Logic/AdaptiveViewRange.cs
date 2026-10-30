using UnityEngine;

/// <summary>
/// How far the Nemesis's eyes reach RIGHT NOW, as a multiple of SO_NemesisData.ViewRange.
///
/// WHY IT STOPPED BEING ONE NUMBER (playtest 03/10: "en las líneas rectas también me pierde muy
/// fácilmente: ¿se puede adaptar en runtime el tamaño del line of sight para que tenga un rango mayor
/// si no lo llega a ver?"). ViewRange was the distance it NOTICES a player at and also the distance
/// it could keep one in sight at, and those are two different jobs. A player it was staring at
/// stepped past the same 7 m that hides one it has never seen: jogging at its own pace held the gap
/// right on that edge for ten seconds at a time, and every sweep that missed was a chase lost on a
/// straight corridor with nothing in between. Seeing is not catching — it can keep someone in sight
/// for longer without being a metre per second faster.
///
/// THREE REGIMES, and the base range is still the only one stealth is balanced on:
///
///   - BASE (x1). Patrolling, walking to a noise, anything that has not SEEN the player: exactly
///     ViewRange. Nothing here changes what an unaware Nemesis notices.
///   - HOLD. While it sees the player it keeps seeing them out to ViewRange x the hold scale
///     (hysteresis). The cone and the line of sight are still required: this is range, not x-ray.
///   - HUNT. It lost a player it WAS seeing and is still after them (chasing to where it lost them,
///     searching, riding the lift towards them): the range it can see them AGAIN at grows from x1 up
///     to the hunt scale over the grow time, counted in seconds of hunting without a sighting. When
///     the hunt is over it settles back to x1 instead of snapping.
///
/// BREAKING LINE OF SIGHT IS STILL WORTH SOMETHING, AND THAT IS ON PURPOSE. Losing sight drops the
/// range from HOLD straight back to the base and the hunt has to grow it again: a pillar between the
/// two of them at twelve metres buys the player the couple of seconds it takes to regrow. Without
/// that drop the hold would simply be a bigger ViewRange, and distance alone would never shake it.
///
/// A NOISE NEVER EARNS IT. The hunt has to be earned by a sighting (<see cref="HuntEarned"/>): a
/// search that grew out of a footstep (plan D26) or a lift ride towards a noise one floor down runs
/// on the base range, so sneaking past a Nemesis that only HEARD you is exactly as hard as it was.
/// The latch lasts until it goes back to patrol (or the sensor is told to forget, on a capture).
///
/// Crouching still halves whatever comes out of here; FieldOfView applies that on top. What it makes
/// out THROUGH a hiding spot, the rear sense, the hard proximity and the possibility map's "not here"
/// stay on the base range: they read ViewRange and never this.
///
/// PURE: flags, seconds and scales in, a scale out, in WIRED.Nemesis.Logic so EditMode tests can
/// reach it. A plain class and not a static one because the hunt is an integrator: it has to
/// remember how far it had grown. FieldOfView owns the one instance and steps it before each sweep.
/// With both scales at 1 it answers 1 for ever, which is the sensor as it was before this existed.
/// </summary>
public sealed class AdaptiveViewRange
{
    /// <summary>Why the range is what it is. For the debug HUD and the gizmos: "it saw me from
    /// fourteen metres" reads as cheating until the panel says HOLD next to it.</summary>
    public enum EReason
    {
        /// <summary>Exactly ViewRange.</summary>
        Base,

        /// <summary>It is seeing the player and holds them further out.</summary>
        Hold,

        /// <summary>It lost a player it was seeing and is still after them.</summary>
        Hunt,

        /// <summary>The hunt is over (or interrupted) and the range is easing back to the base.
        /// </summary>
        Settling,
    }

    /// <summary>Below this much over 1 the scale reads as the base: a hunt in its first frame is not
    /// worth a different word on the HUD.</summary>
    private const float ScaleEpsilon = 0.001f;

    /// <summary>The multiplier on ViewRange for the next sweep. Never under 1.</summary>
    public float Scale { get; private set; } = 1f;

    public EReason Reason { get; private set; } = EReason.Base;

    /// <summary>How far the hunt has grown, 0 (base) to 1 (the full hunt scale).</summary>
    public float HuntLevel { get; private set; }

    /// <summary>Whether it has SEEN the player in this alert episode. Without it a hunting state
    /// earns nothing: see the class comment.</summary>
    public bool HuntEarned { get; private set; }

    /// <summary>Back to the base, as though it had never seen anyone. For a capture (the respawn
    /// makes everything it knew false) and for the escape sequence, which is paced by hand.</summary>
    public void Reset()
    {
        Scale = 1f;
        Reason = EReason.Base;
        HuntLevel = 0f;
        HuntEarned = false;
    }

    /// <summary>
    /// One step, taken before a vision sweep.
    /// </summary>
    /// <param name="seeing">The last sweep had the player (a real sighting, out in the open).</param>
    /// <param name="hunting">It is in a state that goes after the player: chasing, searching,
    /// crossing floors towards them. Not investigating a noise.</param>
    /// <param name="atRest">The alert episode is over: back on patrol, or not started at all. Drops
    /// what a sighting had earned.</param>
    /// <param name="huntGrowTime">Seconds of hunting without a sighting to reach the hunt scale. 0:
    /// at once.</param>
    /// <param name="huntSettleTime">Seconds to ease back from the full hunt scale to the base once
    /// the hunt is over. 0: at once.</param>
    /// <returns>The scale to multiply ViewRange by.</returns>
    public float Step(float deltaTime, bool seeing, bool hunting, bool atRest, float holdScale,
                      float huntScale, float huntGrowTime, float huntSettleTime)
    {
        HuntEarned = StaysEarned(HuntEarned, seeing, atRest);

        bool onTheHunt = hunting && HuntEarned;

        HuntLevel = StepHuntLevel(HuntLevel, deltaTime, seeing, onTheHunt, huntGrowTime, huntSettleTime);
        Scale = ScaleFor(seeing, HuntLevel, holdScale, huntScale);
        Reason = ReasonFor(seeing, onTheHunt, Scale);

        return Scale;
    }

    /// <summary>
    /// Whether a hunt still has a sighting behind it: seeing the player earns it, and it is kept
    /// until the Nemesis is at rest again. Investigating in the middle of a hunt (a decoy that won
    /// its attention for a moment) does not lose it; going back to patrol does.
    /// </summary>
    public static bool StaysEarned(bool earned, bool seeing, bool atRest) => seeing || (earned && !atRest);

    /// <summary>
    /// The hunt as an integrator. Seeing the player empties it — the hold takes over, and losing them
    /// again starts from the base. Hunting fills it over <paramref name="growTime"/>. Anything else
    /// drains it over <paramref name="settleTime"/>, so a hunt that is interrupted and resumed picks
    /// up from where it had got to rather than from nothing.
    /// </summary>
    public static float StepHuntLevel(float level, float deltaTime, bool seeing, bool hunting,
                                      float growTime, float settleTime)
    {
        if (seeing) return 0f;

        float step = Mathf.Max(0f, deltaTime);
        level = Mathf.Clamp01(level);

        if (hunting) return growTime > 0f ? Mathf.Min(1f, level + step / growTime) : 1f;

        return settleTime > 0f ? Mathf.Max(0f, level - step / settleTime) : 0f;
    }

    /// <summary>The multiplier itself: the hold scale while it sees the player, otherwise the hunt
    /// scale by how far the hunt has grown. A scale typed under 1 counts as 1 — this can lengthen the
    /// range, never shorten what the Nemesis notices at.</summary>
    public static float ScaleFor(bool seeing, float huntLevel, float holdScale, float huntScale)
    {
        if (seeing) return Mathf.Max(1f, holdScale);

        return Mathf.Lerp(1f, Mathf.Max(1f, huntScale), Mathf.Clamp01(huntLevel));
    }

    /// <summary>The range in metres for a scale: never under the base one.</summary>
    public static float Range(float baseRange, float scale) =>
        Mathf.Max(0f, baseRange) * Mathf.Max(1f, scale);

    /// <summary>
    /// Seconds of hunting, from the moment sight is lost, until a player standing
    /// <paramref name="distance"/> metres away is inside the range again. 0 when the base range
    /// already covers it; infinity when not even the full hunt scale does. For the SO inspector's
    /// tester, which has to say the same thing the sensor does.
    /// </summary>
    public static float SecondsToReach(float distance, float baseRange, float huntScale, float growTime)
    {
        if (baseRange <= 0f) return float.PositiveInfinity;
        if (distance <= baseRange) return 0f;

        float top = Mathf.Max(1f, huntScale);
        float needed = distance / baseRange;
        if (needed > top) return float.PositiveInfinity;
        if (growTime <= 0f || top <= 1f) return 0f;

        return growTime * (needed - 1f) / (top - 1f);
    }

    private static EReason ReasonFor(bool seeing, bool onTheHunt, float scale)
    {
        if (scale <= 1f + ScaleEpsilon) return EReason.Base;
        if (seeing) return EReason.Hold;

        return onTheHunt ? EReason.Hunt : EReason.Settling;
    }
}
