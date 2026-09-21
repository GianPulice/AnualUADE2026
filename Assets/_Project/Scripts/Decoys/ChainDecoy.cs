using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hanging chains decoy. Infinite uses: each use rattles them, and for
/// <see cref="SO_ChainDecoy.NoiseDuration"/> seconds a Nemesis within
/// <see cref="SO_ChainDecoy.HearingDistance"/> hears it and goes to investigate. Nothing gets
/// broken.
///
/// The swing is <see cref="onRattled"/> (an Animator trigger, a physics impulse — whatever the
/// model ends up needing). Chains set off by the player walking into them are not here: that
/// would be a trigger volume calling <see cref="Rattle"/>.
/// </summary>
[RequireComponent(typeof(DecoyNoiseSource))]
[AddComponentMenu("WIRED/Decoys/Chain Decoy")]
public class ChainDecoy : BaseRangeInteractable
{
    [SerializeField] private SO_ChainDecoy data;

    [Header("Hooks (modelo / animación)")]
    [SerializeField] private UnityEvent onRattled;

    private DecoyNoiseSource noise;
    private float readyAt;
    private CancellationTokenSource noiseCts;

    protected override void Awake()
    {
        base.Awake();
        noise = GetComponent<DecoyNoiseSource>();

        if (data == null)
            Debug.LogError($"[{nameof(ChainDecoy)}] '{name}' has no SO_ChainDecoy assigned.", this);
    }

    public override string GetInteractText() => data != null ? data.InteractText : "Mover cadenas";

    protected override bool CanInteractInCloseRange() => data != null && Time.time >= readyAt;

    public override bool IsRepeatable() => true;

    protected override void OnInteract() => Rattle();

    /// <summary>Public so something other than the E key (a trigger volume, a thrown object) can
    /// set them off. Ignores the cooldown.</summary>
    public void Rattle()
    {
        if (data == null) return;

        readyAt = Time.time + data.Cooldown;

        if (AudioManager.Exists && !string.IsNullOrEmpty(data.RattleSoundId))
            AudioManager.Instance.PlaySFX(data.RattleSoundId, transform.position);

        onRattled?.Invoke();

        // A rattle during the previous one restarts the window instead of stacking a second
        // timer that would cut the new noise short.
        noiseCts?.Cancel();
        noiseCts?.Dispose();
        noiseCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        NoiseWindowAsync(noiseCts.Token).Forget();
    }

    private async UniTaskVoid NoiseWindowAsync(CancellationToken token)
    {
        noise.StartEmitting(data.HearingDistance, audibleEverywhere: false);

        bool cancelled = await UniTask.Delay(TimeSpan.FromSeconds(data.NoiseDuration), cancellationToken: token)
                                       .SuppressCancellationThrow();

        // Cancelled by a newer rattle: that one owns the emission now. Cancelled by destroy:
        // OnDisable already took it out of the registry.
        if (!cancelled) noise.StopEmitting();
    }

    private void OnDestroy()
    {
        noiseCts?.Cancel();
        noiseCts?.Dispose();
        noiseCts = null;
    }
}
