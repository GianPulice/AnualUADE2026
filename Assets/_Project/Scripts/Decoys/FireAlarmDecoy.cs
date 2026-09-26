using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Fire alarm decoy. One use: the player pulls it, <see cref="SO_FireAlarmDecoy.ArmDelay"/>
/// seconds later it rings and the sprinklers open, and for
/// <see cref="SO_FireAlarmDecoy.RingDuration"/> the Nemesis hears it from anywhere and goes to
/// investigate the <see cref="DecoyNoiseSource"/>'s investigate point (put it in the room).
///
/// The water is <see cref="sprinklers"/> plus <see cref="onRingStarted"/>; wet floors, fog or
/// anything the water does to the player is not here.
/// </summary>
[RequireComponent(typeof(DecoyNoiseSource))]
[AddComponentMenu("WIRED/Decoys/Fire Alarm Decoy")]
public class FireAlarmDecoy : BaseRangeInteractable
{
    private enum EAlarmState { Idle, Arming, Ringing, Spent }

    [SerializeField] private SO_FireAlarmDecoy data;

    [Tooltip("Where the siren loop plays.")]
    [SerializeField] private AudioSource ringSource;

    [Tooltip("Rociadores. Play al empezar a sonar, Stop al cortarse. Opcional.")]
    [SerializeField] private ParticleSystem[] sprinklers = Array.Empty<ParticleSystem>();

    [Header("Hooks (modelo / VFX)")]
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onRingStarted;
    [SerializeField] private UnityEvent onRingStopped;

    private DecoyNoiseSource noise;
    private EAlarmState state = EAlarmState.Idle;
    private CancellationTokenSource runCts;

    protected override void Awake()
    {
        base.Awake();
        noise = GetComponent<DecoyNoiseSource>();

        if (data == null)
            Debug.LogError($"[{nameof(FireAlarmDecoy)}] '{name}' has no SO_FireAlarmDecoy assigned.", this);
    }

    public override string GetPromptText() => data != null ? data.InteractText : "Activar alarma";

    protected override bool CanInteractInCloseRange() => data != null && state == EAlarmState.Idle;

    public override bool IsRepeatable() => false;

    public override bool IsFinished() => state != EAlarmState.Idle;

    protected override void OnInteract()
    {
        state = EAlarmState.Arming;

        if (AudioManager.Exists && !string.IsNullOrEmpty(data.PressSoundId))
            AudioManager.Instance.PlaySFX(data.PressSoundId, transform.position);

        onPressed?.Invoke();
        InteractionEvents.RequestPromptRefresh();

        runCts?.Dispose();
        runCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunAsync(runCts.Token).Forget();
    }

    /// <summary>Scaled time on both waits: the pause menu freezes the countdown and the ring.</summary>
    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        if (await UniTask.Delay(TimeSpan.FromSeconds(data.ArmDelay), cancellationToken: token)
                         .SuppressCancellationThrow())
            return;

        state = EAlarmState.Ringing;
        try
        {
            if (AudioManager.Exists && ringSource != null && !string.IsNullOrEmpty(data.RingLoopSoundId))
                AudioManager.Instance.PlayLoop(data.RingLoopSoundId, ringSource);

            for (int i = 0; i < sprinklers.Length; i++)
                if (sprinklers[i] != null) sprinklers[i].Play();

            noise.StartEmitting(0f, audibleEverywhere: true);
            onRingStarted?.Invoke();

            await UniTask.Delay(TimeSpan.FromSeconds(data.RingDuration), cancellationToken: token);
        }
        finally
        {
            // Also on destroy mid-ring: the registry is static, and a ring left in it would be a
            // noise the Nemesis hears from everywhere, forever.
            state = EAlarmState.Spent;
            if (noise != null) noise.StopEmitting();
            if (ringSource != null) ringSource.Stop();

            for (int i = 0; i < sprinklers.Length; i++)
                if (sprinklers[i] != null) sprinklers[i].Stop();

            if (!token.IsCancellationRequested)
            {
                if (AudioManager.Exists && !string.IsNullOrEmpty(data.StopSoundId))
                    AudioManager.Instance.PlaySFX(data.StopSoundId, transform.position);
                onRingStopped?.Invoke();
            }
        }
    }

    private void OnDestroy()
    {
        runCts?.Cancel();
        runCts?.Dispose();
        runCts = null;
    }
}
