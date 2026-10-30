using System;
using UnityEngine;

/// <summary>
/// One step of the Nemesis's difficulty escalation (plan Fase 7, spec §7.2 adapted): from so many
/// completed puzzles on, its senses reach further and its patrol gets less predictable.
///
/// NEVER SPEED. The spec's table also raises chaseSpeed; this project does not (plan §7, analysis
/// §12.2): a monster that visibly gets faster reads as the game cheating, and the chase's balance
/// (the player outruns it; the loop is answered by the trail penalty, not by speed) is the design.
///
/// A plain serialisable class, so the selection rules can live in WIRED.Nemesis.Logic and be
/// tested in EditMode; SO_NemesisEscalation holds the list.
/// </summary>
[Serializable]
public class EscalationTier
{
    [Tooltip("Completed puzzles from which this tier applies. The tier with the highest value not " +
             "above the count wins, whatever the order of the list.")]
    [SerializeField, Min(0)] private int fromCompletedPuzzles;

    [Tooltip("Multiplies View Range: how far it sees, and with it everything measured as a " +
             "fraction of it (the slats of a locker, under a table). Spec §7.2: x1.1 at the " +
             "second tier, x1.15 at the third.")]
    [SerializeField, Min(1f)] private float sightMultiplier = 1f;

    [Tooltip("Multiplies Noise Range Scale and Listen Range together: every noise is heard that " +
             "much further, and the cap moves with it. Listen Range alone would only reach a " +
             "sprinting player. Spec §7.2: x1.1 from the third tier.")]
    [SerializeField, Min(1f)] private float hearingMultiplier = 1f;

    [Tooltip("Multiplies the search's Quiet Window and Hard Cap: how long it keeps looking. The " +
             "spec shortens the search as it escalates (12 -> 10 -> 8 s); here it is 1 until a " +
             "playtest says otherwise (plan D31).")]
    [SerializeField, Range(0.25f, 2f)] private float searchPersistenceMultiplier = 1f;

    [Tooltip("Lowest chance, 0..1, of reversing the round and of skipping a waypoint each time " +
             "the patrol restarts. 0 = as authored. Never lowers an authored chance. Spec §7.2: " +
             "0.25 at the second tier, 0.40 at the third.")]
    [SerializeField, Range(0f, 1f)] private float routeVariationFloor;

    public EscalationTier() { }

    public EscalationTier(int fromCompletedPuzzles, float sightMultiplier, float hearingMultiplier,
                          float searchPersistenceMultiplier, float routeVariationFloor)
    {
        this.fromCompletedPuzzles = fromCompletedPuzzles;
        this.sightMultiplier = sightMultiplier;
        this.hearingMultiplier = hearingMultiplier;
        this.searchPersistenceMultiplier = searchPersistenceMultiplier;
        this.routeVariationFloor = routeVariationFloor;
    }

    public int FromCompletedPuzzles => fromCompletedPuzzles;
    public float SightMultiplier => sightMultiplier;
    public float HearingMultiplier => hearingMultiplier;
    public float SearchPersistenceMultiplier => searchPersistenceMultiplier;
    public float RouteVariationFloor => routeVariationFloor;
}
