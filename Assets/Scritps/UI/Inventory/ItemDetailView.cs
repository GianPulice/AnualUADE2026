using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// VIEW del panel derecho de detalles del ítem seleccionado.
/// 
/// Responsabilidades:
///   - Mostrar estado vacío hasta que se selecciona algo
///   - Poblar encabezado, descripción, metadata y contenido según el ítem
///   - Mostrar doc-box para ítems de tipo Texto
///   - Mostrar reproductor de audio para ítems de tipo Audio (con ignoreListenerPause)
///   - Exponer el botón de descarte (no hay botón de usar)
///   - Notificar el descarte al Controller
///   
/// Notas:
///   - Todo lo relacionado al audio todavia no se implementa, pero se deja la estructura preparada para facilitar su futura incorporación.
/// </summary>
public class ItemDetailView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    [Header("Estado vacío (sin selección)")]
    [Tooltip("Selecciona un item / para ver el detalle")]
    [SerializeField] private GameObject emptyStatePanel;
    [Tooltip("Todo el contenido real")]
    [SerializeField] private GameObject detailContentPanel; 
    // ── Header ───────────────────────────────────────────────────────────────

    [Header("Header del ítem")]
    [SerializeField] private Image iconImage;
    [Tooltip("El fondo verde del tipo en tu UI")]
    [SerializeField] private Image iconBackground;  
    [SerializeField] private TextMeshProUGUI itemNameText; //13px
    [SerializeField] private TextMeshProUGUI categoryTagText; //8px
    [SerializeField] private Image categoryTagBackground;

    [Header("Descripción")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    // ── Parámetros ─────────────────────────────────────────────────────────────
    [Header("Parameters")]
    [SerializeField] private TextMeshProUGUI metallicValueText;    // Sí (#882222) / No
    [SerializeField] private TextMeshProUGUI consumableValueText;
    [SerializeField] private TextMeshProUGUI uniqueValueText;

    // ── Contenido de documento (solo ítems Texto) ─────────────────────────────

    [Header("Doc-box (solo ítems de tipo Texto)")]
    [SerializeField] private GameObject docBox;
    [SerializeField] private TextMeshProUGUI docBoxText;  // Scrolleable, altura máx 72px

    [Header("Debug / WIP")]
    [Tooltip("Activa para habilitar la lógica y UI del reproductor de audio.")]
    [SerializeField] private bool enableAudioFeatures = false;
    //                                        WIP
    //// ── Reproductor de audio (solo ítems Audio) ───────────────────────────────

    [Header("Reproductor de audio (solo ítems de tipo Audio)")]
    [SerializeField] private GameObject audioPlayerBox;
    [SerializeField] private Button playButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Slider audioProgressBar;   // Fill rojo
    [SerializeField] private TextMeshProUGUI audioTimeText;      // "0:00 / 1:23"
    [SerializeField] private AudioSource audioSource;


    // ── Descarte ──────────────────────────────────────────────────────────────

    [Header("Botón de descarte")]
    [SerializeField] private Button discardButton;
    [Tooltip("[ descartar {nombre} ]")]
    [SerializeField] private TextMeshProUGUI discardButtonText;  

    // ── Colores de metadata ───────────────────────────────────────────────────

    private static readonly Color MetallicYesColor = new Color(0.53f, 0.13f, 0.13f); // #882222
    private static readonly Color MetallicNoColor = new Color(0.40f, 0.40f, 0.40f);

    // ── Estado interno ────────────────────────────────────────────────────────

    private SO_InventoryItem currentItem;

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        discardButton?.onClick.AddListener(OnDiscardClicked);
        if (enableAudioFeatures)
        {
            playButton?.onClick.AddListener(OnPlayClicked);
            stopButton?.onClick.AddListener(OnStopClicked);

            if (audioSource != null)
                audioSource.ignoreListenerPause = true;
        }
    }


    private void Update()
    {
        if (enableAudioFeatures)
        {
            UpdateAudioProgress();
        }
    }

    void OnDestroy()
    {
        discardButton?.onClick.RemoveListener(OnDiscardClicked);
        if (enableAudioFeatures)
        {
            playButton?.onClick.RemoveListener(OnPlayClicked);
            stopButton?.onClick.RemoveListener(OnStopClicked);
        }

    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void ShowEmpty()
    {
        currentItem = null;
        if(enableAudioFeatures) StopAudio();

        emptyStatePanel?.SetActive(true);
        detailContentPanel?.SetActive(false);
    }

    /// <summary>Muestra los detalles del ítem seleccionado.</summary>
    public void ShowDetail(SO_InventoryItem item)
    {
        if (item == null) { ShowEmpty(); return; }

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
    public void StopAudio()
    {
        if (!enableAudioFeatures) return;
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();
    }

    private void PopulateHeader(SO_InventoryItem item)
    {
        CategoryVisuals visuals = categoryConfig.Get(item.Category);

        if (iconImage != null && item.ItemIcon != null)
            iconImage.sprite = item.ItemIcon;

        if (iconBackground != null)
            iconBackground.color = visuals.BackgroundColor;

        if (itemNameText != null)
            itemNameText.text = item.ItemName;

        if (categoryTagText != null)
            categoryTagText.text = visuals.TagLabel;

        if (categoryTagBackground != null)
            categoryTagBackground.color = visuals.BackgroundColor;
    }

    private void PopulateDescription(SO_InventoryItem item)
    {
        if (descriptionText != null)
            descriptionText.text = item.Description;
    }

    private void PopulateMetadata(SO_InventoryItem item)
    {
        // Metálico (rojo si es true)
        if (metallicValueText != null)
        {
            metallicValueText.text = item.IsMetallic ? "YES" : "NO";
            metallicValueText.color = item.IsMetallic ? MetallicYesColor : MetallicNoColor;
        }

        // Consumible
        if (consumableValueText != null)
            consumableValueText.text = item.IsConsumable ? "YES" : "NO";

        // Único
        if (uniqueValueText != null)
            uniqueValueText.text = item.IsUnique ? "YES" : "NO";
    }

    private void PopulateContent(SO_InventoryItem item)
    {
        // Ocultar ambas cajas por defecto
        docBox?.SetActive(false);
        audioPlayerBox?.SetActive(false);

        switch (item.ContentType)
        {
            case ItemContentType.Text:
                docBox?.SetActive(true);
                if (docBoxText != null)
                    docBoxText.text = item.TextContent;
                break;

            case ItemContentType.Audio:
                audioPlayerBox?.SetActive(true);
                if (audioSource != null)
                    audioSource.clip = item.AudioClip;
                UpdateAudioTimeText();
                break;

            case ItemContentType.None:
            default:
                // Sin contenido adicional — solo descripción y metadata
                break;
        }
    }

    private void PopulateDiscardButton(SO_InventoryItem item)
    {
        if (discardButtonText != null)
            discardButtonText.text = $"[ DISCARD {item.ItemName.ToUpper()} ]";
    }

    // ── Audio ─────────────────────────────────────────────────────────────────

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

        if (audioProgressBar != null)
            audioProgressBar.value = progress;

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

    private void OnDiscardClicked()
    {
        if (currentItem == null) return;
        // El Controller maneja el diálogo de confirmación
        InventoryManagerUI.Instance.RequestDiscard(currentItem);
    }
}
