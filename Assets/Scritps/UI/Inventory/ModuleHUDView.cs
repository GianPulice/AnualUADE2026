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
    private bool initialized;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnStateChanged += HandleStateChanged;
        ModuleEvents.OnExploded += HandleModuleExploded;
    }

    void OnDisable()
    {
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnStateChanged -= HandleStateChanged;
        ModuleEvents.OnExploded -= HandleModuleExploded;
    }

    void Start()
    {
        // The ModuleManager lives in a persistent scene and initializes in Awake, so by the time
        // any inventory Start runs it is ready.
        TryInitialize();
    }

    // ── Initialization ────────────────────────────────────────────────────────

    private void TryInitialize()
    {
        if (initialized) return;
        if (ModuleManager.Instance == null) return;

        IReadOnlyList<ModuleRuntime> modules = ModuleManager.Instance.GetAllModules();

        rowMap.Clear();
        foreach (Transform child in moduleRowContainer) Destroy(child.gameObject);

        foreach (ModuleRuntime module in modules)
        {
            ModuleRowView row = Instantiate(moduleRowPrefab, moduleRowContainer);
            row.Setup(module);
            rowMap[module.ModuleID] = row;
        }

        RefreshActiveTimer();
        RefreshFailuresPips();
        initialized = true;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleTimerTick(ModuleRuntime module)
    {
        if (!initialized) TryInitialize();

        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateProgress(module);

        if (module.Status == ModuleStatus.Active)
            activeTimerView?.UpdateTimer(module);
    }

    private void HandleStateChanged(ModuleRuntime module)
    {
        if (!initialized) TryInitialize();

        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips();
        RefreshActiveTimer();
    }

    private void HandleModuleExploded(ModuleRuntime module)
    {
        if (!initialized) TryInitialize();

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
