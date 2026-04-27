using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW del panel derecho de detalles del ítem seleccionado.
/// 
/// Responsabilidades:
///   - Mostrar estado vacío hasta que se selecciona algo
///   - Poblar encabezado, descripción, metadata y contenido según el ítem
///   - Mostrar doc panel para ítems de tipo Texto (manejado por el Controller via ESC stack)
///   - Exponer el botón de descarte
///   - Notificar el descarte al Controller
///   
/// Notas:
///   - Audio WIP: estructura preparada, lógica desactivada con enableAudioFeatures.
///   - docPanel es una capa separada manejada por ShowDoc/HideDoc.
///     El Controller decide cuándo abrirla/cerrarla (no ShowEmpty ni ShowDetail).
/// </summary>
public class ItemDetailView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    // ── Paneles raíz ──────────────────────────────────────────────────────────

    [Header("Estado vacío (sin selección)")]
    [Tooltip("Selecciona un item / para ver el detalle")]
    [SerializeField] private GameObject emptyStatePanel;
    [Tooltip("Todo el contenido real")]
    [SerializeField] private GameObject detailContentPanel;

    // ── Header ────────────────────────────────────────────────────────────────

    [Header("Header del ítem")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image iconBackground;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI categoryTagText;
    [SerializeField] private Image categoryTagBackground;

    // ── Descripción ───────────────────────────────────────────────────────────

    [Header("Descripción")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Header("Parameters")]
    [SerializeField] private TextMeshProUGUI metallicValueText;
    [SerializeField] private TextMeshProUGUI consumableValueText;
    [SerializeField] private TextMeshProUGUI uniqueValueText;

    // ── Doc panel ─────────────────────────────────────────────────────────────

    [Header("Doc Panel (ítems tipo Text)")]
    [Tooltip("Panel separado que se abre encima del detalle. Cerrable con ESC sin deseleccionar el ítem.")]
    [SerializeField] private GameObject docPanel;
    [SerializeField] private TextMeshProUGUI docPanelText;
    [SerializeField] private ScrollRect docScrollRect;
    [SerializeField] private Button openDocButton;  // "leer documento" dentro del detalle

    // ── Audio WIP ─────────────────────────────────────────────────────────────

    [Header("Debug / WIP")]
    [Tooltip("Activa para habilitar la lógica y UI del reproductor de audio.")]
    [SerializeField] private bool enableAudioFeatures = false;

    [Header("Reproductor de audio (WIP)")]
    [SerializeField] private GameObject audioPlayerBox;
    [SerializeField] private Button playButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Slider audioProgressBar;
    [SerializeField] private TextMeshProUGUI audioTimeText;
    [SerializeField] private AudioSource audioSource;

    // ── Descarte ──────────────────────────────────────────────────────────────

    [Header("Botón de descarte")]
    [SerializeField] private Button discardButton;
    [SerializeField] private TextMeshProUGUI discardButtonText;

    // ── Colores ───────────────────────────────────────────────────────────────

    private static readonly Color MetallicYesColor = new Color(0.53f, 0.13f, 0.13f);
    private static readonly Color MetallicNoColor = new Color(0.40f, 0.40f, 0.40f);

    // ── Estado interno ────────────────────────────────────────────────────────

    private SO_InventoryItem currentItem;

    /// <summary>
    /// El Controller consulta esto para saber si ESC debe cerrar el doc
    /// antes de cerrar el inventario.
    /// </summary>
    public bool IsDocOpen { get; private set; }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        discardButton?.onClick.AddListener(OnDiscardClicked);
        openDocButton?.onClick.AddListener(OnOpenDocClicked);

        if (enableAudioFeatures)
        {
            playButton?.onClick.AddListener(OnPlayClicked);
            stopButton?.onClick.AddListener(OnStopClicked);
            if (audioSource != null) audioSource.ignoreListenerPause = true;
        }

        // Estado inicial limpio
        docPanel?.SetActive(false);
        IsDocOpen = false;
    }

    void Update()
    {
        if (enableAudioFeatures) UpdateAudioProgress();
    }

    void OnDestroy()
    {
        discardButton?.onClick.RemoveListener(OnDiscardClicked);
        openDocButton?.onClick.RemoveListener(OnOpenDocClicked);

        if (enableAudioFeatures)
        {
            playButton?.onClick.RemoveListener(OnPlayClicked);
            stopButton?.onClick.RemoveListener(OnStopClicked);
        }
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sin selección. Solo alterna paneles raíz.
    /// NO toca docPanel — el Controller lo cierra antes via HideDoc() si estaba abierto.
    /// </summary>
    public void ShowEmpty()
    {
        currentItem = null;

        emptyStatePanel?.SetActive(true);
        detailContentPanel?.SetActive(false);

        if (enableAudioFeatures) StopAudio();
    }

    /// <summary>
    /// Puebla y muestra el detalle del ítem.
    /// Si había un doc abierto de un ítem anterior, lo cierra primero.
    /// El ítem anterior se reemplaza — nunca hay estado mixto.
    /// </summary>
    public void ShowDetail(SO_InventoryItem item)
    {
        if (item == null) { ShowEmpty(); return; }

        // Cambio de ítem con doc abierto → cerrar el doc del anterior
        if (IsDocOpen) HideDoc();

        currentItem = item;

        if (enableAudioFeatures) StopAudio();

        emptyStatePanel?.SetActive(false);
        detailContentPanel?.SetActive(true);

        PopulateHeader(item);
        PopulateDescription(item);
        PopulateMetadata(item);
        PopulateContent(item);
        PopulateDiscardButton(item);
    }

    /// <summary>
    /// Abre el doc panel encima del detalle.
    /// Llamado por el Controller cuando el jugador presiona "leer documento".
    /// El ítem sigue seleccionado — solo se agrega una capa visual.
    /// </summary>
    public void ShowDoc()
    {
        if (docPanel == null) return;

        IsDocOpen = true;
        docPanel.SetActive(true);

        // Resetear scroll al inicio cada vez que se abre
        if (docScrollRect != null)
            docScrollRect.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// Cierra el doc panel.
    /// Llamado por el Controller via ESC stack.
    /// NO deselecciona el ítem ni toca detailContentPanel.
    /// </summary>
    public void HideDoc()
    {
        if (docPanel == null) return;

        IsDocOpen = false;
        docPanel.SetActive(false);
    }

    public void StopAudio()
    {
        if (!enableAudioFeatures) return;
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();
    }

    // ── Populate ──────────────────────────────────────────────────────────────

    private void PopulateHeader(SO_InventoryItem item)
    {
        CategoryVisuals v = categoryConfig.Get(item.Category);

        if (iconImage != null && item.ItemIcon != null) iconImage.sprite = item.ItemIcon;
        if (iconBackground != null) iconBackground.color = v.BackgroundColor;
        if (itemNameText != null) itemNameText.text = item.ItemName;
        if (categoryTagText != null) categoryTagText.text = v.TagLabel;
        if (categoryTagBackground != null) categoryTagBackground.color = v.BackgroundColor;
    }

    private void PopulateDescription(SO_InventoryItem item)
    {
        if (descriptionText != null)
            descriptionText.text = item.Description;
    }

    private void PopulateMetadata(SO_InventoryItem item)
    {
        if (metallicValueText != null)
        {
            metallicValueText.text = item.IsMetallic ? "YES" : "NO";
            metallicValueText.color = item.IsMetallic ? MetallicYesColor : MetallicNoColor;
        }

        if (consumableValueText != null)
            consumableValueText.text = item.IsConsumable ? "YES" : "NO";

        if (uniqueValueText != null)
            uniqueValueText.text = item.IsUnique ? "YES" : "NO";
    }

    private void PopulateContent(SO_InventoryItem item)
    {
        // Ocultar botón de doc por defecto
        openDocButton?.gameObject.SetActive(false);

        if (enableAudioFeatures)
            audioPlayerBox?.SetActive(false);

        switch (item.ContentType)
        {
            case ItemContentType.Text:
                // Cargar el texto en el panel (sin abrirlo — el jugador lo abre con el botón)
                if (docPanelText != null)
                    docPanelText.text = item.TextContent;

                openDocButton?.gameObject.SetActive(true);
                break;

            case ItemContentType.Audio:
                if (enableAudioFeatures)
                {
                    audioPlayerBox?.SetActive(true);
                    if (audioSource != null) audioSource.clip = item.AudioClip;
                    UpdateAudioTimeText();
                }
                break;

            case ItemContentType.None:
            default:
                break;
        }
    }

    private void PopulateDiscardButton(SO_InventoryItem item)
    {
        if (discardButtonText != null)
            discardButtonText.text = $"[ DISCARD {item.ItemName.ToUpper()} ]";
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnOpenDocClicked()
    {
        // La View notifica al Controller, el Controller registra la capa y llama ShowDoc()
        InventoryManagerUI.Instance.OpenDocument();
    }

    private void OnDiscardClicked()
    {
        if (currentItem == null) return;
        InventoryManagerUI.Instance.RequestDiscard(currentItem);
    }

    // ── Audio WIP ─────────────────────────────────────────────────────────────

    private void OnPlayClicked()
    {
        if (!enableAudioFeatures) return;
        if (audioSource != null && audioSource.clip != null && !audioSource.isPlaying)
            audioSource.Play();
    }

    private void OnStopClicked()
    {
        if (!enableAudioFeatures) return;
        StopAudio();
    }

    private void UpdateAudioProgress()
    {
        if (audioSource == null || audioSource.clip == null || !audioSource.isPlaying) return;

        float progress = audioSource.time / audioSource.clip.length;
        if (audioProgressBar != null) audioProgressBar.value = progress;

        UpdateAudioTimeText();
    }

    private void UpdateAudioTimeText()
    {
        if (audioTimeText == null || audioSource == null || audioSource.clip == null) return;

        string current = FormatTime(audioSource.time);
        string total = FormatTime(audioSource.clip.length);
        audioTimeText.text = $"{current} / {total}";
    }

    private string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m}:{s:00}";
    }
}