using UnityEngine;

[CreateAssetMenu(fileName = "ModuleData", menuName = "Scriptable Objects/ModuleData")]
public class ModuleData : ScriptableObject
{
    [Header("ID")]
    public string ModuleID;
    public string ModuleLogLabel;

    [Header("State")]
    public ModuleStatus Status = ModuleStatus.Inactive;

    [Header("Timer")]
    public float TimerDuration;     // Duración total en segundos
    public float TimeRemaining;     // Decrementado por el Controller con unscaledDeltaTime
    public bool IsTimerRunning;

    // Solo lectura para la UI
    public float TimerProgress => TimerDuration > 0f
          ? Mathf.Clamp01(TimeRemaining / TimerDuration)
          : 0f;

    public string FormattedTime
    {
        get
        {
            if (!IsTimerRunning && Status != ModuleStatus.Active)
                return "--:--";

            int minutes = Mathf.FloorToInt(TimeRemaining / 60f);
            int seconds = Mathf.FloorToInt(TimeRemaining % 60f);
            return $"{minutes:00}:{seconds:00}";
        }
    }
    public Color BarColor
    {
        get
        {
            if (Status != ModuleStatus.Active) return GetStatusBarColor();

            return TimerProgress switch
            {
                > 0.50f => new Color(0.80f, 0.10f, 0.10f), // #cc1a1a rojo
                > 0.25f => new Color(0.67f, 0.27f, 0.00f), // #aa4400 naranja
                _ => new Color(0.53f, 0.13f, 0.00f)  // #882200 rojo oscuro
            };
        }
    }

    private Color GetStatusBarColor()
    {
        return Status switch
        {
            ModuleStatus.Resolved => new Color(0.10f, 0.42f, 0.10f), // #1a6a1a verde
            ModuleStatus.Exploded => new Color(0.35f, 0.16f, 0.00f), // #5a2a00 marrón
            ModuleStatus.Inactive => new Color(0.13f, 0.13f, 0.13f), // #222 gris
            _ => new Color(0.13f, 0.13f, 0.13f)
        };
    }
}
public enum ModuleStatus
{
    Inactive,   // No iniciado — barra gris
    Active,     // Corriendo — barra roja, timer vivo
    Resolved,   // Completado correctamente — barra verde, 100% fijo
    Exploded    // Timer llegó a cero — barra marrón, 0%
}