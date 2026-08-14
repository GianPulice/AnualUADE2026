/// <summary>
/// Lifecycle stage of a single module. The ModuleManager owns transitions between these values;
/// consumers should treat this as read-only.
/// </summary>
public enum ModuleStatus
{
    Inactive,   // Timer not started — player has not discovered the zone yet
    Active,     // Timer running — the module is counting down toward explosion
    Resolved,   // Player completed the associated puzzle before the timer expired
    Exploded    // Timer reached zero — penalty applied and permanent for the rest of the run
}
