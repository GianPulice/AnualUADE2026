using TMPro;
using UnityEngine;

/// <summary>
/// The Architect's words at the bottom centre of the screen while a line plays
/// (spec §1.3: monospace, #AAAAAA, no background, fade in 0.2s, fade out 0.5s). On top of the spec
/// the text types itself out (<see cref="TMPTypewriterReveal"/>); switch <c>useTypewriter</c> off to
/// get the plain fade the spec describes.
///
/// Hidden while a menu is open, like the rest of the HUD — except for the lines that belong to
/// those moments (ARC_03, ARC_10), which stay visible over the cinematic and the result screen.
///
/// A line whose bank text has page separators (<see cref="ArchitectLinePages"/>) is shown one page
/// at a time: each page replaces the previous one and types itself out again.
///
/// SETUP (the Architect setup tool builds it): a root with a CanvasGroup for the menu gate, and a
/// child with its own CanvasGroup for the fade plus the TMP text.
/// </summary>
public class ArchitectSubtitleView : MonoBehaviour
{
    [SerializeField] private CanvasGroup visibilityGroup;
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField] private TMP_Text label;

    [Tooltip("Optional. Types the line out instead of showing it all at once.")]
    [SerializeField] private TMPTypewriterReveal typewriter;
    [SerializeField] private bool useTypewriter = true;

    [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;

    private bool currentPlaysOverMenus;

    private string[] pages;
    private float[] pageStarts;
    private int pageIndex;
    private float lineStartTime;

    private void Awake()
    {
        if (fadeGroup != null) fadeGroup.alpha = 0f;
        if (label != null) label.text = string.Empty;

        ArchitectEvents.OnLineStarted += HandleLineStarted;
        ArchitectEvents.OnLineEnded += HandleLineEnded;
        UIStateManager.OnModalPushed += HandleModalChanged;
        UIStateManager.OnModalPopped += HandleModalChanged;

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        ArchitectEvents.OnLineStarted -= HandleLineStarted;
        ArchitectEvents.OnLineEnded -= HandleLineEnded;
        UIStateManager.OnModalPushed -= HandleModalChanged;
        UIStateManager.OnModalPopped -= HandleModalChanged;

        if (fadeGroup != null) LeanTween.cancel(fadeGroup.gameObject);
    }

    private void HandleLineStarted(ArchitectLinePlayback playback)
    {
        if (label == null || fadeGroup == null) return;

        currentPlaysOverMenus = playback.PlaysOverMenus;
        RefreshVisibility();

        pages = ArchitectLinePages.Split(playback.Text);
        pageStarts = ArchitectLinePages.StartTimes(playback, pages);
        lineStartTime = Time.unscaledTime;
        ShowPage(0);

        FadeTo(1f, fadeInDuration);
    }

    private void Update()
    {
        if (pages == null) return;

        // Unscaled, same clock the controller times the line with.
        float elapsed = Time.unscaledTime - lineStartTime;
        while (pageIndex + 1 < pages.Length && elapsed >= pageStarts[pageIndex + 1])
            ShowPage(pageIndex + 1);
    }

    private void ShowPage(int index)
    {
        pageIndex = index;
        label.text = pages[index];
        if (useTypewriter && typewriter != null) typewriter.Play();
        else label.maxVisibleCharacters = int.MaxValue;
    }

    private void HandleLineEnded(bool interrupted)
    {
        pages = null;
        if (fadeGroup == null) return;

        // Cut short (ARC_10 taking over): clear fast so the two lines do not overlap on screen.
        FadeTo(0f, interrupted ? 0.08f : fadeOutDuration);
    }

    private void HandleModalChanged(IModalUI _) => RefreshVisibility();

    private void RefreshVisibility()
    {
        if (visibilityGroup == null) return;

        bool menuOpen = UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen;
        visibilityGroup.alpha = !menuOpen || currentPlaysOverMenus ? 1f : 0f;
    }

    private void FadeTo(float alpha, float duration)
    {
        LeanTween.cancel(fadeGroup.gameObject);

        if (duration <= 0f)
        {
            fadeGroup.alpha = alpha;
            return;
        }

        LeanTween.alphaCanvas(fadeGroup, alpha, duration).setIgnoreTimeScale(true);
    }
}
