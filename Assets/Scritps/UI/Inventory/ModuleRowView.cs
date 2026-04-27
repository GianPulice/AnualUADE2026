using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW de una fila de módulo individual (M1_Logs, M2_Logs, etc.).
/// Muestra: label, barra de progreso roja, status indicator.
/// </summary>
public class ModuleRowView : MonoBehaviour
{
    [Header("Textos")]
    [SerializeField] private TextMeshProUGUI moduleLabel;   // "M1", "M2", "M3"
    [SerializeField] private TextMeshProUGUI statusText;    // "ACTIVO", "RESUELTO", etc.

    [Header("Barra de progreso")]
    [SerializeField] private Image progressFill;  // Para color dinámico

    // Colores de estado del nombre (spec §3.2)
    private static readonly Color LabelActiveColor = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color LabelResolvedColor = new Color(0.10f, 0.42f, 0.10f); // #1a6a1a
    private static readonly Color LabelInactiveColor = new Color(0.16f, 0.16f, 0.16f); // #2a2a2a
    private static readonly Color LabelExplodedColor = new Color(0.35f, 0.16f, 0.00f); // #5a2a00

    public void Setup(ModuleData module)
    {
        if (moduleLabel != null) moduleLabel.text = module.ModuleLogLabel;
        UpdateProgress(module);
        UpdateStatus(module);
    }

    public void UpdateProgress(ModuleData module)
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

    public void UpdateStatus(ModuleData module)
    {
        // Label del módulo
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

        // Texto de estado
        if (statusText != null)
        {
            statusText.text = module.Status switch
            {
                ModuleStatus.Active => "ACTIVO",
                ModuleStatus.Resolved => "RESUELTO",
                ModuleStatus.Inactive => "INACTIVO",
                ModuleStatus.Exploded => "EXPLOTADO",
                _ => "INACTIVO"
            };
        }

        UpdateProgress(module);
    }
}
