using System.Collections.Generic;

/// <summary>
/// The numbers <see cref="HabitLedger"/> scores with (plan §5.2, §12). SO_CounterplayRules
/// implements it for the game; the EditMode tests hand the ledger their own.
///
/// The decays are expressed per MINUTE because they are slow on purpose (R7, D4): a designer
/// reading "0.1 per minute" should not have to divide by sixty to picture it.
/// </summary>
public interface IHabitRules
{
    /// <summary>The rows that turn a count into an unlocked counterplay.</summary>
    IReadOnlyList<CounterplayRule> Rules { get; }

    /// <summary>No counterplay's chance goes above this: it has to stay a bet.</summary>
    float ChanceCap { get; }

    /// <summary>Minutes an exploit count holds after it last went up, before it starts to drain.
    /// </summary>
    float HabitDecayDelayMinutes { get; }

    /// <summary>How much an exploit count drains per minute once the delay is over.</summary>
    float HabitDecayPerMinute { get; }

    /// <summary>What hiding in a spot adds to its meter, on the way in.</summary>
    float SpotUsePoints { get; }

    /// <summary>What a spot's meter gains on the way out when the Nemesis hunted nearby during the
    /// stay and did not find the player (R1).</summary>
    float SpotHuntedBonus { get; }

    /// <summary>Minutes a spot's meter holds after it last went up, before it starts to drain.
    /// </summary>
    float SpotDecayDelayMinutes { get; }

    /// <summary>How much a spot's meter drains per minute once the delay is over.</summary>
    float SpotDecayPerMinute { get; }

    /// <summary>A spot at or above this meter gets checked first (C2).</summary>
    float SpotPriorityThreshold { get; }

    /// <summary>A spot at or above this meter can be torn apart (plan §3.6).</summary>
    float SpotBurnThreshold { get; }

    /// <summary>Chance of opening a used spot inside the area being checked, per point of meter.
    /// </summary>
    float SpotOpenChancePerPoint { get; }

    /// <summary>Ceiling on the chance of opening a used spot.</summary>
    float SpotOpenChanceCap { get; }
}
