using System;
using UnityEngine;

public class SettingsModel : BaseScreenModel
{
    /// <summary>
    /// Disparado cuando <see cref="Apply"/> persiste los cambios.
    /// Suscriptores típicos: <c>CameraSensitivityApplier</c>, sistemas de post-process futuros.
    /// </summary>
    public static event Action OnSettingsApplied;

    // ── Keys de PlayerPrefs ──────────────────────────────────────────────────
    // Conectados al AudioManager:
    private const string KEY_MASTER       = "Settings_MasterVolume";
    private const string KEY_MUSIC        = "Settings_MusicVolume";
    private const string KEY_SFX          = "Settings_SFXVolume";
    private const string KEY_VOICE        = "Settings_VoiceVolume";
    private const string KEY_AMBIENCE     = "Settings_AmbienceVolume";
    private const string KEY_PLAYER       = "Settings_PlayerVolume";
    private const string KEY_NEMESIS      = "Settings_NemesisVolume";
    private const string KEY_UI           = "Settings_UIVolume";
    private const string KEY_SENSITIVITY  = "Settings_Sensitivity";
    // Aplicados al juego por appliers que leen estas keys al dispararse OnSettingsApplied
    // (InvertY→CameraSensitivityApplier, Brightness/Contrast/Gamma→PostProcessSettingsApplier,
    //  CRT/Dither→PS1EffectApplier, Resolution/Window/FPS/VSync→ScreenSettingsApplier,
    //  AudioBG→AudioBackgroundApplier):
    private const string KEY_INVERT_Y     = "Settings_InvertYAxis";
    private const string KEY_BRIGHTNESS   = "Settings_Brightness";
    private const string KEY_CONTRAST     = "Settings_Contrast";
    private const string KEY_GAMMA        = "Settings_Gamma";
    private const string KEY_CRT          = "Settings_CRTScanlines";
    private const string KEY_DITHER       = "Settings_PSXDithering";
    private const string KEY_RESOLUTION   = "Settings_ResolutionIndex";
    private const string KEY_WINDOW_MODE  = "Settings_WindowMode";
    private const string KEY_FPS_LIMIT    = "Settings_FPSLimit";
    private const string KEY_VSYNC        = "Settings_VSync";
    private const string KEY_AUDIO_BG     = "Settings_AudioInBackground";

    // ── Defaults ─────────────────────────────────────────────────────────────
    private const float  DEFAULT_SENSITIVITY = 1f;
    private const float  DEFAULT_VOICE       = 1f;
    private const float  DEFAULT_BRIGHTNESS  = 0.7f;
    private const float  DEFAULT_CONTRAST    = 0.55f;
    private const float  DEFAULT_GAMMA       = 0.5f;
    private const bool   DEFAULT_CRT         = true;
    private const bool   DEFAULT_DITHER      = true;
    private const bool   DEFAULT_INVERT_Y    = false;
    private const int    DEFAULT_RESOLUTION  = 0;
    private const int    DEFAULT_WINDOW_MODE = 0;
    private const int    DEFAULT_FPS_LIMIT   = 0;
    private const bool   DEFAULT_VSYNC       = true;
    private const bool   DEFAULT_AUDIO_BG    = false;

    // ── Estado conectado al AudioManager ─────────────────────────────────────
    public float MasterVolume   { get; private set; }
    public float MusicVolume    { get; private set; }
    public float SFXVolume      { get; private set; }
    public float VoiceVolume    { get; private set; }
    public float AmbienceVolume { get; private set; }
    public float PlayerVolume   { get; private set; }
    public float NemesisVolume  { get; private set; }
    public float UIVolume       { get; private set; }
    public float Sensitivity    { get; private set; }

    // ── Estado de video/controles (leído por los *Applier.cs al hacer Apply) ─
    public bool  InvertYAxis      { get; private set; }
    public float Brightness       { get; private set; }
    public float Contrast         { get; private set; }
    public float Gamma            { get; private set; }
    public bool  CRTScanlines     { get; private set; }
    public bool  PSXDithering     { get; private set; }
    public int   ResolutionIndex  { get; private set; }
    public int   WindowMode       { get; private set; }
    public int   FPSLimit         { get; private set; }
    public bool  VSync            { get; private set; }
    public bool  AudioInBackground { get; private set; }

