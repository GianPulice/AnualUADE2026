using TMPro;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  ItemParameterRow — one parameter row in the details panel
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>VIEW of an item parameter row (e.g. "Item Parameters  YES/NO").</summary>
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
