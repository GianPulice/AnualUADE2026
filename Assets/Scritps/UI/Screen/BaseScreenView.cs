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
            time += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, time / fadeDuration);
            await UniTask.Yield();
        }

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
            elapsed += Time.deltaTime;
            float percent = elapsed / duration;

            // Interpolación lineal entre el alpha actual y el objetivo
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, percent);

            // Esperar al siguiente frame (respetando la cancelación)
            await UniTask.Yield(PlayerLoopTiming.Update, fadeCts.Token);
        }

        canvasGroup.alpha = targetAlpha;
    }
}
