using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// Shows the player's inner monologue (<see cref="PlayerThoughtEvents"/>): one line, faded in,
/// held and faded out. A new thought replaces the one on screen instead of queueing: the player
/// only ever thinks about what is in front of them now.
///
/// The look (font, yellow, size) lives on the label in the HUD prefab, not here. Runs on scaled
/// time, so the pause holds it. Its own CanvasGroup: the HUD's ModalVisibilityGate owns the
/// parent's alpha, and two writers of one alpha flicker.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PlayerThoughtView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.2f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.5f;

    private CanvasGroup group;
    private CancellationTokenSource showCts;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        PlayerThoughtEvents.OnThought += HandleThought;
    }

    private void OnDestroy()
    {
        PlayerThoughtEvents.OnThought -= HandleThought;
        Cancel();
    }

    private void HandleThought(string text, float seconds)
    {
        Cancel();
        showCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        ShowAsync(text, seconds, showCts.Token).Forget();
    }

    private void Cancel()
    {
        if (showCts == null) return;
        showCts.Cancel();
        showCts.Dispose();
        showCts = null;
    }

    private async UniTaskVoid ShowAsync(string text, float seconds, CancellationToken token)
    {
        if (label != null) label.text = text;
        try
        {
            await Fade(group.alpha, 1f, fadeInSeconds, token);
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, seconds)), cancellationToken: token);
            await Fade(1f, 0f, fadeOutSeconds, token);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer thought or destroyed: the newer one starts from the current alpha.
        }
    }

    private async UniTask Fade(float from, float to, float seconds, CancellationToken token)
    {
        if (seconds <= 0f) { group.alpha = to; return; }
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        group.alpha = to;
    }
}
