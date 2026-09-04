using TMPro;
using UnityEngine;
using UnityEngine.UI;
// ══════════════════════════════════════════════════════════════════════════════
//  FailuresPipsView — right block of the HUD (failure pips)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW of the right block of the device HUD.
/// Shows N pips: the used ones in red, the remaining ones dark.
/// </summary>
public class FailuresPipsView : MonoBehaviour
{
    [Header("Pips (assign in the Inspector, order: 0=first)")]
    [SerializeField] private Image[] pips;  // 3 circular Images

    [Header("Text counter")]
    [SerializeField] private TextMeshProUGUI counterText;  // "X / 3"

    private static readonly Color PipUsedColor = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color PipRemainColor = new Color(0.067f, 0.067f, 0.067f); // #111

    /// <summary>
    /// Updates the pips based on the number of exploded modules.
    /// explodedCount: exploded modules (red pips).
    /// totalModules: total modules (pips to display).
    /// </summary>
    public void SetFailures(int explodedCount, int totalModules)
    {
        if (pips != null)
        {
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null) continue;
                pips[i].color = i < explodedCount ? PipUsedColor : PipRemainColor;
                pips[i].gameObject.SetActive(i < totalModules);
            }
        }

        if (counterText != null)
            counterText.text = $"{explodedCount}/{totalModules}";
    }
}
