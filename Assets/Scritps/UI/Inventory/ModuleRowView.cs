using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of an individual module row (M1_Logs, M2_Logs, etc.).
/// Shows: label, red progress bar, status indicator.
/// </summary>
public class ModuleRowView : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI moduleLabel;   // "M1", "M2", "M3"
    [SerializeField] private TextMeshProUGUI statusText;    // "ACTIVE", "RESOLVED", etc.

    [Header("Progress bar")]
    [SerializeField] private Image progressFill;  // For the dynamic color

    // Name status colors (spec §3.2)
    private static readonly Color LabelActiveColor = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color LabelResolvedColor = new Color(0.10f, 0.42f, 0.10f); // #1a6a1a
    private static readonly Color LabelInactiveColor = new Color(0.16f, 0.16f, 0.16f); // #2a2a2a
    private static readonly Color LabelExplodedColor = new Color(0.35f, 0.16f, 0.00f); // #5a2a00

    public void Setup(ModuleRuntime module)
    {
        if (moduleLabel != null) moduleLabel.text = module.ModuleLogLabel;
        UpdateProgress(module);
        UpdateStatus(module);
    }

    public void UpdateProgress(ModuleRuntime module)
    {
        if (progressFill != null)
        {
            progressFill.fillAmount = module.Status switch
            {
                ModuleStatus.Active => module.TimerProgress,
                ModuleStatus.Resolved => 1f,
                _ => 0f
            };
            progressFill.color = module.BarColor;

        }
    }

    public void UpdateStatus(ModuleRuntime module)
    {
        // Module label
        if (moduleLabel != null)
        {
            moduleLabel.color = module.Status switch
            {
                ModuleStatus.Active => LabelActiveColor,
                ModuleStatus.Resolved => LabelResolvedColor,
                ModuleStatus.Inactive => LabelInactiveColor,
                ModuleStatus.Exploded => LabelExplodedColor,
                _ => LabelInactiveColor
            };
        }

        // Status text
        if (statusText != null)
        {
            statusText.text = module.Status switch
            {
                ModuleStatus.Active => "ACTIVE",
                ModuleStatus.Resolved => "RESOLVED",
                ModuleStatus.Inactive => "INACTIVE",
                ModuleStatus.Exploded => "EXPLODED",
                _ => "INACTIVE"
            };
        }

        UpdateProgress(module);
    }
}
