using UnityEngine;

/// <summary>
/// Modula el rango de visión del shader fullscreen <c>Fullscreen_VisionFog</c> según
/// el nivel de luz ambiente en la posición del player.
///
/// Lógica:
///   - Zona oscura  (lightLevel = 0) → fog cierra cerca   (<c>visionEndDark</c>).
///   - Zona iluminada (lightLevel = 1) → fog se aleja      (<c>visionEndLit</c>).
///   - Transición suavizada por <c>lerpSpeed</c> para evitar pops al cruzar entre zonas.
///
/// El player vive en una escena distinta (gameplay aditivo), así que se busca por tag
/// en runtime. Mientras no exista player en escena, se setea <c>_VisionEnd = 0</c> y
/// el shader hace early-out (devuelve la escena limpia sin fog).
///
/// Setea globals que el shader fullscreen lee:
///   _PlayerPos, _VisionStart, _VisionEnd, _FogColor, _LightPreservation
///
/// Configuración:
///   - Asignar un <see cref="SO_VisionFogConfig"/> para usar preset por nivel.
///   - Si no se asigna, usa los campos "Fallback" del Inspector (defaults razonables).
/// </summary>
[DefaultExecutionOrder(100)] // después del PlayerController, antes del render
public class VisionRangeController : MonoBehaviour
{
    [Header("Preset (opcional)")]
    [Tooltip("Si está asignado, sus valores reemplazan los del fallback. Si está vacío, se usan los campos de abajo.")]
    [SerializeField] private SO_VisionFogConfig config;

    [Header("Player")]
    [Tooltip("Tag del GameObject del player. Se busca en runtime porque vive en otra escena (gameplay aditivo).")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Asignación manual opcional. Si está vacío, se busca por tag.")]
    [SerializeField] private Transform playerOverride;

    [Tooltip("Cada cuántos frames re-buscar al player si todavía no apareció. Más bajo = más responsivo, más costoso.")]
    [SerializeField, Min(1)] private int searchEveryNFrames = 30;

    [Header("Fallback (usado solo si no hay config asignada)")]
    [SerializeField, Min(0f)] private float fallbackVisionStart       = 5f;
    [SerializeField, Min(0f)] private float fallbackVisionEndDark     = 6f;
    [SerializeField, Min(0f)] private float fallbackVisionEndLit      = 25f;
    [SerializeField] private Color fallbackFogColor                   = Color.black;
    [SerializeField, Range(0f, 5f)] private float fallbackLightPreservation = 0f;
    [SerializeField, Range(0.1f, 5f)] private float fallbackLerpSpeed       = 2f;

    private Transform _player;
    private float _currentVisionEnd;

    private static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
    private static readonly int VStartId    = Shader.PropertyToID("_VisionStart");
    private static readonly int VEndId      = Shader.PropertyToID("_VisionEnd");
    private static readonly int FogColorId  = Shader.PropertyToID("_FogColor");
    private static readonly int LightPresId = Shader.PropertyToID("_LightPreservation");

    // ── Propiedades efectivas (config si hay, sino fallback) ─────────────────
    private float  VisionStart       => config != null ? config.visionStart       : fallbackVisionStart;
    private float  VisionEndDark     => config != null ? config.visionEndDark     : fallbackVisionEndDark;
    private float  VisionEndLit      => config != null ? config.visionEndLit      : fallbackVisionEndLit;
    private Color  FogColor          => config != null ? config.fogColor          : fallbackFogColor;
    private float  LightPreservation => config != null ? config.lightPreservation : fallbackLightPreservation;
    private float  LerpSpeed         => config != null ? config.lerpSpeed         : fallbackLerpSpeed;

    private void Start()
    {
        _currentVisionEnd = VisionEndLit;
        TryAcquirePlayer();
    }

    private void LateUpdate()
    {
        // Si todavía no hay player, intentar conseguirlo cada N frames (la escena de
        // gameplay puede cargar después que esta).
        if (_player == null)
        {
            if (Time.frameCount % searchEveryNFrames == 0)
                TryAcquirePlayer();

            // Sin player: setear _VisionEnd = 0 para que el shader haga early-out.
            // Así el Main Menu / LevelUI sin gameplay no se ven negros.
            Shader.SetGlobalFloat(VEndId, 0f);
            return;
        }

        float lightLevel = SampleLightLevel();
        float targetVisionEnd = Mathf.Lerp(VisionEndDark, VisionEndLit, lightLevel);
        _currentVisionEnd = Mathf.Lerp(_currentVisionEnd, targetVisionEnd, Time.deltaTime * LerpSpeed);

        Shader.SetGlobalVector(PlayerPosId, _player.position);
        Shader.SetGlobalFloat(VStartId, VisionStart);
        Shader.SetGlobalFloat(VEndId, _currentVisionEnd);
        Shader.SetGlobalColor(FogColorId, FogColor);
        Shader.SetGlobalFloat(LightPresId, LightPreservation);
    }

    private void TryAcquirePlayer()
    {
        // Prioridad: override manual si está asignado.
        if (playerOverride != null)
        {
            _player = playerOverride;
            return;
        }

        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) _player = go.transform;
    }

    /// <summary>
    /// Implementación default: usa el ambient color global de la escena.
    /// Para más fidelidad local, reemplazar por LightProbes.GetInterpolatedProbe(...)
    /// o un sistema de trigger zones con LightZone.cs.
    /// </summary>
    private float SampleLightLevel()
    {
        Color ambient = RenderSettings.ambientLight;
        // Luminancia perceptual estándar (Rec. 601).
        float luminance = 0.299f * ambient.r + 0.587f * ambient.g + 0.114f * ambient.b;
        return Mathf.Clamp01(luminance);
    }
}
