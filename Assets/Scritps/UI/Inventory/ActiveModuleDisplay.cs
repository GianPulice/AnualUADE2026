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
    [Tooltip("00:00")][SerializeField] private TextMeshProUGUI timerText;      
    [Tooltip("Time Left")][SerializeField] private TextMeshProUGUI timeLabelText;  
    [Tooltip("ID del módulo activo")][SerializeField] private TextMeshProUGUI moduleIdText;    

    [Header("Imagen circular / radial fill")]
    [SerializeField] private Image radialFill;               // Image con FillMethod = Radial360

    public void UpdateDisplay(ModuleData module)
    {
        if (timerText != null)
            timerText.text = module.FormattedTime;

        if (moduleIdText != null)
            moduleIdText.text = module.ModuleID;

        if (radialFill != null)
            radialFill.fillAmount = module.TimerProgress;
    }
}






