using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Radio decoy. One use: the player turns it on, it plays loud, and a Nemesis within
/// <see cref="SO_RadioDecoy.HearingDistance"/> comes over and smashes it
/// (<see cref="NemesisDecoyBreaker"/>). Once broken it never works again.
///
/// The angry cut, the high-angle camera and the smash animation are not here: they hang off
/// <see cref="onBreakStarted"/> / <see cref="onBroken"/> until the models and the shot exist.
/// </summary>
[RequireComponent(typeof(DecoyNoiseSource))]
[AddComponentMenu("WIRED/Decoys/Radio Decoy")]
public class RadioDecoy : BaseRangeInteractable, INemesisBreakableDecoy
{
    private enum ERadioState { Off, Playing, BeingBroken, Silent, Broken }

    [SerializeField] private SO_RadioDecoy data;

    [Tooltip("Where the loop plays. Configure it 3D on the prefab so the player can place it by ear.")]
    [SerializeField] private AudioSource loopSource;

    [Header("Hooks (modelo / cámara / VFX)")]
    [SerializeField] private UnityEvent onTurnedOn;
    [Tooltip("Arranca el corte enojado del Nemesis: cámara en picado, animación.")]
    [SerializeField] private UnityEvent onBreakStarted;
    [Tooltip("El corte se interrumpió antes del golpe (vio al jugador). Devolver la cámara.")]
    [SerializeField] private UnityEvent onBreakAborted;
    [Tooltip("El golpe: cambiar a modelo roto, chispas, etc.")]
    [SerializeField] private UnityEvent onBroken;
    [Tooltip("Se apagó sola por MaxPlayTime, sin que la rompan.")]
    [SerializeField] private UnityEvent onWentSilent;

    private DecoyNoiseSource noise;
    private ERadioState state = ERadioState.Off;
    private CancellationTokenSource playCts;

    protected override void Awake()
    {
        base.Awake();
        noise = GetComponent<DecoyNoiseSource>();

        if (data == null)
            Debug.LogError($"[{nameof(RadioDecoy)}] '{name}' has no SO_RadioDecoy assigned.", this);
    }

    public override string GetPromptText() => data != null ? data.InteractText : "Encender radio";

    protected override bool CanInteractInCloseRange() => data != null && state == ERadioState.Off;

    public override bool IsRepeatable() => false;

    public override bool IsFinished() => state != ERadioState.Off;

    protected override void OnInteract()
    {
        state = ERadioState.Playing;

        if (AudioManager.Exists)
        {
            if (!string.IsNullOrEmpty(data.TurnOnSoundId))
                AudioManager.Instance.PlaySFX(data.TurnOnSoundId, transform.position);
            if (!string.IsNullOrEmpty(data.LoopSoundId) && loopSource != null)
                AudioManager.Instance.PlayLoop(data.LoopSoundId, loopSource);
        }

        noise.StartEmitting(data.HearingDistance, audibleEverywhere: false);
        onTurnedOn?.Invoke();
        InteractionEvents.RequestPromptRefresh();

        if (data.MaxPlayTime > 0f)
        {
            CancelPlayTimer();
            playCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            PlayTimerAsync(data.MaxPlayTime, playCts.Token).Forget();
        }
    }

    /// <summary>Scaled time: freezes with the pause menu like every other gameplay timer.</summary>
    private async UniTaskVoid PlayTimerAsync(float seconds, CancellationToken token)
    {
        bool cancelled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token)
                                       .SuppressCancellationThrow();
        if (cancelled || state != ERadioState.Playing) return;

        state = ERadioState.Silent;
        StopSounding();
        onWentSilent?.Invoke();
    }

    // ── INemesisBreakableDecoy ──────────────────────────────────────────────

    public bool CanBeBroken => data != null && state == ERadioState.Playing;

    public Vector3 BreakTargetPosition => transform.position;

    public float BreakReach => data != null ? data.BreakReach : 0f;

    public float BreakWindup => data != null ? data.BreakWindup : 0f;

    public float BreakRecovery => data != null ? data.BreakRecovery : 0f;

    public void OnBreakStarted()
    {
        // The max-play timer must not silence it halfway through the smash.
        state = ERadioState.BeingBroken;
        CancelPlayTimer();
        onBreakStarted?.Invoke();
    }

    public void OnBreakAborted()
    {
        if (state != ERadioState.BeingBroken) return;

        // Keeps sounding with no play timer left: an aborted smash hands it back as a radio that
        // plays until someone does break it, which is the default case anyway.
        state = ERadioState.Playing;
        onBreakAborted?.Invoke();
    }

    public void Break()
    {
        if (state == ERadioState.Broken) return;
        state = ERadioState.Broken;

        StopSounding();
        if (AudioManager.Exists && !string.IsNullOrEmpty(data.BreakSoundId))
            AudioManager.Instance.PlaySFX(data.BreakSoundId, transform.position);

        onBroken?.Invoke();
    }

    private void StopSounding()
    {
        noise.StopEmitting();
        if (loopSource != null) loopSource.Stop();
        InteractionEvents.RequestPromptRefresh();
    }

    private void CancelPlayTimer()
    {
        playCts?.Cancel();
        playCts?.Dispose();
        playCts = null;
    }

    private void OnDestroy() => CancelPlayTimer();
}
