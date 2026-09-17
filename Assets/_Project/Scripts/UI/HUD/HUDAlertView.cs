using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// General alert at the top centre of the screen: a short system line that drops in from the top,
/// types itself out, holds and retracts. Raised through <see cref="HUDMessageEvents.ShowAlert"/>.
///
/// Queued like <see cref="ItemPickupToastView"/>: one at a time, driven off the slide's own
/// OnHidden, and a burst keeps only the newest few.
///
/// SETUP (the Architect setup tool builds it): this view on a root with a ModalVisibilityGate, and a
/// child panel with the UISlideTransition (Start Hidden, From Top) holding the label.
/// </summary>
public class HUDAlertView : MonoBehaviour
{
    [SerializeField] private UISlideTransition slide;
    [SerializeField] private TMP_Text label;

    [Tooltip("Optional. Types the alert out as it drops in.")]
    [SerializeField] private TMPTypewriterReveal typewriter;

    [SerializeField, Min(1)] private int maxQueued = 3;

    private readonly Queue<(string text, float seconds)> pending = new Queue<(string, float)>();
    private bool isShowing;

    private void Awake()
    {
        HUDMessageEvents.OnAlert += HandleAlert;
        if (slide != null) slide.OnHidden += ShowNext;
    }

    private void OnDestroy()
    {
        HUDMessageEvents.OnAlert -= HandleAlert;
        if (slide != null) slide.OnHidden -= ShowNext;
        LeanTween.cancel(gameObject);
    }

    private void HandleAlert(string text, float seconds)
    {
        if (slide == null || label == null) return;

        pending.Enqueue((text, seconds));
        while (pending.Count > maxQueued) pending.Dequeue();

        if (!isShowing) ShowNext();
    }

    private void ShowNext()
    {
        if (pending.Count == 0)
        {
            isShowing = false;
            return;
        }

        isShowing = true;
        (string text, float seconds) = pending.Dequeue();

        label.text = text;
        slide.SlideIn(SlideDirection.FromTop);
        if (typewriter != null) typewriter.Play();

        // Hosted on this object, not the panel: the slide cancels every tween on its own object
        // when it moves, which would kill the hide timer.
        LeanTween.cancel(gameObject);
        LeanTween.delayedCall(gameObject, seconds, () => slide.SlideOut(SlideDirection.FromTop))
                 .setIgnoreTimeScale(true);
    }
}
