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






