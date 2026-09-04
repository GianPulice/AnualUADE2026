using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

[RequireComponent(typeof(CanvasGroup))]
public abstract class BaseScreenView : MonoBehaviour
{
    [SerializeField] protected CanvasGroup canvasGroup;
    [SerializeField] protected float fadeDuration = 0.3f;

    private CancellationTokenSource fadeCts;

    public virtual async UniTask ShowAsync()
    {
        gameObject.SetActive(true);

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float time = 0;
        while (time < fadeDuration)
        {
            time += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, time / fadeDuration);
            await UniTask.Yield();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public virtual async UniTask HideAsync()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        // Immediately block inputs so the player can't double-click during the fade
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float time = 0;
        while (time < fadeDuration)
        {
            if (canvasGroup == null) return;
            time += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, time / fadeDuration);
            await UniTask.Yield();
        }
        if(canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    public async UniTask Fade(float targetAlpha, float duration)
    {
        fadeCts?.Cancel();
        fadeCts?.Dispose();
        fadeCts = new CancellationTokenSource();

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // unscaled so UI fades keep running when a modal sets Time.timeScale to 0
            // (previously the InteractionPrompt fade froze mid-transition during Pause).
            elapsed += Time.unscaledDeltaTime;
            float percent = elapsed / duration;

            // Linear interpolation between the current alpha and the target
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, percent);

            // Wait for the next frame (respecting cancellation)
            await UniTask.Yield(PlayerLoopTiming.Update, fadeCts.Token);
        }

        canvasGroup.alpha = targetAlpha;
    }
}
