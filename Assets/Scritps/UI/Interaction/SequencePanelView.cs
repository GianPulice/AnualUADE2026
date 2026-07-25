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
    [Tooltip("LED de estado al lado del texto. Opcional.")]
    [SerializeField] private Image statusLed;
    [SerializeField] private Button closeButton;

    [Header("Feedback — teclas")]
    // Paleta de teclado de seguridad: metal oscuro en reposo, ambar al acertar,
    // rojo al fallar. Ojo: estos colores GANAN sobre los del SequencePanelUISetup,
    // porque Populate() repinta todas las teclas al abrir.
    [SerializeField] private Color buttonDefaultColor = new Color(0.18f, 0.18f, 0.19f, 1f);
    [SerializeField] private Color buttonActiveColor  = new Color(1f,    0.65f, 0.10f, 1f);
    [SerializeField] private Color buttonWrongColor   = new Color(0.55f, 0.10f, 0.08f, 1f);
    [SerializeField] private float wrongFlashDuration = 0.6f;

    [Header("Feedback — LED de estado")]
    [SerializeField] private Color ledIdleColor  = new Color(0.55f, 0.33f, 0.05f, 1f);
    [SerializeField] private Color ledWrongColor = new Color(0.95f, 0.20f, 0.15f, 1f);
    [SerializeField] private Color ledOkColor    = new Color(0.30f, 0.95f, 0.40f, 1f);

    [Header("Strings")]
    [SerializeField] private string titleString       = "PANEL ELECTRICO";
    [SerializeField] private string statusIdleString  = "INGRESE LA SECUENCIA";
    [SerializeField] private string statusWrongString = "SECUENCIA INCORRECTA";
    [SerializeField] private string statusOkString    = "ACCESO CONCEDIDO";

    [Header("Display LCD")]
    [Tooltip("Prefijo del display, estilo terminal.")]
    [SerializeField] private string displayPrefix = "> ";
    [Tooltip("Caracter de cursor al final de lo ingresado.")]
    [SerializeField] private string displayCursor = "_";

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
            UpdateStatus(statusIdleString, ledIdleColor);
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
        UpdateStatus(statusIdleString, ledIdleColor);

        BuildButtons(model != null ? model.ButtonCount : 0);
        RefreshSequenceDisplay(model != null ? model.EnteredSequence : null);
    }

    /// <summary>
    /// Pinta el display LCD estilo terminal: <c>&gt; 3 7 1 _</c>.
    ///
    /// Solo muestra lo ingresado + el cursor. No dibuja slots vacios porque el modelo
    /// no expone el largo de la secuencia correcta — y mostrarlo seria filtrarle al
    /// jugador cuantos digitos tiene el codigo.
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

    /// <summary>Marca botón individual como recién presionado (verde).</summary>
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

    private void UpdateStatus(string text, Color ledColor)
    {
        if (statusText != null) statusText.text = text;
        if (statusLed != null)  statusLed.color = ledColor;
    }

    /// <summary>
    /// Repinta una tecla. Toca SOLO el color de la Image, nunca el ColorBlock:
    /// el ColorBlock es un multiplicador sobre la Image, asi que escribir el color en
    /// los dos lados lo elevaba al cuadrado y las teclas oscuras se iban a negro.
    /// Dejando el normalColor en blanco, el tinte de hover/pressed sigue funcionando
    /// relativo a cualquier color de feedback que tenga la tecla en ese momento.
    /// </summary>
    private void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }
}
