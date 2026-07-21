using System.Collections.Generic;
using UnityEngine;

public class AudioManager : Singleton<AudioManager>
{
    [Header("Volumenes (0..1)")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    [Header("Pool de SFX")]
    private int initialPoolSize = 20;

    [Header("Sonidos")]
    [Tooltip("Arrastra aca todos los SO_SoundData del proyecto. Pueden estar en cualquier carpeta (ej: Assets/ScriptableObjects/Audio/).")]
    [SerializeField] private SO_SoundData[] sounds;

    private readonly Dictionary<string, SO_SoundData> byId = new();
    private readonly List<AudioSource> sfxPool = new();
    private AudioSource musicSource;
    private SO_SoundData currentMusic;

    private void Awake()
    {
        CreateSingleton(true);
        if (Instance != this) return;

        InitMusicSource();
        InitSfxPool();
        IndexSounds();
        AudioListener.volume = masterVolume;
    }

    // ---------- Init ----------

    private void InitMusicSource()
    {
        var go = new GameObject("Music");
        go.transform.SetParent(transform, false);
        musicSource = go.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
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

    // ---------- API publica ----------

    public void PlaySFX(string id)
    {
        if (!TryGet(id, out var data)) return;

        var src = GetFreeSfxSource();
        src.clip = data.Clip;
        src.volume = sfxVolume;
        src.loop = data.Loop;
        src.Play();
    }

    public void PlayMusic(string id)
    {
        if (!TryGet(id, out var data)) return;

        currentMusic = data;
        musicSource.clip = data.Clip;
        musicSource.volume = musicVolume;
        musicSource.loop = data.Loop;
        musicSource.Play();
    }

    public void StopMusic()
    {
        musicSource.Stop();
        currentMusic = null;
    }

    public void StopAllSFX()
    {
        foreach (var src in sfxPool)
            if (src.isPlaying) src.Stop();
    }

    // ---------- Volumen (para sistema de ajustes) ----------

    public float MasterVolume => masterVolume;
    public float MusicVolume => musicVolume;
    public float SFXVolume => sfxVolume;

    public void SetMasterVolume(float linear01)
    {
        masterVolume = Mathf.Clamp01(linear01);
        AudioListener.volume = masterVolume;
    }

    public void SetMusicVolume(float linear01)
    {
        musicVolume = Mathf.Clamp01(linear01);
        if (musicSource.isPlaying)
            musicSource.volume = musicVolume;
    }

    public void SetSFXVolume(float linear01)
    {
        sfxVolume = Mathf.Clamp01(linear01);
        // Los SFX que ya estan sonando mantienen su volumen; los nuevos usaran este valor.
    }

    // ---------- Internos ----------

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
