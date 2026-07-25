using UnityEngine;

/// <summary>
/// Como se ve la pantalla de resultado para un <see cref="GameState"/> dado.
///
/// Lose y GameOver comparten el 90% del comportamiento (congelar el tiempo, mostrar
/// stats, volver al menu); lo unico que los distingue es presentacion — titulo, color,
/// que botones se ven — y eso son datos, no dos clases distintas. Un preset por estado
/// en el Inspector de <see cref="ResultScreenController"/>.
///
/// Para sumar Win a esta pantalla mas adelante: agregar un preset con State = Win y
/// borrar WinController/WinView.
/// </summary>
[System.Serializable]
public class ResultPresentation
{
    [Tooltip("Resultado que dispara esta presentacion. Los estados sin preset los ignora la pantalla.")]
    [SerializeField] private GameState _state = GameState.Lose;

    [Tooltip("Titulo grande. Vacio = sin titulo (el GameObject se apaga).")]
    [SerializeField] private string _title = string.Empty;

    [SerializeField] private Color _titleColor = Color.white;

    [Tooltip("Tinte del overlay de fondo. Alpha 0 = fondo negro puro sin tinte.")]
    [SerializeField] private Color _vignetteColor = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Mostrar el boton de reintentar. Apagalo cuando no haya partida a la que volver.")]
    [SerializeField] private bool _showRetry = true;

    [Tooltip("Mostrar tiempo y modulos resueltos.")]
    [SerializeField] private bool _showStats;

    public GameState State       => _state;
    public string   Title        => _title;
    public Color    TitleColor   => _titleColor;
    public Color    VignetteColor => _vignetteColor;
    public bool     ShowRetry    => _showRetry;
    public bool     ShowStats    => _showStats;
}
