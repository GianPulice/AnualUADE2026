using UnityEngine;

/// <summary>
/// Opens this RectTransform sideways from its centre every time it is activated: localScale.x runs
/// from 0 to its authored value, so a highlight appears from the middle of whatever it sits on.
///
/// Built for the Settings tabs' active highlight. SettingsTabSelector shows it with SetActive, so
/// hooking activation plays it on every tab switch, and when Settings opens, without the selector
/// knowing about the animation. Same pattern as <see cref="UISignalOnEnable"/>.
///
/// Scale acts around the pivot: keep the pivot's X at 0.5, or the growth starts from that side.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI Animations/UI Grow From Center On Enable")]
public class UIGrowFromCenterOnEnable : MonoBehaviour
{
    [SerializeField] private float duration = UITweenDefaults.HoverInDuration;
    [SerializeField] private LeanTweenType ease = UITweenDefaults.HoverInEase;
    [Tooltip("Tick if this lives in a menu that runs with Time.timeScale = 0 (pause/settings).")]
    [SerializeField] private bool ignoreTimeScale = true;

    private float restScaleX; // authored scale, captured in Awake

    private void Awake() => restScaleX = transform.localScale.x;

    private void OnEnable()
    {
        LeanTween.cancel(gameObject);
        SetScaleX(0f); // before the first frame renders, or the full highlight flashes once

        LeanTween.value(gameObject, 0f, restScaleX, duration)
            .setOnUpdate(SetScaleX)
            .setEase(ease)
            .setIgnoreTimeScale(ignoreTimeScale);
    }

    private void OnDisable()
    {
        LeanTween.cancel(gameObject);
        SetScaleX(restScaleX); // switched off mid-growth: leave it at rest for the next activation
    }

    private void SetScaleX(float x)
    {
        Vector3 scale = transform.localScale;
        scale.x = x;
        transform.localScale = scale;
    }
}
