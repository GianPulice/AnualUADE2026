/// <summary>
/// Every line slot of the Architect voice system (architect_voice_system_spec v1.2, §6). One slot
/// can hold several variants (captures, randoms).
///
/// Serialized as its index in SO_ArchitectLineBank: APPEND new values, never insert or reorder.
/// </summary>
public enum ArchitectLineID
{
    WakeUpMoment1,      // ARC_01a — no input while it plays
    WakeUpMoment2,      // ARC_01b — chained after ARC_01a
    NemesisReleased,    // ARC_02
    GameEnd,            // ARC_03 — last module resolved
    ContextZone1,       // ARC_CTX_01
    ContextZone2,       // ARC_CTX_02
    ContextCentral1,    // ARC_CTX_03 — M1 resolved
    ContextCentral2,    // ARC_CTX_04 — M2 resolved
    ContextFirstNote,   // ARC_CTX_05
    TimerCritical,      // ARC_04 — 3 variants
    ExplodedLegs,       // ARC_05
    ExplodedChest,      // ARC_06
    ExplodedHead,       // ARC_07
    ResolvedInTime,     // ARC_08 — 2 variants
    Captured,           // ARC_09 — 15 variants
    GameOver,           // ARC_10 — absolute priority
    Idle,               // ARC_R01..R23 — inactivity pool
}

/// <summary>Spec §1.2. Decides what happens to a trigger that cannot play right now.</summary>
public enum ArchitectLineCategory
{
    Mandatory,
    Context,
    Conditional,
    Random,
}
