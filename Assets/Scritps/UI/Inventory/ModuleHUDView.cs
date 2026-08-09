using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VIEW of the device HUD inside the inventory (spec §3).
///
/// Responsibilities (SRP):
///   - Show the active module's timer (left block)
///   - Show the status bars of M1/M2/M3 (centre block)
///   - Show the remaining-failure pips (right block)
///   - Subscribe to module events to update in real time
///   - It does NOT manipulate business or module logic
///
/// The timers use unscaledDeltaTime in the Controller, so the tick events arrive correctly
/// even when Time.timeScale == 0.
/// </summary>
public class ModuleHUDView : MonoBehaviour
{
    // ── Sub-views ─────────────────────────────────────────────────────────────

    [Header("Left block — Active module timer")]
    [SerializeField] private ActiveModuleTimerView activeTimerView;

    [Header("Centre block — Module status")]
    [SerializeField] private Transform moduleRowContainer;
    [SerializeField] private ModuleRowView moduleRowPrefab;

    [Header("Right block — Remaining failures")]
    [SerializeField] private FailuresPipsView failuresPipsView;

    // ── State ─────────────────────────────────────────────────────────────────

    private Dictionary<string, ModuleRowView> rowMap = new Dictionary<string, ModuleRowView>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        InventoryEvents.OnModuleTimerTick += HandleTimerTick;
        InventoryEvents.OnModuleStateChanged += HandleStateChanged;
        InventoryEvents.OnModuleExploded += HandleModuleExploded;
    }

    void OnDestroy()
    {
        InventoryEvents.OnModuleTimerTick -= HandleTimerTick;
        InventoryEvents.OnModuleStateChanged -= HandleStateChanged;
        InventoryEvents.OnModuleExploded -= HandleModuleExploded;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Initializes the module rows. Called by the Controller in Start.</summary>
    public void Initialize(List<ModuleData> modules)
    {
        rowMap.Clear();

        foreach (ModuleData module in modules)
        {
            ModuleRowView row = Instantiate(moduleRowPrefab, moduleRowContainer);
            row.Setup(module);
            rowMap[module.ModuleID] = row;
        }

        // Initial state of the timer and the pips
        RefreshActiveTimer(modules);
        RefreshFailuresPips(modules);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleTimerTick(ModuleData module)
    {
        // Update this module's row
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateProgress(module);

        // If it is the active module, update the left block too
        if (module.Status == ModuleStatus.Active)
            activeTimerView?.UpdateTimer(module);
    }

    private void HandleStateChanged(ModuleData module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips(InventoryManagerUI.Instance.GetAllModules());

        // If the active module changed, update the timer block
        RefreshActiveTimer(InventoryManagerUI.Instance.GetAllModules());
    }

    private void HandleModuleExploded(ModuleData module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips(InventoryManagerUI.Instance.GetAllModules());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshActiveTimer(List<ModuleData> modules)
    {
        ModuleData active = InventoryManagerUI.Instance.GetActiveModule();
        activeTimerView?.UpdateTimer(active); // null = no active module -> shows "--:--"
    }

    private void RefreshFailuresPips(List<ModuleData> modules)
    {
        int explodedCount = InventoryManagerUI.Instance.GetExplodedCount();
        failuresPipsView?.SetFailures(explodedCount, modules.Count);
    }
}
