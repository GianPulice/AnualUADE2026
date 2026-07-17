using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Efecto de "sweep" para hover de botones, estilo menú Resident Evil: una imagen hija
/// (barra o ícono de selección) se desliza de izquierda a derecha hacia su posición de
/// reposo al entrar el hover, y vuelve seca al salir.
///
/// Genérico y reutilizable: se agrega a CUALQUIER GameObject de UI (típicamente el mismo que
/// tiene el uGUI Button/Selectable) y solo necesita una referencia a la imagen que barre.
///
/// Responde tanto a mouse (IPointerEnter/Exit) como a navegación por gamepad/teclado
/// (ISelect/IDeselect), por lo que no depende de ningún sistema de Input concreto.
///
/// Setup en Inspector:
///   - sweepTarget: la imagen/barra hija que se desliza. Dejarla posicionada en el editor
///     EN SU POSICIÓN FINAL (de reposo visible); Awake captura esa X como destino y arranca
///     el objeto en la posición oculta.
///   - Si la barra no está dentro de un RectMask2D/Mask, usar useExplicitHiddenX o un
///     hiddenOffsetX grande para que nazca realmente fuera del botón.
/// </summary>
[AddComponentMenu("WIRED/UI Animations/Button Hover Sweep Effect")]
public class ButtonHoverSweepEffect : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("Objetivo")]
    [Tooltip("Imagen hija que hace el sweep (barra/ícono de selección). Requiere RectTransform.")]
    [SerializeField] private RectTransform sweepTarget;

    [Header("Posición oculta (anchoredPosition.x)")]
    [Tooltip("Si está activo, usa 'hiddenX' como valor absoluto. Si no, hidden = posición de reposo + hiddenOffsetX.")]
    [SerializeField] private bool useExplicitHiddenX = false;
    [SerializeField] private float hiddenX = -60f;
    [Tooltip("Offset relativo a la posición de reposo. Negativo = nace a la izquierda y barre hacia la derecha.")]
    [SerializeField] private float hiddenOffsetX = -60f;

    [Header("Timing / Ease")]
    [SerializeField] private float inDuration  = UITweenDefaults.HoverInDuration;
    [SerializeField] private float outDuration = UITweenDefaults.HoverOutDuration;
    [SerializeField] private LeanTweenType inEase  = UITweenDefaults.HoverInEase;
    [SerializeField] private LeanTweenType outEase = UITweenDefaults.HoverOutEase;

    [Header("Opciones")]
    [Tooltip("Marcar si este botón vive en un menú que corre con Time.timeScale = 0 (pausa/inventario).")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Override global opcional. Si se asigna, pisa duraciones/eases locales en Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    private float shownX;         // posición de reposo autoral, capturada en Awake
    private float restingHiddenX; // posición oculta calculada
    private bool initialized;

    private void Awake()
    {
        if (sweepTarget == null)
        {
            Debug.LogWarning($"[ButtonHoverSweepEffect] '{name}': falta asignar sweepTarget.", this);
            enabled = false;
            return;
        }

        ApplySettings();

        shownX = sweepTarget.anchoredPosition.x;
        restingHiddenX = useExplicitHiddenX ? hiddenX : shownX + hiddenOffsetX;

        SetX(restingHiddenX); // arranca oculto
        initialized = true;
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        inDuration  = settings.hoverInDuration;
        outDuration = settings.hoverOutDuration;
        inEase      = settings.hoverInEase;
        outEase     = settings.hoverOutEase;
    }

    // ── Eventos de puntero y de navegación (gamepad/teclado) ──────────────────

    public void OnPointerEnter(PointerEventData eventData) => PlaySweep(show: true);
    public void OnPointerExit(PointerEventData eventData)  => PlaySweep(show: false);
    public void OnSelect(BaseEventData eventData)          => PlaySweep(show: true);
    public void OnDeselect(BaseEventData eventData)        => PlaySweep(show: false);

    // ── Núcleo ────────────────────────────────────────────────────────────────

    private void PlaySweep(bool show)
    {
        if (!initialized) return;

        // Cancela cualquier sweep en curso: hover/unhover rápidos no acumulan tweens.
        LeanTween.cancel(sweepTarget.gameObject);

        float to           = show ? shownX : restingHiddenX;
        float dur          = show ? inDuration : outDuration;
        LeanTweenType ease = show ? inEase : outEase;

        // Tween invertible sobre anchoredPosition.x (mismo tween, distinto destino).
        LeanTween.value(sweepTarget.gameObject, sweepTarget.anchoredPosition.x, to, dur)
            .setOnUpdate(SetX)
            .setEase(ease)
            .setIgnoreTimeScale(ignoreTimeScale);
    }

    private void SetX(float x)
    {
        if (sweepTarget == null) return;
        Vector2 p = sweepTarget.anchoredPosition;
        p.x = x;
        sweepTarget.anchoredPosition = p;
    }

    // ── Limpieza ──────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        if (sweepTarget == null) return;
        LeanTween.cancel(sweepTarget.gameObject);
        if (initialized) SetX(restingHiddenX); // dejar en estado oculto consistente
    }

    private void OnDestroy()
    {
        if (sweepTarget != null) LeanTween.cancel(sweepTarget.gameObject);
    }
}
