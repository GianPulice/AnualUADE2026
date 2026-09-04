using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class VignetteProximityView : BaseScreenView
{
    [SerializeField] private float maxAlpha = 0.85f;

    void Awake()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        NemesisEvents.OnProximityChanged += HandleProximityChanged;
    }

    void OnDestroy() => NemesisEvents.OnProximityChanged -= HandleProximityChanged;

    private void HandleProximityChanged(float t) =>
        canvasGroup.alpha = t * maxAlpha;
}
