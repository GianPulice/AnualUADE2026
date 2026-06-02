using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View del panel de secuencia. Solo presenta — NO conoce al
/// <see cref="SequencePanelInteractable"/>. Emite eventos cuando el usuario
/// interactúa, y expone métodos públicos para que el controller refresque la UI.
///
/// El <c>canvasGroup</c> se hereda de <see cref="BaseScreenView"/>.
/// </summary>
public class SequencePanelView : BaseScreenView
{
    [Header("Refs")]
    [SerializeField] private RectTransform buttonsParent;
    [SerializeField] private Button buttonPrefab;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI sequenceDisplayText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button closeButton;

    [Header("Feedback")]
    [SerializeField] private Color buttonDefaultColor = new Color(0.07f, 0.07f, 0.07f, 1f);
    [SerializeField] private Color buttonActiveColor  = new Color(0.1f, 0.4f, 0.16f, 1f);
    [SerializeField] private Color buttonWrongColor   = new Color(0.4f, 0.1f, 0.1f, 1f);
    [SerializeField] private float wrongFlashDuration = 0.6f;

    [Header("Strings")]
    [SerializeField] private string titleString       = "Panel Electrico";
    [SerializeField] private string statusIdleString  = "Ingrese la secuencia";
    [SerializeField] private string statusWrongString = "Secuencia incorrecta. Reiniciando...";
    [SerializeField] private string statusOkString    = "Secuencia correcta!";

    /// <summary>Se dispara cuando el usuario clickea uno de los botones del grid.</summary>
    public event Action<int> OnButtonClicked;

    /// <summary>Se dispara cuando el usuario clickea el botón de cerrar de la UI.</summary>
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
            UpdateStatus(statusIdleString);
        }
    }

    // ── API pública (la llama el controller) ────────────────────────────────

    /// <summary>
    /// Refresca toda la UI según el modelo. Construye los botones, setea título
    /// y status idle, limpia la secuencia mostrada.
    /// </summary>
    public void Populate(SequencePanelModel model)
    {
        isFlashingWrong = false;
        wrongFlashTimer = 0f;

        if (titleText != null) titleText.text = titleString;
        UpdateStatus(statusIdleString);

        BuildButtons(model != null ? model.ButtonCount : 0);
        RefreshSequenceDisplay(model != null ? model.EnteredSequence : null);
    }

    public void RefreshSequenceDisplay(IReadOnlyList<int> entered)
    {
        if (sequenceDisplayText == null) return;

        if (entered == null || entered.Count == 0)
        {
            sequenceDisplayText.text = "Ingresada: —";
            return;
        }

        sequenceDisplayText.text = "Ingresada: " + string.Join(" — ", entered);
    }

    /// <summary>Marca botón individual como recién presionado (verde).</summary>
    public void HighlightPressedButton(int buttonId)
    {
        int idx = buttonId - 1;
        if (idx < 0 || idx >= spawnedButtons.Count) return;
        SetButtonColor(spawnedButtons[idx], buttonActiveColor);
    }

    public void ShowFailFlash()
    {
        UpdateStatus(statusWrongString);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonWrongColor);
        isFlashingWrong = true;
        wrongFlashTimer = wrongFlashDuration;
    }

    public void ShowCompleted()
    {
        UpdateStatus(statusOkString);
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonActiveColor);
    }

    public void ResetButtonColors()
    {
        foreach (Button b in spawnedButtons) SetButtonColor(b, buttonDefaultColor);
    }

    // ── Internos ────────────────────────────────────────────────────────────

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

    private void UpdateStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    private void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;

        ColorBlock cb = btn.colors;
        cb.normalColor   = color;
        cb.selectedColor = color;
        btn.colors = cb;
    }
}
