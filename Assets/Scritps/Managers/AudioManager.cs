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
    /// PlayInternal hardcodes volume = 1f while never touching pitch, rolloffMode or
    /// min/maxDistance. Looping and positioned ambience is owned by AmbienceController and its
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
        src.Play();
    }

    public void PlayMusic(string id)
    {
        if (!TryGet(id, out var data)) return;

        musicSource.clip = data.Clip;
        musicSource.outputAudioMixerGroup = musicGroup;
        musicSource.loop = data.Loop;
        musicSource.volume = 1f; // Volume is governed by the mixer.
        musicSource.Play();
    }

    public void StopMusic() => musicSource.Stop();

    public void StopAllSFX()
    {
        foreach (var src in sfxPool)
            if (src.isPlaying) src.Stop();
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
    /// The Music bus. Exposed for the same reason as <see cref="NemesisGroup"/>: NemesisChaseMusic
    /// owns its own looping AudioSource for the chase-music layer and routes it here rather than
    /// through <see cref="PlayMusic"/>, which owns the single main music source.
    /// </summary>
    public AudioMixerGroup MusicGroup => musicGroup;

    // ──────────────────────────────────────────────────────────────────────────
    // Volume — setters
    // ──────────────────────────────────────────────────────────────────────────

    public void SetMasterVolume(float v)
    {
        masterVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_MASTER, masterVolume);
    }

    public void SetMusicVolume(float v)
    {
        musicVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_MUSIC, musicVolume);
    }

    /// <summary>Volume of the SFX bus (world effects).</summary>
    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX, sfxVolume);
    }

    /// <summary>
    /// Bundled mode: moves every non-music bus as a block
    /// (SFX, Ambience, Player, Nemesis, UI and Voice).
    ///
    /// This is the mode the game uses: the Settings panel exposes a single "SFX" slider
    /// instead of one per bus. UI and Voice are included on purpose — if they were left out,
    /// they would be the only two buses with no player control at all, stuck at their default.
    /// To pin them again, remove their two lines from here.
    ///
    /// The buses still exist separately in the mixer so they can be balanced against each other
    /// (routing is decided by SO_SoundData.SoundCategory); this only unifies what the player
    /// is allowed to touch.
    /// </summary>
    public void SetGameplaySfxBundle(float v)
    {
        sfxVolume = ambienceVolume = playerVolume = nemesisVolume = uiVolume = voiceVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX,      sfxVolume);
        ApplyVolume(EXP_AMBIENCE, ambienceVolume);
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
        src.volume = 1f; // The final volume is decided by the mixer.
        src.ignoreListenerPause = forceIgnorePause || data.IgnoreListenerPause;

        if (position.HasValue)
        {
            src.spatialBlend = 1f; // 3D
            src.transform.position = position.Value;
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
    private void ApplyVolume(string exposedParam, float linear01)
    {
        if (mixer == null) return;
        float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
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
