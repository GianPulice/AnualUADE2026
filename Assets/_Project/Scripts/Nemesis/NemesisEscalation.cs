using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Nemesis's difficulty escalation by completed puzzles (plan Fase 7, spec §7.2 adapted): as
/// the story advances, it sees and hears further and its patrol gets less predictable. Never faster.
///
/// PERMANENT, THROUGH THE BASELINE. It builds a copy of the authored SO_NemesisData with the tier
/// applied and installs it as the Nemesis's <see cref="NemesisStateManager.BaselineData"/>. The
/// Director's loans (senses, search persistence) are then built on top of it and hand it back, so
/// the two compose instead of fighting. The authored asset is never written: Play-mode edits to a
/// ScriptableObject persist in the Editor.
///
/// IT READS A COUNT. The tier comes from <see cref="PuzzleStateManager.CompletedPuzzleCount"/>,
/// re-read when a level loads and on every completion, respawn (a checkpoint can roll puzzles
/// back) and activation. A tally of completion events would come back from a checkpoint restore at
/// the wrong tier.
///
/// In the Data scene, next to PlayerHabitTracker, and not on the Nemesis: progress belongs to the
/// session, and the Nemesis of the next level has to wake up at the tier the story reached.
///
/// SETUP: one, in the Data scene, with SO_NemesisEscalation assigned. Without it, the defaults.
/// </summary>
public class NemesisEscalation : Singleton<NemesisEscalation>, ISessionResettable
{
    [Tooltip("The tiers by completed puzzles. Empty = the defaults, with a warning.")]
    [SerializeField] private SO_NemesisEscalation escalation;

    private SO_NemesisEscalation fallbackEscalation;

    private NemesisStateManager nemesis;

    /// <summary>The Nemesis the current tier was installed on; another one (the next level) gets
    /// it installed again.</summary>
    private NemesisStateManager appliedTo;
    private int appliedTier = int.MinValue;
    private SO_NemesisData installedCopy;

    /// <summary>F10's preview of a tier without completing puzzles; -1 = follow the count.</summary>
    public int DebugTierOverride { get; private set; } = -1;

    /// <summary>The tiers in use: the assigned asset, or the defaults.</summary>
    public SO_NemesisEscalation ActiveEscalation =>
        escalation != null ? escalation : EnsureFallbackEscalation();

    /// <summary>Completed puzzles right now, 0 without a PuzzleStateManager.</summary>
    public int CompletedPuzzles =>
        PuzzleStateManager.Exists ? PuzzleStateManager.Instance.CompletedPuzzleCount : 0;

    /// <summary>The tier index that applies now (the F10 override when set); -1 = authored tuning.
    /// </summary>
    public int CurrentTierIndex =>
        DebugTierOverride >= 0
            ? Mathf.Min(DebugTierOverride, ActiveEscalation.Tiers.Count - 1)
            : EscalationRules.TierIndex(ActiveEscalation.Tiers, CompletedPuzzles);

    /// <summary>The tier that applies now, or null for the authored tuning.</summary>
    public EscalationTier CurrentTier
    {
        get
        {
            int index = CurrentTierIndex;
            return index >= 0 ? ActiveEscalation.Tiers[index] : null;
        }
    }

    private void Awake()
    {
        CreateSingleton(true);

        // A duplicate is already on its way out (CreateSingleton); subscribed, it would install the
        // tier twice for a frame.
        if (!ReferenceEquals(instance, this)) return;

        GameSession.Register(this);

        SceneManager.sceneLoaded += HandleSceneLoaded;
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        CheckpointManager.OnRespawned += HandleRespawned;
        NemesisEvents.OnActivated += Refresh;
        NemesisEvents.OnStateChanged += HandleNemesisStateChanged;
    }

    private void OnDestroy()
    {
        GameSession.Unregister(this);

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        CheckpointManager.OnRespawned -= HandleRespawned;
        NemesisEvents.OnActivated -= Refresh;
        NemesisEvents.OnStateChanged -= HandleNemesisStateChanged;

        if (fallbackEscalation != null) Destroy(fallbackEscalation);
    }

    /// <summary><see cref="ISessionResettable"/>: a new run starts at the bottom. The Nemesis of
    /// the old run goes away with its level; the new one gets its tier when its scene loads.</summary>
    public void ResetForNewSession()
    {
        ReleaseInstalledCopy();

        DebugTierOverride = -1;
        appliedTo = null;
        appliedTier = int.MinValue;
        nemesis = null;
    }

    private SO_NemesisEscalation EnsureFallbackEscalation()
    {
        if (fallbackEscalation != null) return fallbackEscalation;

        fallbackEscalation = ScriptableObject.CreateInstance<SO_NemesisEscalation>();
        fallbackEscalation.hideFlags = HideFlags.DontSave;

        Debug.LogWarning($"[{nameof(NemesisEscalation)}] No SO_NemesisEscalation assigned: running on " +
                         "the defaults. Assign ScriptableObjects/Nemesis/SO_NemesisEscalation on the " +
                         "escalation in the Data scene.", this);
        return fallbackEscalation;
    }

