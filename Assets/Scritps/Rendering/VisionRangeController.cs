using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Driver of the <c>Fullscreen_VisionFog</c> shader. Sets the globals the shader reads and
/// handles smooth transitions between fog presets.
///
/// Configuration model:
///   - <see cref="defaultConfig"/>: the preset the fog uses when the player is not inside
///     any LightZone (default = dark corridors).
///   - <see cref="PushConfig"/> / <see cref="PopConfig"/>: public API for LightZone
///     triggers. Keeps a stack — the innermost zone wins.
///
/// The player lives in another scene (additive gameplay), so it is looked up by tag at runtime.
/// While there is no player, it sets <c>_VisionEnd = 0</c> → the shader early-outs → no fog.
///
/// Globals set:
///   _PlayerPos, _VisionStart, _VisionEnd, _FogColor, _LightPreservation, _FogDensityPower,
///   _PlayerLightPosition, _PlayerLightRange, _PlayerLightIntensity, _PlayerLightColor,
///   _VisionFogBlurStrength, _FogLightBypassData[8], _FogLightBypassCount
/// </summary>
[DefaultExecutionOrder(100)]
public class VisionRangeController : MonoBehaviour
{
    public const int MaxBypassZones = 8;

    [Header("Default config")]
    [Tooltip("Preset applied when the player is not inside any LightZone. " +
             "Usually a 'dark' / oppressive area — LightZones modulate upwards from it.")]
    [SerializeField] private SO_VisionFogConfig defaultConfig;

    [Header("Player")]
    [Tooltip("Tag of the player GameObject. Looked up at runtime because it lives in another scene.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Optional manual assignment. If empty, the player is looked up by tag.")]
    [SerializeField] private Transform playerOverride;

    [Tooltip("How many frames between re-searching for the player while it has not shown up yet.")]
    [SerializeField, Min(1)] private int searchEveryNFrames = 30;

    // ── State ───────────────────────────────────────────────────────────────
    private Transform _player;

    // Stack of active configs: the last one pushed wins. The bottom is always defaultConfig.
    private readonly List<SO_VisionFogConfig> _configStack = new List<SO_VisionFogConfig>();

    // Player light source (optional) and bypass zones registered by their components.
    private FogLightSource _playerLight;
    private static readonly List<FogLightBypass> s_bypassZones = new List<FogLightBypass>(MaxBypassZones);

    // Reusable buffer for pushing the array to the shader.
    private readonly Vector4[] _bypassBuffer = new Vector4[MaxBypassZones];

    // Current fog values (interpolated frame by frame).
    private float _currentVisionStart;
    private float _currentVisionEnd;
    private Color _currentFogColor;
    private float _currentLightPreservation;
    private float _currentDensityPower = 1f;
    private float _currentPlayerLightRange;
    private float _currentPlayerLightIntensity;
    private Color _currentPlayerLightColor = Color.black;
    private float _currentBlurStrength;

    // Targets (those of the active config at the top of the stack).
    private float _targetVisionStart;
    private float _targetVisionEnd;
    private Color _targetFogColor;
    private float _targetLightPreservation;
    private float _targetDensityPower = 1f;
    private float _targetPlayerLightRange;
    private float _targetPlayerLightIntensity;
    private Color _targetPlayerLightColor = Color.black;
    private float _targetBlurStrength;

    // Current transition speed (in units per second, derived from transitionDuration).
    private float _lerpRate = 4f;

    private static readonly int PlayerPosId       = Shader.PropertyToID("_PlayerPos");
    private static readonly int VStartId          = Shader.PropertyToID("_VisionStart");
    private static readonly int VEndId            = Shader.PropertyToID("_VisionEnd");
    private static readonly int FogColorId        = Shader.PropertyToID("_FogColor");
    private static readonly int LightPresId       = Shader.PropertyToID("_LightPreservation");
    private static readonly int DensityPowerId    = Shader.PropertyToID("_FogDensityPower");
    private static readonly int PlayerLightPosId  = Shader.PropertyToID("_PlayerLightPosition");
    private static readonly int PlayerLightRngId  = Shader.PropertyToID("_PlayerLightRange");
    private static readonly int PlayerLightIntId  = Shader.PropertyToID("_PlayerLightIntensity");
    private static readonly int PlayerLightColId  = Shader.PropertyToID("_PlayerLightColor");
    private static readonly int BlurStrengthId    = Shader.PropertyToID("_VisionFogBlurStrength");
    private static readonly int BypassDataId      = Shader.PropertyToID("_FogLightBypassData");
    private static readonly int BypassCountId     = Shader.PropertyToID("_FogLightBypassCount");

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        if (defaultConfig != null)
        {
            ApplyTargetsFromConfig(defaultConfig);
            // Initialize the current values to the target to avoid lerping from 0 at startup.
            _currentVisionStart         = _targetVisionStart;
            _currentVisionEnd           = _targetVisionEnd;
            _currentFogColor            = _targetFogColor;
            _currentLightPreservation   = _targetLightPreservation;
            _currentDensityPower        = _targetDensityPower;
            _currentPlayerLightRange    = _targetPlayerLightRange;
            _currentPlayerLightIntensity= _targetPlayerLightIntensity;
            _currentPlayerLightColor    = _targetPlayerLightColor;
            _currentBlurStrength        = _targetBlurStrength;
        }

