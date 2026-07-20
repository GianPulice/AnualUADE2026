using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Animador de apertura/cierre de un panel estilo "pestaña de navegador" (Chrome tab):
/// el panel crece en un eje (ancho por defecto) desde 0 hasta su tamaño final, anclado al
/// punto donde "nace" la pestaña, mientras el contenido interno hace fade-in para no verse
/// deformado durante el crecimiento. Al cerrar, la animación es inversa (fade-out + colapso)
/// y desactiva el GameObject al terminar.
///
/// Genérico y reutilizable: se agrega al RectTransform del panel que crece. Pensado para
/// engancharse desde el Controller/Model de inventario (InventoryManagerUI), pero NO lo
/// conoce: solo expone Open()/Close() + callbacks. Ver nota de integración al final.
///
/// ── Sobre el origen del crecimiento (de dónde "nace" la pestaña) ─────────────────────────
/// El eje se controla con <see cref="growAxis"/>; el borde de anclaje lo da el PIVOT del
/// RectTransform:
///   - pivot.x = 0   → crece hacia la derecha (nace del borde izquierdo)
///   - pivot.x = 1   → crece hacia la izquierda
///   - pivot.x = 0.5 → crece hacia ambos lados
/// (análogo con pivot.y para crecimiento vertical). El pivot es el origen tanto en modo Scale
/// como en modo SizeDelta.
///
/// Dato útil: en un rect con anchors en stretch y sizeDelta (0,0) —como el panel LAYOUT del
/// inventario— cambiar el pivot NO altera el layout (sigue llenando al padre); solo mueve el
/// origen del escalado. Es seguro tocarlo para elegir de dónde nace la pestaña.
///
/// Ver <see cref="GrowMode"/>: con anchors en stretch hay que usar Scale, porque sizeDelta ahí
/// es un offset contra los bordes del padre y no el ancho real.
///
/// ── Adaptar a un "flip" tipo página de libreta (RE2 Remake) ──────────────────────────────
/// En vez de animar sizeDelta, reemplazar ApplyGrow() por una rotación en Y sobre un pivot
/// lateral: LeanTween.value(host, 90f, 0f, dur).setOnUpdate(a => panelRect.localEulerAngles =
/// new Vector3(0, a, 0)) combinado con el fade. El resto de la estructura (reentrancia,
/// ignoreTimeScale, callbacks) se reutiliza igual.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI Animations/Inventory Tab Panel Animator")]
public class InventoryTabPanelAnimator : MonoBehaviour
{
    public enum GrowAxis { Horizontal, Vertical, Both }

    /// <summary>
    /// Cómo crece el panel:
    ///   Scale     — anima localScale. Funciona SIEMPRE, incluso con anchors en stretch.
    ///               El contenido se comprime mientras crece (lo tapa el fade). Default seguro.
    ///   SizeDelta — anima sizeDelta (ancho real). Da el "tab" más fiel (el contenido no se
    ///               deforma), pero EXIGE anchors NO-stretch en el eje que crece.
    /// </summary>
    public enum GrowMode { Scale, SizeDelta }

    [Header("Referencias")]
    [Tooltip("Panel que crece. Si se deja vacío, usa el RectTransform de este GameObject.")]
    [SerializeField] private RectTransform panelRect;
    [Tooltip("CanvasGroup del CONTENIDO interno (no del panel), para fade-in/out mientras crece.")]
    [SerializeField] private CanvasGroup contentGroup;

    [Header("Crecimiento")]
    [Tooltip("Scale = seguro con anchors stretch. SizeDelta = ancho real, requiere anchors no-stretch.")]
    [SerializeField] private GrowMode growMode = GrowMode.Scale;
    [SerializeField] private GrowAxis growAxis = GrowAxis.Horizontal;
    [Tooltip("Escala mínima al estar colapsado (modo Scale). 0 = pestaña totalmente cerrada.")]
    [SerializeField] private float collapsedScale = 0f;
    [Tooltip("Tamaño mínimo del eje al estar colapsado (modo SizeDelta).")]
    [SerializeField] private float collapsedSize = 0f;

