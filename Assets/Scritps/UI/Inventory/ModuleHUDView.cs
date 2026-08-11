using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VIEW of the device HUD inside the inventory (spec §3).
///
/// Responsibilities (SRP):
///   - Show the active module's timer (left block)
///   - Show the status bars of M1/M2/M3 (centre block)
///   - Show the remaining-failure pips (right block)
///   - Subscribe to <see cref="ModuleEvents"/> to update in real time
///   - Re-sync with <see cref="ModuleManager"/> every time it becomes visible so events that
///     fired while the panel was hidden do not leave the display stale
///   - It does NOT manipulate business or module logic
///
/// The timers use unscaledDeltaTime in the ModuleManager, so the tick events arrive correctly
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

    private readonly Dictionary<string, ModuleRowView> rowMap = new Dictionary<string, ModuleRowView>();
    private bool rowsBuilt;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnStateChanged += HandleStateChanged;
        ModuleEvents.OnExploded += HandleModuleExploded;

        // Whenever the panel becomes visible again, resync with the manager. Events fired while
        // the HUD was disabled (inventory closed during an explosion or a resolution) never
        // reached us; without this refresh the display keeps showing pre-close state.
        RefreshAll();
    }

    void OnDisable()
    {
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnStateChanged -= HandleStateChanged;
        ModuleEvents.OnExploded -= HandleModuleExploded;
    }

    void Start()
    {
        // ModuleManager may not exist yet when OnEnable first runs (script execution order).
        // Retry from Start when the persistent scene is guaranteed to be up.
        RefreshAll();
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (ModuleManager.Instance == null) return;

        if (!rowsBuilt) BuildRows();

        // Pull the current state from the manager and push it to every row, regardless of
        // whether an event was raised for it. This is what fixes the "stale HUD after events
        // fired while closed" bug.
        foreach (ModuleRuntime module in ModuleManager.Instance.GetAllModules())
        {
            if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
                row.UpdateStatus(module);
        }

        RefreshActiveTimer();
        RefreshFailuresPips();
    }

    private void BuildRows()
    {
        rowMap.Clear();
        foreach (Transform child in moduleRowContainer) Destroy(child.gameObject);

        foreach (ModuleRuntime module in ModuleManager.Instance.GetAllModules())
        {
            ModuleRowView row = Instantiate(moduleRowPrefab, moduleRowContainer);
            row.Setup(module);
            rowMap[module.ModuleID] = row;
        }
        rowsBuilt = true;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleTimerTick(ModuleRuntime module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateProgress(module);

        if (module.Status == ModuleStatus.Active)
            activeTimerView?.UpdateTimer(module);
    }

    private void HandleStateChanged(ModuleRuntime module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshActiveTimer();
        RefreshFailuresPips();
    }

    private void HandleModuleExploded(ModuleRuntime module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshActiveTimer()
    {
        if (ModuleManager.Instance == null) { activeTimerView?.UpdateTimer(null); return; }
        activeTimerView?.UpdateTimer(ModuleManager.Instance.GetActiveModule());
    }

    private void RefreshFailuresPips()
    {
        if (ModuleManager.Instance == null || failuresPipsView == null) return;
        failuresPipsView.SetFailures(
            ModuleManager.Instance.GetExplodedCount(),
            ModuleManager.Instance.TotalModules);
    }
}