        TryAcquirePlayer();
    }

    private void LateUpdate()
    {
        if (_player == null)
        {
            if (Time.frameCount % searchEveryNFrames == 0)
                TryAcquirePlayer();

            Shader.SetGlobalFloat(VEndId, 0f); // shader early-out
            Shader.SetGlobalFloat(PlayerLightRngId, 0f);
            Shader.SetGlobalInt(BypassCountId, 0);
            return;
        }

        // Re-read the active config every frame — that way tweaking the SO's sliders in the
        // Inspector while in Play is visible immediately, without needing a new Push/Pop.
        SO_VisionFogConfig activeConfig = _configStack.Count > 0
            ? _configStack[_configStack.Count - 1]
            : defaultConfig;
        if (activeConfig != null) ApplyTargetsFromConfig(activeConfig);

        // Interpolate the current values towards the targets.
        float t = Time.deltaTime * _lerpRate;
        _currentVisionStart         = Mathf.Lerp(_currentVisionStart,         _targetVisionStart,         t);
        _currentVisionEnd           = Mathf.Lerp(_currentVisionEnd,           _targetVisionEnd,           t);
        _currentFogColor            = Color.Lerp(_currentFogColor,            _targetFogColor,            t);
        _currentLightPreservation   = Mathf.Lerp(_currentLightPreservation,   _targetLightPreservation,   t);
        _currentDensityPower        = Mathf.Lerp(_currentDensityPower,        _targetDensityPower,        t);
        _currentPlayerLightRange    = Mathf.Lerp(_currentPlayerLightRange,    _targetPlayerLightRange,    t);
        _currentPlayerLightIntensity= Mathf.Lerp(_currentPlayerLightIntensity,_targetPlayerLightIntensity,t);
        _currentPlayerLightColor    = Color.Lerp(_currentPlayerLightColor,    _targetPlayerLightColor,    t);
        _currentBlurStrength        = Mathf.Lerp(_currentBlurStrength,        _targetBlurStrength,        t);

        Shader.SetGlobalVector(PlayerPosId, _player.position);
        Shader.SetGlobalFloat(VStartId,   _currentVisionStart);
        Shader.SetGlobalFloat(VEndId,     _currentVisionEnd);
        Shader.SetGlobalColor(FogColorId, _currentFogColor);
        Shader.SetGlobalFloat(LightPresId,      _currentLightPreservation);
        Shader.SetGlobalFloat(DensityPowerId,   _currentDensityPower);
        Shader.SetGlobalFloat(BlurStrengthId,   _currentBlurStrength);

        // Player flashlight — if there is a FogLightSource assigned we take its transform in
        // real time; otherwise we fall back to the player's position.
        // If the FogLightSource is in "read from the Light component" mode (default), its
        // range/color/intensity win over those of the SO — that way the future module
        // degradation (§2.5.1) lowers the fogClearRadius automatically.
        Vector3 lightPos = _playerLight != null ? _playerLight.transform.position : _player.position;
        Shader.SetGlobalVector(PlayerLightPosId, lightPos);

        if (_playerLight != null && _playerLight.HasLightOverride)
        {
            Shader.SetGlobalFloat(PlayerLightRngId, _playerLight.OverrideRange);
            Shader.SetGlobalFloat(PlayerLightIntId, _playerLight.OverrideIntensity);
            Shader.SetGlobalColor(PlayerLightColId, _playerLight.OverrideColor);
        }
        else
        {
            Shader.SetGlobalFloat(PlayerLightRngId, _currentPlayerLightRange);
            Shader.SetGlobalFloat(PlayerLightIntId, _currentPlayerLightIntensity);
            Shader.SetGlobalColor(PlayerLightColId, _currentPlayerLightColor);
        }

        // Active bypass zones — compact the first N into the buffer and push the array.
        int count = 0;
        for (int i = 0; i < s_bypassZones.Count && count < MaxBypassZones; i++)
        {
            FogLightBypass b = s_bypassZones[i];
            if (b == null || !b.isActiveAndEnabled || b.radius <= 0f) continue;
            Vector3 p = b.transform.position;
            _bypassBuffer[count++] = new Vector4(p.x, p.y, p.z, b.radius);
        }
        // Clear the rest so no garbage from previous frames is read.
        for (int i = count; i < MaxBypassZones; i++) _bypassBuffer[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(BypassDataId, _bypassBuffer);
        Shader.SetGlobalInt(BypassCountId, count);
    }

    // ── Public API for LightZones ───────────────────────────────────────────

    /// <summary>
    /// Pushes a config onto the stack. Called by LightZones when the player enters.
    /// Handles nesting — the innermost zone (last pushed) is the one applied.
    /// </summary>
    public void PushConfig(SO_VisionFogConfig config)
    {
        if (config == null) return;
        _configStack.Add(config);
        ApplyTargetsFromConfig(config);
    }

    /// <summary>
    /// Pops a specific config from the stack. Called by LightZones on exit.
    /// If the zone leaving was not on top (odd nesting), it is still removed but the
    /// target does not change until the actual top is popped.
    /// </summary>
    public void PopConfig(SO_VisionFogConfig config)
    {
        if (config == null) return;
        int lastIndex = _configStack.LastIndexOf(config);
        if (lastIndex < 0) return;

        bool wasTop = lastIndex == _configStack.Count - 1;
        _configStack.RemoveAt(lastIndex);

        if (wasTop)
        {
            SO_VisionFogConfig newTop = _configStack.Count > 0
                ? _configStack[_configStack.Count - 1]
                : defaultConfig;

            if (newTop != null) ApplyTargetsFromConfig(newTop);
        }
    }

    /// <summary>Change the default config at runtime (e.g. on a level change).</summary>
    public void SetDefaultConfig(SO_VisionFogConfig newDefault)
    {
        defaultConfig = newDefault;
        // If there are no active zones, apply the new default.
        if (_configStack.Count == 0 && newDefault != null)
            ApplyTargetsFromConfig(newDefault);
    }

    // ── API for FogLightSource / FogLightBypass ─────────────────────────────

    /// <summary>Set (or clear with null) the player flashlight that is read every frame.</summary>
    public void SetPlayerLightSource(FogLightSource source) => _playerLight = source;

    public static void RegisterBypass(FogLightBypass zone)
    {
        if (zone == null || s_bypassZones.Contains(zone)) return;
        s_bypassZones.Add(zone);
    }

    public static void UnregisterBypass(FogLightBypass zone)
    {
        if (zone == null) return;
        s_bypassZones.Remove(zone);
    }

    // ── API for VisionFogTrack (Timeline) ───────────────────────────────────

    /// <summary>
    /// Writes the globals directly, bypassing the stack and the LateUpdate lerp.
    /// Called by <c>VisionFogMixerBehaviour</c> with the already-blended result of the
    /// clips active on the track — meant for scrub-preview in the editor without pressing Play.
    /// </summary>
    public void ApplyPreviewBlend(float visionStart, float visionEnd, Color fogColor,
        float lightPreservation, float densityPower,
        float playerLightRange, float playerLightIntensity, Color playerLightColor,
        float blurStrength)
    {
        Vector3 previewPos = playerOverride != null ? playerOverride.position : transform.position;

        Shader.SetGlobalVector(PlayerPosId, previewPos);
        Shader.SetGlobalFloat(VStartId, visionStart);
        Shader.SetGlobalFloat(VEndId, visionEnd);
        Shader.SetGlobalColor(FogColorId, fogColor);
        Shader.SetGlobalFloat(LightPresId, lightPreservation);
        Shader.SetGlobalFloat(DensityPowerId, densityPower);
        Shader.SetGlobalVector(PlayerLightPosId, previewPos);
        Shader.SetGlobalFloat(PlayerLightRngId, playerLightRange);
        Shader.SetGlobalFloat(PlayerLightIntId, playerLightIntensity);
        Shader.SetGlobalColor(PlayerLightColId, playerLightColor);
        Shader.SetGlobalFloat(BlurStrengthId, blurStrength);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private void ApplyTargetsFromConfig(SO_VisionFogConfig config)
    {
        _targetVisionStart          = config.visionStart;
        _targetVisionEnd            = config.visionEnd;
        _targetFogColor             = config.fogColor;
        _targetLightPreservation    = config.lightPreservation;
        _targetDensityPower         = config.densityPower;
        _targetPlayerLightRange     = config.playerLightRange;
        _targetPlayerLightIntensity = config.playerLightIntensity;
        _targetPlayerLightColor     = config.playerLightColor;
        _targetBlurStrength         = config.blurStrength;

        // Convert transitionDuration (seconds) into a lerp rate (1/s).
        // Approximation: to reach 99% in `transitionDuration` seconds, the exponential rate
        // is ~4.6 / duration. We use 4 for a slightly less abrupt curve.
        _lerpRate = config.transitionDuration > 0.01f
            ? 4f / config.transitionDuration
            : 1000f; // effectively instant
    }

    private void TryAcquirePlayer()
    {
        if (playerOverride != null)
        {
            _player = playerOverride;
            return;
        }

        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) _player = go.transform;
    }
}
