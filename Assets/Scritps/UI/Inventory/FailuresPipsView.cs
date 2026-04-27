using TMPro;
using UnityEngine;
using UnityEngine.UI;
// ══════════════════════════════════════════════════════════════════════════════
//  FailuresPipsView — bloque derecho del HUD (pips de fallos)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW del bloque derecho del HUD del dispositivo.
/// Muestra N pips: los usados en rojo, los restantes en oscuro.
/// </summary>
public class FailuresPipsView : MonoBehaviour
{
    [Header("Pips (asignar en Inspector, orden: 0=primero)")]
    [SerializeField] private Image[] pips;  // 3 Images circulares

    [Header("Contador de texto")]
    [SerializeField] private TextMeshProUGUI counterText;  // "X / 3"

    private static readonly Color PipUsedColor = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color PipRemainColor = new Color(0.067f, 0.067f, 0.067f); // #111

    /// <summary>
    /// Actualiza los pips según la cantidad de módulos explotados.
    /// explodedCount: módulos explotados (pips rojos).
    /// totalModules: total de módulos (pips a mostrar).
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
            counterText.text = $"{explodedCount} / {totalModules}";
    }
}
