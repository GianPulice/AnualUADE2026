using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class VignetteChaseView : BaseScreenView
{
    [SerializeField] private float maxAlpha = 0.7f;

    void Awake()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;
    }

    void OnEnable()
    {
        NemesisEvents.OnChaseStarted += HandleChaseStarted;
        NemesisEvents.OnChaseEnded   += HandleChaseEnded;
    }

    void OnDisable()
    {
        NemesisEvents.OnChaseStarted -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded   -= HandleChaseEnded;
    }

    private void HandleChaseStarted() => Fade(maxAlpha, fadeDuration).Forget();
    private void HandleChaseEnded()   => Fade(0f, fadeDuration).Forget();
}
