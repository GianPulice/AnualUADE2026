using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Runs a skill check sequence, Dead by Daylight style (Central Puzzle 2 — Ventilation Hub).
///
/// Each attempt: a short pause, a warning ding with the success zone popping up somewhere on the
/// ring, then the needle sweeps ONE lap from twelve o'clock. [E] inside the zone passes and moves on;
/// inside its leading perfect slice it also gives the active module time back; anywhere else — or no
/// press before the lap closes — is a miss that costs module time and replays the same check with
/// the zone somewhere new. When the last check passes the overlay holds "stabilized" and closes.
/// The tuning is all in <see cref="SO_SkillCheckData"/>.
///
/// A modal (<see cref="IModalUI"/>) that does not pause the game: the player cannot move or look
/// around, but the world — the Nemesis included — keeps running. Everything here runs on scaled time
/// and only while this is the TOP modal, so the pause menu, or the explosion cinematic of the very
/// module it is timing, freezes the needle instead of letting a lap run out unseen.
///
/// Lives in the LevelUI scene next to its canvas, like <see cref="SequencePanelUIController"/>. The
/// caller — the Hub panel, or <see cref="SkillCheckTestKey"/> for now — calls <see cref="Open"/> and
/// is told how it ended. What this does NOT do: decide whether the Hub may start it, resolve the
/// module (the caller completes the puzzle and the module resolves on that), or the spec's progressive
/// calm-down of camera shake and ambience between checks.
/// </summary>
public class SkillCheckController
    : BaseScreenController<SkillCheckView, SkillCheckModel>, IModalUI, ISessionResettable
{
    public static SkillCheckController Instance { get; private set; }

    [Header("Data")]
    [Tooltip("Sequence played when Open() is called without one.")]
    [SerializeField] private SO_SkillCheckData defaultData;

    // ── IModalUI ────────────────────────────────────────────────────────────
    // The module timer on the HUD stays visible over exactly this id (its ModalVisibilityGate lists
    // it in ignoredModalIds), because this is where its penalties land. Renaming it hides the timer.
    public string ModalId       => "SkillCheck";
    public bool   ConsumesEscape => false;   // ESC opens the pause menu on top, and the needle waits.
    public bool   BlocksPause   => false;
    public bool   PausesGame    => false;    // the world keeps running
    public void RequestClose() => Cancel();

    /// <summary>From <see cref="Open"/> until the overlay has closed again.</summary>
    public bool IsOpen { get; private set; }

    private SO_SkillCheckData activeData;
    private Action<bool> onFinished;
    private CancellationTokenSource runCts;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        model = new SkillCheckModel();
        model.Initialize();

        if (view == null)
            Debug.LogError($"[{nameof(SkillCheckController)}] view not assigned in the Inspector.", this);
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
    }

    /// <summary>A new run never inherits a sequence left open by the last one.</summary>
    public void ResetForNewSession() => Cancel();

    /// <summary>The run is over (win, loss or game over): the dial has nothing left to time.</summary>
    private void HandleGameResult(GameResultModel _) => Cancel();

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a sequence. <paramref name="finished"/> is called once the overlay has closed: true if
    /// every check was passed, false if it was cancelled (<see cref="Cancel"/>, the run ending, a new
    /// session). Returns false — and never calls back — when nothing started: already open, or no
    /// data with at least one step.
    /// </summary>
    public bool Open(SO_SkillCheckData data = null, Action<bool> finished = null)
    {
        if (IsOpen || view == null) return false;

        SO_SkillCheckData chosen = data != null ? data : defaultData;
        if (chosen == null || chosen.TotalSteps == 0)
        {
            Debug.LogError($"[{nameof(SkillCheckController)}] No SO_SkillCheckData with at least one step to play.", this);
            return false;
        }

        IsOpen = true;
        activeData = chosen;
        onFinished = finished;
        model.Configure(chosen);

        runCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunAsync(runCts.Token).Forget();
        return true;
    }

    /// <summary>Aborts the sequence. Its progress is lost: the next Open starts from the first check.</summary>
    public void Cancel() => runCts?.Cancel();

    // ── BaseScreenController hooks ──────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        view.Setup(model.TotalSteps);

        // Time.timeScale and the cursor are governed by UIStateManager.
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
            await base.Open();
            token.ThrowIfCancellationRequested();

            bool first = true;
            while (!model.IsComplete)
            {
                await PlayAttemptAsync(first, token);
                first = false;
            }

            view.ShowComplete(model.TotalSteps);
            PlayClip(activeData.completeClip);
            await WaitAsync(activeData.completeHoldTime, token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled: closed below like a completed run, only reported as not completed.
        }
        finally
        {
            await FinishAsync(completed);
        }
    }

    /// <summary>One attempt: the pause, the warning, the lap, the verdict.</summary>
    private async UniTask PlayAttemptAsync(bool first, CancellationToken token)
    {
        view.ShowStandby();
        // The first check follows the overlay's own fade-in; the rest keep the player guessing.
        if (!first)
            await WaitAsync(RouletteSelection.GetRandom(activeData.gapBetweenChecksMin, activeData.gapBetweenChecksMax), token);

        SO_SkillCheckData.SkillCheckStep step = model.CurrentStep;
        view.ShowCheck(model.ZoneStart, model.ZoneWidth, model.PerfectWidth, model.StepIndex, model.TotalSteps);
        PlayClip(activeData.warningClip);
        await WaitAsync(activeData.warningLeadTime, token);   // [E] is not read in this window

        SkillCheckResult result = await SweepAsync(step.sweepDuration, token);

        model.Register(result);
        ApplyModuleTime(result, step);
        view.ShowResult(result, model.StepIndex, model.TotalSteps);
        PlayClip(result switch
        {
            SkillCheckResult.Perfect => activeData.perfectClip,
            SkillCheckResult.Good => activeData.goodClip,
            _ => activeData.missClip
        });

        await WaitAsync(activeData.resultHoldTime, token);
    }

    /// <summary>
    /// One lap of the needle from twelve o'clock. A press is judged against the angle that was on
    /// screen when it was made — last frame's, since this frame's has not been drawn yet. No press
    /// before the lap closes is a miss.
    /// </summary>
    private async UniTask<SkillCheckResult> SweepAsync(float lapSeconds, CancellationToken token)
    {
        float degreesPerSecond = 360f / Mathf.Max(0.01f, lapSeconds);
        float angle = 0f;

        while (true)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            if (!IsTopModal) continue;

            if (GameInput.InteractPressed) return model.Judge(angle);

            angle += degreesPerSecond * Time.deltaTime;
            if (angle >= 360f) return SkillCheckResult.Miss;
            view.SetNeedle(angle);
        }
    }

    /// <summary>
    /// Scaled seconds that only count while this is the top modal — the same clock the needle runs
    /// on, so no part of an attempt slips by under the pause menu or a cinematic.
    /// </summary>
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

        // Destroyed with its scene: there is nothing left to close or to tell.
        if (this == null) return;

        await base.Close();

        IsOpen = false;
        activeData = null;
        Action<bool> callback = onFinished;
        onFinished = null;
        callback?.Invoke(completed);
    }

    // ── Consequences ────────────────────────────────────────────────────────

    /// <summary>
    /// A miss costs the active module time, a perfect gives some back. Both are no-ops with no module
    /// running — which includes an M2 that already exploded: the spec still makes the player finish
    /// the sequence to move on.
    /// </summary>
    private static void ApplyModuleTime(SkillCheckResult result, SO_SkillCheckData.SkillCheckStep step)
    {
        if (!ModuleManager.Exists) return;

        if (result == SkillCheckResult.Miss) ModuleManager.Instance.ApplyTimePenalty(step.failTimePenalty);
        else if (result == SkillCheckResult.Perfect) ModuleManager.Instance.ApplyTimeBonus(step.perfectTimeBonus);
    }

    /// <summary>On the UI bus, like the sequence panel's clicks: audible under the pause muffle.</summary>
    private void PlayClip(AudioClip clip)
    {
        if (clip == null || activeData == null || !AudioManager.Exists) return;
        AudioManager.Instance.PlayUIClip(clip, activeData.volume);
    }
}
