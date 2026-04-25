using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VIEW de una fila de módulo individual (M1_Logs, M2_Logs, etc.).
/// Muestra: label, barra de progreso roja, status indicator.
/// </summary>
public class ModuleRowView : MonoBehaviour
{
    [Header("Textos")]
    [SerializeField] private TextMeshProUGUI moduleLogLabel;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Barra de progreso")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private Image progressFill;  // Para cambiar color según estado

    [Header("Colores de estado")]
    [SerializeField] private Color activeColor  = new Color(0.8f, 0.1f, 0.1f);  // Rojo (tu UI)
    [SerializeField] private Color warningColor = new Color(1f,   0.6f, 0f);     // Naranja
    [SerializeField] private Color failureColor = new Color(0.3f, 0.3f, 0.3f);  // Gris
    [SerializeField] private Color inactiveColor= new Color(0.2f, 0.2f, 0.2f);

    // ── API pública ──────────────────────────────────────────────────────────

    public void Setup(ModuleData module)
    {
        if (moduleLogLabel != null) moduleLogLabel.text = module.moduleLabel;
        UpdateTimer(module);
        UpdateStatus(module);
    }

    public void UpdateTimer(ModuleData module)
    {
        if (progressBar != null)
            progressBar.value = module.TimerProgress;
    }

    public void UpdateStatus(ModuleData module)
    {
        if (statusText != null)
            statusText.text = module.status.ToString();

        if (progressFill != null)
        {
            progressFill.color = module.status switch
            {
                ModuleStatus.Active   => activeColor,
                ModuleStatus.Warning  => warningColor,
                ModuleStatus.Failure  => failureColor,
                ModuleStatus.Inactive => inactiveColor,
                _                     => activeColor
            };
        }
    }

    public void PlayFailureAnimation()
    {
        // Podés reemplazar por LeanTween o una animación de Unity
        // Por ahora hace un flash rápido en el progress fill
        if (progressFill != null)
        {
            LeanTween.value(gameObject, 1f, 0f, 0.3f)
                .setOnUpdate((float val) => {
                    Color c = progressFill.color;
                    c.a = val;
                    progressFill.color = c;
                })
                .setLoopPingPong(3);
        }
    }
}
