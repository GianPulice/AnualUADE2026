using UnityEngine;

/// <summary>
/// Optional asset for tuning the global look of the UI animations from a single place.
///
/// Usage: create the asset (Assets ▸ Create ▸ WIRED ▸ UI ▸ Animation Settings) and assign it to
/// the "settings" field of any animation component. If assigned, its values OVERRIDE the
/// component's local defaults in Awake. If left empty, the component uses its own
/// [SerializeField] values (which already come initialized from <see cref="UITweenDefaults"/>).
///
/// Useful for doing a global RE tuning pass without editing every button/panel by hand.
/// </summary>
[CreateAssetMenu(fileName = "UIAnimationSettings", menuName = "WIRED/UI/Animation Settings")]
public class UIAnimationSettingsSO : ScriptableObject
{
    [Header("Button hover (sweep)")]
    public float hoverInDuration  = UITweenDefaults.HoverInDuration;
    public float hoverOutDuration = UITweenDefaults.HoverOutDuration;
    public LeanTweenType hoverInEase  = UITweenDefaults.HoverInEase;
    public LeanTweenType hoverOutEase = UITweenDefaults.HoverOutEase;

    [Header("Tab-style panel (inventory)")]
    public float panelOpenDuration  = UITweenDefaults.PanelOpenDuration;
    public float panelCloseDuration = UITweenDefaults.PanelCloseDuration;
    public float panelFadeDuration  = UITweenDefaults.PanelFadeDuration;
    public LeanTweenType panelOpenEase  = UITweenDefaults.PanelOpenEase;
    public LeanTweenType panelCloseEase = UITweenDefaults.PanelCloseEase;

    [Header("Generic slide (prompts / notifications)")]
    public float slideInDuration  = UITweenDefaults.SlideInDuration;
    public float slideOutDuration = UITweenDefaults.SlideOutDuration;
    public LeanTweenType slideSeriousEase  = UITweenDefaults.SlideSeriousEase;
    public LeanTweenType slideFeedbackEase = UITweenDefaults.SlideFeedbackEase;
    public float feedbackOvershoot = UITweenDefaults.FeedbackOvershoot;
}
