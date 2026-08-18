using System;
using UnityEngine;

/// <summary>
/// Static event hub for the ambience system, following the shape of NemesisEvents / PlayerEvents.
///
/// Nothing subscribes to these yet. They exist so a debug overlay can visualise events without
/// coupling to the scheduler, and — the interesting one — so a future mechanic can let a loud RARE
/// event mask the player's footsteps for the Nemesis. That is a real gameplay hook: a gate slamming
/// somewhere in the building is a natural window to move under.
/// </summary>
public static class AmbienceEvents
{
    /// <summary>
    /// Static event fields must be nulled explicitly: domain reload is disabled in this project, so
    /// listeners from a previous Play session otherwise survive into the next one, and the first one
    /// that throws kills the rest of the invocation list. Same guard as NemesisEvents.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnEventPlayed = null;
        OnProfileChanged = null;
    }

    /// <summary>Raised every time an ambient one-shot actually plays.</summary>
    public static event Action<AmbienceEventPlayback> OnEventPlayed;

    /// <summary>Raised when the active ambience profile changes. May carry null.</summary>
    public static event Action<SO_AmbienceProfile> OnProfileChanged;

    public static void RaiseEventPlayed(AmbienceEventPlayback playback) =>
        OnEventPlayed?.Invoke(playback);

    public static void RaiseProfileChanged(SO_AmbienceProfile profile) =>
        OnProfileChanged?.Invoke(profile);
}

/// <summary>
/// Everything about one ambient one-shot playback. A struct so raising the event allocates nothing —
/// this fires on average twice a minute, but the debug overlay that will consume it should not be a
/// reason to produce garbage.
/// </summary>
public readonly struct AmbienceEventPlayback
{
    public readonly AudioClip Clip;
    public readonly Vector3 Position;
    public readonly SO_AmbienceEventBank.ETier Tier;
    public readonly bool Occluded;

    /// <summary>The anchor the sound came from, or null when it was placed by validated random.</summary>
    public readonly AmbienceEmitter Anchor;

    public readonly float Volume;
    public readonly float Pitch;

    public AmbienceEventPlayback(AudioClip clip, Vector3 position, SO_AmbienceEventBank.ETier tier,
                                 bool occluded, AmbienceEmitter anchor, float volume, float pitch)
    {
        Clip = clip;
        Position = position;
        Tier = tier;
        Occluded = occluded;
        Anchor = anchor;
        Volume = volume;
        Pitch = pitch;
    }
}
