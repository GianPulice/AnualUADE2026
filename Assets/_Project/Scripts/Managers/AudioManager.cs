using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Game audio singleton. Routes each sound to the correct AudioMixerGroup based on the
/// SO_SoundData category and controls volumes through the AudioMixer's exposed parameters
/// (dB scale with a logarithmic map from 0..1).
///
/// Mixer map (see MasterMixer.mixer):
///   Master > Music, Ambience, SFX, Player, Nemesis, UI, Voice.
///
/// Public API (in expected order of use):
///   PlaySFX(id [, pos])     — General SFX (routes to the SFX group).
///   PlayMusic(id)           — Background music (single dedicated source).
///   PlayAmbience(id)        — Environment ambiences / loops.
///   PlayPlayer(id [, pos])  — Player sounds (footsteps, breathing, etc.).
///   PlayNemesis(id [, pos]) — Nemesis sounds.
///   PlayUI(id)              — Menu clicks / hovers.
///   PlayVoice(id)           — Voiceover (always 2D).
///   PlayLoop(id, src)       — Takes an existing AudioSource (e.g. on a persistent GameObject
///                              such as the player's device) and loads the correct clip + group
///                              so it plays on loop.
///   Play(id [, pos])        — Generic: infers the group from the SO's category.
///
/// Volume: the player only controls 3 sliders (Master, Music, SFX). The SFX one moves every
/// gameplay bus as a block via SetGameplaySfxBundle(). The per-bus setters remain public for
/// mixing from code, but Settings no longer uses them.
///
/// Legacy API (kept so PickupInteractable does not break):
///   PlaySFX(id), PlayMusic(id), StopMusic(), StopAllSFX(), MasterVolume, MusicVolume,
///   SFXVolume, VoiceVolume, SetMasterVolume, SetMusicVolume, SetSFXVolume, SetVoiceVolume.
/// </summary>
public class AudioManager : Singleton<AudioManager>
{
    // ── Exposed parameter names (must match MasterMixer.mixer) ───────────────
    public const string EXP_MASTER   = "MasterVolume";
    public const string EXP_MUSIC    = "MusicVolume";
    public const string EXP_AMBIENCE = "AmbienceVolume";
    public const string EXP_SFX      = "SFXVolume";
    public const string EXP_PLAYER   = "PlayerVolume";
    public const string EXP_NEMESIS  = "NemesisVolume";
    public const string EXP_UI       = "UIVolume";
    public const string EXP_VOICE    = "VoiceVolume";

