using UnityEngine;

/// <summary>
/// State of one stabilization sequence (Central Puzzle 2 — Ventilation Hub, spec §3): which check
/// the player is on, where its success zone landed this attempt, and whether a needle angle hits
/// it. Pure data and rules — the needle, the time and the input belong to the controller.
///
/// A failed attempt does not move the sequence: the same check is played again with a new zone.
/// Angles follow a clock face (0 = twelve o'clock, clockwise), the convention of UIRingArc.
/// </summary>
public class StabilizationCheckModel : BaseScreenModel
{
    private SO_StabilizationPuzzleData.StabilizationCheck[] checks = new SO_StabilizationPuzzleData.StabilizationCheck[0];
    private float zoneStartMin;
    private float zoneStartMax;

    /// <summary>Index of the check being played. Equal to <see cref="TotalChecks"/> once all are passed.</summary>
    public int CheckIndex { get; private set; }
    public int TotalChecks => checks.Length;
    public bool IsComplete => TotalChecks > 0 && CheckIndex >= TotalChecks;

    /// <summary>Where this attempt's success zone starts, in degrees.</summary>
    public float ZoneStart { get; private set; }

    public SO_StabilizationPuzzleData.StabilizationCheck CurrentCheck =>
        checks[Mathf.Clamp(CheckIndex, 0, Mathf.Max(0, TotalChecks - 1))];

    public float ZoneDegrees => CurrentCheck.successZoneWidth * 360f;
    public float NeedleDegreesPerSecond => CurrentCheck.needleSpeed * 360f;

    public override void Initialize()
    {
        checks = new SO_StabilizationPuzzleData.StabilizationCheck[0];
        CheckIndex = 0;
        ZoneStart = 0f;
        IsInitialized = true;
    }

    /// <summary>A fresh sequence from the first (hardest) check.</summary>
    public void Configure(SO_StabilizationPuzzleData data)
    {
        checks = data != null ? data.Checks : new SO_StabilizationPuzzleData.StabilizationCheck[0];
        zoneStartMin = data != null ? data.ZoneStartMin : 90f;
        zoneStartMax = data != null ? data.ZoneStartMax : 300f;
        CheckIndex = 0;
        IsInitialized = true;
        NotifyDataChanged();
    }

    /// <summary>Places the zone for a new attempt of the current check.</summary>
    public void RollZone()
    {
        ZoneStart = Random.Range(zoneStartMin, zoneStartMax);
        NotifyDataChanged();
    }

    /// <summary>Whether a press with the needle at <paramref name="needleAngle"/> passes the check.</summary>
    public bool Judge(float needleAngle) =>
        TotalChecks > 0 && !IsComplete && ClockArc.Contains(needleAngle, ZoneStart, ZoneDegrees);

    /// <summary>The current check was passed: on to the next one.</summary>
    public void Advance()
    {
        if (IsComplete) return;
        CheckIndex++;
        NotifyDataChanged();
    }
}
