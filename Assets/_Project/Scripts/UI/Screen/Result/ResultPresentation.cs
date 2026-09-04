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

    [SerializeField] private Color _titleColor = Color.white;

    [Tooltip("Tint of the background overlay. Alpha 0 = pure black background with no tint.")]
    [SerializeField] private Color _vignetteColor = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Show the retry button. Switch it off when there is no run to go back to.")]
    [SerializeField] private bool _showRetry = true;

    [Tooltip("Show time and resolved modules.")]
    [SerializeField] private bool _showStats;

    public GameState State       => _state;
    public string   Title        => _title;
    public Color    TitleColor   => _titleColor;
    public Color    VignetteColor => _vignetteColor;
    public bool     ShowRetry    => _showRetry;
    public bool     ShowStats    => _showStats;
}
