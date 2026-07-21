using UnityEngine;

/// <summary>
/// Asset opcional para ajustar la estética global de las animaciones de UI desde un solo lugar.
///
/// Uso: crear el asset (Assets ▸ Create ▸ WIRED ▸ UI ▸ Animation Settings) y asignarlo al campo
/// "settings" de cualquier componente de animación. Si está asignado, sus valores PISAN a los
/// defaults locales del componente en Awake. Si se deja vacío, el componente usa sus propios
/// [SerializeField] (que ya vienen inicializados desde <see cref="UITweenDefaults"/>).
///
/// Sirve para hacer un pase de tuning global RE sin editar cada botón/panel a mano.
/// </summary>
[CreateAssetMenu(fileName = "UIAnimationSettings", menuName = "WIRED/UI/Animation Settings")]
public class UIAnimationSettingsSO : ScriptableObject
{
    [Header("Hover de botones (sweep)")]
    public float hoverInDuration  = UITweenDefaults.HoverInDuration;
    public float hoverOutDuration = UITweenDefaults.HoverOutDuration;
    public LeanTweenType hoverInEase  = UITweenDefaults.HoverInEase;
    public LeanTweenType hoverOutEase = UITweenDefaults.HoverOutEase;

    [Header("Panel tipo pestaña (inventario)")]
    public float panelOpenDuration  = UITweenDefaults.PanelOpenDuration;
    public float panelCloseDuration = UITweenDefaults.PanelCloseDuration;
    public float panelFadeDuration  = UITweenDefaults.PanelFadeDuration;
    public LeanTweenType panelOpenEase  = UITweenDefaults.PanelOpenEase;
    public LeanTweenType panelCloseEase = UITweenDefaults.PanelCloseEase;

    [Header("Slide genérico (prompts / notificaciones)")]
    public float slideInDuration  = UITweenDefaults.SlideInDuration;
    public float slideOutDuration = UITweenDefaults.SlideOutDuration;
    public LeanTweenType slideSeriousEase  = UITweenDefaults.SlideSeriousEase;
    public LeanTweenType slideFeedbackEase = UITweenDefaults.SlideFeedbackEase;
    public float feedbackOvershoot = UITweenDefaults.FeedbackOvershoot;
}
