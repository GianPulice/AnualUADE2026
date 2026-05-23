using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vista del panel de secuencia. Genera dinamicamente N botones segun el panel
/// recibido en Bind() y delega cada press al SequencePanelInteractable.
/// No conoce nada del puzzle especifico — recibe solo el panel y la cantidad de botones.
/// </summary>
public class SequencePanelView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform buttonsParent;
    [SerializeField] private Button buttonPrefab;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI sequenceDisplayText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button closeButton;

    [Header("Feedback")]
    [SerializeField] private Color buttonDefaultColor = new Color(0.07f, 0.07f, 0.07f, 1f);
    [SerializeField] private Color buttonActiveColor = new Color(0.1f, 0.4f, 0.16f, 1f);
    [SerializeField] private Color buttonWrongColor = new Color(0.4f, 0.1f, 0.1f, 1f);
    [SerializeField] private float wrongFlashDuration = 0.6f;

    [Header("Strings")]
    [SerializeField] private string titleString = "Panel Electrico";
    [SerializeField] private string statusIdleString = "Ingrese la secuencia";
    [SerializeField] private string statusWrongString = "Secuencia incorrecta. Reiniciando...";
    [SerializeField] private string statusOkString = "Secuencia correcta!";

    private SequencePanelInteractable boundPanel;
    private readonly List<Button> spawnedButtons = new List<Button>();
    private float wrongFlashTimer;
    private bool isFlashingWrong;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        UnsubscribeFromPanel();
    }

    private void Update()
    {
        if (!isFlashingWrong) return;
        wrongFlashTimer -= Time.unscaledDeltaTime;
        if (wrongFlashTimer <= 0f)
        {
            isFlashingWrong = false;
            ResetButtonColors();
            UpdateStatus(statusIdleString);
        }
    }

    public void SetVisibleImmediate(bool visible)
    {
        gameObject.SetActive(visible);
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    public void Bind(SequencePanelInteractable panel)
    {
        UnsubscribeFromPanel();
        boundPanel = panel;

        if (titleText != null) titleText.text = titleString;
        UpdateStatus(statusIdleString);
        UpdateSequenceDisplay();

        BuildButtons(panel.ButtonCount);
        SubscribeToPanel();
    }

    private void SubscribeToPanel()
    {
        if (boundPanel == null) return;
        boundPanel.OnButtonPressed += HandleButtonPressed;
        boundPanel.OnSequenceFailed += HandleSequenceFailed;
        boundPanel.OnSequenceCompleted += HandleSequenceCompleted;
    }

    private void UnsubscribeFromPanel()
    {
        if (boundPanel == null) return;
        boundPanel.OnButtonPressed -= HandleButtonPressed;
        boundPanel.OnSequenceFailed -= HandleSequenceFailed;
        boundPanel.OnSequenceCompleted -= HandleSequenceCompleted;
        boundPanel = null;
    }

    private void BuildButtons(int count)
    {
        if (buttonPrefab == null || buttonsParent == null)
        {
            Debug.LogError("[SequencePanelView] Falta buttonPrefab o buttonsParent.");
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

            btn.onClick.AddListener(() => OnButtonClicked(buttonId));
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

    private void OnButtonClicked(int buttonId)
    {
        if (boundPanel == null) return;
        if (isFlashingWrong) return;
        boundPanel.TryPressButton(buttonId);
    }

    private void HandleButtonPressed(int buttonId)
    {
        UpdateSequenceDisplay();

        int idx = buttonId - 1;
        if (idx >= 0 && idx < spawnedButtons.Count)
            SetButtonColor(spawnedButtons[idx], buttonActiveColor);
    }

    private void HandleSequenceFailed()
    {
        UpdateStatus(statusWrongString);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonWrongColor);
        isFlashingWrong = true;
        wrongFlashTimer = wrongFlashDuration;
        UpdateSequenceDisplay();
    }

    private void HandleSequenceCompleted()
    {
        UpdateStatus(statusOkString);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonActiveColor);
    }

    private void UpdateSequenceDisplay()
    {
        if (sequenceDisplayText == null || boundPanel == null) return;

        IReadOnlyList<int> entered = boundPanel.EnteredSequence;
        if (entered.Count == 0)
        {
            sequenceDisplayText.text = "Ingresada: —";
            return;
        }

        sequenceDisplayText.text = "Ingresada: " + string.Join(" — ", entered);
    }

    private void UpdateStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    private void ResetButtonColors()
    {
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonDefaultColor);
    }

    private void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;

        ColorBlock cb = btn.colors;
        cb.normalColor = color;
        cb.selectedColor = color;
        btn.colors = cb;
    }

    private void OnCloseClicked()
    {
        if (SequencePanelUIController.Instance != null)
            SequencePanelUIController.Instance.Close();
    }
}
