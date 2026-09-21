using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Authoring data shared by every hiding spot in the game (Hiding System spec v1.0 §4).
///
/// ONE asset for the whole family, not one per spot: breathing has to feel the same everywhere or
/// the player cannot learn it, and the differences between a locker, a table and a container are
/// expressed as the multipliers below rather than as three sets of numbers that drift apart.
/// A <see cref="HidingSpot"/> contributes its <see cref="EHidingSpotType"/>; everything else is here.
///
/// THE RADII ARE EMITTER RADII, NOT METRES OF AUDIBILITY. The Nemesis hears a sphere, not an
/// event: <c>FieldOfListening</c> reads the radius off the collider it caught and scales it by
/// <c>SO_NemesisData.NoiseRangeScale</c> (2.5) before attenuating through walls and floors. A
/// breathing radius of 0.8 is heard from about 2 m in the open and less than that through the
/// locker's own shell. Tune against <c>NemesisGizmos</c>, which draws the player's three gait
/// radii to scale, and not against the spec table. See "Noise is a sphere, not an event" in
/// docs/CLAUDE.md.
/// </summary>
[CreateAssetMenu(fileName = "SO_HidingData", menuName = "Scriptable Objects/SO_HidingData")]
public class SO_HidingData : ScriptableObject
{
    [Header("Breathing")]
    [Tooltip("Seconds between one breath and the next while hidden and not holding it. The spec's " +
             "3 s is slow enough that a player can hear the gap and decide to hold, which is the " +
             "whole mechanic.")]
    [SerializeField, Min(0.25f)] private float breathingInterval = 3f;

    [Tooltip("How long each breath leaves the noise emitter on, in seconds.\n\n" +
             "MUST STAY WELL ABOVE FieldOfListening's listenDelay (0.1 s). The Nemesis sweeps for " +
             "noise on a timer, so a pulse shorter than one sweep is a coin flip that can be " +
             "heard by nobody — see \"Noise is a sphere, not an event\" in docs/CLAUDE.md.")]
    [SerializeField, Range(0.15f, 1.5f)] private float breathingPulseDuration = 0.4f;

    [Tooltip("Emitter radius of one breath, before NoiseRangeScale and before wall/floor " +
             "occlusion. Deliberately below the crouch-walk radius: breathing gives you away when " +
             "the monster is already on top of the spot, not from across the room.")]
    [SerializeField, Min(0f)] private float breathingNoiseRadius = 0.8f;

    [Header("Holding your breath")]
    [Tooltip("Emitter radius of the involuntary exhale when the player lets go of the hold-breath " +
             "input, before NoiseRangeScale and occlusion. This is the cost of holding: bigger " +
             "than a breath, so a badly timed release is worse than never having held at all.")]
    [SerializeField, Min(0f)] private float exhaleNoiseRadius = 2.5f;

    [Tooltip("How long the exhale leaves the emitter on, in seconds. Longer than a breath so it " +
             "cannot be missed by the sweep that matters.")]
    [SerializeField, Range(0.15f, 2f)] private float exhalePulseDuration = 0.6f;

    [Tooltip("Seconds the player can hold their breath before the lungs give out and the exhale " +
             "happens whether they let go or not. 0 = no limit, which is what the spec asks for " +
             "(holding keeps the emitter off for as long as the key is down).\n\n" +
             "NOT IN THE SPEC — a knob for playtesting. Holding forever silences only the " +
             "breathing: vision leaks and extreme proximity still find the player, so it is not " +
             "immunity. Set it if playtests show players camping with the key held.")]
    [SerializeField, Min(0f)] private float maxHoldSeconds = 0f;

    [Header("Per type")]
    [Tooltip("Multiplies the breathing and exhale RADII inside a cargo container: it is sealed, so " +
             "what leaks out is quieter. Spec value 0.5.")]
    [SerializeField, Range(0f, 2f)] private float containerNoiseMultiplier = 0.5f;

    [Tooltip("Multiplies the VOLUME of the player's own breathing loop inside a metal locker — " +
             "what the PLAYER hears, not what the Nemesis hears. A steel box next to your face is " +
             "louder from the inside. Spec value 1.2. Read by HiddenBreathing.")]
    [SerializeField, Range(0f, 3f)] private float closetBreathingMultiplier = 1.2f;

    [Header("Getting in and out")]
    [Tooltip("Seconds the climb-in takes. The player is immobilized for it and STILL FULLY " +
             "VISIBLE — this window is the whole of the Nemesis's \"I saw you climb in\" rule " +
             "(plan §3.4). Make it 0 and there is nothing left for that rule to catch.")]
    [SerializeField, Range(0.1f, 2f)] private float enterDuration = 0.6f;

