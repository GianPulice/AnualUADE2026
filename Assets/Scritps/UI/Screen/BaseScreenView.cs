using UnityEngine;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(CanvasGroup))]
public abstract class BaseScreenView : MonoBehaviour
{
    [SerializeField] protected CanvasGroup canvasGroup;
    [SerializeField] protected float fadeDuration = 0.3f;

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
}
