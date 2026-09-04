using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// Black blindness panel driven by the head module (M3). It stays active in the scene: it uses
// CanvasGroup.alpha, never SetActive.
// The Nemesis eyes are rendered on top of this panel (NemesisEyes layer, see TA A6).
//
// Today it does a single fade-in/hold/fade-out when the head module explodes. The full spec
// (§3, M3) calls for a periodic BlindnessLoop (every X seconds); when that loop is implemented
// it can drive this overlay by calling PlayBlindness(duration) directly on each tick.
[RequireComponent(typeof(CanvasGroup))]
public class BlindnessOverlayView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [SerializeField] private float fadeInDuration  = 0.2f;
    [SerializeField] private float fadeOutDuration = 0.3f;

    private CancellationTokenSource _cts;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void OnEnable()
    {
        ModuleEvents.OnExploded += HandleModuleExploded;
    }

    private void OnDisable()
    {
        ModuleEvents.OnExploded -= HandleModuleExploded;
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void HandleModuleExploded(ModuleRuntime module)
    {
        if (module == null || module.Data == null) return;
        if (module.Data.Penalty != PenaltyType.Head) return;
        PlayBlindness(module.Data.BlindnessDuration);
    }

    /// <summary>Public so the future BlindnessLoop can trigger episodes without going through events.</summary>
    public void PlayBlindness(float duration)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RunBlindness(duration, _cts.Token).Forget();
    }

    private async UniTaskVoid RunBlindness(float holdDuration, CancellationToken token)
    {
        await FadeAlpha(0f, 1f, fadeInDuration, token);
        if (token.IsCancellationRequested) return;

        await UniTask.Delay(
            System.TimeSpan.FromSeconds(holdDuration),
            DelayType.UnscaledDeltaTime,
            cancellationToken: token);
        if (token.IsCancellationRequested) return;

        await FadeAlpha(1f, 0f, fadeOutDuration, token);
    }

    private async UniTask FadeAlpha(float from, float to, float duration, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (token.IsCancellationRequested) return;
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        canvasGroup.alpha = to;
    }
}
