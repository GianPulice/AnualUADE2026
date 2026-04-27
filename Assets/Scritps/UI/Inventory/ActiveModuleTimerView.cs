using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ══════════════════════════════════════════════════════════════════════════════
//  ActiveModuleTimerView — bloque izquierdo del HUD (timer en grande)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW del bloque izquierdo del HUD del dispositivo.
/// Muestra el timer MM:SS del módulo activo en rojo parpadeante (spec §3.1).
/// Si no hay módulo activo, muestra "--:--" en gris.
/// </summary>
public class ActiveModuleTimerView : MonoBehaviour
{
    [Header("Textos")]
    [SerializeField] private TextMeshProUGUI timerText;      // 22px, rojo #cc1a1a con blink
    [SerializeField] private TextMeshProUGUI moduleIdText;   // "// modulo activo"

    [Header("Blink del timer")]
    [SerializeField] private float blinkSpeed = 2f;  // ciclos por segundo

    private static readonly Color ActiveTimerColor   = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color InactiveTimerColor = new Color(0.35f, 0.35f, 0.35f);

    private bool isActive = false;
    private float blinkTime = 0f;

    void Update()
    {
        if (!isActive || timerText == null) return;

        // Blink: opacidad oscila entre 1.0 y 0.4 continuamente (spec §3.1)
        blinkTime += Time.unscaledDeltaTime * blinkSpeed;
        float alpha = Mathf.Lerp(0.4f, 1.0f, (Mathf.Sin(blinkTime * Mathf.PI * 2f) + 1f) * 0.5f);

        Color c = ActiveTimerColor;
        c.a = alpha;
        timerText.color = c;
    }

    /// <summary>Actualiza el display del timer. Pasar null si no hay módulo activo.</summary>
    public void UpdateTimer(ModuleData module)
    {
        isActive = module != null && module.Status == ModuleStatus.Active;

        if (timerText == null) return;

        if (module == null || !isActive)
        {
            timerText.text  = "--:--";
            timerText.color = InactiveTimerColor;

            if (moduleIdText != null) moduleIdText.text = "sin módulo";
            return;
        }

        timerText.text = module.FormattedTime;
        if (moduleIdText != null) moduleIdText.text = module.ModuleID;
    }
}
