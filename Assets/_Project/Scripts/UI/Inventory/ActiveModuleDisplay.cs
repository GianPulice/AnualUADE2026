using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ═══════════════════════════════════════════════════════════════════════════════
//  ActiveModuleDisplay — the left circular panel with the main timer
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW of the active module's circular display (upper left panel).
/// Shows the formatted timer and the "Active Module" label.
/// </summary>
public class ActiveModuleDisplay : MonoBehaviour
{
    [Header("Texts")]
    [Tooltip("00:00")][SerializeField] private TextMeshProUGUI timerText;
    [Tooltip("Time Left")][SerializeField] private TextMeshProUGUI timeLabelText;
    [Tooltip("ID of the active module")][SerializeField] private TextMeshProUGUI moduleIdText;

    [Header("Circular image / radial fill")]
    [SerializeField] private Image radialFill;               // Image with FillMethod = Radial360

    public void UpdateDisplay(ModuleRuntime module)
    {
        if (timerText != null)
            timerText.text = module.FormattedTime;

        if (moduleIdText != null)
            moduleIdText.text = module.ModuleID;

        if (radialFill != null)
            radialFill.fillAmount = module.TimerProgress;
    }
}
