using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single view for the end-of-run screens (Lose and GameOver).
///
/// Its face is decided by the <see cref="ResultPresentation"/> the controller passes right
/// before opening, so the same prefab serves both results.
///
/// It uses the base's Retry and MainMenu buttons. Exit and NextLevel are switched off: in
/// the prefab the "EXIT" button is wired to the _btnMainMenu slot (and _btnExit was left
/// empty), so MainMenu is the one that actually goes back to the menu.
/// </summary>
public class ResultView : BaseResultView
{
    [Header("Result")]
    [Tooltip("Large title. Switched off if the presentation carries no text.")]
    [SerializeField] private TextMeshProUGUI _titleText;

    [Tooltip("Time and resolved modules. Switched off if the presentation does not ask for stats.")]
    [SerializeField] private TextMeshProUGUI _statsText;

    [Tooltip("Background overlay to tint. Optional.")]
    [SerializeField] private Image _vignetteImage;

    [Tooltip("Total modules in the level; only affects the 'N / total' text.")]
    [SerializeField] private int _totalModules = 3;

    protected override void Awake()
    {
        base.Awake();

        // This screen works with Retry + MainMenu. NextLevel (which in the prefab is the
        // "OPTIONS" button, miswired there) and Exit are unused.
        HideNextLevelButton();
        SetExitVisible(false);
    }

    /// <summary>Applies title, color, vignette and button visibility. Call before Open().</summary>
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
            $"Time: {minutes:00}:{seconds:00}\n" +
            $"Modules resolved: {model.CompletedModules} / {_totalModules}";
    }
}
