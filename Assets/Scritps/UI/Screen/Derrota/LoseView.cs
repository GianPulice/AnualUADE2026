using UnityEngine;

// Pantalla de muerte por captura del Nemesis.
// Neutra: sin título, fondo negro puro.
// Botones: [Cargar último guardado] (Retry) y [Ir al menú] (Exit).
public class LoseView : BaseResultView
{
    protected override void Awake()
    {
        base.Awake();
        HideNextLevelButton();
        HideMainMenuButton();
    }
}
