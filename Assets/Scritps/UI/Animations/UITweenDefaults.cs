/// <summary>
/// Fuente única de verdad para los valores por defecto de las animaciones de UI.
///
/// Estética "Resident Evil" (RE2 / RE4 Remake): movimientos rígidos, cortos y "clicky",
/// sin rebote elástico. La anticipación (overshoot) se reserva solo para feedback rápido.
///
/// Estos consts se usan como valor inicial de los [SerializeField] de cada componente de
/// animación, de modo que cualquier botón/panel/prompt nuevo arranca ya con la sensación RE
/// sin tocar números. Para overrides globales en runtime, ver <see cref="UIAnimationSettingsSO"/>.
///
/// Al ajustar la estética global, cambiar acá y (si se usan) en el asset SO.
/// </summary>
public static class UITweenDefaults
{
    // ── Duraciones (segundos) ────────────────────────────────────────────────
    // Todo por debajo de ~0.25s: se percibe como inmediato y mecánico, no gomoso.

    public const float HoverInDuration  = 0.18f;
    public const float HoverOutDuration = 0.14f;

    public const float PanelOpenDuration  = 0.22f;
    public const float PanelCloseDuration = 0.18f;
    public const float PanelFadeDuration  = 0.14f; // fade del contenido, más corto que el crecimiento

    public const float SlideInDuration  = 0.20f;
    public const float SlideOutDuration = 0.16f;

    public const float DefaultVisibleDuration = 2.5f; // auto-hide de notificaciones

    // ── Eases ─────────────────────────────────────────────────────────────────
    // "In"  con easeOut* → desacelera al llegar = se asienta firme.
    // "Out" con easeIn*  → acelera al salir     = se retira seco.

    public const LeanTweenType HoverInEase  = LeanTweenType.easeOutCubic;
    public const LeanTweenType HoverOutEase = LeanTweenType.easeInCubic;

    public const LeanTweenType PanelOpenEase  = LeanTweenType.easeOutQuad;
    public const LeanTweenType PanelCloseEase = LeanTweenType.easeInQuad;

    // Prompts "serios" (persistentes): sin overshoot.
    public const LeanTweenType SlideSeriousEase  = LeanTweenType.easeOutQuad;
    // Feedback rápido (notificaciones de ítem): leve overshoot mecánico.
    public const LeanTweenType SlideFeedbackEase = LeanTweenType.easeOutBack;

    // Overshoot bajo: anticipación mínima, nada de rebote elástico.
    public const float FeedbackOvershoot = 1.08f;
}
