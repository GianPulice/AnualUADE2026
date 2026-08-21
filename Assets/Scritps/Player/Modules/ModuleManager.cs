using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the state of the device modules for a run.
///
/// Responsibilities:
///  • Build one <see cref="ModuleRuntime"/> per <see cref="ModuleData"/> in the config.
///  • Enforce sequentiality (only one module Active at a time) and the prerequisite chain
///    (module N cannot activate until N-1 is Resolved).
///  • Tick the active module's timer with unscaledDeltaTime, so it keeps counting while the
///    inventory pauses the game.
///  • Detect explosion, raise <see cref="ModuleEvents.OnExploded"/>, apply penalty via the
///    player, and report game over via <see cref="GameResultManager"/> when all modules explode.
///
/// Lives as a Singleton (DontDestroyOnLoad) hosted in the Data scene alongside InventoryManager
/// and PuzzleStateManager. Designers control everything through <see cref="SO_ModulesConfig"/> —
/// no inspector or code edits are needed to add/reorder modules or tweak timers.
/// </summary>
public class ModuleManager : Singleton<ModuleManager>, ISessionResettable
{
    [Header("Config")]
    [Tooltip("Ordered list of modules for this run. Designers edit this asset — the manager only reads it.")]
    [SerializeField] private SO_ModulesConfig config;

    [Header("Debug")]
    [Tooltip("Verbose logging for the module lifecycle. Turn off in shipping builds.")]
    [SerializeField] private bool debugLog = true;

    private readonly List<ModuleRuntime> runtimes = new List<ModuleRuntime>();
    private ModuleRuntime activeRuntime;
    private float sessionTime;
    private bool gameOverReported;
    private int pauseRequestCount;

