using UnityEngine;

/// <summary>
/// Puts the Architect's voice through a voice changer (<see cref="VoiceDisguiseProcessor"/>): lower,
/// rougher and squeezed through a small speaker, so it reads as a person hiding who they are
/// instead of a clean synthetic narrator. All it does is feed the audio thread's buffers to the
/// processor; the sound itself lives in <see cref="SO_VoiceDisguiseSettings"/>.
///
/// Sits on the ArchitectVoice GameObject, the one <see cref="ArchitectVoiceController"/> plays its
/// lines from. Component order matters: AudioSource FIRST, this component after it. A filter placed
/// before the source (or on a GameObject with none) would not process the voice. The controller
/// reuses the AudioSource already on the GameObject and only adds one when it is missing.
///
/// Threading: OnAudioFilterRead runs on the audio thread, where touching a UnityEngine.Object is
/// not allowed. The asset is therefore read on the main thread (in LateUpdate, when its
/// <see cref="SO_VoiceDisguiseSettings.Version"/> or the reference changes) into an immutable
/// snapshot that the processor picks up lock-free.
///
/// The processor state is cleared whenever the source starts playing, and rebuilt when the audio
/// configuration changes (new output device, new sample rate), so one line never leaks into the next.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class ArchitectVoiceDisguise : MonoBehaviour
{
    // Mono and stereo are what the project plays; the rest is room for a surround output.
    private const int MaxChannels = 8;

    [Tooltip("The disguise to apply. Empty or disabled: the voice plays clean.")]
    [SerializeField] private SO_VoiceDisguiseSettings settings;

    private AudioSource source;

    // Written on the main thread, read on the audio thread.
    private volatile VoiceDisguiseProcessor processor;
    private volatile bool active;

    private SO_VoiceDisguiseSettings appliedAsset;
    private int appliedVersion = -1;
    private bool sourceWasPlaying;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        BuildProcessor();
        PublishSettings();
    }

    private void OnEnable()
    {
        AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;

        // The filter did not run while this was disabled: whatever the delay line holds is old.
        VoiceDisguiseProcessor current = processor;
        if (current != null) current.RequestReset();
    }

    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
    }

    // LateUpdate, so the controller's Play (in its Update) has already happened this frame.
    private void LateUpdate()
    {
        if (settings != appliedAsset || (settings != null && settings.Version != appliedVersion))
            PublishSettings();

        bool playing = source != null && source.isPlaying;
        if (playing && !sourceWasPlaying)
        {
            // A new line: the delay line still holds the end of the last one.
            VoiceDisguiseProcessor current = processor;
            if (current != null) current.RequestReset();
        }
        sourceWasPlaying = playing;
    }

    private void HandleAudioConfigurationChanged(bool deviceWasChanged)
    {
        // The sample rate may have changed, and the filters and delay line are sized for the old one.
        BuildProcessor();
        PublishSettings();
    }

    private void BuildProcessor()
    {
        processor = new VoiceDisguiseProcessor(AudioSettings.outputSampleRate, MaxChannels);
    }

    /// <summary>Copies the asset into a snapshot for the audio thread. Main thread only.</summary>
    private void PublishSettings()
    {
        appliedAsset = settings;
        appliedVersion = settings != null ? settings.Version : -1;

        VoiceDisguiseProcessor current = processor;
        if (current == null) return;

        bool shouldRun = settings != null && settings.Enabled;
        if (shouldRun)
        {
            current.SetSettings(settings.CreateProcessorSettings());

            // Coming back from bypass: the processor has not seen audio for a while.
            if (!active) current.RequestReset();
        }
        active = shouldRun;
    }

    // Audio thread. Nothing here may touch a UnityEngine.Object or allocate.
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!active) return;

        VoiceDisguiseProcessor current = processor;
        if (current != null) current.Process(data, channels);
    }
}