    // ── Snapshot para revert ─────────────────────────────────────────────────
    private float _snapMaster, _snapMusic, _snapSFX, _snapVoice, _snapAmbience, _snapPlayer, _snapNemesis, _snapUI, _snapSensitivity;
    private bool  _snapInvertY, _snapCRT, _snapDither, _snapVSync, _snapAudioBg;
    private float _snapBrightness, _snapContrast, _snapGamma;
    private int   _snapResolution, _snapWindowMode, _snapFPSLimit;

    public override void Initialize()
    {
        float defMaster   = AudioManager.Exists ? AudioManager.Instance.MasterVolume   : 1f;
        float defMusic    = AudioManager.Exists ? AudioManager.Instance.MusicVolume    : 1f;
        float defSFX      = AudioManager.Exists ? AudioManager.Instance.SFXVolume      : 1f;
        float defVoice    = AudioManager.Exists ? AudioManager.Instance.VoiceVolume    : DEFAULT_VOICE;
        float defAmbience = AudioManager.Exists ? AudioManager.Instance.AmbienceVolume : defSFX;
        float defPlayer   = AudioManager.Exists ? AudioManager.Instance.PlayerVolume   : defSFX;
        float defNemesis  = AudioManager.Exists ? AudioManager.Instance.NemesisVolume  : defSFX;
        float defUI       = AudioManager.Exists ? AudioManager.Instance.UIVolume       : 1f;

        MasterVolume   = PlayerPrefs.GetFloat(KEY_MASTER,      defMaster);
        MusicVolume    = PlayerPrefs.GetFloat(KEY_MUSIC,       defMusic);
        SFXVolume      = PlayerPrefs.GetFloat(KEY_SFX,         defSFX);
        VoiceVolume    = PlayerPrefs.GetFloat(KEY_VOICE,       defVoice);
        AmbienceVolume = PlayerPrefs.GetFloat(KEY_AMBIENCE,    defAmbience);
        PlayerVolume   = PlayerPrefs.GetFloat(KEY_PLAYER,      defPlayer);
        NemesisVolume  = PlayerPrefs.GetFloat(KEY_NEMESIS,     defNemesis);
        UIVolume       = PlayerPrefs.GetFloat(KEY_UI,          defUI);
        Sensitivity    = PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);

        InvertYAxis       = GetPrefBool(KEY_INVERT_Y,             DEFAULT_INVERT_Y);
        Brightness        = PlayerPrefs.GetFloat(KEY_BRIGHTNESS,  DEFAULT_BRIGHTNESS);
        Contrast          = PlayerPrefs.GetFloat(KEY_CONTRAST,    DEFAULT_CONTRAST);
        Gamma             = PlayerPrefs.GetFloat(KEY_GAMMA,       DEFAULT_GAMMA);
        CRTScanlines      = GetPrefBool(KEY_CRT,                  DEFAULT_CRT);
        PSXDithering      = GetPrefBool(KEY_DITHER,               DEFAULT_DITHER);
        ResolutionIndex   = PlayerPrefs.GetInt(KEY_RESOLUTION,    DEFAULT_RESOLUTION);
        WindowMode        = PlayerPrefs.GetInt(KEY_WINDOW_MODE,   DEFAULT_WINDOW_MODE);
        FPSLimit          = PlayerPrefs.GetInt(KEY_FPS_LIMIT,     DEFAULT_FPS_LIMIT);
        VSync             = GetPrefBool(KEY_VSYNC,                DEFAULT_VSYNC);
        AudioInBackground = GetPrefBool(KEY_AUDIO_BG,             DEFAULT_AUDIO_BG);

        IsInitialized = true;
        TakeSnapshot();

