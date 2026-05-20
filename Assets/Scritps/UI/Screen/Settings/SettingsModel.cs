using UnityEngine;

public class SettingsModel : BaseScreenModel
{
    private const string KEY_MASTER      = "Settings_MasterVolume";
    private const string KEY_MUSIC       = "Settings_MusicVolume";
    private const string KEY_SFX         = "Settings_SFXVolume";
    private const string KEY_SENSITIVITY = "Settings_Sensitivity";
    private const float  DEFAULT_SENSITIVITY = 1f;

    public float MasterVolume { get; private set; }
    public float MusicVolume  { get; private set; }
    public float SFXVolume    { get; private set; }
    public float Sensitivity  { get; private set; }

    private float _snapMaster;
    private float _snapMusic;
    private float _snapSFX;
    private float _snapSensitivity;

    // Carga desde PlayerPrefs (o valores actuales de AudioManager como fallback)
    public override void Initialize()
    {
        float defMaster = AudioManager.Instance != null ? AudioManager.Instance.MasterVolume : 1f;
        float defMusic  = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume  : 1f;
        float defSFX    = AudioManager.Instance != null ? AudioManager.Instance.SFXVolume    : 1f;

        MasterVolume = PlayerPrefs.GetFloat(KEY_MASTER,      defMaster);
        MusicVolume  = PlayerPrefs.GetFloat(KEY_MUSIC,       defMusic);
        SFXVolume    = PlayerPrefs.GetFloat(KEY_SFX,         defSFX);
        Sensitivity  = PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);

        IsInitialized = true;
        TakeSnapshot();
    }

    // ── Setters (llamados por la View al mover sliders) ──────────

    public void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v);       NotifyDataChanged(); }
    public void SetMusicVolume(float v)  { MusicVolume  = Mathf.Clamp01(v);       NotifyDataChanged(); }
    public void SetSFXVolume(float v)    { SFXVolume    = Mathf.Clamp01(v);       NotifyDataChanged(); }
    public void SetSensitivity(float v)  { Sensitivity  = Mathf.Max(0.01f, v);    NotifyDataChanged(); }

    // ── Persistencia ─────────────────────────────────────────────

    public void Apply()
    {
        PlayerPrefs.SetFloat(KEY_MASTER,      MasterVolume);
        PlayerPrefs.SetFloat(KEY_MUSIC,       MusicVolume);
        PlayerPrefs.SetFloat(KEY_SFX,         SFXVolume);
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, Sensitivity);
        PlayerPrefs.Save();

        if (AudioManager.Exists)
        {
            AudioManager.Instance.SetMasterVolume(MasterVolume);
            AudioManager.Instance.SetMusicVolume(MusicVolume);
            AudioManager.Instance.SetSFXVolume(SFXVolume);
        }

        TakeSnapshot();
    }

    // Descarta cambios sin persistir y restaura el snapshot
    public void Revert()
    {
        MasterVolume = _snapMaster;
        MusicVolume  = _snapMusic;
        SFXVolume    = _snapSFX;
        Sensitivity  = _snapSensitivity;
        NotifyDataChanged();
    }

    private void TakeSnapshot()
    {
        _snapMaster      = MasterVolume;
        _snapMusic       = MusicVolume;
        _snapSFX         = SFXVolume;
        _snapSensitivity = Sensitivity;
    }
}
