using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of the detail pop-up for the selected item.
///
/// The pop-up sits in the bottom-right corner of the inventory, unfolds out of that corner when an
/// item is selected and folds back into it when the selection is cleared. Its height follows the
/// description: the prefab drives it with a VerticalLayoutGroup + ContentSizeFitter, so a one-line
/// description gets a short pop-up and a long one a tall one.
///
/// Responsibilities:
///   - Stay folded until something is selected
///   - Populate header, description, metadata and content from the item
///   - Own the doc toggle button for Text-type items
///   - Unfold / fold the pop-up
///
/// Notes:
///   - Audio WIP: structure ready, logic disabled with enableAudioFeatures.
///   - There is no discard: items leave the inventory only through the world (puzzles, consumption).
///   - The doc pop-up itself is <see cref="DocPanelView"/>, a separate layer. This view only
///     owns the button that toggles it, because whether the button exists at all depends on
///     the item's ContentType — which is this view's business.
///     The Controller decides when to open/close it (not ShowEmpty nor ShowDetail).
/// </summary>
public class ItemDetailView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    // ── Pop-up ──────────────────────────────────────────────────────────────

    [Header("Pop-up")]
    [Tooltip("Panel that unfolds when an item is selected. Defaults to this RectTransform. It grows " +
             "out of its PIVOT, so pivot (1, 0) makes it come out of the bottom-right corner.")]
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private float popupOpenDuration = UITweenDefaults.PanelOpenDuration;
    [SerializeField] private float popupCloseDuration = UITweenDefaults.PanelCloseDuration;

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
    [Tooltip("Pop-up that opens on top of the detail. Closable with ESC without deselecting the item.")]
    [SerializeField] private DocPanelView docPanel;
    [Tooltip("Toggles the pop-up. Only visible while a Text-type item is selected.")]
    [SerializeField] private Button openDocButton;
    [SerializeField] private TextMeshProUGUI openDocButtonText;
    [SerializeField] private string openDocLabel = "[ OPEN DOC ]";
    [SerializeField] private string closeDocLabel = "[ CLOSE DOC ]";

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

    // ── Colors ────────────────────────────────────────────────────────────────

    private static readonly Color MetallicYesColor = new Color(0.80f, 0.20f, 0.20f);
    private static readonly Color MetallicNoColor = new Color(0.88f, 0.88f, 0.88f);

    /// <summary>
    /// The category colour in the config asset is a saturated fill — it reads as a solid block
    /// of colour behind the icon and the tag. The detail panel wants the terminal look instead:
    /// a near-black chip with the category colour surviving only in the label. These two factors
    /// derive both from the same authored colour, so a category still needs exactly one colour
    /// in the asset.
    /// </summary>
    private const float ChipBackgroundFactor = 0.30f;
    private const float ChipLabelFactor = 1.65f;

    // ── Internal state ────────────────────────────────────────────────────────

    private SO_InventoryItem currentItem;

    /// <summary>
    /// The Controller queries this to know whether ESC should close the doc
    /// before closing the inventory.
    /// </summary>
    public bool IsDocOpen => docPanel != null && docPanel.IsOpen;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        openDocButton?.onClick.AddListener(OnDocButtonClicked);

        // The X inside the pop-up routes back through the Controller like ESC does, so there is
        // a single close path and IsDocOpen can never lie.
        if (docPanel != null) docPanel.OnCloseRequested += OnDocCloseRequested;

        if (enableAudioFeatures)
        {
            playButton?.onClick.AddListener(OnPlayClicked);
            stopButton?.onClick.AddListener(OnStopClicked);
            if (audioSource != null) audioSource.ignoreListenerPause = true;
        }

        // Clean initial state
        docPanel?.gameObject.SetActive(false);
        EnsurePopup();
        SetPopupScale(0f);
        RefreshDocButtonLabel();
    }

    void Update()
    {
        if (enableAudioFeatures) UpdateAudioProgress();
    }

    void OnDestroy()
    {
        if (popupRect != null) LeanTween.cancel(popupRect.gameObject);
        openDocButton?.onClick.RemoveListener(OnDocButtonClicked);

        if (docPanel != null) docPanel.OnCloseRequested -= OnDocCloseRequested;

        if (enableAudioFeatures)
        {
            playButton?.onClick.RemoveListener(OnPlayClicked);
            stopButton?.onClick.RemoveListener(OnStopClicked);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// No selection. Folds the pop-up back into its corner.
    /// It does NOT touch docPanel — the Controller closes it first via HideDoc() if it was open.
    /// </summary>
    public void ShowEmpty()
    {
        currentItem = null;

        HidePopup();

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

        PopulateHeader(item);
        PopulateDescription(item);
        PopulateMetadata(item);
        PopulateContent(item);

        ShowPopup();
    }

    /// <summary>
    /// Opens the doc panel on top of the detail.
    /// Called by the Controller when the player presses "read document".
    /// The item stays selected — only a visual layer is added.
    /// </summary>
    public void ShowDoc()
    {
        if (docPanel == null) return;

        docPanel.Open();
        RefreshDocButtonLabel();
    }

    /// <summary>
    /// Closes the doc panel through its reverse animation.
    /// Called by the Controller via the ESC stack, by the toggle button and by the panel's X.
    /// It does NOT deselect the item nor fold the pop-up.
    /// </summary>
    public void HideDoc()
    {
        if (docPanel == null) return;

        docPanel.Close();
        RefreshDocButtonLabel();
    }

    /// <summary>
    /// The one button says what it will do next, so the player never has to guess whether a
    /// second click re-opens or closes.
    /// </summary>
    private void RefreshDocButtonLabel()
    {
        if (openDocButtonText == null) return;
        openDocButtonText.text = IsDocOpen ? closeDocLabel : openDocLabel;
    }

    // ── Pop-up ──────────────────────────────────────────────────────────────

    private void EnsurePopup()
    {
        if (popupRect == null) popupRect = (RectTransform)transform;
    }

    /// <summary>
    /// Unfolds from wherever the pop-up is, so reselecting mid-fold picks up from there instead of
    /// snapping shut first. Already open, it is a no-op — changing items only swaps the content,
    /// which ItemSelectionTransitionTrigger animates.
    /// </summary>
    private void ShowPopup()
    {
        EnsurePopup();
        TweenPopup(1f, popupOpenDuration, UITweenDefaults.PanelOpenEase);
    }

    private void HidePopup()
    {
        EnsurePopup();
        TweenPopup(0f, popupCloseDuration, UITweenDefaults.PanelCloseEase);
    }

    /// <summary>
    /// Scale, not SetActive: the pop-up stays active so ItemSelectionTransitionTrigger, which lives
    /// on it, keeps listening to the selection. At scale 0 it draws nothing and catches no clicks.
    /// </summary>
    private void TweenPopup(float target, float duration, LeanTweenType ease)
    {
        LeanTween.cancel(popupRect.gameObject);

        float from = popupRect.localScale.x;
        if (Mathf.Approximately(from, target)) { SetPopupScale(target); return; }

        // Inactive (the inventory is closed): no frames will tick the tween, so land at once.
        if (!popupRect.gameObject.activeInHierarchy) { SetPopupScale(target); return; }

        LeanTween.value(popupRect.gameObject, from, target, duration)
            .setOnUpdate(SetPopupScale)
            .setEase(ease)
            .setIgnoreTimeScale(true);
    }

    private void SetPopupScale(float s) => popupRect.localScale = new Vector3(s, s, 1f);

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
        Color chipBackground = Scaled(v.BackgroundColor, ChipBackgroundFactor);

        if (iconImage != null && item.ItemIcon != null) iconImage.sprite = item.ItemIcon;
        if (iconBackground != null) iconBackground.color = chipBackground;
        // Filename-style header, e.g. "> MECHANICAL_CORE.CMP"
        if (itemNameText != null)
            itemNameText.text = $"> {InventoryTextFormat.MachineName(item.ItemName)}.{v.TagLabel}";
        if (categoryTagText != null)
        {
            categoryTagText.text = $"[{v.TagLabel}]";
            categoryTagText.color = Scaled(v.MainColor, ChipLabelFactor);
        }
        if (categoryTagBackground != null) categoryTagBackground.color = chipBackground;
    }

    /// <summary>Multiplies RGB, keeps alpha opaque, clamps. See the chip factors above.</summary>
    private static Color Scaled(Color c, float factor) => new Color(
        Mathf.Clamp01(c.r * factor),
        Mathf.Clamp01(c.g * factor),
        Mathf.Clamp01(c.b * factor),
        1f);

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
                docPanel?.SetContent(item.ItemName, item.TextContent);

                openDocButton?.gameObject.SetActive(true);
                RefreshDocButtonLabel();
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

    // ── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One button, both directions. The View notifies the Controller, the Controller registers
    /// the layer and calls back into ShowDoc()/HideDoc().
    /// </summary>
    private void OnDocButtonClicked()
    {
        if (IsDocOpen) InventoryManagerUI.Instance.CloseDocument();
        else InventoryManagerUI.Instance.OpenDocument();
    }

    /// <summary>The X inside the pop-up. Same path as ESC.</summary>
    private void OnDocCloseRequested() => InventoryManagerUI.Instance.CloseDocument();

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
