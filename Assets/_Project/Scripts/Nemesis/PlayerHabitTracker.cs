using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Counts what the player keeps doing to get away from the Nemesis, and remembers which hiding
/// spots they keep using (plan §5, Phase 3; the per-spot meter of plan §17.6, D23, Phase 2D).
///
/// DIRECTOR, NOT AGENT (R4). It knows everything — where the player really is, which spot they
/// are in, where the Nemesis really is — the way NemesisDirector does, and it may, because all it
/// ever changes is WHICH behaviours exist, never where they are aimed. Whatever reads it has to aim
/// with what the Nemesis sensed: "open the used spots inside the area it is searching", never "open
/// the spot the player is in". That is why <see cref="CollectUsedSpots"/> takes an area and has no
/// overload that takes the player.
///
/// COUNTS, DOES NOT REACT (Phase 3). Nothing in the game acts on an unlocked counterplay yet. The
/// phase is played with the F9 panel open, to calibrate SO_CounterplayRules from what really gets
/// counted instead of from guesses. The read API below is what the choice of plan §17.4 (question 7)
/// and Phase 6 will call.
///
/// ESCAPES, NOT ATTEMPTS (R1, D28). Hiding just in case is the mechanic working, and punishing it
/// would teach the player not to use it. So a spot scores its use on the way in, but the extra point
/// — and <see cref="EExploitKind.SameSpotReused"/>, and every hunt outlasted inside
/// (<see cref="EExploitKind.EscapedWhileHidden"/>) — only once the stay has turned into an escape:
/// the Nemesis hunted nearby, the player walked out, and nothing caught or chased them for a few
/// seconds after (<see cref="HidingStayBook"/> decides that part, and has the tests).
///
/// SURVIVES THE CAPTURE (R5, D3). It lives in the Data scene, outside the checkpoint rollback (which
/// restores PuzzleStateManager and nothing else), so being caught forgets nothing. New Game clears
/// it, through <see cref="ISessionResettable"/>.
///
/// It never calls the Nemesis or a spot: it listens to HidingEvents, NemesisEvents, PlayerEvents and
/// PlayerRegistry and reads public state. The one thing it measures itself is "hunting nearby", with
/// a NavMesh path query every <see cref="NearbyCheckInterval"/> seconds while the player is hidden.
///
/// SETUP: one, in the Data scene, with SO_CounterplayRules assigned. Without the asset it runs on
/// the defaults and says so once.
/// </summary>
public class PlayerHabitTracker : Singleton<PlayerHabitTracker>, ISessionResettable
{
    /// <summary>Seconds between two "is it hunting near the spot" checks during a stay. One path
    /// query each, and only while the Nemesis is actually hunting.</summary>
    private const float NearbyCheckInterval = 0.5f;

    /// <summary>Seconds a failed Nemesis lookup waits before trying again: in the menu, or in a
    /// level with no Nemesis, a FindAnyObjectByType every check would be the whole cost of this
    /// component.</summary>
    private const float NemesisLookupRetry = 1f;

    [Tooltip("Thresholds, chances, decays and what counts as near. Empty = the defaults, with a " +
             "warning.")]
    [SerializeField] private SO_CounterplayRules rules;

    private HabitLedger ledger;
    private SO_CounterplayRules fallbackRules;
    private readonly HidingStayBook stays = new HidingStayBook();

    private NemesisStateManager nemesis;
    private float nextNemesisLookupAt;

    /// <summary>The spot of the stay in progress. The book keeps its key; this is only for the
    /// "hunting nearby" check, which needs the door's position.</summary>
    private HidingSpot staySpot;
    private float nextNearbyCheckAt;

    /// <summary>
    /// Hunts, as the Nemesis's broadcasts draw them: one starts when it leaves Patrolling and ends
    /// when it gets back to it. Investigating a breath and searching again is still the same hunt,
    /// which is what keeps one stay from banking every search of it (see HidingStayBook).
    /// </summary>
    private int huntId;
    private bool huntOpen;

    private bool chaseRunning;

    /// <summary>
    /// A capture not yet closed by a ChaseEnded. Consumed there rather than reset at ChaseStarted:
    /// a grab that is not the end of a chase (the arm's-length rung, from patrol) raises the capture
    /// and the chase start in the same frame, and a flag reset by the start would forget it — then
    /// the capture's own ChaseEnded, with the player respawned inside the Hub, would count as a
    /// SafeZoneEscape.
    /// </summary>
    private bool captureAwaitingChaseEnd;

