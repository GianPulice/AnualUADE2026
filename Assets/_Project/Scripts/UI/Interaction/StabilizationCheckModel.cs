using UnityEngine;

/// <summary>
/// State of one stabilization sequence (Central Puzzle 2 — Ventilation Hub): which check the
/// player is on, where its success zone landed this attempt, whether a needle angle hits it (or its
/// orange perfect slice), and whether this check is eased by a perfect on the previous one. Pure
/// data and rules — the needle, the time and the input belong to the controller.
///
/// What a failed attempt does (repeat the check, or restart) is the controller's call, from the asset.
/// Angles follow a clock face (0 = twelve o'clock, clockwise), the convention of UIRingArc.
/// </summary>
public class StabilizationCheckModel : BaseScreenModel
{
    private SO_StabilizationPuzzleData.StabilizationCheck[] checks = new SO_StabilizationPuzzleData.StabilizationCheck[0];
    private float zoneStartMin;
    private float zoneStartMax;
    private float perfectZoneWidening;
    private float perfectNeedleSlowdown;

    /// <summary>Index of the check being played. Equal to <see cref="TotalChecks"/> once all are passed.</summary>
    public int CheckIndex { get; private set; }
    public int TotalChecks => checks.Length;
    public bool IsComplete => TotalChecks > 0 && CheckIndex >= TotalChecks;

    /// <summary>Where this attempt's success zone starts, in degrees.</summary>
    public float ZoneStart { get; private set; }

    /// <summary>The previous check was passed with a perfect: this one has a wider zone and a slower
    /// needle, relative to its own values. Kept through retries of this check.</summary>
    public bool IsEased { get; private set; }

    public SO_StabilizationPuzzleData.StabilizationCheck CurrentCheck =>
        checks[Mathf.Clamp(CheckIndex, 0, Mathf.Max(0, TotalChecks - 1))];

    public float ZoneDegrees =>
        Mathf.Min(360f, CurrentCheck.successZoneWidth * 360f * (IsEased ? 1f + perfectZoneWidening : 1f));

    /// <summary>The perfect slice sits at the start of the zone and never outgrows it.</summary>
    public float PerfectDegrees => Mathf.Min(CurrentCheck.perfectZoneWidth * 360f, ZoneDegrees);

    public float NeedleDegreesPerSecond =>
        CurrentCheck.needleSpeed * 360f * (IsEased ? 1f - perfectNeedleSlowdown : 1f);

    public override void Initialize()
    {
        checks = new SO_StabilizationPuzzleData.StabilizationCheck[0];
        CheckIndex = 0;
        ZoneStart = 0f;
        IsEased = false;
        IsInitialized = true;
    }

    /// <summary>A fresh sequence from the first (easiest) check.</summary>
    public void Configure(SO_StabilizationPuzzleData data)
    {
        checks = data != null ? data.Checks : new SO_StabilizationPuzzleData.StabilizationCheck[0];
        zoneStartMin = data != null ? data.ZoneStartMin : 90f;
        zoneStartMax = data != null ? data.ZoneStartMax : 300f;
        perfectZoneWidening = data != null ? data.PerfectZoneWidening : 0f;
        perfectNeedleSlowdown = data != null ? data.PerfectNeedleSlowdown : 0f;
        CheckIndex = 0;
        IsEased = false;
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

    /// <summary>Whether a press with the needle at <paramref name="needleAngle"/> lands in the
    /// orange perfect slice (which is inside the zone, so it also passes).</summary>
    public bool IsPerfect(float needleAngle) =>
        Judge(needleAngle) && ClockArc.Contains(needleAngle, ZoneStart, PerfectDegrees);

    /// <summary>Back to the first check (a failure with the Restart From First Check rule). Any
    /// ease is lost with the progress.</summary>
    public void Restart()
    {
        CheckIndex = 0;
        IsEased = false;
        NotifyDataChanged();
    }

    /// <summary>The current check was passed: on to the next one, eased when it was a perfect.</summary>
    public void Advance(bool perfect)
    {
        if (IsComplete) return;
        CheckIndex++;
        IsEased = perfect;
        NotifyDataChanged();
    }
}
