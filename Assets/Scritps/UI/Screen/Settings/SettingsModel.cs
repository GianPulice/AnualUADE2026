using System;
using UnityEngine;

public class SettingsModel : BaseScreenModel
{
    /// <summary>
    /// Raised when <see cref="Apply"/> persists the changes.
    /// Typical subscribers: <c>CameraSensitivityApplier</c>, future post-process systems.
    /// </summary>
    public static event Action OnSettingsApplied;

    // ── PlayerPrefs keys ─────────────────────────────────────────────────────
    // Connected to the AudioManager. Only 3 buses are visible to the player: the SFX slider
    // groups Ambience/Player/Nemesis/UI/Voice (see AudioManager.SetGameplaySfxBundle).
    private const string KEY_MASTER       = "Settings_MasterVolume";
    private const string KEY_MUSIC        = "Settings_MusicVolume";
    private const string KEY_SFX          = "Settings_SFXVolume";
    private const string KEY_SENSITIVITY  = "Settings_Sensitivity";
    // Applied to the game by the appliers that read these keys when OnSettingsApplied fires
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

    // ── State connected to the AudioManager ──────────────────────────────────
    public float MasterVolume { get; private set; }
    public float MusicVolume  { get; private set; }
    public float SFXVolume    { get; private set; }
    public float Sensitivity  { get; private set; }

    // ── Video/controls state (read by the *Applier.cs on Apply) ──────────────
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

    // ── Snapshot for revert ──────────────────────────────────────────────────
    private float _snapMaster, _snapMusic, _snapSFX, _snapSensitivity;
    private bool  _snapInvertY, _snapCRT, _snapDither, _snapVSync, _snapAudioBg;
    private float _snapBrightness, _snapContrast, _snapGamma;
    private int   _snapResolution, _snapWindowMode, _snapFPSLimit;

    public override void Initialize()
    {
        float defMaster = AudioManager.Exists ? AudioManager.Instance.MasterVolume : 1f;
        float defMusic  = AudioManager.Exists ? AudioManager.Instance.MusicVolume  : 1f;
        float defSFX    = AudioManager.Exists ? AudioManager.Instance.SFXVolume    : 1f;

        MasterVolume = PlayerPrefs.GetFloat(KEY_MASTER,      defMaster);
        MusicVolume  = PlayerPrefs.GetFloat(KEY_MUSIC,       defMusic);
        SFXVolume    = PlayerPrefs.GetFloat(KEY_SFX,         defSFX);
        Sensitivity  = PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);

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

        // Sync the AudioManager with what we just read from PlayerPrefs
        // (in case the AudioManager started before SettingsModel was initialized).
        PushVolumesToAudioManager();
    }

    // ── Setters (Volume + Sensitivity: applied live to the mixer) ───────────
    //
    // Each setter changes the in-memory state, notifies the View and, if the
    // AudioManager exists, writes the corresponding dB to the mixer straight away.
    // This gives a live preview: moving the slider is audible without needing Apply.
    // Apply() persists to PlayerPrefs; Revert() restores and pushes to the mixer again.

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
    /// <summary>
    /// A single slider for everything that is not music: moves SFX, Ambience, Player,
    /// Nemesis, UI and Voice as a block.
    /// </summary>
    public void SetSFXVolume(float v)
    {
        SFXVolume = Mathf.Clamp01(v);
        if (AudioManager.Exists) AudioManager.Instance.SetGameplaySfxBundle(SFXVolume);
        NotifyDataChanged();
    }
    public void SetSensitivity(float v)  { Sensitivity  = Mathf.Max(0.01f, v); NotifyDataChanged(); }

    // ── Video/controls setters (applied by the *Applier.cs on Apply) ─────────

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

    // ── Persistence ──────────────────────────────────────────────────────────

    public void Apply()
    {
        PlayerPrefs.SetFloat(KEY_MASTER,      MasterVolume);
        PlayerPrefs.SetFloat(KEY_MUSIC,       MusicVolume);
        PlayerPrefs.SetFloat(KEY_SFX,         SFXVolume);
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

        // Push to the mixer again (the live setters already did, but this is
        // defensive in case the AudioManager was reset at some point).
        PushVolumesToAudioManager();

        TakeSnapshot();
        OnSettingsApplied?.Invoke();
    }

    /// <summary>Restores defaults in memory. Does NOT persist until Apply is called.</summary>
    public void ResetToDefaults()
    {
        MasterVolume = 1f;
        MusicVolume  = 1f;
        SFXVolume    = 1f;
        Sensitivity  = DEFAULT_SENSITIVITY;

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
        MasterVolume = _snapMaster;
        MusicVolume  = _snapMusic;
        SFXVolume    = _snapSFX;
        Sensitivity  = _snapSensitivity;

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

        // The mixer was changing live while the user moved the sliders;
        // on revert we have to push the snapshot values back to the mixer.
        PushVolumesToAudioManager();
        NotifyDataChanged();
    }

    private void TakeSnapshot()
    {
        _snapMaster      = MasterVolume;
        _snapMusic       = MusicVolume;
        _snapSFX         = SFXVolume;
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
        am.SetGameplaySfxBundle(SFXVolume);
    }

    // ── PlayerPrefs helpers (it does not support bool natively) ──────────────

    private static bool GetPrefBool(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) != 0;
    private static void SetPrefBool(string key, bool value) => PlayerPrefs.SetInt(key, value ? 1 : 0);
}
