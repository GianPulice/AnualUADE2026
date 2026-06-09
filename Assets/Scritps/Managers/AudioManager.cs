using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton de audio del juego. Rutea cada sonido al AudioMixerGroup correcto
/// segun la categoria del SO_SoundData y controla los volumenes via parametros
/// expuestos del AudioMixer (escala dB con map logaritmico desde 0..1).
///
/// Mapa Mixer (ver MasterMixer.mixer):
///   Master > Music, Ambience, SFX, Player, Nemesis, UI, Voice.
///
/// API publica (en orden de uso esperado):
///   PlaySFX(id [, pos])     — SFX general (rutea a grupo SFX).
///   PlayMusic(id)           — Musica de fondo (un solo source dedicado).
///   PlayAmbience(id)        — Ambientes/loops del entorno.
///   PlayPlayer(id [, pos])  — Sonidos del jugador (pasos, respiracion, etc.).
///   PlayNemesis(id [, pos]) — Sonidos del Nemesis.
///   PlayUI(id)              — Clicks/hover del menu.
///   PlayVoice(id)           — Voiceover (siempre 2D).
///   PlayLoop(id, src)       — Toma una AudioSource ya existente (ej. en un GameObject
///                              persistente como el dispositivo del jugador) y le carga
///                              el clip + grupo correctos para que reproduzca en loop.
///   Play(id [, pos])        — Generico: deduce el grupo desde la categoria del SO.
///
/// API legacy (se mantiene para no romper PickupInteractable y el SettingsModel actual):
///   PlaySFX(id), PlayMusic(id), StopMusic(), StopAllSFX(), MasterVolume, MusicVolume,
///   SFXVolume, VoiceVolume, SetMasterVolume, SetMusicVolume, SetSFXVolume, SetVoiceVolume.
/// </summary>
public class AudioManager : Singleton<AudioManager>
{
    // ── Exposed parameter names (deben coincidir con MasterMixer.mixer) ──────
    public const string EXP_MASTER   = "MasterVolume";
    public const string EXP_MUSIC    = "MusicVolume";
    public const string EXP_AMBIENCE = "AmbienceVolume";
    public const string EXP_SFX      = "SFXVolume";
    public const string EXP_PLAYER   = "PlayerVolume";
    public const string EXP_NEMESIS  = "NemesisVolume";
    public const string EXP_UI       = "UIVolume";
    public const string EXP_VOICE    = "VoiceVolume";

    [Header("Audio Mixer (arrastrar MasterMixer.mixer y sus 8 grupos)")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup masterGroup;
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup ambienceGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioMixerGroup playerGroup;
    [SerializeField] private AudioMixerGroup nemesisGroup;
    [SerializeField] private AudioMixerGroup uiGroup;
    [SerializeField] private AudioMixerGroup voiceGroup;

    [Header("Volumenes 0..1 (defaults; al arrancar se pisa con PlayerPrefs)")]
    private float masterVolume   = 0.5f;
    private float musicVolume    = 0.5f;
    private float ambienceVolume = 0.5f;
    private float sfxVolume      = 0.5f;
    private float playerVolume   = 0.5f;
    private float nemesisVolume  = 0.5f;
    private float uiVolume       = 0.5f;
    private float voiceVolume    = 0.5f;

    [Header("Pool de SFX")]
    [SerializeField] private int initialPoolSize = 20;

    [Header("Sonidos")]
    [Tooltip("Arrastra aca todos los SO_SoundData del proyecto. Pueden estar en cualquier carpeta (ej: Assets/ScriptableObjects/Audio/).")]
    [SerializeField] private SO_SoundData[] sounds;

    private readonly Dictionary<string, SO_SoundData> byId = new();
    private readonly List<AudioSource> sfxPool = new();
    private AudioSource musicSource;

    // Mismas keys que SettingsModel para que el AudioManager arranque ya sincronizado.
    private const string KEY_MASTER   = "Settings_MasterVolume";
    private const string KEY_MUSIC    = "Settings_MusicVolume";
    private const string KEY_SFX      = "Settings_SFXVolume";
    private const string KEY_VOICE    = "Settings_VoiceVolume";
    private const string KEY_AMBIENCE = "Settings_AmbienceVolume";
    private const string KEY_PLAYER   = "Settings_PlayerVolume";
    private const string KEY_NEMESIS  = "Settings_NemesisVolume";
    private const string KEY_UI       = "Settings_UIVolume";

    private void Awake()
    {
        CreateSingleton(true);
        if (Instance != this) return;

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
        masterVolume   = PlayerPrefs.GetFloat(KEY_MASTER,   masterVolume);
        musicVolume    = PlayerPrefs.GetFloat(KEY_MUSIC,    musicVolume);
        sfxVolume      = PlayerPrefs.GetFloat(KEY_SFX,      sfxVolume);
        voiceVolume    = PlayerPrefs.GetFloat(KEY_VOICE,    voiceVolume);
        ambienceVolume = PlayerPrefs.GetFloat(KEY_AMBIENCE, ambienceVolume);
        playerVolume   = PlayerPrefs.GetFloat(KEY_PLAYER, playerVolume);
        nemesisVolume  = PlayerPrefs.GetFloat(KEY_NEMESIS, nemesisVolume);
        uiVolume       = PlayerPrefs.GetFloat(KEY_UI, uiVolume);
    }

    private void InitMusicSource()
    {
        var go = new GameObject("Music");
        go.transform.SetParent(transform, false);
        musicSource = go.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f; // Musica = 2D.
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
                Debug.LogWarning($"[AudioManager] Sonido con id duplicado: '{s.Id}'. Se reemplaza el anterior.");
            byId[s.Id] = s;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // API publica
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Reproduce el sonido en el grupo que indique su SoundCategory. 2D por defecto.</summary>
    public void Play(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, GroupFor(data.Category), null);
    }

    /// <summary>Reproduce el sonido en 3D en la posicion dada, ruteado por categoria.</summary>
    public void Play(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, GroupFor(data.Category), position);
    }

