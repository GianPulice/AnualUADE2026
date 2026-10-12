using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Runs the stabilization checks of the Ventilation Hub (Central Puzzle 2 — spec §3).
///
/// Each attempt: the success zone appears somewhere on the dial, a short warning, then the needle
/// turns at the check's constant speed. [E] inside the zone passes and moves on to the next
/// (easier) check; [E] outside — or no press within the asset's lap limit — fails: the running
/// module loses the check's penalty seconds and, by default, the SAME check is played again (the
/// asset can make it restart from the first one instead). The sequence only ends when every check
/// is passed, or when it is interrupted with ESC (if the asset allows it), which loses its progress.
///
/// Between checks the system calms down, as the spec asks: the camera shake and the ambience
/// agitation drop step by step (provisional, through <see cref="CameraShake"/> and
/// AmbienceController.SetTensionScalars), and both are gone when the overlay closes. The hub lights
/// read <see cref="PassedChecks"/> on their own (<see cref="StabilizationFlickerLight"/>).
///
/// A modal (<see cref="IModalUI"/>) that does not pause the game: the player cannot move, the world
/// keeps running. Time only counts while this is the TOP modal, so the pause menu freezes the needle,
/// and so does a module explosion cinematic — M2 can run out mid-sequence. While another modal is on
/// top the overlay is hidden (nothing may cover the explosion), and it comes back where it was: a
/// penalty with no module running any more costs nothing, but the checks still have to be passed.
/// Lives in the LevelUI scene next to its canvas. The Hub panel
/// (<see cref="StabilizationPanelInteractable"/>) calls <see cref="Open"/> and completes the puzzle
/// when told it ended well.
/// </summary>
public class StabilizationCheckController
    : BaseScreenController<StabilizationCheckView, StabilizationCheckModel>, IModalUI, ISessionResettable
{
    public static StabilizationCheckController Instance { get; private set; }

    // ── IModalUI ────────────────────────────────────────────────────────────
    public string ModalId => "StabilizationCheck";
    // ESC interrupts the stabilization (spec: leaving the checks) when the asset allows it;
    // otherwise it passes through to the pause menu and the needle waits under it.
    public bool ConsumesEscape => ActiveData == null || ActiveData.AllowAbort;
    // ESC both opens the pause menu (a global overlay) and reaches the top modal. While aborting is
    // allowed the stabilization keeps ESC for itself, so the key leaves the checks instead of pausing
    // over them; with aborting off it lets the pause menu open and the needle waits underneath.
    public bool BlocksPause => IsOpen && ActiveData != null && ActiveData.AllowAbort;
    public bool PausesGame => false;      // the world keeps running
    // Always honoured: with aborting off, ESC never reaches here (ConsumesEscape is false), so the
    // only callers left are the ones that must close it — UIStateManager.CloseAll on the way back
    // to the main menu, for one.
    public void RequestClose() => Cancel();

    /// <summary>From <see cref="Open"/> until the overlay has closed again.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The puzzle being stabilized while <see cref="IsOpen"/>, else null.</summary>
    public SO_StabilizationPuzzleData ActiveData { get; private set; }

    /// <summary>Checks passed so far in the running sequence (0 when none is running).</summary>
    public int PassedChecks => IsOpen && model != null ? model.CheckIndex : 0;

    private enum SweepResult { Hit, Miss, Timeout }

    private Action<bool> onFinished;
    private CancellationTokenSource runCts;
    private bool closing;
    private AmbienceController ambience;

    private void Awake()
    {
        Instance = this;

        model = new StabilizationCheckModel();
        model.Initialize();

        if (view == null)
        {
            Debug.LogError($"[{nameof(StabilizationCheckController)}] view not assigned in the Inspector.", this);
        }
        else
        {
            view.gameObject.SetActive(false);
            view.CloseClicked += RequestClose;   // the window's [X]
        }

        GameSession.Register(this);
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (view != null) view.CloseClicked -= RequestClose;
        GameSession.Unregister(this);
        GameResultManager.OnGameResult -= HandleGameResult;
        StopCalmDown();
    }

    public void ResetForNewSession() => Cancel();

    // Hidden, not closed, while another modal is on top (the module explosion cinematic, the pause
    // menu): the run keeps its place and shows again as soon as it is the top one.
    private void LateUpdate()
    {
        if (IsOpen && !closing && view != null) view.SetSuspended(!IsTopModal);
    }

    private void HandleGameResult(GameResultModel _) => Cancel();

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the sequence. <paramref name="finished"/> is called once the overlay has closed: true
    /// when every check was passed, false when it was interrupted. Returns false — and never calls
    /// back — when nothing started (already open, no checks).
    /// </summary>
    public bool Open(SO_StabilizationPuzzleData data, Action<bool> finished)
    {
        if (IsOpen || view == null || data == null) return false;
        if (data.Checks.Length == 0)
        {
            Debug.LogError($"[{nameof(StabilizationCheckController)}] '{data.name}' has no checks.", this);
            return false;
        }

        IsOpen = true;
        ActiveData = data;
        onFinished = finished;
        model.Configure(data);

        runCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunAsync(runCts.Token).Forget();
        return true;
    }

    /// <summary>Interrupts the sequence. Its progress is lost.</summary>
    public void Cancel() => runCts?.Cancel();

    // ── BaseScreenController hooks ──────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        view.Setup(model.TotalChecks, ActiveData != null && ActiveData.AllowAbort);
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    // ── Sequence ────────────────────────────────────────────────────────────

    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        bool completed = false;
        try
        {
            PlaySound(ActiveData.OpenSoundId);
            ApplyCalmDown(0);

            await base.Open();
            token.ThrowIfCancellationRequested();

            bool retry = false;
            while (!model.IsComplete)
            {
                bool passed = await PlayAttemptAsync(retry, token);
                if (passed)
                {
                    model.Advance();
                    retry = false;
                }
                else if (ActiveData.OnFail == SO_StabilizationPuzzleData.FailBehavior.RestartFromFirstCheck)
                {
                    model.Restart();
                    retry = false;
                }
                else
                {
                    retry = true;
                }

                ApplyCalmDown(model.CheckIndex);
            }

            view.ShowComplete(model.TotalChecks);
            PlaySound(ActiveData.CompleteSoundId);
            await WaitAsync(ActiveData.CompleteHoldTime, token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            // Interrupted: closed below like any other run, reported as not completed.
        }
        finally
        {
            await FinishAsync(completed);
        }
    }

    /// <summary>
    /// One attempt at the current check. True when it was passed. A retry of the same check keeps
    /// its zone unless the asset asks for a new one.
    /// </summary>
    private async UniTask<bool> PlayAttemptAsync(bool retry, CancellationToken token)
    {
        if (!retry || ActiveData.NewZoneOnRetry) model.RollZone();
        SO_StabilizationPuzzleData.StabilizationCheck check = model.CurrentCheck;

        view.ShowCheck(model.ZoneStart, model.ZoneDegrees, model.CheckIndex, model.TotalChecks, ActiveData.AllowAbort);
        await WaitAsync(ActiveData.WarningLeadTime, token);   // [E] is not read in this window

        SweepResult result = await SweepAsync(token);

        if (result == SweepResult.Hit)
        {
            view.ShowSuccess(model.CheckIndex + 1, model.TotalChecks);
            PlaySound(ActiveData.SuccessSoundId);
        }
        else
        {
            bool penalize = result == SweepResult.Miss || ActiveData.PenalizeTimeout;
            view.ShowFailure(penalize ? ApplyPenalty(check.timerPenaltyOnFail) : 0f);
            PlaySound(ActiveData.FailSoundId);
        }

        await WaitAsync(ActiveData.ResultHoldTime, token);
        view.ShowStandby();
        return result == SweepResult.Hit;
    }

    /// <summary>
    /// Takes the penalty off the running module. Returns what it actually cost: 0 with no module
    /// running — M2 already exploded, which per spec still leaves the checks to be passed.
    /// </summary>
    private static float ApplyPenalty(float seconds)
    {
        if (seconds <= 0f || !ModuleManager.Exists) return 0f;

        ModuleRuntime active = ModuleManager.Instance.GetActiveModule();
        if (active == null || !active.IsTimerRunning) return 0f;

        float applied = Mathf.Min(seconds, active.TimeRemaining);
        ModuleManager.Instance.ApplyTimePenalty(seconds);
        return applied;
    }

    /// <summary>
    /// The needle turning from twelve o'clock until [E] (judged against the angle on screen, last
    /// frame's) or until the lap limit runs out.
    /// </summary>
    private async UniTask<SweepResult> SweepAsync(CancellationToken token)
    {
        float degreesPerSecond = model.NeedleDegreesPerSecond;
        int maxLaps = ActiveData.MaxLapsPerAttempt;
        float angle = 0f;

        while (true)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            if (!IsTopModal) continue;

            if (GameInput.InteractPressed) return model.Judge(angle) ? SweepResult.Hit : SweepResult.Miss;

            angle += degreesPerSecond * Time.deltaTime;
            if (maxLaps > 0 && angle >= 360f * maxLaps) return SweepResult.Timeout;
            view.SetNeedle(ClockArc.Normalize(angle));
        }
    }

    /// <summary>Scaled seconds that only count while this is the top modal.</summary>
    private async UniTask WaitAsync(float seconds, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            if (IsTopModal) elapsed += Time.deltaTime;
        }
    }

    private bool IsTopModal => UIStateManager.Exists && ReferenceEquals(UIStateManager.Instance.Peek(), this);

    private async UniTask FinishAsync(bool completed)
    {
        runCts?.Dispose();
        runCts = null;
        StopCalmDown();

        if (this == null) return;

        // Back on screen for the fade-out only when nothing covers it: closed under a cinematic
        // (the run ended), it stays hidden and fades out from there.
        if (IsTopModal) view.SetSuspended(false);
        closing = true;
        await base.Close();
        closing = false;

        IsOpen = false;
        ActiveData = null;
        Action<bool> callback = onFinished;
        onFinished = null;
        callback?.Invoke(completed);
    }

    // ── Progressive calm-down (provisional) ────────────────────────────────

    /// <summary>Shake and ambience for <paramref name="passed"/> checks done: full while none is.</summary>
    private void ApplyCalmDown(int passed)
    {
        if (ActiveData == null) return;

        CameraShake.Set(this, ActiveData.ShakeAmplitude * ActiveData.ShakeLeft(passed), ActiveData.ShakeFrequency);

        if (ambience == null) ambience = FindAnyObjectByType<AmbienceController>();
        if (ambience != null)
        {
            float agitation = 1f + (ActiveData.AmbienceAgitation - 1f) * ActiveData.AmbienceLeft(passed);
            ambience.SetTensionScalars(agitation, 1f, agitation);
        }
    }

    private void StopCalmDown()
    {
        CameraShake.Clear(this);
        if (ambience != null) ambience.SetTensionScalars(1f, 1f, 1f);
        ambience = null;
    }

    private void PlaySound(string id)
    {
        if (!string.IsNullOrEmpty(id) && AudioManager.Exists) AudioManager.Instance.PlaySFX(id);
    }
}
