using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Nemesis's difficulty escalation by completed puzzles (plan Fase 7, spec §7.2 adapted): one
/// tier per step, read by NemesisEscalation in the Data scene.
///
/// The defaults follow the spec's table with two deliberate differences. Speed never escalates
/// (plan §7). And the search is not shortened (D31): the spec's 12 → 10 → 8 s was written for a
/// fixed timeout, and here shortening the search would reward hiding and waiting — the
/// persistence is the Director's lever. Everything is a starting point for playtest.
/// </summary>
[CreateAssetMenu(fileName = "SO_NemesisEscalation", menuName = "Scriptable Objects/SO_NemesisEscalation")]
public class SO_NemesisEscalation : ScriptableObject
{
    [Tooltip("One row per step. The tier with the highest 'From Completed Puzzles' not above the " +
             "count applies; below the first one, the Nemesis keeps its authored tuning. The first " +
             "puzzle is the one that wakes the Nemesis (spec §7.1), so the spec's module 1 is " +
             "0-1 puzzles, module 2 is 2 and module 3+ is 3 or more.")]
    [SerializeField] private List<EscalationTier> tiers = new List<EscalationTier>
    {
        new EscalationTier(0, 1f, 1f, 1f, 0f),
        new EscalationTier(2, 1.1f, 1f, 1f, 0.25f),
        new EscalationTier(3, 1.15f, 1.1f, 1f, 0.4f),
    };

    [Tooltip("One console line each time a tier is installed on a Nemesis.")]
    [SerializeField] private bool logTierChanges = true;

    public IReadOnlyList<EscalationTier> Tiers => tiers;
    public bool LogTierChanges => logTierChanges;
}
