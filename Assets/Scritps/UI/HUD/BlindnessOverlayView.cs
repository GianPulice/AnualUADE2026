using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// Panel negro de ceguera (Módulo M3 o cualquier módulo con causesBlindness=true).
// Permanece activo en escena: usa CanvasGroup.alpha, nunca SetActive.
// Los ojos del Nemesis se renderizan sobre este panel (layer NemesisEyes, ver TA A6).
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

        InventoryEvents.OnBlindnessTriggered += HandleBlindness;
    }

    private void OnDestroy()
    {
        InventoryEvents.OnBlindnessTriggered -= HandleBlindness;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void HandleBlindness(float duration)
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
