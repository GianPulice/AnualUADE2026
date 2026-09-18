using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The black screen that covers a scene change, and whatever loading visuals sit on top of it.
///
/// Only visuals — the sequencing (when to fade, how long to hold) is <see cref="ScreenManager"/>'s.
/// Instantiated by ScreenManager as a child of itself, so it lives in DontDestroyOnLoad and cannot
/// be unloaded along with the scenes it is covering.
///
/// Layout of the prefab:
///   root     — Canvas on top of everything + the CanvasGroup faded here. Its child "Black" is the
///              full-screen black image. Shown through the CRT tube (CanvasCRTPresenter) like every
///              other screen.
///   content  — the loading visuals: the Mystify screensaver (MystifyScreensaver), a Win95 status
///              window with the progress bar, and the signal static the other screens have. Hidden
///              while fading to black, shown while loading, and faded out together with the black on
///              the way back. Nothing here depends on what it contains.
/// </summary>
[RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
public class LoadingScreenView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Canvas canvas;

    [Tooltip("The loading visuals shown on top of the black while loading.")]
    [SerializeField] private GameObject content;

    [Tooltip("Optional. Fills from 0 to 1 over the loading time.")]
    [SerializeField] private Slider progressSlider;

    [Tooltip("Optional. Shows the same progress as a percentage.")]
    [SerializeField] private TMP_Text progressLabel;

    [Tooltip("Seconds to fade to black, and to fade back out of it.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.5f;

    /// <summary>Most a single frame may advance a fade, in seconds (a 30 fps frame).</summary>
    private const float MaxFadeStep = 1f / 30f;

    private int shownPercent = -1;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponent<Canvas>();
    }

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvas == null) canvas = GetComponent<Canvas>();
        HideImmediate();
    }

    /// <summary>Fully transparent, not rendering and not blocking input.</summary>
    public void HideImmediate()
    {
        SetAlpha(0f);
        SetContentVisible(false);
    }

    /// <summary>
    /// Fades the black in over the scene. Blocks input from the first frame, so nothing under it
    /// can be clicked while it is still half transparent.
    /// </summary>
    public UniTask FadeToBlackAsync(CancellationToken token)
    {
        SetContentVisible(false);
        return FadeAsync(1f, token);
    }

    /// <summary>Fades the black and the loading visuals out, back to the scene underneath.</summary>
    public async UniTask FadeFromBlackAsync(CancellationToken token)
    {
        await FadeAsync(0f, token);
        SetContentVisible(false);
    }

    public void SetContentVisible(bool visible)
    {
        if (content != null) content.SetActive(visible);
    }

    /// <param name="progress">0 to 1.</param>
    public void SetProgress(float progress)
    {
        progress = Mathf.Clamp01(progress);
        if (progressSlider != null) progressSlider.value = progress;
        if (progressLabel == null) return;

        // Rewritten only when the number changes: this runs every frame of the load.
        int percent = Mathf.FloorToInt(progress * 100f);
        if (percent == shownPercent) return;
        shownPercent = percent;
        progressLabel.text = percent + "%";
    }

    private async UniTask FadeAsync(float target, CancellationToken token)
    {
        float start = canvasGroup.alpha;
        float elapsed = 0f;

        // Rendering and blocking from the first frame of either fade: fading in must stop clicks
        // straight away, and fading out still has to draw what is left of the black.
        canvas.enabled = true;
        canvasGroup.blocksRaycasts = true;

        while (elapsed < fadeDuration)
        {
            // Unscaled: the pause and result screens leave Time.timeScale at 0 until the very
            // moment they hand over to a scene change.
            //
            // Capped, because a scene change is exactly when frames hitch: the first frame after
            // a heavy load can report a delta of half a second or more, and uncapped that single
            // frame would finish the whole fade — the reveal would just pop in.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);
            canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / fadeDuration);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        SetAlpha(target);
    }

    private void SetAlpha(float alpha)
    {
        canvasGroup.alpha = alpha;

        bool visible = alpha > 0f;
        canvas.enabled = visible;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = false;
    }
}