    private readonly HashSet<string> warnedWithoutId = new HashSet<string>();

    /// <summary>The last count, as one line for the HUD; null before the first.</summary>
    public string LastRegistration { get; private set; }

    /// <summary>Time.time of <see cref="LastRegistration"/>.</summary>
    public float LastRegistrationAt { get; private set; } = float.NegativeInfinity;

    /// <summary>The key of the spot the player is in, or null. For the HUD.</summary>
    public string StaySpotKey => staySpot != null ? stays.StayKey : null;

    /// <summary>Whether the Nemesis has hunted near the player's current spot during this stay —
    /// i.e. whether walking out now would leave an escape to confirm. For the HUD.</summary>
    public bool IsStayHunted => staySpot != null && stays.IsHunted;

    /// <summary>Hunts outlasted during this stay, still to be counted if it becomes an escape. For
    /// the HUD.</summary>
    public int StaySearchesSurvived => staySpot != null ? stays.SearchesOutlasted : 0;

    /// <summary>Whether a finished stay is waiting out its confirmation window. For the HUD.</summary>
    public bool HasPendingEscape => stays.HasPendingEscape;

    /// <summary>The rules in use: the assigned asset, or the defaults when there is none.</summary>
    public SO_CounterplayRules ActiveRules => rules != null ? rules : EnsureFallbackRules();

    private static float Now => Time.time;

    private void Awake()
    {
        CreateSingleton(true);

        // A duplicate is already scheduled for destruction by CreateSingleton, but the rest of this
        // Awake would still run: subscribed, it would count every event twice for a frame.
        if (!ReferenceEquals(instance, this)) return;

        GameSession.Register(this);
        ledger = new HabitLedger(ActiveRules);

        HidingEvents.OnEntered += HandleSpotEntered;
        HidingEvents.OnExited += HandleSpotExited;
        NemesisEvents.OnStateChanged += HandleNemesisStateChanged;
        NemesisEvents.OnSearchEnded += HandleSearchEnded;
        NemesisEvents.OnChaseStalled += HandleChaseStalled;
        NemesisEvents.OnChaseStarted += HandleChaseStarted;
        NemesisEvents.OnChaseEnded += HandleChaseEnded;
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;
    }

    private void OnDestroy()
    {
        GameSession.Unregister(this);

        HidingEvents.OnEntered -= HandleSpotEntered;
        HidingEvents.OnExited -= HandleSpotExited;
        NemesisEvents.OnStateChanged -= HandleNemesisStateChanged;
        NemesisEvents.OnSearchEnded -= HandleSearchEnded;
        NemesisEvents.OnChaseStalled -= HandleChaseStalled;
        NemesisEvents.OnChaseStarted -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded -= HandleChaseEnded;
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;

        if (fallbackRules != null) Destroy(fallbackRules);
    }

    /// <summary><see cref="ISessionResettable"/>, dispatched by <see cref="GameSession.BeginNewSession"/>.
    /// </summary>
    public void ResetForNewSession()
    {
        ledger?.Clear();
        stays.Clear();

        staySpot = null;
        huntOpen = false;
        chaseRunning = false;
        captureAwaitingChaseEnd = false;

        LastRegistration = null;
        LastRegistrationAt = float.NegativeInfinity;
    }

    private SO_CounterplayRules EnsureFallbackRules()
    {
        if (fallbackRules != null) return fallbackRules;

        fallbackRules = ScriptableObject.CreateInstance<SO_CounterplayRules>();
        fallbackRules.hideFlags = HideFlags.DontSave;

        Debug.LogWarning($"[{nameof(PlayerHabitTracker)}] No SO_CounterplayRules assigned: running " +
                         "on the defaults. Assign ScriptableObjects/Nemesis/SO_CounterplayRules on " +
                         "the tracker in the Data scene.", this);
        return fallbackRules;
    }

    // ── Hiding stays ────────────────────────────────────────────────────────

    private void HandleSpotEntered(HidingSpot spot)
    {
        if (spot == null) return;

        string key = BeginStay(spot);

        float meter = ledger.AddSpotUse(key, Now);
        Log($"Hid in '{key}': use {ledger.GetSpotUses(key)}, meter {meter:0.0}{SpotFlags(key)}.");
    }

