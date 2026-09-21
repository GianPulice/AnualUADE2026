using System;
using UnityEngine;

/// <summary>
/// The sound of the cabin travelling: one clip for a trip up, another for a trip down, each with a
/// stretch of it that repeats until the cabin arrives.
///
/// Why it is not <c>AudioManager.PlaySFX(id, position)</c>: that plays at a fixed point and the
/// cabin moves, so the sound would stay at the landing. This owns an AudioSource that rides along
/// with the cabin.
///
/// Why the loop is a stretch and not the whole clip: a lift needs a start, a steady hum and a stop,
/// and one recording carries all three. Playback runs from the start up to <c>loopEnd</c>, jumps
/// back to <c>loopStart</c> for as long as the trip lasts, and on arrival is simply let go: the
/// rest of the clip after <c>loopEnd</c> plays out as the stop. Unity has no loop points on an
/// AudioSource, so the jump is done here.
///
/// Driven by <see cref="MovingPlatform.OnRideStarted"/> / <see cref="MovingPlatform.OnRideCompleted"/>,
/// so it sounds the same whoever sent the cabin: the player, a call panel or the Nemesis.
/// </summary>
[RequireComponent(typeof(MovingPlatform))]
public class ElevatorTravelSound : MonoBehaviour
{
    [Serializable]
    public class Trip
    {
        [Tooltip("SO_SoundData of the trip. Its clip, volume and 3D range are used. Leave empty for " +
                 "a silent trip.")]
        [SoundId] public string soundId = string.Empty;

        [Tooltip("Seconds into the clip where the repeating stretch begins. Tune by ear: a cut " +
                 "that lands on a silent or steady moment does not click.")]
        [Min(0f)] public float loopStart = 0.35f;

        [Tooltip("Seconds into the clip where the repeating stretch ends and playback jumps back " +
                 "to Loop Start. What comes after this point is the stop, played once on " +
                 "arrival. Not past the clip's length. Not above Loop Start = no loop, the clip " +
                 "just plays once.")]
        [Min(0f)] public float loopEnd = 1.15f;
    }

    [SerializeField] private Trip goingUp = new Trip();
    [SerializeField] private Trip goingDown = new Trip();

    /// <summary>Shorter than this and a jump back is a stutter, not a loop.</summary>
    private const float MinLoopLength = 0.05f;

    private MovingPlatform platform;
    private AudioSource source;

    private bool looping;
    private float loopStart;
    private float loopEnd;

    private void Awake()
    {
        platform = GetComponent<MovingPlatform>();

        // A child so the source rides with the cabin, and 3D so distance means something. Doppler
        // is off: the cabin moves at walking pace and a pitch bend on a hum just sounds broken.
        var go = new GameObject("TravelAudio");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
    }

    private void OnEnable()
    {
        platform.OnRideStarted += HandleRideStarted;
        platform.OnRideCompleted += HandleRideCompleted;
    }

    private void OnDisable()
    {
        platform.OnRideStarted -= HandleRideStarted;
        platform.OnRideCompleted -= HandleRideCompleted;

        looping = false;
        if (source != null) source.Stop();
    }

    private void HandleRideStarted(bool up)
    {
        Trip trip = up ? goingUp : goingDown;
        if (trip == null || string.IsNullOrWhiteSpace(trip.soundId) || !AudioManager.Exists) return;

        // PlayLoop is what routes the source to the bus of the sound's category and copies its
        // volume and 3D range. It also sets loop = true, which is undone right below: the source
        // must reach the end of the clip on its own when the trip ends, so the stop can play.
        AudioManager.Instance.PlayLoop(trip.soundId, source);
        source.loop = false;
        source.time = 0f;

        if (source.clip == null) return;

        loopStart = trip.loopStart;
        loopEnd = Mathf.Min(trip.loopEnd, source.clip.length);
        looping = loopEnd - loopStart >= MinLoopLength;
    }

    private void HandleRideCompleted()
    {
        // Let go, do not stop: what is left of the clip past loopEnd is the sound of stopping.
        looping = false;
    }

    private void Update()
    {
        if (!looping || AudioListener.pause) return;

        // Also covers a source that ran off the end of the clip before the jump could happen.
        if (source.isPlaying && source.time < loopEnd) return;

        float overshoot = source.isPlaying ? source.time - loopEnd : 0f;
        source.time = Mathf.Min(loopStart + overshoot, loopEnd - 0.001f);
        if (!source.isPlaying) source.Play();
    }
}
