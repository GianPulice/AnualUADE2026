using UnityEngine;

/// <summary>
/// How the result screen looks for a given <see cref="GameState"/>.
///
/// Lose and GameOver share 90% of the behaviour (freeze time, show stats, go back to the
/// menu); the only thing that distinguishes them is presentation — title, color, which
/// buttons are visible — and that is data, not two separate classes. One preset per state
/// in the <see cref="ResultScreenController"/> Inspector.
///
/// To bring Win into this screen later: add a preset with State = Win and delete
/// WinController/WinView.
/// </summary>
[System.Serializable]
public class ResultPresentation
{
    [Tooltip("Result that triggers this presentation. States without a preset are ignored by the screen.")]
    [SerializeField] private GameState _state = GameState.Lose;

    [Tooltip("Large title. Empty = no title (the GameObject is switched off).")]
    [SerializeField] private string _title = string.Empty;

    [Tooltip("Theme token for the title. Goes through the title's UIThemeApplier, so it follows " +
             "UITheme.asset like the rest of the UI instead of holding a literal colour.")]
    [SerializeField] private UIThemeRole _titleRole = UIThemeRole.TextPrimary;

    [Tooltip("Tint of the background overlay. Alpha 0 = pure black background with no tint.")]
    [SerializeField] private Color _vignetteColor = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Show the retry button. Switch it off when there is no run to go back to.")]
    [SerializeField] private bool _showRetry = true;

    [Tooltip("Show time and resolved modules.")]
    [SerializeField] private bool _showStats;

    [Tooltip("Seconds the screen takes to fade in. Short for a plain result; long for GameOver, " +
             "which arrives right after the explosion cinematic and should rise slowly out of it.")]
    [SerializeField, Min(0f)] private float _fadeInDuration = 0.3f;

    public GameState State       => _state;
    public string   Title        => _title;
    public UIThemeRole TitleRole => _titleRole;
    public Color    VignetteColor => _vignetteColor;
    public bool     ShowRetry    => _showRetry;
    public bool     ShowStats    => _showStats;
    public float    FadeInDuration => _fadeInDuration;
}
