using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Desde qué borde entra / hacia qué borde sale un elemento de UI.</summary>
public enum SlideDirection { FromLeft, FromRight, FromTop, FromBottom }

/// <summary>
/// Transición de slide genérica y reutilizable para prompts de interacción, notificaciones de
/// ítem recogido, subtítulos, tooltips, etc. Un mismo componente sirve para distintos casos
/// según la <see cref="SlideDirection"/> que se le pase.
///
/// API:
///   void SlideIn(SlideDirection direction);
///   void SlideOut(SlideDirection direction);
///
/// Estética RE:
///   - Elementos "serios"/persistentes (prompt "Presioná E para..."): easeOutQuad, sin overshoot.
///   - Feedback rápido (notificación de ítem): easeOutBack con overshoot bajo (feedbackStyle = true).
///
/// El elemento se coloca en el editor EN SU POSICIÓN VISIBLE final; Awake la captura como reposo.
/// La posición oculta se calcula por dirección: por defecto se auto-computa desde el tamaño del
/// RectTransform para que salga completamente de cuadro (o fijar slideDistance a un valor > 0).
///
/// Input-agnóstico: se maneja por métodos públicos, no lee Input directamente.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI Animations/UI Slide Transition")]
public class UISlideTransition : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("CanvasGroup opcional para acompañar el slide con fade. Si es null, solo se mueve.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Distancia oculta")]
    [Tooltip("Distancia en px hacia fuera de cuadro. Si es <= 0, se auto-calcula desde el tamaño del RectTransform.")]
    [SerializeField] private float slideDistance = -1f;

    [Header("Estilo")]
    [Tooltip("true = feedback rápido con overshoot (notificaciones). false = serio sin overshoot (prompts).")]
    [SerializeField] private bool feedbackStyle = false;

    [Header("Timing / Ease")]
    [SerializeField] private float inDuration  = UITweenDefaults.SlideInDuration;
    [SerializeField] private float outDuration = UITweenDefaults.SlideOutDuration;
    [Tooltip("Ease de entrada para modo serio (sin overshoot).")]
    [SerializeField] private LeanTweenType seriousInEase = UITweenDefaults.SlideSeriousEase;
    [Tooltip("Ease de entrada para modo feedback (con overshoot).")]
    [SerializeField] private LeanTweenType feedbackInEase = UITweenDefaults.SlideFeedbackEase;
    [SerializeField] private float feedbackOvershoot = UITweenDefaults.FeedbackOvershoot;
    [Tooltip("Ease de salida (retracción). Seco, sin overshoot.")]
    [SerializeField] private LeanTweenType outEase = LeanTweenType.easeInQuad;

    [Header("Auto-hide")]
    [Tooltip("Si está activo, tras SlideIn se retrae solo luego de 'visibleDuration' segundos.")]
    [SerializeField] private bool autoHide = false;
    [SerializeField] private float visibleDuration = UITweenDefaults.DefaultVisibleDuration;

    [Header("Opciones")]
    [Tooltip("Ocultar el elemento en Awake (fuera de cuadro + alpha 0) para que arranque invisible.")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("Dirección usada solo para el estado oculto INICIAL (antes del primer SlideIn).")]
    [SerializeField] private SlideDirection initialHiddenDirection = SlideDirection.FromBottom;
    [Tooltip("Marcar si vive en un menú/HUD que puede correr con Time.timeScale = 0.")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Acompañar el slide con fade del CanvasGroup (requiere canvasGroup asignado).")]
    [SerializeField] private bool fadeWithSlide = true;
    [Tooltip("Desactivar el GameObject al terminar SlideOut.")]
    [SerializeField] private bool deactivateOnHidden = false;
    [Tooltip("Override global opcional. Si se asigna, pisa duraciones/eases locales en Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    [Header("Eventos")]
    public UnityEvent onShown;
    public UnityEvent onHidden;
    public event Action OnShown;
    public event Action OnHidden;

    private RectTransform rect;
    private Vector2 shownPos;              // posición visible autoral, capturada en Awake
    private SlideDirection lastDirection;  // para que el auto-hide/SlideOut sin dirección retracte por donde entró

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        ApplySettings();
        shownPos = rect.anchoredPosition; // el elemento se autora en su posición VISIBLE de reposo
        lastDirection = initialHiddenDirection;

        if (startHidden)
        {
            rect.anchoredPosition = HiddenPositionFor(initialHiddenDirection);
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        inDuration        = settings.slideInDuration;
        outDuration       = settings.slideOutDuration;
        seriousInEase     = settings.slideSeriousEase;
        feedbackInEase    = settings.slideFeedbackEase;
        feedbackOvershoot = settings.feedbackOvershoot;
    }

    // ── API pública ─────────────────────────────────────────────────────────────

    /// <summary>Entra deslizándose desde el borde indicado hacia su posición de reposo.</summary>
    public void SlideIn(SlideDirection direction)
    {
        gameObject.SetActive(true);
        KillTweens();

        lastDirection = direction;

        // Colocar en la posición oculta correspondiente y (opcional) partir de alpha 0.
        rect.anchoredPosition = HiddenPositionFor(direction);
        if (fadeWithSlide && canvasGroup != null) canvasGroup.alpha = 0f;

        LeanTweenType ease = feedbackStyle ? feedbackInEase : seriousInEase;

        LTDescr move = LeanTween.value(rect.gameObject, rect.anchoredPosition, shownPos, inDuration)
            .setOnUpdate((Vector2 p) => rect.anchoredPosition = p)
            .setEase(ease)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleShown);

        // Overshoot solo tiene efecto real con eases *Back.
        if (feedbackStyle) move.setOvershoot(feedbackOvershoot);

        if (fadeWithSlide && canvasGroup != null)
            LeanTween.alphaCanvas(canvasGroup, 1f, inDuration).setIgnoreTimeScale(ignoreTimeScale);

        if (autoHide)
            LeanTween.delayedCall(rect.gameObject, inDuration + visibleDuration, () => SlideOut(direction))
                .setIgnoreTimeScale(ignoreTimeScale);
    }

    /// <summary>Sale deslizándose hacia el borde indicado. Cancela cualquier auto-hide pendiente.</summary>
    public void SlideOut(SlideDirection direction)
    {
        if (rect == null) return;
        KillTweens();

        lastDirection = direction;
        Vector2 target = HiddenPositionFor(direction);

        LeanTween.value(rect.gameObject, rect.anchoredPosition, target, outDuration)
            .setOnUpdate((Vector2 p) => rect.anchoredPosition = p)
            .setEase(outEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleHidden);

        if (fadeWithSlide && canvasGroup != null)
            LeanTween.alphaCanvas(canvasGroup, 0f, outDuration).setIgnoreTimeScale(ignoreTimeScale);
    }

    /// <summary>Retrae por donde entró (útil para cerrar sin recordar la dirección afuera).</summary>
    public void SlideOut() => SlideOut(lastDirection);

    // ── Callbacks ────────────────────────────────────────────────────────────────

    private void HandleShown()
    {
        onShown?.Invoke();
        OnShown?.Invoke();
    }

    private void HandleHidden()
    {
        if (deactivateOnHidden) gameObject.SetActive(false);
        onHidden?.Invoke();
        OnHidden?.Invoke();
    }

    // ── Núcleo ────────────────────────────────────────────────────────────────────

    private Vector2 HiddenPositionFor(SlideDirection direction)
    {
        // Distancia efectiva: auto desde el tamaño del rect si slideDistance <= 0.
        float horizontal = slideDistance > 0f ? slideDistance : rect.rect.width;
        float vertical   = slideDistance > 0f ? slideDistance : rect.rect.height;

        return direction switch
        {
            SlideDirection.FromLeft   => shownPos + Vector2.left  * horizontal,
            SlideDirection.FromRight  => shownPos + Vector2.right * horizontal,
            SlideDirection.FromTop    => shownPos + Vector2.up    * vertical,
            SlideDirection.FromBottom => shownPos + Vector2.down  * vertical,
            _                         => shownPos
        };
    }

    private void KillTweens()
    {
        if (rect != null) LeanTween.cancel(rect.gameObject); // cancela slide + auto-hide (mismo host)
    }

    private void OnDisable() => KillTweens();
    private void OnDestroy() => KillTweens();
}
