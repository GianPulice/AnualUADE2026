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
///              window with the connection step and the progress bar, and the signal static the
///              other screens have. Hidden
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

    [Header("Connection steps")]
    [Tooltip("Optional. Shows the step the connection to the player's camera is on.")]
    [SerializeField] private TMP_Text stepLabel;

    [Tooltip("The steps of the connection, in order. Each one ends at its fraction of the loading " +
             "time; the last one always ends at 1. Empty = a plain linear bar.")]
    [SerializeField] private ConnectionStep[] connectionSteps =
    {
        new ConnectionStep { text = "Locating bodycam...",    endsAt = 0.2f  },
        new ConnectionStep { text = "Handshake...",           endsAt = 0.45f },
        new ConnectionStep { text = "Authenticating...",      endsAt = 0.7f  },
        new ConnectionStep { text = "Syncing video feed...",  endsAt = 1f    },
    };

    [Tooltip("Share of each step's time the bar spends filling it; the rest it holds, as if " +
             "waiting for the other end to answer.")]
    [SerializeField, Range(0.1f, 1f)] private float stepFillShare = 0.6f;

    [Tooltip("Step text once the load is done.")]
    [SerializeField] private string connectedText = "Link established.";

    [Tooltip("Seconds to fade to black, and to fade back out of it.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.5f;

    [Header("Exit mode (quitting the game)")]
    [Tooltip("Title of the status window. Reads loadingTitle on scene changes, exitingTitle on quit.")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private string loadingTitle = "CONNECTING...";
    [SerializeField] private string exitingTitle = "EXITING...";

    [Tooltip("Shown on scene changes only — the Mystify screensaver.")]
    [SerializeField] private GameObject[] loadingOnly;

    [Tooltip("Shown when quitting only — the Starfield screensaver.")]
    [SerializeField] private GameObject[] exitOnly;

    [Tooltip("Collapsed by the CRT power-off at the very end of a quit: squashed into a line, then " +
             "a dot. Empty = no power-off.")]
    [SerializeField] private RectTransform powerOffTarget;

    [Tooltip("Optional white overlay on top of the content: the picture flares white as it collapses, " +
             "like a tube switching off.")]
    [SerializeField] private Image powerOffFlash;

    [Tooltip("Seconds of the CRT power-off at the end of a quit.")]
    [SerializeField, Min(0f)] private float powerOffDuration = 0.45f;

    /// <summary>Seconds <see cref="PowerOffAsync"/> takes. 0 when there is nothing to collapse.</summary>
    public float PowerOffDuration => powerOffTarget != null ? powerOffDuration : 0f;

    /// <summary>Most a single frame may advance a fade, in seconds (a 30 fps frame).</summary>
    private const float MaxFadeStep = 1f / 30f;

    /// <summary>
    /// Most the bar shows before the load is done. A connection hanging at 99 % is believable; a
    /// full bar that does not go away is not.
    /// </summary>
    private const float AlmostFull = 0.99f;

    /// <summary><see cref="shownStep"/> once the load is done: <see cref="connectedText"/>.</summary>
    private const int ConnectedStep = -1;

    [System.Serializable]
    private struct ConnectionStep
    {
        public string text;
        [Range(0f, 1f)] public float endsAt;
    }

    private int shownPercent = -1;
    private int shownStep = int.MinValue;

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
        SetExitMode(false);
    }

    /// <summary>
    /// Switches between the scene-change look (CONNECTING, Mystify) and the quit look (EXITING,
    /// Starfield). Call before the content is shown: the screensavers roll their pattern on enable.
    /// </summary>
    public void SetExitMode(bool exiting)
    {
        if (titleLabel != null) titleLabel.text = exiting ? exitingTitle : loadingTitle;
        SetActive(loadingOnly, !exiting);
        SetActive(exitOnly, exiting);
        ResetPowerOff();
    }

    /// <summary>
    /// The tube switching off: the picture flares white while it squashes into a horizontal line,
    /// the line shrinks to a dot, and the dot fades. The last thing on screen before the game closes.
    /// Unscaled, capped like the fades.
    /// </summary>
    public async UniTask PowerOffAsync(CancellationToken token)
    {
        if (powerOffTarget == null || powerOffDuration <= 0f) return;

        float squash = powerOffDuration * 0.4f;
        float shrink = powerOffDuration * 0.35f;
        float fade = powerOffDuration - squash - shrink;

        await Animate(squash, token, t =>
        {
            float eased = t * t;
            powerOffTarget.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.006f, eased), 1f);
            SetFlash(t);
        });

        await Animate(shrink, token, t =>
        {
            float eased = 1f - (1f - t) * (1f - t);
            powerOffTarget.localScale = new Vector3(Mathf.Lerp(1f, 0.004f, eased), 0.006f, 1f);
        });

        await Animate(fade, token, t => SetFlash(1f - t));

        powerOffTarget.localScale = Vector3.zero;
    }

    private async UniTask Animate(float duration, CancellationToken token, System.Action<float> step)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            step(elapsed / duration);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);
        }
        step(1f);
    }

    private void ResetPowerOff()
    {
        if (powerOffTarget != null) powerOffTarget.localScale = Vector3.one;
        SetFlash(0f);
    }

    private void SetFlash(float alpha)
    {
        if (powerOffFlash == null) return;
        Color c = powerOffFlash.color;
        c.a = alpha;
        powerOffFlash.color = c;
        powerOffFlash.enabled = alpha > 0f;
    }

    private static void SetActive(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        foreach (GameObject go in objects)
        {
            if (go != null) go.SetActive(active);
        }
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

    /// <param name="progress">
    /// 0 to 1, linear in time. Shown through the connection steps; only exactly 1 reads as done.
    /// </param>
    public void SetProgress(float progress)
    {
        progress = Mathf.Clamp01(progress);
        bool done = progress >= 1f;

        int step = ConnectedStep;
        float bar = done ? 1f : Mathf.Min(StepBar(progress, out step), AlmostFull);
        SetStep(step);

        if (progressSlider != null) progressSlider.value = bar;
        if (progressLabel == null) return;

        // Rewritten only when the number changes: this runs every frame of the load.
        int percent = Mathf.FloorToInt(bar * 100f);
        if (percent == shownPercent) return;
        shownPercent = percent;
        progressLabel.text = percent + "%";
    }

    /// <summary>
    /// The bar for <paramref name="t"/>: each step fills its stretch of the bar quickly, eased out,
    /// over the first <see cref="stepFillShare"/> of its time, then holds until the next one starts.
    /// A bar that moves in spurts and waits reads as a connection; a perfectly even one makes any
    /// real stall stand out.
    /// </summary>
    private float StepBar(float t, out int step)
    {
        step = 0;
        if (connectionSteps == null || connectionSteps.Length == 0) return t;

        float start = 0f;
        for (int i = 0; i < connectionSteps.Length; i++)
        {
            bool last = i == connectionSteps.Length - 1;
            float end = last ? 1f : Mathf.Clamp(connectionSteps[i].endsAt, start, 1f);
            if (!last && t >= end)
            {
                start = end;
                continue;
            }

            step = i;
            float span = end - start;
            if (span <= 0f) return end;

            float fill = Mathf.Clamp01((t - start) / (span * stepFillShare));
            return Mathf.Lerp(start, end, 1f - (1f - fill) * (1f - fill));
        }

        return t;
    }

    private void SetStep(int step)
    {
        if (stepLabel == null || step == shownStep) return;
        shownStep = step;

        if (step == ConnectedStep) stepLabel.text = connectedText;
        else if (connectionSteps != null && step < connectionSteps.Length) stepLabel.text = connectionSteps[step].text;
        else stepLabel.text = string.Empty;
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
