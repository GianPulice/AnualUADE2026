using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ═══════════════════════════════════════════════════════════════════════════════
//  ActiveModuleDisplay — el panel circular izquierdo con timer principal
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW del display circular del módulo activo (panel izquierdo superior).
/// Muestra el timer formateado y el label "Active Module".
/// </summary>
public class ActiveModuleDisplay : MonoBehaviour
{
    [Header("Textos")]
    [SerializeField] private TextMeshProUGUI timerText;      // "00:00"
    [SerializeField] private TextMeshProUGUI timeLabelText;  // "Time Left"
    [SerializeField] private TextMeshProUGUI moduleIdText;   // ID del módulo activo

    [Header("Imagen circular / radial fill")]
    [SerializeField] private Image radialFill;               // Image con FillMethod = Radial360

    public void UpdateDisplay(ModuleData module)
    {
        if (timerText != null)
            timerText.text = module.FormattedTime;

        if (moduleIdText != null)
            moduleIdText.text = module.moduleID;

        if (radialFill != null)
            radialFill.fillAmount = module.TimerProgress;
    }
}


// ═══════════════════════════════════════════════════════════════════════════════
//  FailuresCounterView — contador de fallos totales (esquina superior derecha)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>VIEW del contador de fallos totales.</summary>
public class FailuresCounterView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI labelText;   // "Failures Left"

    public void SetCount(int count)
    {
        if (countText != null)
            countText.text = count.ToString();
    }
}


// ═══════════════════════════════════════════════════════════════════════════════
//  ItemParameterRow — una fila de parámetro en el panel de detalles
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>VIEW de una fila de parámetro del ítem (ej: "Item Parameters  YES/NO").</summary>
public class ItemParameterRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI parameterNameText;
    [SerializeField] private TextMeshProUGUI parameterValueText;

    public void SetParameter(string name, string value)
    {
        if (parameterNameText != null)  parameterNameText.text  = name;
        if (parameterValueText != null) parameterValueText.text = value;
    }
}
