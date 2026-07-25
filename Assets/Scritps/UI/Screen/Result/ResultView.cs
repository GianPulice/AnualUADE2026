using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vista unica de las pantallas de fin de partida (Lose y GameOver).
///
/// Su cara la decide el <see cref="ResultPresentation"/> que le pasa el controller
/// justo antes de abrir, asi que el mismo prefab sirve para los dos resultados.
///
/// Usa los botones Retry y MainMenu del base. Exit y NextLevel quedan apagados: en el
/// prefab el boton "EXIT" esta cableado al slot _btnMainMenu (y _btnExit quedo vacio),
/// asi que MainMenu es el que realmente lleva al menu.
/// </summary>
public class ResultView : BaseResultView
{
    [Header("Result")]
    [Tooltip("Titulo grande. Se apaga si la presentacion no trae texto.")]
    [SerializeField] private TextMeshProUGUI _titleText;

    [Tooltip("Tiempo y modulos resueltos. Se apaga si la presentacion no pide stats.")]
    [SerializeField] private TextMeshProUGUI _statsText;

    [Tooltip("Overlay de fondo a tintar. Opcional.")]
    [SerializeField] private Image _vignetteImage;

    [Tooltip("Total de modulos del nivel; solo afecta al texto 'N / total'.")]
    [SerializeField] private int _totalModules = 3;

    protected override void Awake()
    {
        base.Awake();

        // Esta pantalla trabaja con Retry + MainMenu. NextLevel (que en el prefab es el
        // boton "OPTIONS", mal cableado ahi) y Exit no se usan.
        HideNextLevelButton();
        SetExitVisible(false);
    }

    /// <summary>Aplica titulo, color, vignette y visibilidad de botones. Llamar antes de Open().</summary>
    public void ApplyPresentation(ResultPresentation presentation)
    {
        if (presentation == null) return;

        if (_titleText != null)
        {
            bool hasTitle = !string.IsNullOrEmpty(presentation.Title);
            _titleText.gameObject.SetActive(hasTitle);
            if (hasTitle)
            {
                _titleText.text  = presentation.Title;
                _titleText.color = presentation.TitleColor;
            }
        }

        if (_statsText != null) _statsText.gameObject.SetActive(presentation.ShowStats);
        if (_vignetteImage != null) _vignetteImage.color = presentation.VignetteColor;

        SetRetryVisible(presentation.ShowRetry);
    }

    public override void SetData(GameResultModel model)
    {
        if (_statsText == null || model == null) return;

        int minutes = Mathf.FloorToInt(model.Time / 60f);
        int seconds = Mathf.FloorToInt(model.Time % 60f);
        _statsText.text =
            $"Tiempo: {minutes:00}:{seconds:00}\n" +
            $"Módulos resueltos: {model.CompletedModules} / {_totalModules}";
    }
}
