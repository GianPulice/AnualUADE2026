using TMPro;
using UnityEngine;

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
        if (parameterNameText != null) parameterNameText.text = name;
        if (parameterValueText != null) parameterValueText.text = value;
    }
}