    /// <summary>Starts tracking a stay in <paramref name="spot"/>, confirming the escape a previous
    /// stay was waiting on — getting into another spot unharmed is as safe as it gets.</summary>
    private string BeginStay(HidingSpot spot)
    {
        string key = KeyOf(spot);

        if (stays.Enter(key, out HidingStayBook.Escape confirmed)) CommitEscape(confirmed);

        staySpot = spot;
        nextNearbyCheckAt = 0f;
        return key;
    }

    private void HandleSpotExited(HidingSpot spot)
    {
        if (staySpot == null || !ReferenceEquals(spot, staySpot)) return;

        string key = stays.StayKey;
        stays.Exit(IsChosenExit(), Now);
        staySpot = null;

        if (stays.HasPendingEscape)
        {
            Log($"Got out of '{key}' with the Nemesis hunting nearby: an escape if nothing catches or " +
                $"chases them in the next {stays.ConfirmSeconds:0} s.");
        }
    }

    /// <summary>
    /// Whether the player walked out, as opposed to being taken out.
    ///
    /// A capture: PlayerStateManager.OnCaptured disables the player BEFORE it raises PlayerEvents.
    /// OnPlayerCaptured, and the spot lets the player go inside that same dispatch, so by the time
    /// OnExited arrives IsDisabled already says why. A cinematic owns the player. And with no player
    /// registered, the level is going away. None of those is getting away from anything.
    /// </summary>
    private static bool IsChosenExit()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        return player != null && !player.IsDisabled && !CinematicState.IsPlaying;
    }

    private void Update()
    {
        if (stays.TryConfirm(Now, chaseRunning, out HidingStayBook.Escape escape)) CommitEscape(escape);

        if (staySpot == null || stays.IsHunted) return;
        if (Time.time < nextNearbyCheckAt) return;

        nextNearbyCheckAt = Time.time + NearbyCheckInterval;

        if (IsNemesisHuntingNear(staySpot.ApproachPoint.position)) stays.MarkHunted();
    }

    /// <summary>An escape confirmed: every hunt outlasted inside, the extra point on the spot, and
    /// SameSpotReused if the spot had been escaped from before.</summary>
    private void CommitEscape(HidingStayBook.Escape escape)
    {
        for (int i = 0; i < escape.SearchesOutlasted; i++)
        {
            Register(EExploitKind.EscapedWhileHidden,
                     $"'{escape.SpotKey}', hunt {i + 1} of {escape.SearchesOutlasted} outlasted");
        }

        // Read before the escape is added: "reused" means this spot had already been escaped from.
        bool reused = ledger.GetSpotEscapes(escape.SpotKey) > 0;
        float meter = ledger.AddSpotEscape(escape.SpotKey, Now);

        Log($"Got away from '{escape.SpotKey}': meter {meter:0.0}{SpotFlags(escape.SpotKey)}.");

        if (reused)
        {
            Register(EExploitKind.SameSpotReused,
                     $"'{escape.SpotKey}', escape {ledger.GetSpotEscapes(escape.SpotKey)}");
        }
    }

    /// <summary>
    /// R1's "the Nemesis was hunting nearby": investigating, chasing or searching, within
    /// <see cref="SO_CounterplayRules.NearbyRadius"/> of <paramref name="point"/> over the NavMesh.
    /// Measured against the real Nemesis, which the tracker may do (R4): it decides what counts,
    /// not where anything goes.
    /// </summary>
    private bool IsNemesisHuntingNear(Vector3 point)
    {
        NemesisStateManager hunter = ResolveNemesis();
        if (hunter == null) return false;

        NemesisStateManager.ENemesisState? state = hunter.CurrentStateKey;
        bool hunting = state == NemesisStateManager.ENemesisState.Investigating ||
                       state == NemesisStateManager.ENemesisState.Chasing ||
                       state == NemesisStateManager.ENemesisState.Searching;
        if (!hunting) return false;

        return IsWithinNearby(hunter.transform.position, point, out _);
    }

    /// <summary>Path distance from <paramref name="from"/> to <paramref name="to"/> within the
    /// nearby radius. Prefiltered by the straight line, which no path can beat.</summary>
    private bool IsWithinNearby(Vector3 from, Vector3 to, out float distance)
    {
        distance = float.PositiveInfinity;

        float radius = ActiveRules.NearbyRadius;
        if ((to - from).sqrMagnitude > radius * radius) return false;

        return NemesisNav.TryGetPathDistance(from, to, out distance) && distance <= radius;
    }

    private NemesisStateManager ResolveNemesis()
    {
        if (nemesis != null) return nemesis;
        if (Time.time < nextNemesisLookupAt) return null;

        nextNemesisLookupAt = Time.time + NemesisLookupRetry;

        // Include inactive: the Nemesis GameObject can sit disabled until something wakes it.
        nemesis = FindAnyObjectByType<NemesisStateManager>(FindObjectsInactive.Include);
        return nemesis;
    }

    // ── What the Nemesis broadcasts ─────────────────────────────────────────

    private void HandleNemesisStateChanged(NemesisStateManager.ENemesisState state)
    {
        if (state == NemesisStateManager.ENemesisState.Patrolling)
        {
            huntOpen = false;
            return;
        }

        if (huntOpen) return;

        huntOpen = true;
        huntId++;
    }

    /// <summary>
    /// EscapedWhileHidden (C1): a search ended without finding the player, and it had come within
    /// the nearby radius of where they were hiding.
    ///
    /// Measured from the spot's door, because that is where a search would have to get to. In a spot
    /// it is only noted here, once per hunt, and counted if the stay becomes an escape. Hidden with no
    /// spot at all (the Hide toggle of F10) there is no way out to wait for, so it counts at once,
    /// from the player's own position — that is what lets the count be exercised in the testbed,
    /// which has no spots.
    /// </summary>
    private void HandleSearchEnded(Vector3 area, bool found)
    {
        if (found) return;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || !player.IsHidden) return;

        HidingSpot spot = player.CurrentHidingSpot;
        Vector3 hiddenAt = spot != null ? spot.ApproachPoint.position : player.transform.position;

        if (!IsWithinNearby(area, hiddenAt, out float distance)) return;

        if (spot == null)
        {
            Register(EExploitKind.EscapedWhileHidden,
                     $"hidden with no spot (F10), search ended {distance:0.0} m away");
            return;
        }

        // A spot the tracker is not following (the habits were cleared from F10 with the player
        // inside): take it up from here, without scoring a use it never saw.
        if (!ReferenceEquals(spot, staySpot)) BeginStay(spot);

        if (stays.NoteSearchOutlasted(huntId))
        {
            Log($"A search ended {distance:0.0} m from '{stays.StayKey}' without finding them: an " +
                "escape if they get out.");
        }
    }

    /// <summary>
    /// ChaseStalled (C4): one per CHASE, which is the detector's cut and not this class's —
    /// NemesisChaseProgress raises the event on a chase's first stalled window only
    /// (<see cref="ChaseStallCounter"/>). The ledger takes each raise for a separate occurrence of
    /// the habit, so it must not be fed one per window: that is what once took the flank and
    /// zone-defence counterplays from 35% to 85% in five minutes.
    /// </summary>
    private void HandleChaseStalled() => Register(EExploitKind.ChaseStalled, "one per chase");

    private void HandleChaseStarted() => chaseRunning = true;

    /// <summary>SafeZoneEscape (C5): a chase ended with the player inside the Hub, and not because
    /// it ended in a capture.</summary>
    private void HandleChaseEnded()
    {
        chaseRunning = false;

        bool captured = captureAwaitingChaseEnd;
        captureAwaitingChaseEnd = false;
        if (captured) return;

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null || !NemesisSafeZones.Contains(player.position)) return;

        Register(EExploitKind.SafeZoneEscape, null);
    }

    private void HandlePlayerCaptured(PlayerStateManager player)
    {
        captureAwaitingChaseEnd = true;

        if (stays.CancelPending()) Log("Caught right after getting out of a spot: that was no escape.");
    }

    /// <summary>The player going away (the level unloading, quitting to the menu) is not the end of
    /// anything they got away from.</summary>
    private void HandlePlayerUnregistered(PlayerStateManager player)
    {
        stays.CancelPending();
        stays.Exit(false, Now);
        staySpot = null;
    }

    // ── Counting ────────────────────────────────────────────────────────────

    private void Register(EExploitKind kind, string detail)
    {
        float count = ledger.RegisterExploit(kind, Now);

        LastRegistration = detail != null ? $"{kind} ({detail})" : kind.ToString();
        LastRegistrationAt = Now;

        Log($"{kind}: {count:0.0}{(detail != null ? $" — {detail}" : "")}.{DescribeRowsOf(kind)}");
    }

    /// <summary>What the rows fed by <paramref name="kind"/> say now, so each line of the log
    /// already answers "how far is this from unlocking anything".</summary>
    private string DescribeRowsOf(EExploitKind kind)
    {
        IReadOnlyList<CounterplayRule> rows = ActiveRules.Rules;
        if (rows == null) return "";

        StringBuilder text = new StringBuilder();
        float now = Now;

        for (int i = 0; i < rows.Count; i++)
        {
            CounterplayRule row = rows[i];
            if (row == null || row.Kind != kind) continue;

            float count = ledger.GetExploitCount(kind, now);
            text.Append(row.IsMetBy(count)
                ? $" {row.Unlocks} unlocked ({ledger.GetChance(row.Unlocks, now):P0})."
                : $" {row.Unlocks} at {count:0.0}/{row.Threshold}.");
        }

        return text.ToString();
    }

    private void Log(string message)
    {
        if (!ActiveRules.LogRegistrations) return;

        Debug.Log($"[{nameof(PlayerHabitTracker)}] {message}", this);
    }

    private string SpotFlags(string key)
    {
        float now = Now;
        if (ledger.IsBurnable(key, now)) return " (burnable)";
        if (ledger.IsPrioritySpot(key, now)) return " (checked first)";
        return "";
    }

    /// <summary>
    /// The SpotId when the designer set one — it is what stays the same when the level loads again —
    /// and the GameObject's name otherwise, the same fallback the F9 panel uses. Tools/Player/
    /// Validate Hiding Spots lists the spots with no id; this says it once per spot at runtime.
    /// </summary>
    private string KeyOf(HidingSpot spot)
    {
        if (!string.IsNullOrWhiteSpace(spot.SpotId)) return spot.SpotId.Trim();

        if (warnedWithoutId.Add(spot.name))
        {
            Debug.LogWarning($"[{nameof(PlayerHabitTracker)}] Hiding spot '{spot.name}' has no Spot " +
                             "Id: its uses are remembered under its name, which two spots can share.",
                             spot);
        }

        return spot.name;
    }

    // ── What the Nemesis side reads (plan §17.4 question 7, Phase 6) ─────────

    /// <summary>The usage meter of <paramref name="spot"/> (D23); 0 for a spot never used.</summary>
    public float GetSpotUsage(HidingSpot spot) =>
        spot != null ? ledger.GetSpotMeter(KeyOf(spot), Now) : 0f;

    /// <summary>Chance, 0..1, of opening <paramref name="spot"/> while investigating or searching
    /// the area it is in: meter × chance per point, capped.</summary>
    public float OpenChance(HidingSpot spot) =>
        spot != null ? ledger.GetOpenChance(KeyOf(spot), Now) : 0f;

    /// <summary>Whether <paramref name="spot"/> is used enough to be checked first (C2).</summary>
    public bool IsPrioritySpot(HidingSpot spot) =>
        spot != null && ledger.IsPrioritySpot(KeyOf(spot), Now);

    /// <summary>Whether <paramref name="spot"/> is used enough to be torn apart (plan §3.6).</summary>
    public bool IsBurnable(HidingSpot spot) =>
        spot != null && ledger.IsBurnable(KeyOf(spot), Now);

    /// <summary>
    /// Fills <paramref name="results"/> with the used, unburned spots whose door is within
    /// <paramref name="radius"/> NavMesh metres of <paramref name="centre"/>, most used first.
    /// Returns how many.
    ///
    /// The area is the caller's, and it has to be one the Nemesis sensed — the zone it is
    /// investigating or sweeping (R4). It never crosses the level for a spot on the other side
    /// (case 40): outside the radius a spot does not exist for this question, however used.
    /// </summary>
    public int CollectUsedSpots(Vector3 centre, float radius, List<HidingSpot> results)
    {
        if (results == null) return 0;
        results.Clear();

        float now = Now;
        float sqrRadius = radius * radius;
        IReadOnlyList<HidingSpot> active = HidingSpot.Active;

        for (int i = 0; i < active.Count; i++)
        {
            HidingSpot spot = active[i];
            if (spot == null || spot.IsBurned) continue;
            if (ledger.GetSpotMeter(KeyOf(spot), now) <= 0f) continue;

            Vector3 door = spot.ApproachPoint.position;
            if ((door - centre).sqrMagnitude > sqrRadius) continue;
            if (!NemesisNav.TryGetPathDistance(centre, door, out float distance) || distance > radius) continue;

            results.Add(spot);
        }

        results.Sort((a, b) => GetSpotUsage(b).CompareTo(GetSpotUsage(a)));
        return results.Count;
    }

    /// <summary>The count of <paramref name="kind"/> now, drain applied.</summary>
    public float GetExploitCount(EExploitKind kind) => ledger.GetExploitCount(kind, Now);

    /// <summary>Times <paramref name="kind"/> was counted this session, drain ignored.</summary>
    public int GetExploitTotal(EExploitKind kind) => ledger.GetExploitTotal(kind);

    /// <summary>Whether a row of SO_CounterplayRules unlocks <paramref name="counterplay"/> now.
    /// The meter-scored ones are answered per spot instead (see <see cref="ECounterplay"/>).</summary>
    public bool IsUnlocked(ECounterplay counterplay) => ledger.IsUnlocked(counterplay, Now);

    /// <summary>The chance, 0..1, that <paramref name="counterplay"/> happens when its moment comes.
    /// </summary>
    public float CounterplayChance(ECounterplay counterplay) => ledger.GetChance(counterplay, Now);

    /// <summary>Whether <paramref name="counterplay"/> has run before this session (R3: the first
    /// run has to happen where the player can see or hear it).</summary>
    public bool HasRun(ECounterplay counterplay) => ledger.HasRun(counterplay);

    /// <summary>Records a run of <paramref name="counterplay"/>; true the first time only.</summary>
    public bool MarkRun(ECounterplay counterplay) => ledger.MarkRun(counterplay);

    /// <summary>Every spot with meter left, most used first. For the F9 panel.</summary>
    public int CollectSpotReadings(List<HabitLedger.SpotReading> results) =>
        ledger.CollectSpots(results, Now);

    // ── Test console ────────────────────────────────────────────────────────

    /// <summary>Forgets everything, as New Game would, without leaving the level. A player inside a
    /// spot stays followed, from now on, with no use scored for it.</summary>
    public void DebugReset()
    {
        ResetForNewSession();

        if (HidingSpot.Occupied != null) BeginStay(HidingSpot.Occupied);

        Debug.Log($"[{nameof(PlayerHabitTracker)}] Habits cleared from the test console.", this);
    }

    /// <summary>Writes the whole ledger to the console: every count, every used spot, every
    /// counterplay's chance.</summary>
    public void DebugLogLedger()
    {
        float now = Now;
        StringBuilder text = new StringBuilder($"[{nameof(PlayerHabitTracker)}] Ledger at {now:0} s:\n");

        foreach (EExploitKind kind in System.Enum.GetValues(typeof(EExploitKind)))
            text.AppendLine($"  {kind}: {ledger.GetExploitCount(kind, now):0.00} (total {ledger.GetExploitTotal(kind)})");

        foreach (ECounterplay counterplay in System.Enum.GetValues(typeof(ECounterplay)))
        {
            text.AppendLine($"  {counterplay}: {(ledger.IsUnlocked(counterplay, now) ? "unlocked" : "locked")}, " +
                            $"chance {ledger.GetChance(counterplay, now):P0}" +
                            $"{(ledger.HasRun(counterplay) ? ", has run" : "")}");
        }

        List<HabitLedger.SpotReading> spots = new List<HabitLedger.SpotReading>();
        ledger.CollectSpots(spots, now);
        if (spots.Count == 0) text.AppendLine("  No used spots.");

        foreach (HabitLedger.SpotReading spot in spots)
        {
            text.AppendLine($"  Spot '{spot.SpotId}': meter {spot.Meter:0.00}, {spot.Uses} use(s), " +
                            $"{spot.Escapes} escape(s), open chance {ledger.GetOpenChance(spot.SpotId, now):P0}" +
                            $"{SpotFlags(spot.SpotId)}");
        }

        if (stays.HasPendingEscape) text.AppendLine("  An escape is waiting out its confirmation window.");

        Debug.Log(text.ToString(), this);
    }
}
