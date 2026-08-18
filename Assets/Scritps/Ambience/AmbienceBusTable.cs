using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The four Ambience sub-buses, resolved once by AmbienceController and handed to each layer.
///
/// These are the child AudioMixerGroups of Ambience created by
/// Tools/Audio/Create or Update Master Mixer. Their faders carry the fixed balance between the
/// ambient layers, which is why the mix ratios live there and not in an exposed mixer parameter:
/// AudioManager.SetGameplaySfxBundle rewrites AmbienceVolume whenever the player touches the
/// single SFX slider, and a child group's volume is an offset that sums in dB with its parent, so
/// it survives that write untouched.
///
/// If the four groups are left unassigned the table falls back to AudioManager's Ambience bus for
/// all of them, so the ambience system is audible before the manual mixer step has been done. The
/// balance between layers is wrong in that state — everything sits at 0 dB — but nothing is silent
/// and nothing throws.
/// </summary>
[Serializable]
public class AmbienceBusTable
{
    [Tooltip("Ambience/Bed. Suggested fader: 0 dB.")]
    [SerializeField] private AudioMixerGroup bed;

    [Tooltip("Ambience/Events. Suggested fader: -2 dB.")]
    [SerializeField] private AudioMixerGroup events;

    [Tooltip("Ambience/Texture — the pink noise. Suggested fader: -3 dB.")]
    [SerializeField] private AudioMixerGroup texture;

    [Tooltip("Ambience/Sub — the low-frequency drones. Suggested fader: -12 dB, plus a Highpass " +
             "at 12 Hz and a Lowpass at 120 Hz.")]
    [SerializeField] private AudioMixerGroup sub;

    private AudioMixerGroup fallback;

    /// <summary>True when every one of the four groups has been assigned in the inspector.</summary>
    public bool IsFullyAssigned =>
        bed != null && events != null && texture != null && sub != null;

    /// <summary>True when not one of the four groups has been assigned.</summary>
    public bool IsEmpty =>
        bed == null && events == null && texture == null && sub == null;

    /// <summary>
    /// Sets the group returned for any bus that was left unassigned. Called by AmbienceController
    /// in Start with AudioManager's Ambience bus.
    /// </summary>
    public void SetFallback(AudioMixerGroup group) => fallback = group;

    /// <summary>
    /// The mixer group for a bus, or the fallback, or null if neither exists. A null return is
    /// survivable: an AudioSource with no output group routes to the AudioListener directly, so
    /// the sound is audible but unmixed.
    /// </summary>
    public AudioMixerGroup For(EAmbienceBus bus)
    {
        AudioMixerGroup group;

        switch (bus)
        {
            case EAmbienceBus.Events:  group = events;  break;
            case EAmbienceBus.Texture: group = texture; break;
            case EAmbienceBus.Sub:     group = sub;     break;
            case EAmbienceBus.Bed:
            default:                   group = bed;     break;
        }

        // Explicit == null rather than ?? — Unity overloads the null operator on UnityEngine.Object
        // so a destroyed group would pass a ?? check while still being unusable.
        return group == null ? fallback : group;
    }
}