    // ── Unity ────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        CreateSingleton(true);
        BuildRuntimes();
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;
        GameSession.Register(this);
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;
        GameSession.Unregister(this);
    }

    /// <summary>Pause the timer while the pause menu is open — spec §7: the timer must not tick
    /// while the player has no control over the character.</summary>
    private void HandlePauseStateChanged(PauseState state)
    {
        if (state == PauseState.Paused) PauseTicking();
        else ResumeTicking();
    }

    /// <summary>Resolve the module that declares this puzzle as its associated puzzle, if any.</summary>
    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (string.IsNullOrEmpty(puzzleId)) return;

        foreach (ModuleRuntime r in runtimes)
        {
            if (r.Data != null && r.Data.AssociatedPuzzleId == puzzleId)
            {
                ResolveModule(r.ModuleID);
                return;
            }
        }
    }

    private void Update()
    {
        // Session time starts ticking as soon as the first module goes Active. Before that we are
        // still in menus / bootstrapping and should not count.
        if (activeRuntime != null || GetResolvedCount() > 0 || GetExplodedCount() > 0)
            sessionTime += Time.unscaledDeltaTime;

        TickActive();
    }

    // ── Public API — called by triggers, puzzles, skill-checks, player ──────────────────

    /// <summary>
    /// Activate a module by its index in the config. Called by <see cref="ZoneTrigger"/> the first
    /// time the player enters the zone. Silently ignored if the module is already activated, the
    /// index is out of range, or the previous module has not been resolved yet.
    /// </summary>
    public void ActivateModule(int index)
    {
        if (index < 0 || index >= runtimes.Count)
        {
            Log($"ActivateModule({index}) ignored: index out of range (count={runtimes.Count}).");
            return;
        }

        ModuleRuntime target = runtimes[index];

        if (target.HasBeenActivated)
        {
            Log($"ActivateModule({index}) ignored: '{target.ModuleID}' was already activated.");
            return;
        }

        if (index > 0 && runtimes[index - 1].Status != ModuleStatus.Resolved)
        {
            Log($"ActivateModule({index}) blocked: previous module '{runtimes[index - 1].ModuleID}' " +
                $"is '{runtimes[index - 1].Status}', must be Resolved first.");
            return;
        }

        if (activeRuntime != null)
        {
            // Should not happen with a well-formed config (the previous module must be Resolved,
            // which sets activeRuntime = null). Guard defensively so a malformed setup does not
            // start two timers.
            Log($"ActivateModule({index}) blocked: another module '{activeRuntime.ModuleID}' is still Active.");
            return;
        }

        target.HasBeenActivated = true;
        target.Status = ModuleStatus.Active;
        target.TimeRemaining = target.Data.TimerDuration;
        target.IsTimerRunning = true;
        activeRuntime = target;

        Log($"Module '{target.ModuleID}' activated ({target.Data.TimerDuration:F1}s).");
        ModuleEvents.RaiseStateChanged(target);
    }

    /// <summary>Convenience overload for ZoneTrigger, which holds a ModuleData reference.</summary>
    public void ActivateModule(ModuleData data)
    {
        if (data == null) return;
        int index = IndexOf(data);
        if (index >= 0) ActivateModule(index);
    }

    /// <summary>
    /// Marks a module as Resolved. Called by <see cref="PuzzleController.CompletePuzzle"/> when the
    /// associated puzzle finishes. If the module had already exploded, the penalty stays applied —
    /// only the state changes to Resolved.
    /// </summary>
    public void ResolveModule(string moduleId)
    {
        ModuleRuntime target = GetRuntime(moduleId);
        if (target == null)
        {
            Log($"ResolveModule('{moduleId}') ignored: no module with that id.");
            return;
        }

        if (target.Status == ModuleStatus.Resolved)
        {
            Log($"ResolveModule('{moduleId}') ignored: already Resolved.");
            return;
        }

        bool hadExploded = target.Status == ModuleStatus.Exploded;

        target.IsTimerRunning = false;
        target.Status = ModuleStatus.Resolved;
        if (activeRuntime == target) activeRuntime = null;

        Log(hadExploded
            ? $"Module '{moduleId}' resolved after having exploded — penalty stays."
            : $"Module '{moduleId}' resolved on time — no penalty.");

        ModuleEvents.RaiseStateChanged(target);
    }

    /// <summary>
    /// Subtract seconds from the active module's timer. Used by systems like the SkillCheck to
    /// apply a time penalty on failure without touching runtime fields directly.
    /// </summary>
    public void ApplyTimePenalty(float seconds)
    {
        if (activeRuntime == null || !activeRuntime.IsTimerRunning) return;
        activeRuntime.TimeRemaining = Mathf.Max(0f, activeRuntime.TimeRemaining - seconds);
    }

    /// <summary>
    /// Pause the active module's timer. Reference-counted so multiple concurrent pausers
    /// (e.g. a cinematic + a modal UI) do not interfere: the timer only resumes when every
    /// pauser has released. Safe to call even if no module is active.
    ///
    /// Used by cinematics (spec §7 — timer must not tick during non-interactive events) and
    /// can be reused by any future system that needs to freeze the countdown temporarily.
    /// </summary>
    public void PauseTicking()
    {
        pauseRequestCount++;
    }

    /// <summary>Release one pause request. Ignored if there are no pending pauses.</summary>
    public void ResumeTicking()
    {
        if (pauseRequestCount > 0) pauseRequestCount--;
    }

    public bool IsTickingPaused => pauseRequestCount > 0;

    /// <summary>
    /// Report a loss (Nemesis capture, other death states) with the current session's stats so
    /// the result screen sees the same time and resolved-module count as the timer-driven flow.
    /// </summary>
    public void ReportLoss()
    {
        GameResultManager.ReportLoss(sessionTime, GetResolvedCount());
    }

    /// <summary>
    /// Clears all module state and session time. Call when starting a new game (post GameOver
    /// scene reload) so the same manager instance starts from Inactive again.
    /// </summary>
    public void ResetSession()
    {
        BuildRuntimes();
        activeRuntime = null;
        sessionTime = 0f;
        gameOverReported = false;
        pauseRequestCount = 0;
        Log("Session reset.");
    }

    /// <summary><see cref="ISessionResettable"/> — dispatched by <see cref="GameSession.BeginNewSession"/>.</summary>
    public void ResetForNewSession() => ResetSession();

    // ── Queries ─────────────────────────────────────────────────────────────────

    public ModuleRuntime GetActiveModule() => activeRuntime;
    public ModuleRuntime GetRuntime(string moduleId) => runtimes.Find(r => r.ModuleID == moduleId);
    public IReadOnlyList<ModuleRuntime> GetAllModules() => runtimes;
    public float SessionTime => sessionTime;
    public int TotalModules => runtimes.Count;
    public int GetExplodedCount() => runtimes.FindAll(r => r.Status == ModuleStatus.Exploded).Count;
    public int GetResolvedCount() => runtimes.FindAll(r => r.Status == ModuleStatus.Resolved).Count;

    // ── Internals ──────────────────────────────────────────────────────────────

    private void BuildRuntimes()
    {
        runtimes.Clear();

        if (config == null)
        {
            Debug.LogError("[ModuleManager] No SO_ModulesConfig assigned. The module system will not run.");
            return;
        }

        foreach (ModuleData data in config.Modules)
        {
            if (data == null)
            {
                Debug.LogWarning("[ModuleManager] Null entry in SO_ModulesConfig.modules — skipped.");
                continue;
            }
            runtimes.Add(new ModuleRuntime(data));
        }
    }

    private void TickActive()
    {
        if (activeRuntime == null || !activeRuntime.IsTimerRunning) return;
        if (pauseRequestCount > 0) return;

        activeRuntime.TimeRemaining -= Time.unscaledDeltaTime;

        if (activeRuntime.TimeRemaining <= 0f)
        {
            Explode(activeRuntime);
        }
        else
        {
            ModuleEvents.RaiseTimerTick(activeRuntime);
        }
    }

    private void Explode(ModuleRuntime target)
    {
        target.TimeRemaining = 0f;
        target.IsTimerRunning = false;
        target.Status = ModuleStatus.Exploded;
        activeRuntime = null;

        Log($"Module '{target.ModuleID}' EXPLODED. Applying penalty '{target.Data.Penalty}'.");

        ModuleEvents.RaiseExploded(target);
        ModuleEvents.RaiseStateChanged(target);

        CheckGameOver();
    }

    private void CheckGameOver()
    {
        if (gameOverReported) return;
        if (runtimes.Count == 0) return;
        if (GetExplodedCount() < runtimes.Count) return;

        gameOverReported = true;
        ModuleEvents.RaiseAllModulesExploded();
        GameResultManager.ReportGameOver(sessionTime, GetResolvedCount());
        Log("All modules exploded — GameOver reported.");
    }

    private int IndexOf(ModuleData data)
    {
        for (int i = 0; i < runtimes.Count; i++)
            if (runtimes[i].Data == data) return i;
        return -1;
    }

    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[ModuleManager] {msg}");
    }
}
