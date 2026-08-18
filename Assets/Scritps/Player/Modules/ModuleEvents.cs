using System;

/// <summary>
/// Static bus for module lifecycle events. The manager is the sole publisher; the HUD, the player,
/// audio hooks and any future subsystem subscribe here.
///
/// Kept separate from InventoryEvents on purpose: modules are gameplay state, not inventory. The
/// old InventoryEvents.OnModule* forwarders were removed once every consumer moved here.
/// </summary>
public static class ModuleEvents
{
    /// <summary>A module changed state (Inactive → Active → Resolved/Exploded).</summary>
    public static event Action<ModuleRuntime> OnStateChanged;

    /// <summary>Active module's timer ticked. Fires every frame while a module is Active.</summary>
    public static event Action<ModuleRuntime> OnTimerTick;

    /// <summary>A module reached zero. Fired once per module, right before OnStateChanged with Exploded.</summary>
    public static event Action<ModuleRuntime> OnExploded;

    /// <summary>All modules have exploded — game over will be reported next frame.</summary>
    public static event Action OnAllModulesExploded;

    // ── Invokers (kept internal so only the manager can raise) ─────────────────────────
    internal static void RaiseStateChanged(ModuleRuntime m) => OnStateChanged?.Invoke(m);
    internal static void RaiseTimerTick(ModuleRuntime m) => OnTimerTick?.Invoke(m);
    internal static void RaiseExploded(ModuleRuntime m) => OnExploded?.Invoke(m);
    internal static void RaiseAllModulesExploded() => OnAllModulesExploded?.Invoke();
}
