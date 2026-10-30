/// <summary>
/// A behaviour the Nemesis can learn from the player's habits (plan §5.4). Phase 3 only counts and
/// reports which ones WOULD be unlocked; nothing in the game acts on them yet (Phase 6).
///
/// The first three are scored by the per-spot usage meter (plan §17.6, Phase 2D) rather than by a
/// row of SO_CounterplayRules: since 27/09, checking a spot grows with how much it was used instead
/// of switching on after N escapes. They keep their values here because R3 — the first time a
/// counterplay runs, the player has to be able to see or hear it — is remembered per value.
///
/// APPEND ONLY, for the same reason as <see cref="EExploitKind"/>.
/// </summary>
public enum ECounterplay
{
    /// <summary>Opens used spots inside the area it is investigating or searching, with a chance
    /// that grows with each spot's meter.</summary>
    CheckHidingSpots,

    /// <summary>Checks a much-used spot before anything else (meter at the priority threshold).
    /// </summary>
    PrioritizeSuspiciousSpots,

    /// <summary>Tears a spot apart for good (meter at the burn threshold; needs the broken-locker
    /// art, D2).</summary>
    BurnHidingSpot,

    /// <summary>After an empty search, waits in view of the spot's way out for a bounded time (C1).
    /// </summary>
    ExitAmbush,

    /// <summary>Starts every chase with the trail penalty already on (C4).</summary>
    ChaseFlank,

    /// <summary>Waits by the likely exits, out of sight (C4, C5).</summary>
    ZoneDefense,
}
