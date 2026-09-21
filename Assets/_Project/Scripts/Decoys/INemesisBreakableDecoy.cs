using UnityEngine;

/// <summary>
/// A decoy the Nemesis smashes when it gets to it. Implemented by the component that sits next to
/// the decoy's <see cref="DecoyNoiseSource"/>; <see cref="NemesisDecoyBreaker"/> drives it.
///
/// The timings live on the decoy and not on the breaker because they belong to the decoy's own
/// staging: how long the angry beat before the hit lasts is a per-prop decision, the same way a
/// door owns its OpenDuration.
/// </summary>
public interface INemesisBreakableDecoy
{
    /// <summary>False once broken, or while it is not sounding.</summary>
    bool CanBeBroken { get; }

    /// <summary>What the Nemesis turns to face.</summary>
    Vector3 BreakTargetPosition { get; }

    /// <summary>Horizontal metres from <see cref="BreakTargetPosition"/> at which it starts.</summary>
    float BreakReach { get; }

    /// <summary>Seconds from the start of the beat to the hit.</summary>
    float BreakWindup { get; }

    /// <summary>Seconds it stays put after the hit.</summary>
    float BreakRecovery { get; }

    /// <summary>The beat starts: the angry cut, the camera, the animation.</summary>
    void OnBreakStarted();

    /// <summary>The beat was cut short before the hit (it saw the player). The decoy keeps
    /// sounding.</summary>
    void OnBreakAborted();

    /// <summary>The hit. From here on it is broken for good.</summary>
    void Break();
}
