using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drive del shader fullscreen <c>Fullscreen_VisionFog</c>. Setea las globals que
/// el shader lee y maneja transiciones suaves entre presets de niebla.
///
/// Modelo de configuración:
///   - <see cref="defaultConfig"/>: el preset que usa el fog cuando el player no está
///     en ninguna LightZone (default = pasillos oscuros).
///   - <see cref="PushConfig"/> / <see cref="PopConfig"/>: API pública para LightZone
///     triggers. Maneja un stack — la zona más interna gana.
///
/// El player vive en otra escena (gameplay aditivo), así que se busca por tag en runtime.
/// Mientras no hay player, setea <c>_VisionEnd = 0</c> → el shader hace early-out → no fog.
///
/// Globals seteadas:
///   _PlayerPos, _VisionStart, _VisionEnd, _FogColor, _LightPreservation, _FogDensityPower,
///   _PlayerLightPosition, _PlayerLightRange, _PlayerLightIntensity, _PlayerLightColor,
///   _VisionFogBlurStrength, _FogLightBypassData[8], _FogLightBypassCount
/// </summary>
[DefaultExecutionOrder(100)]
public class VisionRangeController : MonoBehaviour
{
    public const int MaxBypassZones = 8;

    [Header("Default config")]
    [Tooltip("Preset que se aplica cuando el player no está en ninguna LightZone. " +
             "Suele ser una zona 'oscura' / opresiva — los LightZones modulan hacia arriba.")]
    [SerializeField] private SO_VisionFogConfig defaultConfig;

    [Header("Player")]
    [Tooltip("Tag del GameObject del player. Se busca en runtime porque vive en otra escena.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Asignación manual opcional. Si está vacío, se busca por tag.")]
    [SerializeField] private Transform playerOverride;

    [Tooltip("Cada cuántos frames re-buscar al player si todavía no apareció.")]
    [SerializeField, Min(1)] private int searchEveryNFrames = 30;

    // ── Estado ──────────────────────────────────────────────────────────────
    private Transform _player;

    // Stack de configs activas: la última pusheada gana. El bottom es siempre defaultConfig.
    private readonly List<SO_VisionFogConfig> _configStack = new List<SO_VisionFogConfig>();

    // Fuente de luz del player (opcional) y bypass zones registradas por sus componentes.
    private FogLightSource _playerLight;
    private static readonly List<FogLightBypass> s_bypassZones = new List<FogLightBypass>(MaxBypassZones);

    // Buffer reutilizable para el push del array al shader.
    private readonly Vector4[] _bypassBuffer = new Vector4[MaxBypassZones];

    // Valores actuales del fog (interpolados frame a frame).
    private float _currentVisionStart;
    private float _currentVisionEnd;
    private Color _currentFogColor;
    private float _currentLightPreservation;
    private float _currentDensityPower = 1f;
    private float _currentPlayerLightRange;
    private float _currentPlayerLightIntensity;
    private Color _currentPlayerLightColor = Color.black;
    private float _currentBlurStrength;

    // Targets (los del config activo del top del stack).
    private float _targetVisionStart;
    private float _targetVisionEnd;
    private Color _targetFogColor;
    private float _targetLightPreservation;
    private float _targetDensityPower = 1f;
    private float _targetPlayerLightRange;
    private float _targetPlayerLightIntensity;
    private Color _targetPlayerLightColor = Color.black;
    private float _targetBlurStrength;

    // Velocidad de transición actual (en unidades por segundo, derivada del transitionDuration).
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
            // Inicializar valores actuales al target para evitar lerp desde 0 al arrancar.
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

            Shader.SetGlobalFloat(VEndId, 0f); // early-out del shader
            Shader.SetGlobalFloat(PlayerLightRngId, 0f);
            Shader.SetGlobalInt(BypassCountId, 0);
            return;
        }

        // Releer el config activo cada frame — así tocar sliders del SO en el Inspector
        // mientras estás en Play se ve al toque, sin necesitar un nuevo Push/Pop.
        SO_VisionFogConfig activeConfig = _configStack.Count > 0
            ? _configStack[_configStack.Count - 1]
            : defaultConfig;
        if (activeConfig != null) ApplyTargetsFromConfig(activeConfig);

        // Interpolar valores actuales hacia los targets.
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

        // Linterna del player — si hay un FogLightSource asignado tomamos su transform
        // en tiempo real; si no, usamos la posición del player como fallback.
        // Si el FogLightSource está en modo "leer del Light component" (default), sus
        // range/color/intensity ganan sobre los del SO — así la degradación futura
        // de módulos (§2.5.1) baja el fogClearRadius automáticamente.
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

        // Bypass zones activas — compacto los primeros N en el buffer y pusheo el array.
        int count = 0;
        for (int i = 0; i < s_bypassZones.Count && count < MaxBypassZones; i++)
        {
            FogLightBypass b = s_bypassZones[i];
            if (b == null || !b.isActiveAndEnabled || b.radius <= 0f) continue;
            Vector3 p = b.transform.position;
            _bypassBuffer[count++] = new Vector4(p.x, p.y, p.z, b.radius);
        }
        // Limpia el resto para no leer basura de frames anteriores.
        for (int i = count; i < MaxBypassZones; i++) _bypassBuffer[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(BypassDataId, _bypassBuffer);
        Shader.SetGlobalInt(BypassCountId, count);
    }

    // ── API pública para LightZones ─────────────────────────────────────────

    /// <summary>
    /// Push de un config al stack. Lo llaman los LightZones cuando el player entra.
    /// Maneja anidamiento — la zona más interna (última pusheada) es la que se aplica.
    /// </summary>
    public void PushConfig(SO_VisionFogConfig config)
    {
        if (config == null) return;
        _configStack.Add(config);
        ApplyTargetsFromConfig(config);
    }

    /// <summary>
    /// Pop de un config específico del stack. Lo llaman los LightZones al salir.
    /// Si la zona que sale no estaba en el top (anidamiento raro), igual se remueve
    /// pero el target no cambia hasta que se pop el verdadero top.
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

    /// <summary>Cambiar el default config en runtime (ej: cambio de nivel).</summary>
    public void SetDefaultConfig(SO_VisionFogConfig newDefault)
    {
        defaultConfig = newDefault;
        // Si no hay zonas activas, aplicar el nuevo default.
        if (_configStack.Count == 0 && newDefault != null)
            ApplyTargetsFromConfig(newDefault);
    }

    // ── API para FogLightSource / FogLightBypass ────────────────────────────

    /// <summary>Setear (o limpiar con null) la linterna del player que se lee cada frame.</summary>
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

    // ── API para VisionFogTrack (Timeline) ──────────────────────────────────

    /// <summary>
    /// Escribe los globals directo, sin pasar por el stack ni el lerp de LateUpdate.
    /// La llama <c>VisionFogMixerBehaviour</c> con el resultado ya mezclado de los
    /// clips activos en la pista — pensado para scrub-preview en el editor sin dar Play.
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

        // Convertir transitionDuration (segundos) en lerp rate (1/s).
        // Aproximación: si querés llegar al 99% en `transitionDuration` segundos, el rate
        // exponencial es ~4.6 / duration. Usamos 4 para una curva un poco menos abrupta.
        _lerpRate = config.transitionDuration > 0.01f
            ? 4f / config.transitionDuration
            : 1000f; // efectivamente instantáneo
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
