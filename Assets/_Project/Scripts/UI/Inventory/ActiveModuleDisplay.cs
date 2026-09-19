using UnityEngine;
using UnityEngine.UI;

// ═══════════════════════════════════════════════════════════════════════════════
//  ActiveModuleDisplay — the circle around the inventory's main timer
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Drains the inventory's timer circle (upper left panel) with the active module's remaining time.
///
/// It drives itself off <see cref="ModuleEvents"/>: nothing ever called the old UpdateDisplay, so
/// the circle sat full forever. Only the fill is its job — the texts next to it belong to
/// <see cref="ActiveModuleTimerView"/>, and writing them from here as well gave them two owners.
///
/// Subscribed in OnEnable/OnDisable on purpose, like <see cref="ModuleHUDView"/>: the panel is
/// hidden with the inventory, and it re-reads the manager every time it comes back, so nothing
/// that happened while closed is lost.
/// </summary>
public class ActiveModuleDisplay : MonoBehaviour
{
    [Header("Circular image / radial fill")]
    [SerializeField] private Image radialFill;               // Image with FillMethod = Radial360

    private void OnEnable()
    {
        ModuleEvents.OnTimerTick += HandleModuleChanged;
        ModuleEvents.OnStateChanged += HandleModuleChanged;
        ModuleEvents.OnTimeAdjusted += HandleTimeAdjusted;
        Refresh();
    }

    private void OnDisable()
    {
        ModuleEvents.OnTimerTick -= HandleModuleChanged;
        ModuleEvents.OnStateChanged -= HandleModuleChanged;
        ModuleEvents.OnTimeAdjusted -= HandleTimeAdjusted;
    }

    private void Start() => Refresh(); // the manager may come up after the first OnEnable

    private void HandleModuleChanged(ModuleRuntime _) => Refresh();
    private void HandleTimeAdjusted(ModuleRuntime _, float __) => Refresh();

    private void Refresh()
    {
        if (radialFill == null) return;

        // Exists rather than 'Instance == null': the property logs a warning every time it is read
        // while null.
        ModuleRuntime active = ModuleManager.Exists ? ModuleManager.Instance.GetActiveModule() : null;
        radialFill.fillAmount = active != null ? active.TimerProgress : 0f;
    }
}