    [Tooltip("Seconds the climb-out takes. The player is visible and cannot move for it.")]
    [SerializeField, Range(0.1f, 2f)] private float exitDuration = 0.6f;

    [Header("What the Nemesis still sees")]
    [Tooltip("Inside a LOCKER, how far the monster can still make the player out through the " +
             "slats, as a fraction of its View Range, and only from the door side. Only ever " +
             "through the peripheral accumulator, never instantly: past the suspicion threshold it " +
             "walks over to look, and a full meter marks the locker as known rather than starting a " +
             "chase (plan §3.4, level B).\n\n" +
             "Measured from the EYE, about 1.8 m up, so below ~0.3 (with View Range 7) the whole " +
             "band falls inside the 1.5 m proximity disc and this does nothing — which is where the " +
             "first value, 0.25, left it. 0 = a locker is as blind as a container, which makes the " +
             "spec's 'medium risk' and 'low risk' the same thing.")]
    [SerializeField, Range(0f, 1f)] private float lockerVisionExposure = 0.5f;

    [Header("Mix (MasterMixer snapshots)")]
    [Tooltip("The snapshot the mix returns to on the way out — MasterMixer's default, 'Snapshot'.")]
    [SerializeField] private AudioMixerSnapshot outsideSnapshot;

    [Tooltip("Inside a locker: the world behind a sheet of steel. Lowpasses the outside buses " +
             "(Ambience, SFX, Nemesis) and leaves the player's own breathing, the UI and the " +
             "Architect's voice clear.")]
    [SerializeField] private AudioMixerSnapshot lockerSnapshot;

    [Tooltip("Under a table the player is still in the room, so by default the mix does not " +
             "change. Assign a snapshot here only if playtesting says it should.")]
    [SerializeField] private AudioMixerSnapshot underTableSnapshot;

    [Tooltip("Inside a container: sealed, so the heaviest lowpass of the three.")]
    [SerializeField] private AudioMixerSnapshot containerSnapshot;

    [Tooltip("Seconds the mix takes to change, both ways. Close to the camera blend so the ear and " +
             "the eye arrive inside together.")]
    [SerializeField, Range(0f, 2f)] private float snapshotTransitionSeconds = 0.3f;

    public float BreathingInterval { get => breathingInterval; set => breathingInterval = value; }
    public float BreathingPulseDuration { get => breathingPulseDuration; set => breathingPulseDuration = value; }
    public float BreathingNoiseRadius { get => breathingNoiseRadius; set => breathingNoiseRadius = value; }
    public float ExhaleNoiseRadius { get => exhaleNoiseRadius; set => exhaleNoiseRadius = value; }
    public float ExhalePulseDuration { get => exhalePulseDuration; set => exhalePulseDuration = value; }
    public float MaxHoldSeconds { get => maxHoldSeconds; set => maxHoldSeconds = value; }
    public float ContainerNoiseMultiplier { get => containerNoiseMultiplier; set => containerNoiseMultiplier = value; }
    public float ClosetBreathingMultiplier { get => closetBreathingMultiplier; set => closetBreathingMultiplier = value; }
    public float EnterDuration { get => enterDuration; set => enterDuration = value; }
    public float ExitDuration { get => exitDuration; set => exitDuration = value; }
    public float LockerVisionExposure { get => lockerVisionExposure; set => lockerVisionExposure = value; }
    public AudioMixerSnapshot OutsideSnapshot => outsideSnapshot;
    public float SnapshotTransitionSeconds => snapshotTransitionSeconds;

    /// <summary>The mix to transition to inside a spot of this type, or null to leave it alone.</summary>
    public AudioMixerSnapshot SnapshotFor(EHidingSpotType type)
    {
        switch (type)
        {
            case EHidingSpotType.Locker:     return lockerSnapshot;
            case EHidingSpotType.UnderTable: return underTableSnapshot;
            case EHidingSpotType.Container:  return containerSnapshot;
            default:                         return null;
        }
    }

    /// <summary>
    /// What to multiply a breathing or exhale RADIUS by inside a spot of this type. Only the
    /// container muffles; the locker and the table leak at full strength and the monster's own
    /// wall occlusion does the rest.
    /// </summary>
    public float NoiseRadiusMultiplierFor(EHidingSpotType type)
        => type == EHidingSpotType.Container ? containerNoiseMultiplier : 1f;

    /// <summary>
    /// What to multiply the VOLUME of the player's breathing loop by inside a spot of this type.
    /// Player-facing mix only — it has no effect on what the Nemesis can hear.
    /// </summary>
    public float BreathingVolumeMultiplierFor(EHidingSpotType type)
        => type == EHidingSpotType.Locker ? closetBreathingMultiplier : 1f;
}