    [Header("Audio Mixer (drag in MasterMixer.mixer and its 8 groups)")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup masterGroup;
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup ambienceGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioMixerGroup playerGroup;
    [SerializeField] private AudioMixerGroup nemesisGroup;
    [SerializeField] private AudioMixerGroup uiGroup;
    [SerializeField] private AudioMixerGroup voiceGroup;

    [Header("Volumes 0..1 (defaults; overwritten from PlayerPrefs at startup)")]
    private float masterVolume   = 0.5f;
    private float musicVolume    = 0.5f;
    private float ambienceVolume = 0.5f;
    private float sfxVolume      = 0.5f;
    private float playerVolume   = 0.5f;
    private float nemesisVolume  = 0.5f;
    private float uiVolume       = 0.5f;
    private float voiceVolume    = 0.5f;

    [Header("SFX pool")]
    [SerializeField] private int initialPoolSize = 20;

    [Header("Sounds")]
    [Tooltip("Drag every SO_SoundData in the project here. They can live in any folder (e.g. Assets/ScriptableObjects/Audio/).")]
    [SerializeField] private SO_SoundData[] sounds;

    private readonly Dictionary<string, SO_SoundData> byId = new();
    private readonly List<AudioSource> sfxPool = new();
    private AudioSource musicSource;

    // Same keys as SettingsModel so the AudioManager starts already in sync.
    // Only 3: the rest of the buses are derived from SFX (see SetGameplaySfxBundle).
    private const string KEY_MASTER = "Settings_MasterVolume";
    private const string KEY_MUSIC  = "Settings_MusicVolume";
    private const string KEY_SFX    = "Settings_SFXVolume";

    private void Awake()
    {
        CreateSingleton(true);
        LoadVolumesFromPrefs();
        InitMusicSource();
        InitSfxPool();
        IndexSounds();
        ApplyAllVolumesToMixer();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Init
    // ──────────────────────────────────────────────────────────────────────────

    private void LoadVolumesFromPrefs()
    {
        masterVolume = PlayerPrefs.GetFloat(KEY_MASTER, masterVolume);
        musicVolume  = PlayerPrefs.GetFloat(KEY_MUSIC,  musicVolume);
        sfxVolume    = PlayerPrefs.GetFloat(KEY_SFX,    sfxVolume);

        // Settings only persists 3 keys; the other buses hang off the SFX slider.
        ambienceVolume = playerVolume = nemesisVolume = uiVolume = voiceVolume = sfxVolume;
    }

    private void InitMusicSource()
    {
        var go = new GameObject("Music");
        go.transform.SetParent(transform, false);
        musicSource = go.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f; // Music = 2D.
        musicSource.outputAudioMixerGroup = musicGroup;
    }

    private void InitSfxPool()
    {
        for (int i = 0; i < initialPoolSize; i++)
            sfxPool.Add(CreateSfxSource(i));
    }

    private AudioSource CreateSfxSource(int idx)
    {
        var go = new GameObject($"SFX_{idx}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        return src;
    }

    private void IndexSounds()
    {
        if (sounds == null) return;
        foreach (var s in sounds)
        {
            if (s == null) continue;
            if (byId.ContainsKey(s.Id))
                Debug.LogWarning($"[AudioManager] Duplicate sound id: '{s.Id}'. The previous one is replaced.");

            // An entry with no AudioClip is worse than a missing entry: TryGet finds it, the
            // source plays a null clip, and the caller gets silence with nothing in the console.
            // A sound that exists on paper and cannot be heard is exactly how a door ends up
            // "having" an open sound that nobody ever hears.
            if (s.Clip == null)
                Debug.LogWarning($"[AudioManager] Sound '{s.Id}' has no AudioClip assigned, so it " +
                                 "will play silently. Drop the clip into the SO_SoundData asset.");

            byId[s.Id] = s;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Plays the sound on the group its SoundCategory indicates. 2D by default.</summary>
    public void Play(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, GroupFor(data.Category), null);
    }

    /// <summary>Plays the sound in 3D at the given position, routed by category.</summary>
    public void Play(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, GroupFor(data.Category), position);
    }

    /// <summary>General SFX (compatible with existing code that uses PlaySFX(id)).</summary>
    public void PlaySFX(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, sfxGroup ?? GroupFor(data.Category), null);
    }

    /// <summary>General SFX at a world position (3D spatial).</summary>
    public void PlaySFX(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, sfxGroup ?? GroupFor(data.Category), position);
    }

    /// <summary>
    /// Fire-and-forget 2D one-shot on the Ambience bus.
    /// </summary>
    /// <remarks>
    /// NOT usable for ambient loops. It borrows a source from the shared SFX pool, so it returns
    /// no handle (there is no StopAmbience), it can be cut mid-clip when the pool runs dry, and
    /// PlayInternal only applies the SO's fixed volume, pins pitch to 1 and, for a 2D sound,
    /// never touches rolloffMode or min/maxDistance. Looping and positioned ambience is owned by AmbienceController and its
    /// layers, which create their own AudioSources and route them through
    /// <see cref="AmbienceGroup"/>.
    /// </remarks>
    public void PlayAmbience(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, ambienceGroup ?? GroupFor(data.Category), null);
    }

