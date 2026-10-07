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
/// module loses the check's penalty seconds and the SAME check is played again with a new zone. The
/// sequence only ends when every check is passed, or when it is interrupted with ESC, which loses
/// its progress (the next time starts from the first check).
///
/// Between checks the system calms down, as the spec asks: the camera shake and the ambience
/// agitation drop step by step (provisional, through <see cref="CameraShake"/> and
/// AmbienceController.SetTensionScalars), and both are gone when the overlay closes.
///
/// A modal (<see cref="IModalUI"/>) that does not pause the game: the player cannot move, the world
/// keeps running. Time only counts while this is the TOP modal, so the pause menu freezes the needle.
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
    public bool ConsumesEscape => true;   // ESC interrupts the stabilization (spec: leaving the checks)
    public bool BlocksPause => false;
    public bool PausesGame => false;      // the world keeps running
    public void RequestClose() => Cancel();

    /// <summary>From <see cref="Open"/> until the overlay has closed again.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The puzzle being stabilized while <see cref="IsOpen"/>, else null.</summary>
    public SO_StabilizationPuzzleData ActiveData { get; private set; }

    private Action<bool> onFinished;
    private CancellationTokenSource runCts;
    private AmbienceController ambience;

    private void Awake()
    {
        Instance = this;

        model = new StabilizationCheckModel();
        model.Initialize();

        if (view == null)
            Debug.LogError($"[{nameof(StabilizationCheckController)}] view not assigned in the Inspector.", this);
        else
            view.gameObject.SetActive(false);

        GameSession.Register(this);
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameSession.Unregister(this);
        GameResultManager.OnGameResult -= HandleGameResult;
        StopCalmDown();
    }

    public void ResetForNewSession() => Cancel();

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
        view.Setup(model.TotalChecks);
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

            while (!model.IsComplete)
            {
                bool passed = await PlayAttemptAsync(token);
                if (!passed) continue;

                model.Advance();
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

    /// <summary>One attempt at the current check. True when it was passed.</summary>
    private async UniTask<bool> PlayAttemptAsync(CancellationToken token)
    {
        model.RollZone();
        SO_StabilizationPuzzleData.StabilizationCheck check = model.CurrentCheck;

        view.ShowCheck(model.ZoneStart, model.ZoneDegrees, model.CheckIndex, model.TotalChecks);
        await WaitAsync(ActiveData.WarningLeadTime, token);   // [E] is not read in this window

        bool passed = await SweepAsync(token);

        if (passed)
        {
            view.ShowSuccess(model.CheckIndex + 1, model.TotalChecks);
            PlaySound(ActiveData.SuccessSoundId);
        }
        else
        {
            if (ModuleManager.Exists) ModuleManager.Instance.ApplyTimePenalty(check.timerPenaltyOnFail);
            view.ShowFailure(check.timerPenaltyOnFail);
            PlaySound(ActiveData.FailSoundId);
        }

        await WaitAsync(ActiveData.ResultHoldTime, token);
        view.ShowStandby();
        return passed;
    }

    /// <summary>
    /// The needle turning from twelve o'clock until [E] (judged against the angle on screen, last
    /// frame's) or until the lap limit runs out, which is a failure.
    /// </summary>
    private async UniTask<bool> SweepAsync(CancellationToken token)
    {
        float degreesPerSecond = model.NeedleDegreesPerSecond;
        int maxLaps = ActiveData.MaxLapsPerAttempt;
        float angle = 0f;

        while (true)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            if (!IsTopModal) continue;

            if (GameInput.InteractPressed) return model.Judge(angle);

            angle += degreesPerSecond * Time.deltaTime;
            if (maxLaps > 0 && angle >= 360f * maxLaps) return false;
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

        await base.Close();

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
