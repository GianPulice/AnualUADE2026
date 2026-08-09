using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View of the sequence panel. It only presents — it does NOT know about the
/// <see cref="SequencePanelInteractable"/>. It raises events when the user interacts,
/// and exposes public methods so the controller can refresh the UI.
///
/// The <c>canvasGroup</c> is inherited from <see cref="BaseScreenView"/>.
/// </summary>
public class SequencePanelView : BaseScreenView
{
    [Header("Refs")]
    [SerializeField] private RectTransform buttonsParent;
    [SerializeField] private Button buttonPrefab;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI sequenceDisplayText;
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Status LED next to the text. Optional.")]
    [SerializeField] private Image statusLed;
    [SerializeField] private Button closeButton;

    [Header("Feedback — keys")]
    // Security keypad palette: dark metal at rest, amber on a correct press,
    // red on a wrong one. Note: these colors WIN over those in SequencePanelUISetup,
    // because Populate() repaints every key on open.
    [SerializeField] private Color buttonDefaultColor = new Color(0.18f, 0.18f, 0.19f, 1f);
    [SerializeField] private Color buttonActiveColor  = new Color(1f,    0.65f, 0.10f, 1f);
    [SerializeField] private Color buttonWrongColor   = new Color(0.55f, 0.10f, 0.08f, 1f);
    [SerializeField] private float wrongFlashDuration = 0.6f;

    [Header("Feedback — status LED")]
    [SerializeField] private Color ledIdleColor  = new Color(0.55f, 0.33f, 0.05f, 1f);
    [SerializeField] private Color ledWrongColor = new Color(0.95f, 0.20f, 0.15f, 1f);
    [SerializeField] private Color ledOkColor    = new Color(0.30f, 0.95f, 0.40f, 1f);

    [Header("Strings")]
    [SerializeField] private string titleString       = "ELECTRICAL PANEL";
    [SerializeField] private string statusIdleString  = "ENTER THE SEQUENCE";
    [SerializeField] private string statusWrongString = "INCORRECT SEQUENCE";
    [SerializeField] private string statusOkString    = "ACCESS GRANTED";

    [Header("LCD display")]
    [Tooltip("Display prefix, terminal style.")]
    [SerializeField] private string displayPrefix = "> ";
    [Tooltip("Cursor character at the end of the entered input.")]
    [SerializeField] private string displayCursor = "_";

    /// <summary>Raised when the user clicks one of the grid buttons.</summary>
    public event Action<int> OnButtonClicked;

    /// <summary>Raised when the user clicks the UI's close button.</summary>
    public event Action OnCloseClicked;

    private readonly List<Button> spawnedButtons = new List<Button>();
    private float wrongFlashTimer;
    private bool  isFlashingWrong;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HandleCloseClicked);
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(HandleCloseClicked);

        ClearButtons();
    }

    private void Update()
    {
        if (!isFlashingWrong) return;
        wrongFlashTimer -= Time.unscaledDeltaTime;
        if (wrongFlashTimer <= 0f)
        {
            isFlashingWrong = false;
            ResetButtonColors();
            UpdateStatus(statusIdleString, ledIdleColor);
        }
    }

    // ── Public API (called by the controller) ───────────────────────────────

    /// <summary>
    /// Refreshes the whole UI from the model. Builds the buttons, sets the title
    /// and idle status, and clears the displayed sequence.
    /// </summary>
    public void Populate(SequencePanelModel model)
    {
        isFlashingWrong = false;
        wrongFlashTimer = 0f;

        if (titleText != null) titleText.text = titleString;
        UpdateStatus(statusIdleString, ledIdleColor);

        BuildButtons(model != null ? model.ButtonCount : 0);
        RefreshSequenceDisplay(model != null ? model.EnteredSequence : null);
    }

    /// <summary>
    /// Paints the terminal-style LCD display: <c>&gt; 3 7 1 _</c>.
    ///
    /// It only shows what has been entered + the cursor. It does not draw empty slots because
    /// the model does not expose the length of the correct sequence — and showing it would
    /// leak to the player how many digits the code has.
    /// </summary>
    public void RefreshSequenceDisplay(IReadOnlyList<int> entered)
    {
        if (sequenceDisplayText == null) return;

        if (entered == null || entered.Count == 0)
        {
            sequenceDisplayText.text = displayPrefix + displayCursor;
            return;
        }

        sequenceDisplayText.text = displayPrefix + string.Join(" ", entered) + " " + displayCursor;
    }

    /// <summary>Marks an individual button as just pressed (green).</summary>
    public void HighlightPressedButton(int buttonId)
    {
        int idx = buttonId - 1;
        if (idx < 0 || idx >= spawnedButtons.Count) return;
        SetButtonColor(spawnedButtons[idx], buttonActiveColor);
    }

    public void ShowFailFlash()
    {
        UpdateStatus(statusWrongString, ledWrongColor);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonWrongColor);
        isFlashingWrong = true;
        wrongFlashTimer = wrongFlashDuration;
    }

    public void ShowCompleted()
    {
        UpdateStatus(statusOkString, ledOkColor);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonActiveColor);
    }

    public void ResetButtonColors()
    {
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonDefaultColor);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private void BuildButtons(int count)
    {
        if (buttonPrefab == null || buttonsParent == null)
        {
            Debug.LogError("[SequencePanelView] buttonPrefab or buttonsParent is missing.");
            return;
        }

        ClearButtons();

        for (int i = 1; i <= count; i++)
        {
            int buttonId = i;
            Button btn = Instantiate(buttonPrefab, buttonsParent);
            btn.gameObject.SetActive(true);
            btn.name = $"Button_{buttonId}";

            TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = buttonId.ToString();

            btn.onClick.AddListener(() => HandleButtonClicked(buttonId));
            SetButtonColor(btn, buttonDefaultColor);

            spawnedButtons.Add(btn);
        }
    }

    private void ClearButtons()
    {
        foreach (Button b in spawnedButtons)
        {
            if (b == null) continue;
            b.onClick.RemoveAllListeners();
            Destroy(b.gameObject);
        }
        spawnedButtons.Clear();
    }

    private void HandleButtonClicked(int buttonId)
    {
        if (isFlashingWrong) return;
        OnButtonClicked?.Invoke(buttonId);
    }

    private void HandleCloseClicked() => OnCloseClicked?.Invoke();

    private void UpdateStatus(string text, Color ledColor)
    {
        if (statusText != null) statusText.text = text;
        if (statusLed != null)  statusLed.color = ledColor;
    }

    /// <summary>
    /// Repaints a key. It touches ONLY the Image color, never the ColorBlock:
    /// the ColorBlock is a multiplier over the Image, so writing the color on both sides
    /// squared it and dark keys went to black.
    /// Leaving normalColor white keeps the hover/pressed tint working relative to whatever
    /// feedback color the key currently has.
    /// </summary>
    private void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }
}
