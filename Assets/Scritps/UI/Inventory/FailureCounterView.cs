// ═══════════════════════════════════════════════════════════════════════════════
//  FailuresCounterView — contador de fallos totales (esquina superior derecha)
// ═══════════════════════════════════════════════════════════════════════════════

using TMPro;
using UnityEngine;

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
