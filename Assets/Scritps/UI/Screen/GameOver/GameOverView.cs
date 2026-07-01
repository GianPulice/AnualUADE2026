using System;
using TMPro;
using UnityEngine;

// Pantalla de Game Over por módulos (los 3 explotan).
// Título GAME OVER en rojo #CC1A1A — único uso de rojo grande en la UI.
// Vignette roja muy sutil (#0D0000) siempre encendida.
// Botón [IR AL MENÚ]. Sin retry (el save ya fue borrado).
public class GameOverView : BaseResultView
{
    [Header("Game Over — específico")]
    [SerializeField] private TextMeshProUGUI _statsText;

    public event Action OnMenuClicked;

    protected override void Awake()
    {
        base.Awake();
        HideRetryButton();
        HideNextLevelButton();

        OnMainMenuClicked += () => OnMenuClicked?.Invoke();
    }

    public override void SetData(GameResultModel model)
    {
        if (_statsText == null) return;

        int minutes = Mathf.FloorToInt(model.Time / 60f);
        int seconds = Mathf.FloorToInt(model.Time % 60f);
        _statsText.text =
            $"Tiempo: {minutes:00}:{seconds:00}\n" +
            $"Módulos resueltos: {model.CompletedModules} / 3";
    }
}