    [Header("Timing / Ease")]
    [SerializeField] private float openDuration  = UITweenDefaults.PanelOpenDuration;
    [SerializeField] private float closeDuration = UITweenDefaults.PanelCloseDuration;
    [SerializeField] private float fadeDuration  = UITweenDefaults.PanelFadeDuration;
    [SerializeField] private LeanTweenType openEase  = UITweenDefaults.PanelOpenEase;
    [SerializeField] private LeanTweenType closeEase = UITweenDefaults.PanelCloseEase;

    [Header("Opciones")]
    [Tooltip("El inventario abre con Time.timeScale = 0, así que normalmente debe estar en true.")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Colapsar y ocultar (SetActive false) el panel en Awake para que arranque cerrado.")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("Override global opcional. Si se asigna, pisa duraciones/eases locales en Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    [Header("Eventos (para el Controller: bloquear input, etc.)")]
    public UnityEvent onOpenStarted;
    public UnityEvent onOpened;
    public UnityEvent onCloseStarted;
    public UnityEvent onClosed;

    /// <summary>Callbacks en código para el Controller (además de los UnityEvent del Inspector).</summary>
    public event Action OnOpened;
    public event Action OnClosed;

    /// <summary>true entre el inicio de Open() y el fin de Close() (incluye la animación).</summary>
    public bool IsOpen { get; private set; }
    /// <summary>true mientras una animación de apertura o cierre está corriendo.</summary>
    public bool IsAnimating { get; private set; }

    private Vector2 expandedSize; // sizeDelta final autoral, capturado en Awake (modo SizeDelta)
    private Vector3 baseScale;    // localScale final autoral, capturado en Awake (modo Scale)
    private float growT;          // progreso actual del crecimiento (0 = colapsado, 1 = full)
    private bool initialized;

    private void Awake() => EnsureInitialized();

    /// <summary>
    /// Captura el tamaño final autoral y, si startHidden, deja el panel colapsado (invisible).
    /// Idempotente. NO desactiva el GameObject a propósito: si el panel arranca inactivo en la
    /// escena, Awake corre recién en el SetActive(true) de Open(); desactivar acá cortaría el
    /// tween en pleno vuelo. El panel debe estar autorado a tamaño COMPLETO en el prefab/escena.
    /// </summary>
    private void EnsureInitialized()
    {
        if (initialized) return;
        if (panelRect == null) panelRect = GetComponent<RectTransform>();
        ApplySettings();

        expandedSize = panelRect.sizeDelta;
        baseScale = panelRect.localScale;
        initialized = true;

        WarnIfStretchedWithSizeDelta();

        if (startHidden && !IsOpen)
        {
            growT = 0f;
            ApplyGrow(0f);
            if (contentGroup != null) contentGroup.alpha = 0f;
            SetContentInteractable(false);
        }
        else
        {
            growT = 1f;
        }
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        openDuration  = settings.panelOpenDuration;
        closeDuration = settings.panelCloseDuration;
        fadeDuration  = settings.panelFadeDuration;
        openEase      = settings.panelOpenEase;
        closeEase     = settings.panelCloseEase;
    }

    // ── API pública ─────────────────────────────────────────────────────────────

    /// <summary>Abre la pestaña: crece el ancho desde el estado actual + fade-in del contenido.</summary>
    public void Open()
    {
        gameObject.SetActive(true); // por si venía desactivado (startHidden o cierre previo)
        EnsureInitialized();
        KillTweens();

        IsOpen = true;
        IsAnimating = true;
        SetContentInteractable(false); // input bloqueado durante la animación
        onOpenStarted?.Invoke();

        // Crecimiento desde el progreso actual (reentrancia: si estaba cerrando, arranca de ahí).
        LeanTween.value(panelRect.gameObject, growT, 1f, openDuration)
            .setOnUpdate(ApplyGrow)
            .setEase(openEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleOpenComplete);

        // Fade-in del contenido (más corto: aparece ya con algo de ancho, sin deformarse).
        if (contentGroup != null)
        {
            LeanTween.alphaCanvas(contentGroup, 1f, fadeDuration)
                .setIgnoreTimeScale(ignoreTimeScale);
        }
    }

    /// <summary>Cierra la pestaña: fade-out del contenido + colapso del ancho, luego desactiva.</summary>
    public void Close()
    {
        if (!gameObject.activeSelf) return; // ya cerrado
        KillTweens();

        IsAnimating = true;
        SetContentInteractable(false);
        onCloseStarted?.Invoke();

        // Fade-out del contenido (lidera: el ancho colapsa en paralelo pero el contenido se va antes).
        if (contentGroup != null)
        {
            LeanTween.alphaCanvas(contentGroup, 0f, fadeDuration)
                .setIgnoreTimeScale(ignoreTimeScale);
        }

        // Colapso del ancho desde el progreso actual.
        // NOTA: para "fade-out PRIMERO y después colapsar" (más secuencial), agregar
        //       .setDelay(fadeDuration) a este tween.
        LeanTween.value(panelRect.gameObject, growT, 0f, closeDuration)
            .setOnUpdate(ApplyGrow)
            .setEase(closeEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleCloseComplete);
    }

    // ── Callbacks de completado ──────────────────────────────────────────────────

    private void HandleOpenComplete()
    {
        IsAnimating = false;
        SetContentInteractable(true);
        onOpened?.Invoke();
        OnOpened?.Invoke();
    }

    private void HandleCloseComplete()
    {
        IsAnimating = false;
        IsOpen = false;
        gameObject.SetActive(false);
        onClosed?.Invoke();
        OnClosed?.Invoke();
    }

    // ── Núcleo ────────────────────────────────────────────────────────────────────

    private void ApplyGrow(float t)
    {
        if (panelRect == null) return;
        growT = t;

        bool horizontal = growAxis == GrowAxis.Horizontal || growAxis == GrowAxis.Both;
        bool vertical   = growAxis == GrowAxis.Vertical   || growAxis == GrowAxis.Both;

        if (growMode == GrowMode.Scale)
        {
            Vector3 s = baseScale;
            if (horizontal) s.x = Mathf.Lerp(collapsedScale, baseScale.x, t);
            if (vertical)   s.y = Mathf.Lerp(collapsedScale, baseScale.y, t);
            panelRect.localScale = s;
        }
        else
        {
            Vector2 s = expandedSize;
            if (horizontal) s.x = Mathf.Lerp(collapsedSize, expandedSize.x, t);
            if (vertical)   s.y = Mathf.Lerp(collapsedSize, expandedSize.y, t);
            panelRect.sizeDelta = s;
        }
    }

    /// <summary>
    /// En modo SizeDelta con anchors en stretch, sizeDelta NO es el ancho sino un offset contra
    /// los bordes del padre: el panel quedaría con "tamaño final 0" y la animación no se vería.
    /// Este aviso existe porque es exactamente el caso del panel LAYOUT del inventario.
    /// </summary>
    private void WarnIfStretchedWithSizeDelta()
    {
        if (growMode != GrowMode.SizeDelta) return;

        bool stretchX = !Mathf.Approximately(panelRect.anchorMin.x, panelRect.anchorMax.x);
        bool stretchY = !Mathf.Approximately(panelRect.anchorMin.y, panelRect.anchorMax.y);

        bool conflict = ((growAxis == GrowAxis.Horizontal || growAxis == GrowAxis.Both) && stretchX)
                     || ((growAxis == GrowAxis.Vertical   || growAxis == GrowAxis.Both) && stretchY);

        if (conflict)
        {
            Debug.LogWarning(
                $"[InventoryTabPanelAnimator] '{name}': growMode = SizeDelta pero los anchors están " +
                $"en stretch sobre el eje {growAxis}. sizeDelta no representa el tamaño real y la " +
                $"animación no se va a ver. Usá growMode = Scale, o sacá el stretch de ese eje.", this);
        }
    }

    private void SetContentInteractable(bool value)
    {
        if (contentGroup == null) return;
        contentGroup.interactable = value;
        contentGroup.blocksRaycasts = value;
    }

    private void KillTweens()
    {
        if (panelRect != null) LeanTween.cancel(panelRect.gameObject);
        if (contentGroup != null) LeanTween.cancel(contentGroup.gameObject);
    }

    private void OnDisable() => KillTweens();
    private void OnDestroy() => KillTweens();
}
