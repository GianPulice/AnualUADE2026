using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class VignetteProximityView : BaseScreenView
{
    [SerializeField] private NemesisStateManager nemesis;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxAlpha = 0.85f;

    void Awake()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;
    }

    void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        float dist = Vector3.Distance(playerTransform.position, nemesis.SelfTransform.position);
        float t = Mathf.InverseLerp(nemesis.DetectionRange, minDistance, dist);
        canvasGroup.alpha = Mathf.Lerp(0f, maxAlpha, t);
    }
}