    /// <summary>
    /// A level's Nemesis gets its tier as its scene finishes loading: after its Awake, before its
    /// Start. Its activation is one step late for the route: it enters Patrolling, and rolls its
    /// first cycle on the chances it has, before it raises Activated. Looked up in the scene that
    /// loaded and not anywhere: on a Retry the old level's Nemesis can still be alive.
    /// </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            NemesisStateManager found = root.GetComponentInChildren<NemesisStateManager>(true);
            if (found == null) continue;

            nemesis = found;
            Refresh();
            return;
        }
    }

    private void HandlePuzzleCompleted(string puzzleId) => Refresh();

    private void HandleRespawned(Checkpoint checkpoint) => Refresh();

    /// <summary>A safety net, cheap after the first time: a Nemesis that already has the current
    /// tier returns at once. It catches a Nemesis that was not ready when the last Refresh looked
    /// (spawned later, or its Awake had not run yet).</summary>
    private void HandleNemesisStateChanged(NemesisStateManager.ENemesisState state) => Refresh();

    /// <summary>Installs the tier that applies now on the Nemesis of this level, if it does not have
    /// it yet.</summary>
    public void Refresh()
    {
        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>(FindObjectsInactive.Include);
        if (nemesis == null) return;

        int tier = CurrentTierIndex;
        if (ReferenceEquals(nemesis, appliedTo) && tier == appliedTier) return;

        Install(nemesis, tier);
    }

    private void Install(NemesisStateManager target, int tierIndex)
    {
        SO_NemesisData authored = target.AuthoredData;
        if (authored == null) return;

        EscalationTier tier = tierIndex >= 0 ? ActiveEscalation.Tiers[tierIndex] : null;

        SO_NemesisData next = EscalationRules.IsIdentity(tier) ? authored : BuildCopy(authored, tier, tierIndex);

        // Another Nemesis than last time (the next level): the old one lets go of its copy first.
        if (!ReferenceEquals(target, appliedTo)) ReleaseInstalledCopy();

        target.InstallBaseline(next);

        // The previous copy goes only once the new baseline is in and the Director has rebuilt its
        // loan on it (InstallBaseline raises that synchronously): nothing points at it any more.
        if (installedCopy != null && !ReferenceEquals(installedCopy, next)) Destroy(installedCopy);
        installedCopy = ReferenceEquals(next, authored) ? null : next;

        appliedTo = target;
        appliedTier = tierIndex;

        if (!ActiveEscalation.LogTierChanges) return;

        string source = DebugTierOverride >= 0 ? "F10" : $"{CompletedPuzzles} completed puzzle(s)";
        Debug.Log($"[{nameof(NemesisEscalation)}] {Describe(tierIndex, tier)} ({source}).", this);
    }

    /// <summary>
    /// Throws the installed copy away. The Nemesis it was installed on gets its authored tuning back
    /// first if it is still alive (a Retry resets the session with the old level loaded), so its
    /// senses never point at a destroyed asset.
    /// </summary>
    private void ReleaseInstalledCopy()
    {
        if (installedCopy == null) return;

        if (appliedTo != null && ReferenceEquals(appliedTo.BaselineData, installedCopy))
            appliedTo.InstallBaseline(appliedTo.AuthoredData);

        Destroy(installedCopy);
        installedCopy = null;
    }

    private static SO_NemesisData BuildCopy(SO_NemesisData authored, EscalationTier tier, int tierIndex)
    {
        SO_NemesisData copy = Instantiate(authored);
        copy.name = $"{authored.name} (escalation tier {tierIndex})";

        copy.ViewRange = EscalationRules.Scale(authored.ViewRange, tier.SightMultiplier);
        copy.NoiseRangeScale = EscalationRules.Scale(authored.NoiseRangeScale, tier.HearingMultiplier);
        copy.ListenRange = EscalationRules.Scale(authored.ListenRange, tier.HearingMultiplier);
        copy.SearchQuietWindow = EscalationRules.Scale(authored.SearchQuietWindow, tier.SearchPersistenceMultiplier);
        copy.SearchHardCap = EscalationRules.Scale(authored.SearchHardCap, tier.SearchPersistenceMultiplier);
        copy.RouteReverseChance = EscalationRules.RouteChance(authored.RouteReverseChance, tier.RouteVariationFloor);
        copy.RouteSkipWaypointChance = EscalationRules.RouteChance(authored.RouteSkipWaypointChance, tier.RouteVariationFloor);

        return copy;
    }

    /// <summary>One line for the log and the console.</summary>
    public static string Describe(int tierIndex, EscalationTier tier)
    {
        if (tier == null) return "No tier: authored tuning";

        return $"Tier {tierIndex}: sight x{tier.SightMultiplier:0.##}, hearing x{tier.HearingMultiplier:0.##}, " +
               $"search x{tier.SearchPersistenceMultiplier:0.##}, route variation " +
               $"{(tier.RouteVariationFloor > 0f ? $">= {tier.RouteVariationFloor:0.##}" : "as authored")}";
    }

    // ── Test console ────────────────────────────────────────────────────────

    /// <summary>Previews a tier without completing puzzles (-1 = follow the count again).</summary>
    public void DebugSetTierOverride(int tierIndex)
    {
        int last = ActiveEscalation.Tiers.Count - 1;
        DebugTierOverride = tierIndex < 0 ? -1 : Mathf.Clamp(tierIndex, 0, Mathf.Max(0, last));

        Refresh();
    }
}