        // Sincronizo el AudioManager con lo que acabamos de leer de PlayerPrefs
        // (por si el AudioManager arrancó antes de que SettingsModel se inicialice).
        PushVolumesToAudioManager();
    }

    // ── Setters (Volumen + Sensibilidad: aplican en vivo al mixer) ──────────
    //
    // Cada setter cambia el state en memoria, notifica a la View y, si el
    // AudioManager existe, escribe el dB correspondiente en el mixer al toque.
    // Esto da preview en vivo: mover el slider se escucha sin necesidad de Apply.
    // Apply() persiste a PlayerPrefs; Revert() restablece y vuelve a empujar al mixer.

    public void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetMasterVolume(MasterVolume);
        NotifyDataChanged();
    }
    public void SetMusicVolume(float v)
    {
        MusicVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetMusicVolume(MusicVolume);
        NotifyDataChanged();
    }
    public void SetSFXVolume(float v)
    {
        SFXVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetSFXVolumeOnly(SFXVolume);
        NotifyDataChanged();
    }
    public void SetVoiceVolume(float v)
    {
        VoiceVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetVoiceVolume(VoiceVolume);
        NotifyDataChanged();
    }
    public void SetAmbienceVolume(float v)
    {
        AmbienceVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetAmbienceVolume(AmbienceVolume);
        NotifyDataChanged();
    }
    public void SetPlayerVolume(float v)
    {
        PlayerVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetPlayerVolume(PlayerVolume);
        NotifyDataChanged();
    }
    public void SetNemesisVolume(float v)
    {
        NemesisVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetNemesisVolume(NemesisVolume);
        NotifyDataChanged();
    }
    public void SetUIVolume(float v)
    {
        UIVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetUIVolume(UIVolume);
        NotifyDataChanged();
    }
    public void SetSensitivity(float v)  { Sensitivity  = Mathf.Max(0.01f, v); NotifyDataChanged(); }

    // ── Setters de video/controles (aplicados por los *Applier.cs en Apply) ──

    public void SetInvertYAxis(bool v)      { InvertYAxis = v;                NotifyDataChanged(); }
    public void SetBrightness(float v)      { Brightness = Mathf.Clamp01(v);  NotifyDataChanged(); }
    public void SetContrast(float v)        { Contrast = Mathf.Clamp01(v);    NotifyDataChanged(); }
    public void SetGamma(float v)           { Gamma = Mathf.Clamp01(v);       NotifyDataChanged(); }
    public void SetCRTScanlines(bool v)     { CRTScanlines = v;               NotifyDataChanged(); }
    public void SetPSXDithering(bool v)     { PSXDithering = v;               NotifyDataChanged(); }
    public void SetResolutionIndex(int v)   { ResolutionIndex = v;            NotifyDataChanged(); }
    public void SetWindowMode(int v)        { WindowMode = v;                 NotifyDataChanged(); }
    public void SetFPSLimit(int v)          { FPSLimit = v;                   NotifyDataChanged(); }
    public void SetVSync(bool v)            { VSync = v;                      NotifyDataChanged(); }
    public void SetAudioInBackground(bool v){ AudioInBackground = v;          NotifyDataChanged(); }

    // ── Persistencia ─────────────────────────────────────────────────────────

    public void Apply()
    {
        PlayerPrefs.SetFloat(KEY_MASTER,      MasterVolume);
        PlayerPrefs.SetFloat(KEY_MUSIC,       MusicVolume);
        PlayerPrefs.SetFloat(KEY_SFX,         SFXVolume);
        PlayerPrefs.SetFloat(KEY_VOICE,       VoiceVolume);
        PlayerPrefs.SetFloat(KEY_AMBIENCE,    AmbienceVolume);
        PlayerPrefs.SetFloat(KEY_PLAYER,      PlayerVolume);
        PlayerPrefs.SetFloat(KEY_NEMESIS,     NemesisVolume);
        PlayerPrefs.SetFloat(KEY_UI,          UIVolume);
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, Sensitivity);

        SetPrefBool(KEY_INVERT_Y,            InvertYAxis);
        PlayerPrefs.SetFloat(KEY_BRIGHTNESS, Brightness);
        PlayerPrefs.SetFloat(KEY_CONTRAST,   Contrast);
        PlayerPrefs.SetFloat(KEY_GAMMA,      Gamma);
        SetPrefBool(KEY_CRT,                 CRTScanlines);
        SetPrefBool(KEY_DITHER,              PSXDithering);
        PlayerPrefs.SetInt(KEY_RESOLUTION,   ResolutionIndex);
        PlayerPrefs.SetInt(KEY_WINDOW_MODE,  WindowMode);
        PlayerPrefs.SetInt(KEY_FPS_LIMIT,    FPSLimit);
        SetPrefBool(KEY_VSYNC,               VSync);
        SetPrefBool(KEY_AUDIO_BG,            AudioInBackground);

        PlayerPrefs.Save();

        // Re-empuja al mixer (los setters en vivo ya lo hicieron, pero esto es
        // defensivo por si en algun caso el AudioManager se hubiera reseteado).
        PushVolumesToAudioManager();

        TakeSnapshot();
        OnSettingsApplied?.Invoke();
    }

    /// <summary>Restaura defaults en memoria. NO persiste hasta que se llame Apply.</summary>
    public void ResetToDefaults()
    {
        MasterVolume   = 1f;
        MusicVolume    = 1f;
        SFXVolume      = 1f;
        VoiceVolume    = DEFAULT_VOICE;
        AmbienceVolume = 1f;
        PlayerVolume   = 1f;
        NemesisVolume  = 1f;
        UIVolume       = 1f;
        Sensitivity    = DEFAULT_SENSITIVITY;

        InvertYAxis       = DEFAULT_INVERT_Y;
        Brightness        = DEFAULT_BRIGHTNESS;
        Contrast          = DEFAULT_CONTRAST;
        Gamma             = DEFAULT_GAMMA;
        CRTScanlines      = DEFAULT_CRT;
        PSXDithering      = DEFAULT_DITHER;
        ResolutionIndex   = DEFAULT_RESOLUTION;
        WindowMode        = DEFAULT_WINDOW_MODE;
        FPSLimit          = DEFAULT_FPS_LIMIT;
        VSync             = DEFAULT_VSYNC;
        AudioInBackground = DEFAULT_AUDIO_BG;

        PushVolumesToAudioManager();
        NotifyDataChanged();
    }

    public void Revert()
    {
        MasterVolume   = _snapMaster;
        MusicVolume    = _snapMusic;
        SFXVolume      = _snapSFX;
        VoiceVolume    = _snapVoice;
        AmbienceVolume = _snapAmbience;
        PlayerVolume   = _snapPlayer;
        NemesisVolume  = _snapNemesis;
        UIVolume       = _snapUI;
        Sensitivity    = _snapSensitivity;

        InvertYAxis       = _snapInvertY;
        Brightness        = _snapBrightness;
        Contrast          = _snapContrast;
        Gamma             = _snapGamma;
        CRTScanlines      = _snapCRT;
        PSXDithering      = _snapDither;
        ResolutionIndex   = _snapResolution;
        WindowMode        = _snapWindowMode;
        FPSLimit          = _snapFPSLimit;
        VSync             = _snapVSync;
        AudioInBackground = _snapAudioBg;

        // El mixer fue cambiando en vivo mientras el usuario movia los sliders;
        // al revertir hay que volver a empujar los valores del snapshot al mixer.
        PushVolumesToAudioManager();
        NotifyDataChanged();
    }

    private void TakeSnapshot()
    {
        _snapMaster      = MasterVolume;
        _snapMusic       = MusicVolume;
        _snapSFX         = SFXVolume;
        _snapVoice       = VoiceVolume;
        _snapAmbience    = AmbienceVolume;
        _snapPlayer      = PlayerVolume;
        _snapNemesis     = NemesisVolume;
        _snapUI          = UIVolume;
        _snapSensitivity = Sensitivity;
        _snapInvertY     = InvertYAxis;
        _snapBrightness  = Brightness;
        _snapContrast    = Contrast;
        _snapGamma       = Gamma;
        _snapCRT         = CRTScanlines;
        _snapDither      = PSXDithering;
        _snapResolution  = ResolutionIndex;
        _snapWindowMode  = WindowMode;
        _snapFPSLimit    = FPSLimit;
        _snapVSync       = VSync;
        _snapAudioBg     = AudioInBackground;
    }

    private void PushVolumesToAudioManager()
    {
        if (!AudioManager.Exists) return;
        var am = AudioManager.Instance;
        am.SetMasterVolume(MasterVolume);
        am.SetMusicVolume(MusicVolume);
        am.SetSFXVolumeOnly(SFXVolume);
        am.SetVoiceVolume(VoiceVolume);
        am.SetAmbienceVolume(AmbienceVolume);
        am.SetPlayerVolume(PlayerVolume);
        am.SetNemesisVolume(NemesisVolume);
        am.SetUIVolume(UIVolume);
    }

    // ── Helpers PlayerPrefs (no soporta bool nativamente) ────────────────────

    private static bool GetPrefBool(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) != 0;
    private static void SetPrefBool(string key, bool value) => PlayerPrefs.SetInt(key, value ? 1 : 0);
}
