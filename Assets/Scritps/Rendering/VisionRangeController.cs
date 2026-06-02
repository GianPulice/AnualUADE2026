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
///   _PlayerPos, _VisionStart, _VisionEnd, _FogColor, _LightPreservation
/// </summary>
[DefaultExecutionOrder(100)]
public class VisionRangeController : MonoBehaviour
{
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

    // Valores actuales del fog (interpolados frame a frame).
    private float _currentVisionStart;
    private float _currentVisionEnd;
    private Color _currentFogColor;
    private float _currentLightPreservation;

    // Targets (los del config activo del top del stack).
    private float _targetVisionStart;
    private float _targetVisionEnd;
    private Color _targetFogColor;
    private float _targetLightPreservation;

    // Velocidad de transición actual (en unidades por segundo, derivada del transitionDuration).
    private float _lerpRate = 4f;

    private static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
    private static readonly int VStartId    = Shader.PropertyToID("_VisionStart");
    private static readonly int VEndId      = Shader.PropertyToID("_VisionEnd");
    private static readonly int FogColorId  = Shader.PropertyToID("_FogColor");
    private static readonly int LightPresId = Shader.PropertyToID("_LightPreservation");

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        if (defaultConfig != null)
        {
            ApplyTargetsFromConfig(defaultConfig);
            // Inicializar valores actuales al target para evitar lerp desde 0 al arrancar.
            _currentVisionStart = _targetVisionStart;
            _currentVisionEnd   = _targetVisionEnd;
            _currentFogColor    = _targetFogColor;
            _currentLightPreservation = _targetLightPreservation;
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
            return;
        }

        // Interpolar valores actuales hacia los targets.
        float t = Time.deltaTime * _lerpRate;
        _currentVisionStart       = Mathf.Lerp(_currentVisionStart, _targetVisionStart, t);
        _currentVisionEnd         = Mathf.Lerp(_currentVisionEnd,   _targetVisionEnd,   t);
        _currentFogColor          = Color.Lerp(_currentFogColor,    _targetFogColor,    t);
        _currentLightPreservation = Mathf.Lerp(_currentLightPreservation, _targetLightPreservation, t);

        Shader.SetGlobalVector(PlayerPosId, _player.position);
        Shader.SetGlobalFloat(VStartId, _currentVisionStart);
        Shader.SetGlobalFloat(VEndId, _currentVisionEnd);
        Shader.SetGlobalColor(FogColorId, _currentFogColor);
        Shader.SetGlobalFloat(LightPresId, _currentLightPreservation);
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

    // ── Internals ───────────────────────────────────────────────────────────

    private void ApplyTargetsFromConfig(SO_VisionFogConfig config)
    {
        _targetVisionStart       = config.visionStart;
        _targetVisionEnd         = config.visionEnd;
        _targetFogColor          = config.fogColor;
        _targetLightPreservation = config.lightPreservation;

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
