using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Architect's voice (architect_voice_system_spec v1.2): decides which line plays when, plays
/// its clip on the Voice bus (2D) and tells the HUD what to show (<see cref="ArchitectEvents"/>).
///
/// Rules from the spec, and where they live here:
///   - One line at a time (<see cref="IsSpeaking"/>). A trigger that arrives while a line is
///     playing is dropped — except ARC_10 (GameOver), which cuts the active line.
///   - ARC_01a locks the player; ARC_01b chains straight after it.
///   - Context lines play once per run (<see cref="TriggerContext"/>).
///   - Variants never repeat until all of them have played (shuffle bag per line).
///   - Random lines after N seconds without touching a puzzle.
///   - Nothing plays with a menu open, except lines flagged PlaysOverMenus (ARC_03, ARC_10).
///
/// One deliberate deviation: a Mandatory or Context line that is blocked (by another line or by a
/// menu) is queued and plays as soon as it can, instead of being dropped. Dropping them would lose
/// lines the spec says play "always" — the first note, for instance, opens the document reader,
/// which is a menu, so its context line could otherwise never play.
///
/// Lives under HUDCanvas in LevelUI, so there is one per loaded level. Everything it listens to is
/// a static bus, so it needs no scene references.
/// </summary>
public class ArchitectVoiceController : MonoBehaviour
{
    public static ArchitectVoiceController Instance { get; private set; }

    [SerializeField] private SO_ArchitectLineBank bank;

    [Header("Wake-up (ARC_01a / ARC_01b)")]
    [Tooltip("Plays the wake-up when the level starts. Off to skip it while testing.")]
    [SerializeField] private bool playWakeUpOnStart = true;

    [Tooltip("Turns the cinematic off and sets its skip key. Both this and playWakeUpOnStart must be on for it to play.")]
    [SerializeField] private SO_WakeUpCinematicConfig wakeUpConfig;

    [Tooltip("Seconds after the level starts before ARC_01a, so the scene fade-in finishes first.")]
    [SerializeField, Min(0f)] private float wakeUpDelay = 1.5f;

    [Header("Triggers")]
    [Tooltip("Seconds without touching a puzzle before a random line plays. Spec suggests 90; calibrate in QA.")]
    [SerializeField, Min(5f)] private float inactivityThreshold = 90f;

    [Tooltip("Fraction of a module's timer left when ARC_04 fires. 0.2 = 20%.")]
    [SerializeField, Range(0.01f, 0.99f)] private float timerCriticalFraction = 0.2f;

    [Tooltip("Longest an alert stays up. It never outlasts its line.")]
    [SerializeField, Min(0.5f)] private float alertDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugLog;
    [SerializeField] private ArchitectLineID debugLine = ArchitectLineID.Idle;

    private struct Request
    {
        public ArchitectLineID Id;
        public string ModuleLabel;
    }

    private class ShuffleBag
    {
        public readonly List<int> Order = new List<int>();
        public int Cursor;
        public int Last = -1;
    }

    private AudioSource voiceSource;

    private SO_ArchitectLineBank.Line currentLine;
    private float lineEndTime;
    private Request? chained;
    private bool chainedIsInternal;
    private readonly List<Request> deferred = new List<Request>();

    private readonly HashSet<ArchitectLineID> contextPlayed = new HashSet<ArchitectLineID>();
    private readonly Dictionary<ArchitectLineID, ShuffleBag> bags = new Dictionary<ArchitectLineID, ShuffleBag>();
    private readonly HashSet<string> criticalPlayed = new HashSet<string>();
    private readonly HashSet<string> explodedModules = new HashSet<string>();
    private readonly HashSet<string> resolvedHandled = new HashSet<string>();
    private readonly HashSet<ArchitectLineID> warnedMissing = new HashSet<ArchitectLineID>();

    private PlayerStateManager player;
    private bool lockedPlayerForWakeUp;
    private bool wakeUpDone;
    private float wakeUpCountdown = -1f;
    private float inactivityTimer;

    public bool IsSpeaking => currentLine != null;

    /// <summary>The wake-up (ARC_01a + ARC_01b) is set to play when the level starts.</summary>
    public bool PlaysWakeUpOnStart =>
        playWakeUpOnStart && (wakeUpConfig == null || wakeUpConfig.CinematicEnabled) && IsWakeUpLevel;