    /// <summary>SFX general (compat con el codigo existente que usa PlaySFX(id)).</summary>
    public void PlaySFX(string id)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, sfxGroup ?? GroupFor(data.Category), null);
    }

    /// <summary>SFX general en una posicion del mundo (3D espacial).</summary>
    public void PlaySFX(string id, Vector3 position)
    {
        if (!TryGet(id, out var data)) return;
        PlayInternal(data, sfxGroup ?? GroupFor(data.Category), position);
    }

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

    /// <summary>Carga el clip + grupo de mixer en una AudioSource externa y la arranca en loop.</summary>
    public void PlayLoop(string id, AudioSource src)
    {
        if (src == null) { Debug.LogWarning("[AudioManager] PlayLoop sin AudioSource."); return; }
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
        musicSource.volume = 1f; // El volumen se gobierna por el mixer.
        musicSource.Play();
    }

    public void StopMusic() => musicSource.Stop();

    public void StopAllSFX()
    {
        foreach (var src in sfxPool)
            if (src.isPlaying) src.Stop();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Volumen — getters
    // ──────────────────────────────────────────────────────────────────────────

    public float MasterVolume   => masterVolume;
    public float MusicVolume    => musicVolume;
    public float AmbienceVolume => ambienceVolume;
    public float SFXVolume      => sfxVolume;
    public float PlayerVolume   => playerVolume;
    public float NemesisVolume  => nemesisVolume;
    public float UIVolume       => uiVolume;
    public float VoiceVolume    => voiceVolume;

    // ──────────────────────────────────────────────────────────────────────────
    // Volumen — setters
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

    /// <summary>Volumen del bus SFX (efectos de mundo).</summary>
    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX, sfxVolume);
    }

    /// <summary>
    /// Alias explicito de <see cref="SetSFXVolume"/>. Existe para que el SettingsModel
    /// pueda distinguir entre "slider SFX afecta solo SFX" (modo nuevo, recomendado)
    /// y un futuro "slider SFX agrupa varios buses" sin cambiar la firma.
    /// </summary>
    public void SetSFXVolumeOnly(float v) => SetSFXVolume(v);

    /// <summary>
    /// Modo agrupado (legacy): mueve SFX, Ambience, Player y Nemesis en bloque.
    /// Util si el panel de Settings expone un unico slider "SFX" en lugar de uno
    /// por cada bus de gameplay.
    /// </summary>
    public void SetGameplaySfxBundle(float v)
    {
        sfxVolume = ambienceVolume = playerVolume = nemesisVolume = Mathf.Clamp01(v);
        ApplyVolume(EXP_SFX,      sfxVolume);
        ApplyVolume(EXP_AMBIENCE, ambienceVolume);
        ApplyVolume(EXP_PLAYER,   playerVolume);
        ApplyVolume(EXP_NEMESIS,  nemesisVolume);
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
    // Internos
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
        src.volume = 1f; // El volumen final lo decide el mixer.
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
    /// Convierte 0..1 lineal a dB y lo escribe en el mixer. 0 mapea a -80dB
    /// (silencio practico, evita -infinito por Log10(0)).
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
        Debug.LogWarning($"[AudioManager] No existe sonido con id '{id}'.");
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
