using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of the right-hand detail panel for the selected item.
///
/// Responsibilities:
///   - Show the empty state until something is selected
///   - Populate header, description, metadata and content from the item
///   - Show the doc panel for Text-type items (handled by the Controller via the ESC stack)
///   - Expose the discard button
///   - Notify the Controller of the discard
///
/// Notes:
///   - Audio WIP: structure ready, logic disabled with enableAudioFeatures.
///   - docPanel is a separate layer handled by ShowDoc/HideDoc.
///     The Controller decides when to open/close it (not ShowEmpty nor ShowDetail).
/// </summary>
public class ItemDetailView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    // ── Root panels ───────────────────────────────────────────────────────────

    [Header("Empty state (no selection)")]
    [Tooltip("Select an item / to see the detail")]
    [SerializeField] private GameObject emptyStatePanel;
    [Tooltip("All the real content")]
    [SerializeField] private GameObject detailContentPanel;

    // ── Header ────────────────────────────────────────────────────────────────

    [Header("Item header")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image iconBackground;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI categoryTagText;
    [SerializeField] private Image categoryTagBackground;

    // ── Description ───────────────────────────────────────────────────────────

    [Header("Description")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Header("Parameters")]
    [SerializeField] private TextMeshProUGUI metallicValueText;
    [SerializeField] private TextMeshProUGUI consumableValueText;
    [SerializeField] private TextMeshProUGUI uniqueValueText;

    // ── Doc panel ─────────────────────────────────────────────────────────────

    [Header("Doc Panel (Text-type items)")]
    [Tooltip("Separate panel that opens on top of the detail. Closable with ESC without deselecting the item.")]
    [SerializeField] private GameObject docPanel;
    [SerializeField] private TextMeshProUGUI docPanelText;
    [SerializeField] private ScrollRect docScrollRect;
    [SerializeField] private RectTransform docViewport;
    [SerializeField] private Button openDocButton;  // "read document" inside the detail

    // ── Audio WIP ─────────────────────────────────────────────────────────────

    [Header("Debug / WIP")]
    [Tooltip("Enable to turn on the audio player logic and UI.")]
    [SerializeField] private bool enableAudioFeatures = false;

    [Header("Audio player (WIP)")]
    [SerializeField] private GameObject audioPlayerBox;
    [SerializeField] private Button playButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Slider audioProgressBar;
    [SerializeField] private TextMeshProUGUI audioTimeText;
    [SerializeField] private AudioSource audioSource;

    // ── Discard ───────────────────────────────────────────────────────────────

    [Header("Discard button")]
    [SerializeField] private Button discardButton;
    [SerializeField] private TextMeshProUGUI discardButtonText;

    // ── Colors ────────────────────────────────────────────────────────────────

    private static readonly Color MetallicYesColor = new Color(0.53f, 0.13f, 0.13f);
    private static readonly Color MetallicNoColor = new Color(0.40f, 0.40f, 0.40f);

    // ── Internal state ────────────────────────────────────────────────────────

    private SO_InventoryItem currentItem;

    /// <summary>
    /// The Controller queries this to know whether ESC should close the doc
    /// before closing the inventory.
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

        // Clean initial state
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

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// No selection. It only toggles the root panels.
    /// It does NOT touch docPanel — the Controller closes it first via HideDoc() if it was open.
    /// </summary>
    public void ShowEmpty()
    {
        currentItem = null;

        emptyStatePanel?.SetActive(true);
        detailContentPanel?.SetActive(false);

        if (enableAudioFeatures) StopAudio();
    }

    /// <summary>
    /// Populates and shows the item's detail.
    /// If a doc from a previous item was open, it closes it first.
    /// The previous item is replaced — there is never a mixed state.
    /// </summary>
    public void ShowDetail(SO_InventoryItem item)
    {
        if (item == null) { ShowEmpty(); return; }

        // Item change with the doc open -> close the previous item's doc
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
    /// Opens the doc panel on top of the detail.
    /// Called by the Controller when the player presses "read document".
    /// The item stays selected — only a visual layer is added.
    /// </summary>
    public void ShowDoc()
    {
        if (docPanel == null) return;

        IsDocOpen = true;
        docPanel.SetActive(true);

        // Reset the scroll to the top every time it opens
        if (docScrollRect != null)
            docScrollRect.verticalNormalizedPosition = 1f;
        CheckDocScrollNeeded().Forget();
    }

    /// <summary>
    /// Closes the doc panel.
    /// Called by the Controller via the ESC stack.
    /// It does NOT deselect the item nor touch detailContentPanel.
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
        // Filename-style header, e.g. "> MECHANICAL_CORE.CMP"
        if (itemNameText != null)
            itemNameText.text = $"> {InventoryTextFormat.MachineName(item.ItemName)}.{v.TagLabel}";
        if (categoryTagText != null) categoryTagText.text = $"[{v.TagLabel}]";
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
        // Hide the doc button by default
        openDocButton?.gameObject.SetActive(false);

        if (enableAudioFeatures)
            audioPlayerBox?.SetActive(false);

        switch (item.ContentType)
        {
            case ItemContentType.Text:
                // Load the text into the panel (without opening it — the player opens it with the button)
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
        // The View notifies the Controller, the Controller registers the layer and calls ShowDoc()
        InventoryManagerUI.Instance.OpenDocument();
    }

    private void OnDiscardClicked()
    {
        if (currentItem == null) return;
        InventoryManagerUI.Instance.RequestDiscard(currentItem);
    }

    private async UniTaskVoid CheckDocScrollNeeded()
    {
        await UniTask.WaitForEndOfFrame(this);

        if (docScrollRect == null || docViewport == null) return;

        RectTransform content = docScrollRect.content;
        bool overflows = content.sizeDelta.y > docViewport.rect.height;
        docScrollRect.vertical = overflows;
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