    /// <summary>
    /// The Player's scene is one the config lists (dev scenes are not). This controller lives in
    /// LevelUI, so the level is read off the player. Until the player registers it answers true —
    /// the countdown re-checks every frame and cancels the wake-up as soon as it knows better.
    /// </summary>
    private bool IsWakeUpLevel =>
        player == null || wakeUpConfig == null || wakeUpConfig.PlaysInScene(player.gameObject.scene.name);

    /// <summary>May be null: the cinematic then plays and can be skipped with the default key.</summary>
    public SO_WakeUpCinematicConfig WakeUpConfig => wakeUpConfig;

    /// <summary>
    /// Seconds left before ARC_01a starts, while the level-start countdown runs; -1 otherwise.
    /// The wake-up cinematic starts the stand-up clip off it, ahead of the line.
    /// </summary>
    public float WakeUpSecondsUntilLine => wakeUpCountdown;

    /// <summary>The wake-up already finished, was cut, or was skipped because it has no text.</summary>
    public bool IsWakeUpDone => wakeUpDone;

    // ── Lifecycle ────────────────────────────────────────────────────────────────────────

    // Awake/OnDestroy, not OnEnable/OnDisable: static buses, see docs/UI-System.md §7.1.
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{nameof(ArchitectVoiceController)}] A second instance on '{name}' was ignored.", this);
            enabled = false;
            return;
        }
        Instance = this;

        voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = 0f;           // Spec: the voice comes from everywhere.
        voiceSource.ignoreListenerPause = true;  // Same as AudioManager.PlayVoice.

        ModuleEvents.OnExploded += HandleModuleExploded;
        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnStateChanged += HandleModuleStateChanged;
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        // ARC_02 (NemesisReleased) has no trigger on purpose: the Nemesis only wakes up for the
        // escape cinematic, and the announcement is not wanted there.
        InteractionEvents.OnInteracted += HandleInteracted;
        InventoryEvents.OnItemAdded += HandleItemAdded;
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;

        ModuleEvents.OnExploded -= HandleModuleExploded;
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnStateChanged -= HandleModuleStateChanged;
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        InteractionEvents.OnInteracted -= HandleInteracted;
        InventoryEvents.OnItemAdded -= HandleItemAdded;
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);

        ReleaseWakeUpLock();
    }

    private void Start()
    {
        if (bank == null)
            Debug.LogWarning($"[{nameof(ArchitectVoiceController)}] No line bank assigned. The Architect stays silent.", this);

        if (PlaysWakeUpOnStart) wakeUpCountdown = wakeUpDelay;
        else wakeUpDone = true;
    }

    private void Update()
    {
        if (wakeUpCountdown >= 0f && !IsWakeUpLevel)
        {
            // The player registered in a scene without the cinematic (a dev scene): start with
            // control. WakeUpCinematicView sees IsWakeUpDone and lifts its black on the same frame.
            wakeUpCountdown = -1f;
            wakeUpDone = true;
            ReleaseWakeUpLock();
        }

        if (wakeUpCountdown >= 0f)
        {
            // The wake-up cinematic starts on a black screen: the player must not be walking around
            // behind it during the delay either, not only once ARC_01a is playing.
            LockPlayerForWakeUp();

            // Held while the loading screen is still covering the level: the scene is live behind
            // it, and counting down here would start ARC_01a (and read its first page) where
            // nobody can see it. The delay only starts once the level is being revealed.
            if (!LoadingScreen.IsLoading) wakeUpCountdown -= Time.unscaledDeltaTime;

            if (wakeUpCountdown < 0f && !TriggerLine(ArchitectLineID.WakeUpMoment1))
            {
                // No line in the bank, or blocked and queued: in the first case the run must not
                // wait for a wake-up that will never come.
                if (bank == null || bank.Find(ArchitectLineID.WakeUpMoment1) == null)
                {
                    wakeUpDone = true;
                    ReleaseWakeUpLock();
                }
            }
        }

        if (IsSpeaking && Time.unscaledTime >= lineEndTime) FinishLine();

        if (!IsSpeaking) PlayNextQueued();

        TickInactivity();
    }

    private void HandlePlayerRegistered(PlayerStateManager registered) => player = registered;

    // ── Public API ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a line if nothing else is playing and no menu is open. ARC_10 (GameOver) always plays,
    /// cutting whatever was active.
    /// </summary>
    /// <param name="moduleLabel">Fills {0} in the line's alert (M1, M2, M3).</param>
    /// <returns>True if the line started now.</returns>
    public bool TriggerLine(ArchitectLineID id, string moduleLabel = null)
    {
        SO_ArchitectLineBank.Line line = FindLine(id);
        if (line == null) return false;

        Request request = new Request { Id = id, ModuleLabel = moduleLabel };

        if (IsSpeaking)
        {
            if (id == ArchitectLineID.GameOver)
            {
                StopCurrent(interrupted: true);
            }
            else
            {
                if (IsQueueable(line)) Defer(request);
                else Log($"{id} dropped: {currentLine.id} is still playing.");
                return false;
            }
        }

        if (IsInMenus && !line.playsOverMenus)
        {
            if (IsQueueable(line)) Defer(request);
            else Log($"{id} dropped: a menu is open.");
            return false;
        }

        Play(line, moduleLabel);
        return true;
    }

    /// <summary>
    /// Cuts the wake-up wherever it is: during the delay before ARC_01a, during ARC_01a, or during
    /// ARC_01b. Neither line plays afterwards and the player gets control back. Does nothing once
    /// the wake-up is done.
    /// </summary>
    public void SkipWakeUp()
    {
        if (wakeUpDone) return;

        wakeUpCountdown = -1f;
        deferred.RemoveAll(r => r.Id == ArchitectLineID.WakeUpMoment1 || r.Id == ArchitectLineID.WakeUpMoment2);

        if (IsSpeaking && (currentLine.id == ArchitectLineID.WakeUpMoment1 || currentLine.id == ArchitectLineID.WakeUpMoment2))
        {
            StopCurrent(interrupted: true);   // Releases the lock and marks the wake-up done.
        }
        else
        {
            if (chained.HasValue && chained.Value.Id == ArchitectLineID.WakeUpMoment2) chained = null;
            ReleaseWakeUpLock();
            wakeUpDone = true;
        }

        Log("Wake-up skipped.");
    }

    /// <summary>A context line: plays once per run, however many times it is triggered.</summary>
    public void TriggerContext(ArchitectLineID id)
    {
        if (contextPlayed.Contains(id) || IsDeferred(id)) return;
        TriggerLine(id);
    }

    // ── Playback ─────────────────────────────────────────────────────────────────────────

    private void Play(SO_ArchitectLineBank.Line line, string moduleLabel)
    {
        int variant = NextVariant(line);
        string text = line.variants[variant];
        AudioClip clip = line.clips != null && variant < line.clips.Length ? line.clips[variant] : null;

        // Page separators are not read aloud, so they do not count towards the reading time.
        float speaking = clip != null
            ? clip.length
            : Mathf.Max(bank.MinLineDuration, ArchitectLinePages.Joined(text).Length / bank.ReadingCharsPerSecond);
        float duration = speaking + bank.HoldAfterLine;

        if (clip != null)
        {
            voiceSource.outputAudioMixerGroup = AudioManager.Exists ? AudioManager.Instance.VoiceGroup : null;
            voiceSource.clip = clip;
            voiceSource.Play();
        }

        currentLine = line;
        lineEndTime = Time.unscaledTime + duration;
        inactivityTimer = 0f;

        if (line.category == ArchitectLineCategory.Context) contextPlayed.Add(line.id);

        if (line.id == ArchitectLineID.WakeUpMoment1) LockPlayerForWakeUp();

        Log($"{line.id} [{variant}] \"{ArchitectLinePages.Joined(text)}\" ({duration:F1}s{(clip != null ? ", voiced" : "")})");

        ArchitectEvents.LineStarted(new ArchitectLinePlayback(line.id, text, duration, speaking, line.playsOverMenus));

        if (!string.IsNullOrEmpty(line.alert))
            HUDMessageEvents.ShowAlert(line.alert.Replace("{0}", moduleLabel ?? string.Empty),
                                       Mathf.Min(alertDuration, duration));
    }

    private void FinishLine()
    {
        ArchitectLineID finished = currentLine.id;
        EndLine(interrupted: false);

        if (finished == ArchitectLineID.WakeUpMoment1)
        {
            // Spec §7: input comes back when ARC_01a ends, and ARC_01b follows without going
            // through the isSpeaking check — it is an internal chain, not a trigger.
            ReleaseWakeUpLock();
            chained = new Request { Id = ArchitectLineID.WakeUpMoment2 };
            chainedIsInternal = true;
        }
        else if (finished == ArchitectLineID.WakeUpMoment2)
        {
            wakeUpDone = true;
        }
    }

    private void StopCurrent(bool interrupted)
    {
        if (!IsSpeaking) return;

        ArchitectLineID stopped = currentLine.id;
        EndLine(interrupted);
        chained = null;

        if (stopped == ArchitectLineID.WakeUpMoment1 || stopped == ArchitectLineID.WakeUpMoment2)
        {
            ReleaseWakeUpLock();
            wakeUpDone = true;
        }
    }

    private void EndLine(bool interrupted)
    {
        if (voiceSource.isPlaying) voiceSource.Stop();
        currentLine = null;
        inactivityTimer = 0f;
        ArchitectEvents.LineEnded(interrupted);
    }

    private void PlayNextQueued()
    {
        if (chained.HasValue)
        {
            Request next = chained.Value;
            bool isInternal = chainedIsInternal;
            chained = null;
            chainedIsInternal = false;

            if (isInternal)
            {
                SO_ArchitectLineBank.Line line = FindLine(next.Id);
                if (line != null) Play(line, next.ModuleLabel);
                else if (next.Id == ArchitectLineID.WakeUpMoment2) wakeUpDone = true;
                return;
            }

            if (FindLine(next.Id)?.category == ArchitectLineCategory.Context) TriggerContext(next.Id);
            else TriggerLine(next.Id, next.ModuleLabel);
            return;
        }

        if (deferred.Count == 0 || IsInMenus) return;

        Request queued = deferred[0];
        deferred.RemoveAt(0);

        SO_ArchitectLineBank.Line queuedLine = FindLine(queued.Id);
        if (queuedLine == null) return;
        if (queuedLine.category == ArchitectLineCategory.Context && contextPlayed.Contains(queued.Id)) return;

        Play(queuedLine, queued.ModuleLabel);
    }

    private static bool IsQueueable(SO_ArchitectLineBank.Line line) =>
        line.category == ArchitectLineCategory.Mandatory || line.category == ArchitectLineCategory.Context;

    private void Defer(Request request)
    {
        if (IsDeferred(request.Id)) return;
        deferred.Add(request);
        Log($"{request.Id} queued.");
    }

    private bool IsDeferred(ArchitectLineID id)
    {
        foreach (Request r in deferred) if (r.Id == id) return true;
        return chained.HasValue && chained.Value.Id == id;
    }

    /// <summary>Fisher-Yates bag per line: no variant repeats until every one has played.</summary>
    private int NextVariant(SO_ArchitectLineBank.Line line)
    {
        int count = line.variants.Length;
        if (count <= 1) return 0;

        if (!bags.TryGetValue(line.id, out ShuffleBag bag))
        {
            bag = new ShuffleBag();
            bags[line.id] = bag;
        }

        if (bag.Order.Count != count || bag.Cursor >= count)
        {
            bag.Order.Clear();
            for (int i = 0; i < count; i++) bag.Order.Add(i);

            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag.Order[i], bag.Order[j]) = (bag.Order[j], bag.Order[i]);
            }

            // A reshuffle must not hand back the variant that just played.
            if (bag.Order[0] == bag.Last) (bag.Order[0], bag.Order[count - 1]) = (bag.Order[count - 1], bag.Order[0]);

            bag.Cursor = 0;
        }

        bag.Last = bag.Order[bag.Cursor++];
        return bag.Last;
    }

    // ── Player lock ──────────────────────────────────────────────────────────────────────

    private void LockPlayerForWakeUp()
    {
        if (player == null || player.IsDisabled) return;
        player.IsDisabled = true;
        lockedPlayerForWakeUp = true;
    }

    private void ReleaseWakeUpLock()
    {
        if (!lockedPlayerForWakeUp) return;
        lockedPlayerForWakeUp = false;
        if (player != null) player.IsDisabled = false;
    }

    // ── Inactivity ───────────────────────────────────────────────────────────────────────

    private void TickInactivity()
    {
        if (!wakeUpDone || IsSpeaking || IsInMenus) return;
        if (player != null && player.IsDisabled) return;

        inactivityTimer += Time.deltaTime;
        if (inactivityTimer < inactivityThreshold) return;

        inactivityTimer = 0f;
        TriggerLine(ArchitectLineID.Idle);
    }

    private static bool IsInMenus =>
        (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) ||
        (PauseManager.Exists && PauseManager.Instance.IsPaused);

    // ── Game hooks ───────────────────────────────────────────────────────────────────────

    private void HandleModuleExploded(ModuleRuntime runtime)
    {
        if (runtime == null) return;
        explodedModules.Add(runtime.ModuleID);

        if (GameResultManager.ExplosionEndsRun(runtime))
        {
            TriggerLine(ArchitectLineID.GameOver);
            return;
        }

        ArchitectLineID id = runtime.Data != null && runtime.Data.Penalty == PenaltyType.Legs ? ArchitectLineID.ExplodedLegs
                           : runtime.Data != null && runtime.Data.Penalty == PenaltyType.Head ? ArchitectLineID.ExplodedHead
                           : ArchitectLineID.ExplodedChest;
        TriggerLine(id, runtime.ModuleLogLabel);
    }

    private void HandleTimerTick(ModuleRuntime runtime)
    {
        if (runtime == null || runtime.TimerProgress > timerCriticalFraction) return;
        if (!criticalPlayed.Add(runtime.ModuleID)) return;   // Once per module (spec §4).

        TriggerLine(ArchitectLineID.TimerCritical, runtime.ModuleLogLabel);
    }

    private void HandleModuleStateChanged(ModuleRuntime runtime)
    {
        if (runtime == null || runtime.Status != ModuleStatus.Resolved) return;
        if (!resolvedHandled.Add(runtime.ModuleID)) return;

        inactivityTimer = 0f;

        int index = -1, total = 0;
        if (ModuleManager.Exists)
        {
            IReadOnlyList<ModuleRuntime> all = ModuleManager.Instance.GetAllModules();
            total = all.Count;
            for (int i = 0; i < all.Count; i++) if (all[i] == runtime) index = i;
        }

        // ARC_03: the last module, whatever happened to the others.
        if (index >= 0 && index == total - 1)
        {
            TriggerLine(ArchitectLineID.GameEnd, runtime.ModuleLogLabel);
            return;
        }

        ArchitectLineID? context = index == 0 ? ArchitectLineID.ContextCentral1
                                 : index == 1 ? ArchitectLineID.ContextCentral2
                                 : (ArchitectLineID?)null;

        bool resolvedInTime = !explodedModules.Contains(runtime.ModuleID);

        // Both ARC_08 and the context line are due at the same moment. The short "Good." goes first
        // and the orientation line follows it, instead of one of the two being dropped.
        if (resolvedInTime && TriggerLine(ArchitectLineID.ResolvedInTime))
        {
            if (context.HasValue)
            {
                chained = new Request { Id = context.Value };
                chainedIsInternal = false;
            }
            return;
        }

        if (context.HasValue) TriggerContext(context.Value);
    }

    private void HandlePlayerCaptured(PlayerStateManager _) => TriggerLine(ArchitectLineID.Captured);

    private void HandleInteracted(IInteractable interactable)
    {
        if (interactable is IPuzzleInteractable) inactivityTimer = 0f;
        if (interactable is NoteInteractable) TriggerContext(ArchitectLineID.ContextFirstNote);
    }

    private void HandleItemAdded(SO_InventoryItem item)
    {
        // A note picked up into the inventory counts as found, same as one read in place.
        if (item != null && item.ContentType == ItemContentType.Text)
            TriggerContext(ArchitectLineID.ContextFirstNote);
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        inactivityTimer = 0f;

        // An exploded module never turns Resolved, so HandleModuleStateChanged cannot see the last
        // module's puzzle being finished late. ARC_03 still belongs to that moment.
        if (!ModuleManager.Exists) return;
        IReadOnlyList<ModuleRuntime> all = ModuleManager.Instance.GetAllModules();
        if (all.Count == 0) return;

        ModuleRuntime last = all[all.Count - 1];
        if (last.Status != ModuleStatus.Exploded || last.Data == null ||
            last.Data.AssociatedPuzzleId != puzzleId) return;
        if (!resolvedHandled.Add(last.ModuleID)) return;

        TriggerLine(ArchitectLineID.GameEnd, last.ModuleLogLabel);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private SO_ArchitectLineBank.Line FindLine(ArchitectLineID id)
    {
        SO_ArchitectLineBank.Line line = bank != null ? bank.Find(id) : null;
        if (line != null && line.variants != null && line.variants.Length > 0) return line;

        if (bank != null && warnedMissing.Add(id))
            Debug.LogWarning($"[{nameof(ArchitectVoiceController)}] The bank has no text for {id}.", this);
        return null;
    }

    private void Log(string message)
    {
        if (debugLog) Debug.Log($"[Architect] {message}", this);
    }

    [ContextMenu("Debug/Trigger Debug Line")]
    private void DebugTriggerLine()
    {
        if (!Application.isPlaying) return;
        if (!TriggerLine(debugLine)) Debug.Log($"[Architect] {debugLine} did not start (see debugLog).", this);
    }

    [ContextMenu("Debug/Force Inactivity Line")]
    private void DebugForceInactivity() => inactivityTimer = inactivityThreshold;
}
