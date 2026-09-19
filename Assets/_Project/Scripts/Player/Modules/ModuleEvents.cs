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

    /// <summary>
    /// The active module's timer jumped by a discrete amount outside the regular countdown: a skill
    /// check miss (negative) or a perfect hit (positive). The delta is what was actually applied after
    /// clamping, so a view can print it as is. Also raised while the timer is paused, where no
    /// <see cref="OnTimerTick"/> will follow to show the new value.
    /// </summary>
    public static event Action<ModuleRuntime, float> OnTimeAdjusted;

    /// <summary>A module reached zero. Fired once per module, right before OnStateChanged with Exploded.</summary>
    public static event Action<ModuleRuntime> OnExploded;

    /// <summary>All modules have exploded — game over will be reported next frame.</summary>
    public static event Action OnAllModulesExploded;

    /// <summary>
    /// The exploded module's penalty takes effect on the player (limp, blindness, sprint loss).
    ///
    /// Separate from <see cref="OnExploded"/> because the two no longer happen on the same frame:
    /// with an explosion cinematic on screen the player must not start limping, or go blind, while
    /// the camera is still showing the blast. The cinematic raises this once control is handed back;
    /// with no cinematic, ModuleManager raises it right after OnExploded. Exactly once per explosion.
    /// </summary>
    public static event Action<ModuleRuntime> OnPenaltyApplied;

    /// <summary>
    /// Set by the explosion presentation (ModuleExplosionSequence) while it is alive: it then owns
    /// raising <see cref="OnPenaltyApplied"/>, and ModuleManager does not. False = ModuleManager
    /// raises it itself, so a scene without the presentation still gets its penalties.
    /// </summary>
    public static bool PenaltyPresenterActive { get; set; }

    /// <summary>
    /// The explosion VFX of this module has just gone off on screen. Raised by the presentation
    /// (ModuleExplosionSequence) on the frame the effect spawns, which with a cinematic is well after
    /// <see cref="OnExploded"/>: anything that should change "when the blast is seen" (the LED
    /// turning red) waits for this instead of the state change.
    /// </summary>
    public static event Action<ModuleRuntime> OnExplosionShown;

    // ── Invokers (kept internal so only the manager can raise) ─────────────────────────
    internal static void RaiseStateChanged(ModuleRuntime m) => OnStateChanged?.Invoke(m);
    internal static void RaiseTimerTick(ModuleRuntime m) => OnTimerTick?.Invoke(m);
    internal static void RaiseTimeAdjusted(ModuleRuntime m, float delta) => OnTimeAdjusted?.Invoke(m, delta);
    internal static void RaiseExploded(ModuleRuntime m) => OnExploded?.Invoke(m);
    internal static void RaiseAllModulesExploded() => OnAllModulesExploded?.Invoke();
    internal static void RaisePenaltyApplied(ModuleRuntime m) => OnPenaltyApplied?.Invoke(m);
    internal static void RaiseExplosionShown(ModuleRuntime m) => OnExplosionShown?.Invoke(m);
}
