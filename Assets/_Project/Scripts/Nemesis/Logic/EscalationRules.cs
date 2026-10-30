using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the escalation reads its tiers (plan Fase 7): which one applies to a completed-puzzle count,
/// and what it does to a number. Pure, so the choices that are easy to get backwards are tested in
/// EditMode; NemesisEscalation applies the result to a copy of SO_NemesisData.
///
/// It counts PUZZLES, not modules (docs/CLAUDE.md, Spec deltas): ModuleManager is the device
/// timers and never advances the story. And it reads a count rather than tallying completions:
/// a checkpoint restore refills the completed set without raising the event, so a tally would come
/// back from a respawn with the monster of the opening room.
/// </summary>
public static class EscalationRules
{
    /// <summary>
    /// Index in <paramref name="tiers"/> of the tier that applies at
    /// <paramref name="completedPuzzles"/>: the one with the highest threshold not above the count,
    /// the later one on a tie. -1 when none applies (no tiers, or every threshold above the count):
    /// the Nemesis keeps its authored tuning.
    /// </summary>
    public static int TierIndex(IReadOnlyList<EscalationTier> tiers, int completedPuzzles)
    {
        if (tiers == null) return -1;

        int best = -1;
        int bestFrom = int.MinValue;

        for (int i = 0; i < tiers.Count; i++)
        {
            EscalationTier tier = tiers[i];
            if (tier == null || tier.FromCompletedPuzzles > completedPuzzles) continue;
            if (tier.FromCompletedPuzzles < bestFrom) continue;

            best = i;
            bestFrom = tier.FromCompletedPuzzles;
        }

        return best;
    }

    /// <summary>Whether <paramref name="tier"/> changes nothing. The authored asset is installed
    /// as it is, with no copy.</summary>
    public static bool IsIdentity(EscalationTier tier) =>
        tier == null ||
        (Mathf.Approximately(tier.SightMultiplier, 1f) &&
         Mathf.Approximately(tier.HearingMultiplier, 1f) &&
         Mathf.Approximately(tier.SearchPersistenceMultiplier, 1f) &&
         tier.RouteVariationFloor <= 0f);

    /// <summary><paramref name="authored"/> × <paramref name="multiplier"/>, a negative multiplier
    /// read as 0.</summary>
    public static float Scale(float authored, float multiplier) => authored * Mathf.Max(0f, multiplier);

    /// <summary>A route chance raised to <paramref name="floor"/>, never lowered: an escalation
    /// makes the patrol less predictable, not more. A floor of 0 leaves it as authored.</summary>
    public static float RouteChance(float authored, float floor) =>
        floor > 0f ? Mathf.Clamp01(Mathf.Max(authored, floor)) : authored;
}
