using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player's habits unlock, and how the hiding-spot memory is scored (plan §5.2, §12;
/// Phase 3, and the per-spot meter of Phase 2D). Read by PlayerHabitTracker, in the Data scene.
///
/// An asset and not fields on the tracker for the same reason SO_DirectorPacing is one: a
/// difficulty setting (D8) is expected to move these thresholds — never the Nemesis's speed — and
/// that should be a different asset, not an edited scene.
///
/// Phase 3 counts and reports; nothing in the game acts on an unlocked counterplay yet. Every
/// number here is a starting point to calibrate against the F9 panel and the log, not a value to
/// keep because it was written down first.
/// </summary>
[CreateAssetMenu(fileName = "SO_CounterplayRules", menuName = "Scriptable Objects/SO_CounterplayRules")]
public class SO_CounterplayRules : ScriptableObject, IHabitRules
{
    [Header("Counterplays unlocked by counting (plan §5.4)")]
    [Tooltip("One row per 'this many of that unlocks this', read top to bottom. Two rows may unlock " +
             "the same counterplay; the likelier wins.\n\n" +
             "CheckHidingSpots, PrioritizeSuspiciousSpots and BurnHidingSpot have no row on purpose: " +
             "since 27/09 they are scored per spot by the meter below (plan §17.6, D23).")]
    [SerializeField] private List<CounterplayRule> rules = new List<CounterplayRule>
    {
        new CounterplayRule(EExploitKind.EscapedWhileHidden, 3, ECounterplay.ExitAmbush, 0.35f, 0.1f),
        new CounterplayRule(EExploitKind.ChaseStalled, 1, ECounterplay.ChaseFlank, 0.35f, 0.1f),
        new CounterplayRule(EExploitKind.ChaseStalled, 2, ECounterplay.ZoneDefense, 0.35f, 0.1f),
        new CounterplayRule(EExploitKind.SafeZoneEscape, 2, ECounterplay.ZoneDefense, 0.35f, 0.1f),
    };

    [Tooltip("No counterplay's chance goes above this, however much the player insists. It has to " +
             "stay a bet: a counterplay that always fires is learned exactly like the cheese.")]
    [SerializeField, Range(0f, 1f)] private float chanceCap = 0.85f;

    [Header("Habit decay (R7, D4)")]
    [Tooltip("Minutes a count holds after it last went up before it starts to drain. Also what keeps " +
             "a count from slipping back under its threshold the moment it reaches it.")]
    [SerializeField, Min(0f)] private float habitDecayDelayMinutes = 5f;

    [Tooltip("How much a count drains per minute once the delay is over. Slow on purpose: what was " +
             "learned in one area should fade before the next, not between two searches.")]
    [SerializeField, Min(0f)] private float habitDecayPerMinute = 0.1f;

    [Header("Hiding-spot memory (D23, plan §17.6)")]
    [Tooltip("What hiding in a spot adds to its meter, on the way in. Any spot type.")]
    [SerializeField, Min(0f)] private float spotUsePoints = 1f;

    [Tooltip("What the meter gains on the way OUT when the Nemesis hunted nearby during the stay and " +
             "did not find the player. Escapes, not attempts (R1): hiding just in case earns only " +
             "the use.")]
    [SerializeField, Min(0f)] private float spotHuntedBonus = 1f;

    [Tooltip("Minutes a spot's meter holds after it last went up before it starts to drain. 0 = it " +
             "drains from the first minute, as plan §12 has it.")]
    [SerializeField, Min(0f)] private float spotDecayDelayMinutes = 0f;

    [Tooltip("How much a spot's meter drains per minute once the delay is over.")]
    [SerializeField, Min(0f)] private float spotDecayPerMinute = 0.1f;

    [Tooltip("At this meter the spot gets checked first (C2).")]
    [SerializeField, Min(0f)] private float spotPriorityThreshold = 2f;

    [Tooltip("At this meter the spot can be torn apart (plan §3.6). Needs the broken-locker art.")]
    [SerializeField, Min(0f)] private float spotBurnThreshold = 4f;

    [Tooltip("Chance of opening a used spot inside the area being investigated or searched, per " +
             "point of its meter.")]
    [SerializeField, Range(0f, 1f)] private float spotOpenChancePerPoint = 0.25f;

    [Tooltip("Ceiling on the chance of opening a used spot.")]
    [SerializeField, Range(0f, 1f)] private float spotOpenChanceCap = 0.85f;

    [Header("What counts as near (R1)")]
    [Tooltip("NavMesh metres. 'Hunting nearby' is the Nemesis investigating, chasing or searching " +
             "within this of the hidden player's spot, and an escape while hidden is a search that " +
             "ended within this of it. Of the order of the room sweep (Room Sweep Radius, 8): the " +
             "12 m of the proximity vignette covers almost the whole of a dense level.")]
    [SerializeField, Min(1f)] private float nearbyRadius = 8f;

    [Header("Debug")]
    [Tooltip("One console line per count and per hide. It is how Phase 3 calibrates: play, then read " +
             "the log.")]
    [SerializeField] private bool logRegistrations = true;

    public IReadOnlyList<CounterplayRule> Rules => rules;
    public float ChanceCap => chanceCap;
    public float HabitDecayDelayMinutes => habitDecayDelayMinutes;
    public float HabitDecayPerMinute => habitDecayPerMinute;
    public float SpotUsePoints => spotUsePoints;
    public float SpotHuntedBonus => spotHuntedBonus;
    public float SpotDecayDelayMinutes => spotDecayDelayMinutes;
    public float SpotDecayPerMinute => spotDecayPerMinute;
    public float SpotPriorityThreshold => spotPriorityThreshold;
    public float SpotBurnThreshold => spotBurnThreshold;
    public float SpotOpenChancePerPoint => spotOpenChancePerPoint;
    public float SpotOpenChanceCap => spotOpenChanceCap;
    public float NearbyRadius => nearbyRadius;
    public bool LogRegistrations => logRegistrations;
}
