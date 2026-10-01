using System.Collections.Generic;
using UnityEngine;

/// <summary>What a press (or no press) on one skill check attempt was worth.</summary>
public enum SkillCheckResult
{
    Miss,
    Good,
    Perfect
}

/// <summary>
/// State of one skill check sequence (Central Puzzle 2 — Ventilation Hub): which check the player is
/// on, where its success zone landed this attempt, and what a needle angle is worth against it.
///
/// Pure data and rules — no time, no input, no drawing. The needle itself belongs to the controller,
/// which feeds its angle to <see cref="Judge"/> and the verdict back to <see cref="Register"/>.
///
/// Angles follow a clock face (0 = twelve o'clock, clockwise), the convention of
/// <see cref="SO_SkillCheckData"/> and <see cref="UIRingArc"/>.
/// </summary>
public class SkillCheckModel : BaseScreenModel
{
    // Used when the data has no sectors at all: the spread of the SO's defaults, clear of twelve
    // o'clock where the needle starts.
    private const float FallbackSectorStart = 110f;
    private const float FallbackSectorEnd = 340f;

    private SO_SkillCheckData.SkillCheckStep[] steps = new SO_SkillCheckData.SkillCheckStep[0];
    private SO_SkillCheckData.ZoneSector[] sectors = new SO_SkillCheckData.ZoneSector[0];

    // Reused on every roll so picking a sector allocates nothing.
    private readonly List<float> sectorWeights = new List<float>();

    /// <summary>Index of the check being played. Equal to <see cref="TotalSteps"/> once every check has been hit.</summary>
    public int StepIndex { get; private set; }
    public int TotalSteps => steps.Length;

    /// <summary>A check was missed: the sequence ends there, failed.</summary>
    public bool IsFailed { get; private set; }

    /// <summary>Every check has been hit: the only way to finish.</summary>
    public bool IsComplete => TotalSteps > 0 && StepIndex >= TotalSteps;

    /// <summary>Nothing left to play, whichever way it went.</summary>
    public bool IsOver => IsComplete || IsFailed;

    /// <summary>Where this attempt's success zone starts, in degrees.</summary>
    public float ZoneStart { get; private set; }

    public SO_SkillCheckData.SkillCheckStep CurrentStep =>
        steps[Mathf.Clamp(StepIndex, 0, Mathf.Max(0, TotalSteps - 1))];

    public float ZoneWidth => CurrentStep.successZoneDegrees;

    /// <summary>The perfect slice sits at the start of the zone and never outgrows it.</summary>
    public float PerfectWidth => Mathf.Min(CurrentStep.perfectZoneDegrees, ZoneWidth);

    public override void Initialize()
    {
        steps = new SO_SkillCheckData.SkillCheckStep[0];
        sectors = new SO_SkillCheckData.ZoneSector[0];
        StepIndex = 0;
        IsFailed = false;
        ZoneStart = 0f;
        IsInitialized = true;
    }

    /// <summary>Starts a sequence from the first check, with its zone already placed.</summary>
    public void Configure(SO_SkillCheckData data)
    {
        steps = data != null && data.steps != null ? data.steps : new SO_SkillCheckData.SkillCheckStep[0];
        sectors = data != null && data.zoneSectors != null ? data.zoneSectors : new SO_SkillCheckData.ZoneSector[0];

        sectorWeights.Clear();
        foreach (SO_SkillCheckData.ZoneSector sector in sectors) sectorWeights.Add(sector.weight);

        StepIndex = 0;
        IsFailed = false;
        IsInitialized = true;

        if (TotalSteps > 0) RollZone();
        NotifyDataChanged();
    }

    /// <summary>
    /// What a press at <paramref name="needleAngle"/> is worth on the current attempt. Pure: nothing
    /// changes until the verdict goes back through <see cref="Register"/>.
    /// </summary>
    public SkillCheckResult Judge(float needleAngle)
    {
        if (TotalSteps == 0 || IsOver) return SkillCheckResult.Miss;

        float intoZone = needleAngle - ZoneStart;
        if (intoZone < 0f || intoZone > ZoneWidth) return SkillCheckResult.Miss;
        return PerfectWidth > 0f && intoZone <= PerfectWidth ? SkillCheckResult.Perfect : SkillCheckResult.Good;
    }

    /// <summary>
    /// Every check gets one attempt. A hit moves on to the next check; a miss ends the sequence on
    /// the spot (<see cref="IsFailed"/>), and the next one starts again from the first check — the
    /// only way through is every check hit in a row.
    /// </summary>
    public void Register(SkillCheckResult result)
    {
        if (TotalSteps == 0 || IsOver) return;

        if (result == SkillCheckResult.Miss)
        {
            IsFailed = true;
        }
        else
        {
            StepIndex++;
            if (!IsComplete) RollZone();
        }

        NotifyDataChanged();
    }

    // -- Zone -------------------

    /// <summary>
    /// Picks a sector by weight, then a start inside it that keeps the whole zone in the sector. A
    /// sector narrower than the zone pins the zone to the sector's start, and nothing may run past
    /// twelve o'clock: the needle's lap ends there.
    /// </summary>
    private void RollZone()
    {
        float from = FallbackSectorStart;
        float to = FallbackSectorEnd;

        int picked = RouletteSelection.Roulette(sectorWeights);
        if (picked >= 0)
        {
            from = sectors[picked].startDegrees;
            to = sectors[picked].endDegrees;
        }

        float width = ZoneWidth;
        float latestStart = Mathf.Max(from, to - width);
        float start = RouletteSelection.GetRandom(from, latestStart);
        ZoneStart = Mathf.Clamp(start, 0f, Mathf.Max(0f, 360f - width));
    }
}