    public void PlayPlayer(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, playerGroup ?? GroupFor(data.Category), null);
    }

    public void PlayPlayer(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, playerGroup ?? GroupFor(data.Category), position);
    }

    public void PlayNemesis(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, nemesisGroup ?? GroupFor(data.Category), position);
    }

    public void PlayUI(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, uiGroup ?? GroupFor(data.Category), null, forceIgnorePause: true);
    }

    /// <summary>
    /// 2D one-shot of a held clip on the UI bus, audible while paused. For layers built in code
    /// (e.g. the hover static) that have no SO_SoundData and need their own volume and pitch.
    /// </summary>
    public void PlayUIClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        var src = GetFreeSfxSource();

        src.clip = clip;
        src.outputAudioMixerGroup = uiGroup ?? GroupFor(SO_SoundData.SoundCategory.UI);
        src.loop = false;
        src.ignoreListenerPause = true;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Max(pitch, 0.01f);
        src.spatialBlend = 0f;
        src.transform.localPosition = Vector3.zero;

        src.Play();
    }

    public void PlayVoice(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, voiceGroup ?? GroupFor(data.Category), null, forceIgnorePause: true);
    }

    /// <summary>Loads the clip + mixer group into an external AudioSource and starts it on loop.</summary>
    public void PlayLoop(string id, AudioSource src)
    {
        if (src == null) { Debug.LogWarning("[AudioManager] PlayLoop without an AudioSource."); return; }
        if (!TryGet(id, out var data)) return;

        src.clip = data.Clip;
        src.outputAudioMixerGroup = GroupFor(data.Category);
        src.loop = true;
        src.ignoreListenerPause = data.IgnoreListenerPause;
        src.volume = data.Volume;

        // Same 3D range as the pooled path. Only meaningful when the caller made the source 3D;
        // the SO defaults match Unity's, so a loop whose SO was never tuned sounds as before.
        src.rolloffMode = data.Rolloff;
        src.minDistance = data.MinDistance;
        src.maxDistance = data.MaxDistance;
        src.Play();
    }

    /// <summary>
    /// Fire-and-forget 3D one-shot of a clip the caller already holds, with per-shot volume, pitch
    /// and falloff, on the bus for <paramref name="category"/>.
    /// </summary>
    /// <remarks>
    /// The id-based API cannot express any of those three: <see cref="PlayInternal"/> plays every
    /// pooled sound at the SO's fixed volume and pitch 1, and takes its distances off the SO. That is fine for a
    /// door, which sounds the same every time it opens, and wrong for anything drawn from a bank —
    /// a footstep needs a different pitch and volume on every step or it reads as a copy-paste.
    ///
    /// It also skips the id indirection, which for bank content is pure overhead: footsteps would
    /// otherwise need ~20 SO_SoundData assets and ~20 inspector drags into <see cref="sounds"/>,
    /// none of which anything would ever look up by name. Same argument SO_AmbienceEventBank makes.
    ///
    /// Uses the shared SFX pool, so it inherits the pool's terms: no handle comes back, and a shot
    /// can be cut short when every source is busy. Both are correct for a one-shot and wrong for a
    /// loop — use <see cref="PlayLoop(AudioClip, AudioSource, SO_SoundData.SoundCategory, float)"/>
    /// for those.
    /// </remarks>
    public void PlayClip(AudioClip clip, SO_SoundData.SoundCategory category, Vector3 position,
                         float volume = 1f, float pitch = 1f,
                         float minDistance = 1f, float maxDistance = 25f,
                         AudioRolloffMode rolloff = AudioRolloffMode.Linear)
    {
        if (clip == null) return;

        var src = GetFreeSfxSource();

        src.clip = clip;
        src.outputAudioMixerGroup = GroupFor(category);
        src.loop = false;
        src.ignoreListenerPause = false;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Max(pitch, 0.01f);

        src.spatialBlend = 1f;
        src.transform.position = position;
        src.rolloffMode = rolloff;
        src.minDistance = minDistance;
        src.maxDistance = Mathf.Max(maxDistance, minDistance + 0.1f);

        src.Play();
    }

    /// <summary>Player-bus one-shot of a held clip. Convenience over <see cref="PlayClip"/>.</summary>
    public void PlayPlayer(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f,
                           float minDistance = 1f, float maxDistance = 25f)
        => PlayClip(clip, SO_SoundData.SoundCategory.Player, position, volume, pitch,
                    minDistance, maxDistance);

    /// <summary>Nemesis-bus one-shot of a held clip. Convenience over <see cref="PlayClip"/>.</summary>
    public void PlayNemesis(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f,
                            float minDistance = 1f, float maxDistance = 25f)
        => PlayClip(clip, SO_SoundData.SoundCategory.Nemesis, position, volume, pitch,
                    minDistance, maxDistance);

    /// <summary>SFX-bus one-shot of a held clip. Convenience over <see cref="PlayClip"/>.</summary>
    public void PlaySFX(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f,
                        float minDistance = 1f, float maxDistance = 25f)
        => PlayClip(clip, SO_SoundData.SoundCategory.SFX, position, volume, pitch,
                    minDistance, maxDistance);

    public void PlayMusic(string id)
    {
        if (!TryGet(id, out var data)) return;

        musicSource.clip = data.Clip;
        musicSource.outputAudioMixerGroup = musicGroup;
        musicSource.loop = data.Loop;
        musicSource.volume = data.Volume; // Per-clip trim; the Music volume is applied by the mixer on top.
        musicSource.Play();
    }

    public void StopMusic() => musicSource.Stop();

    public void StopAllSFX()
    {
        foreach (var src in sfxPool)
            if (src.isPlaying) src.Stop();
    }

    /// <summary>
    /// Length in seconds of the clip registered under <paramref name="id"/>, or 0 when the id is
    /// unknown or its clip is missing. Used by callers that want to gate re-triggers on the
    /// natural end of the clip (e.g. locked-door bump when the player mashes E) without asking
    /// the pool for an AudioSource handle.
    /// </summary>
    public float GetSoundLength(string id)
    {
        if (!byId.TryGetValue(id, out var data)) return 0f;
        if (data == null || data.Clip == null) return 0f;
        return data.Clip.length;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Volume — getters
    // ──────────────────────────────────────────────────────────────────────────

    public float MasterVolume   => masterVolume;
    public float MusicVolume    => musicVolume;
    public float AmbienceVolume => ambienceVolume;
    public float SFXVolume      => sfxVolume;
    public float PlayerVolume   => playerVolume;
    public float NemesisVolume  => nemesisVolume;
    public float UIVolume       => uiVolume;
    public float VoiceVolume    => voiceVolume;

    /// <summary>
    /// The Nemesis bus. Exposed so components that own their own looping AudioSources
    /// (NemesisAudio) can route through the mixer without the group being dragged in twice.
    /// </summary>
    public AudioMixerGroup NemesisGroup => nemesisGroup;

    /// <summary>
    /// The Ambience bus. Exposed for the same reason as <see cref="NemesisGroup"/>: the ambience
    /// system (AmbienceController and its layers) owns its own looping and one-shot AudioSources
    /// and routes them through the Ambience sub-groups.
    ///
    /// Nothing in that system ever calls mixer.SetFloat — see <see cref="SetGameplaySfxBundle"/>,
    /// which rewrites AmbienceVolume the moment the player touches the SFX slider and would
    /// destroy any per-layer mix ratio stored in an exposed parameter. Ratios between the ambient
    /// layers live in the fixed faders of the Ambience sub-groups (child volumes are offsets that
    /// sum in dB with the parent, so they survive every write to AmbienceVolume) and in
    /// AudioSource.volume.
    /// </summary>
    public AudioMixerGroup AmbienceGroup => ambienceGroup;

    /// <summary>
    /// The Player bus. Exposed for the same reason as <see cref="NemesisGroup"/>: components that
    /// own their own AudioSources route through the mixer without the group being dragged in twice.
    ///
    /// Its users are <see cref="FootstepEmitter"/> and <see cref="HiddenBreathing"/>, and they own
    /// their sources rather than borrowing from the SFX pool for two reasons the pool cannot serve:
    /// <see cref="PlayInternal"/> uses one fixed volume per SO and pins pitch to 1 — a footstep needs
    /// both per step, and a pitch left on a shared pooled source would leak into whatever plays on
    /// it next — and a breathing loop needs a handle it can fade.
    /// </summary>
    public AudioMixerGroup PlayerGroup => playerGroup;

    /// <summary>
    /// The Music bus. Exposed for the same reason as <see cref="NemesisGroup"/>: NemesisChaseMusic
    /// owns its own looping AudioSource for the chase-music layer and routes it here rather than
    /// through <see cref="PlayMusic"/>, which owns the single main music source.
    /// </summary>
    public AudioMixerGroup MusicGroup => musicGroup;

    /// <summary>
    /// The Voice bus. Exposed for <see cref="ArchitectVoiceController"/>, which owns a 2D AudioSource
    /// of its own: it has to cut a line mid-way (ARC_10 interrupts) and know when a line ends.
    /// </summary>
    public AudioMixerGroup VoiceGroup => voiceGroup;

    // ──────────────────────────────────────────────────────────────────────────
    // Volume — setters
    // ──────────────────────────────────────────────────────────────────────────

    public void SetMasterVolume(float v)
    {
        masterVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_MASTER, masterVolume);
    }

    /// <summary>
    /// Volume of the music bus, and of Ambience with it.
    ///
    /// The two move together because ambience is atmosphere, not an effect: in a horror game the
    /// player who turns the music down is asking for less mood, not for quieter footsteps. It used
    /// to ride the SFX bundle instead, which is what forced the "never call mixer.SetFloat under
    /// Ambience" rule — see <see cref="SetGameplaySfxBundle"/>.
    ///
    /// Both fields are updated so AmbienceVolume still reports what is actually on the bus; nothing
    /// reads them apart from this class, but a stale mirror of the mixer is a debugging trap.
    /// </summary>
    public void SetMusicVolume(float v)
    {
        musicVolume = ambienceVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_MUSIC,    musicVolume);
        ApplyVolume(EXP_AMBIENCE, ambienceVolume);
    }

    /// <summary>Volume of the SFX bus (world effects).</summary>
    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX, sfxVolume);
    }

    /// <summary>
    /// Moves the gameplay effect buses together (SFX, Player, Nemesis, UI and Voice), which is what
    /// the single "SFX" slider in Settings drives instead of one slider per bus.
    ///
    /// UI and Voice are included on purpose — left out they would be the only buses with no player
    /// control at all, stuck at their default. To pin them again, remove their two lines below.
    ///
    /// <b>Ambience is deliberately NOT here.</b> It used to be, and that is where the awkward rule
    /// in docs/Ambience-System.md came from: "never call mixer.SetFloat for anything under Ambience"
    /// existed only because this method overwrote it on every drag of the SFX slider, so per-layer
    /// balance had to hide in fixed faders instead. Ambience now follows the MUSIC slider (see
    /// <see cref="SetMusicVolume"/>) — it is atmosphere, not an effect, and in a horror game the
    /// player reaching for the music slider is reaching for the same thing.
    ///
    /// The buses still exist separately in the mixer so they can be balanced against each other
    /// (routing is decided by SO_SoundData.SoundCategory); this only unifies what the player is
    /// allowed to touch.
    /// </summary>
    public void SetGameplaySfxBundle(float v)
    {
        sfxVolume = playerVolume = nemesisVolume = uiVolume = voiceVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX,      sfxVolume);
        ApplyVolume(EXP_PLAYER,   playerVolume);
        ApplyVolume(EXP_NEMESIS,  nemesisVolume);
        ApplyVolume(EXP_UI,       uiVolume);
        ApplyVolume(EXP_VOICE,    voiceVolume);
    }

    public void SetAmbienceVolume(float v)
    {
        ambienceVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_AMBIENCE, ambienceVolume);
    }

    public void SetPlayerVolume(float v)
    {
        playerVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_PLAYER, playerVolume);
    }

    public void SetNemesisVolume(float v)
    {
        nemesisVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_NEMESIS, nemesisVolume);
    }

    public void SetUIVolume(float v)
    {
        uiVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_UI, uiVolume);
    }

    public void SetVoiceVolume(float v)
    {
        voiceVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_VOICE, voiceVolume);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internals
    // ──────────────────────────────────────────────────────────────────────────

    private AudioMixerGroup GroupFor(SO_SoundData.SoundCategory cat)
    {
        switch (cat)
        {
            case SO_SoundData.SoundCategory.Music:    return musicGroup;
            case SO_SoundData.SoundCategory.Ambience: return ambienceGroup;
            case SO_SoundData.SoundCategory.Player:   return playerGroup;
            case SO_SoundData.SoundCategory.Nemesis:  return nemesisGroup;
            case SO_SoundData.SoundCategory.UI:       return uiGroup;
            case SO_SoundData.SoundCategory.Voice:    return voiceGroup;
            case SO_SoundData.SoundCategory.SFX:
            default:                                  return sfxGroup;
        }
    }

    private void PlayInternal(SO_SoundData data, AudioMixerGroup group, Vector3? position, bool forceIgnorePause = false)
    {
        var src = GetFreeSfxSource();

        src.clip = data.Clip;
        src.outputAudioMixerGroup = group;
        src.loop = data.Loop;
        src.volume = data.Volume; // Per-clip trim; the category volume is applied by the mixer on top.

        // Reset, because the pool is shared and PlayClip leaves a per-shot pitch on the source it
        // borrowed. Without this a footstep at pitch 1.07 detunes whatever plays on that source
        // next — a door, a pickup — and the symptom (an occasional slightly wrong-sounding door)
        // points nowhere near the footsteps.
        src.pitch = 1f;

        src.ignoreListenerPause = forceIgnorePause || data.IgnoreListenerPause;

        if (position.HasValue)
        {
            src.spatialBlend = 1f; // 3D
            src.transform.position = position.Value;

            // Authored per clip. Pooled sources are created in code, so without this they
            // carry Unity's defaults — maxDistance 500, which is audible across the whole
            // level and makes distance useless as information. Defaults on SO_SoundData
            // match Unity's, so this changed nothing until a clip opts in.
            src.rolloffMode = data.Rolloff;
            src.minDistance = data.MinDistance;
            src.maxDistance = data.MaxDistance;
        }
        else
        {
            src.spatialBlend = 0f; // 2D
            src.transform.localPosition = Vector3.zero;
        }

        src.Play();
    }

    private void ApplyAllVolumesToMixer()
    {
        ApplyVolume(EXP_MASTER,   masterVolume);
        ApplyVolume(EXP_MUSIC,    musicVolume);
        ApplyVolume(EXP_AMBIENCE, ambienceVolume);
        ApplyVolume(EXP_SFX,      sfxVolume);
        ApplyVolume(EXP_PLAYER,   playerVolume);
        ApplyVolume(EXP_NEMESIS,  nemesisVolume);
        ApplyVolume(EXP_UI,       uiVolume);
        ApplyVolume(EXP_VOICE,    voiceVolume);
    }

    /// <summary>
    /// Converts linear 0..1 to dB and writes it to the mixer. 0 maps to -80dB
    /// (practical silence, avoids -infinity from Log10(0)).
    /// </summary>
    /// <summary>
    /// Global attenuation applied on top of every slider, 1 = untouched, 0 = silent. Owned by
    /// <see cref="AudioBackgroundApplier"/>, which fades it out just before the pause takes hold so
    /// the world does not cut to silence in a single frame.
    ///
    /// A multiplier and not a second write to the same exposed parameters on purpose: the sliders
    /// already own those, and two writers to one mixer parameter is the same class of bug as two
    /// writers to a state machine's next-state channel — whichever ran last that frame wins, and
    /// the result depends on script execution order.
    ///
    /// UI is excluded: menu clicks have to stay audible while paused, which is the whole reason
    /// PlayUI forces ignoreListenerPause.
    /// </summary>
    public float PauseDuck
    {
        get => pauseDuck;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(clamped, pauseDuck)) return;

            pauseDuck = clamped;
            ApplyAllVolumesToMixer();
        }
    }

    private float pauseDuck = 1f;

    /// <summary>Buses the pause duck does NOT touch.</summary>
    private static bool IsDuckExempt(string exposedParam) => exposedParam == EXP_UI;

    private void ApplyVolume(string exposedParam, float linear01)
    {
        if (mixer == null) return;
        float effective = IsDuckExempt(exposedParam) ? linear01 : linear01 * pauseDuck;
        float db = effective <= 0.0001f ? -80f : Mathf.Log10(effective) * 20f;
        mixer.SetFloat(exposedParam, db);
    }

    private bool TryGet(string id, out SO_SoundData data)
    {
        if (byId.TryGetValue(id, out data)) return true;
        Debug.LogWarning($"[AudioManager] There is no sound with id '{id}'.");
        return false;
    }

    private AudioSource GetFreeSfxSource()
    {
        foreach (var src in sfxPool)
            if (!src.isPlaying) return src;

        var newSrc = CreateSfxSource(sfxPool.Count);
        sfxPool.Add(newSrc);
        return newSrc;
    }
}
