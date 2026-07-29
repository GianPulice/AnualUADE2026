/// <summary>
/// Single source of truth for the default values of the UI animations.
///
/// "Resident Evil" look (RE2 / RE4 Remake): rigid, short and "clicky" movements,
/// with no elastic bounce. Anticipation (overshoot) is reserved for quick feedback only.
///
/// These consts are used as the initial value of each animation component's [SerializeField],
/// so any new button/panel/prompt starts out with the RE feel without touching numbers.
/// For global runtime overrides, see <see cref="UIAnimationSettingsSO"/>.
///
/// When adjusting the global look, change it here and (if used) in the SO asset.
/// </summary>
public static class UITweenDefaults
{
    // ── Durations (seconds) ──────────────────────────────────────────────────
    // Anything under ~0.25s reads as immediate and mechanical, not rubbery.

    public const float HoverInDuration  = 0.18f;
    public const float HoverOutDuration = 0.14f;

    public const float PanelOpenDuration  = 0.22f;
    public const float PanelCloseDuration = 0.18f;
    public const float PanelFadeDuration  = 0.14f; // content fade, shorter than the growth

    public const float SlideInDuration  = 0.20f;
    public const float SlideOutDuration = 0.16f;

    public const float DefaultVisibleDuration = 2.5f; // notification auto-hide

    // ── Eases ─────────────────────────────────────────────────────────────────
    // "In"  with easeOut* -> decelerates on arrival = settles firmly.
    // "Out" with easeIn*  -> accelerates on exit    = leaves sharply.

    public const LeanTweenType HoverInEase  = LeanTweenType.easeOutCubic;
    public const LeanTweenType HoverOutEase = LeanTweenType.easeInCubic;

    public const LeanTweenType PanelOpenEase  = LeanTweenType.easeOutQuad;
    public const LeanTweenType PanelCloseEase = LeanTweenType.easeInQuad;

    // "Serious" (persistent) prompts: no overshoot.
    public const LeanTweenType SlideSeriousEase  = LeanTweenType.easeOutQuad;
    // Quick feedback (item notifications): slight mechanical overshoot.
    public const LeanTweenType SlideFeedbackEase = LeanTweenType.easeOutBack;

    // Low overshoot: minimal anticipation, no elastic bounce.
    public const float FeedbackOvershoot = 1.08f;
}
