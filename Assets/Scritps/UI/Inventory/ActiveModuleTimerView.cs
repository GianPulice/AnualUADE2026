using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ══════════════════════════════════════════════════════════════════════════════
//  ActiveModuleTimerView — left block of the HUD (large timer)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// VIEW of the left block of the device HUD.
/// Shows the MM:SS timer of the active module in blinking red (spec §3.1).
/// If there is no active module, it shows "--:--" in grey.
/// </summary>
public class ActiveModuleTimerView : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI timerText;      // 22px, red #cc1a1a with blink
    [SerializeField] private TextMeshProUGUI moduleIdText;   // "// active module"

    [Header("Timer blink")]
    [SerializeField] private float blinkSpeed = 2f;  // cycles per second

    private static readonly Color ActiveTimerColor   = new Color(0.80f, 0.10f, 0.10f); // #cc1a1a
    private static readonly Color InactiveTimerColor = new Color(0.35f, 0.35f, 0.35f);

    private bool isActive = false;
    private float blinkTime = 0f;

    void Update()
    {
        if (!isActive || timerText == null) return;

        // Blink: opacity oscillates continuously between 1.0 and 0.4 (spec §3.1)
        blinkTime += Time.unscaledDeltaTime * blinkSpeed;
        float alpha = Mathf.Lerp(0.4f, 1.0f, (Mathf.Sin(blinkTime * Mathf.PI * 2f) + 1f) * 0.5f);

        Color c = ActiveTimerColor;
        c.a = alpha;
        timerText.color = c;
    }

    /// <summary>Updates the timer display. Pass null if there is no active module.</summary>
    public void UpdateTimer(ModuleRuntime module)
    {
        isActive = module != null && module.Status == ModuleStatus.Active;

        if (timerText == null) return;

        if (module == null || !isActive)
        {
            timerText.text  = "--:--";
            timerText.color = InactiveTimerColor;

            if (moduleIdText != null) moduleIdText.text = "no module";
            return;
        }

        timerText.text = module.FormattedTime;
        if (moduleIdText != null) moduleIdText.text = module.ModuleID;
    }
}
