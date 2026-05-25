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
/// Setup:
///   1. Pegar este componente a un GameObject en escena persistente (LevelUI).
///   2. Asegurarse de que el player en gameplay tenga el tag <c>"Player"</c>.
///   3. Ajustar rangos en Inspector según el nivel.
/// </summary>
[DefaultExecutionOrder(100)] // después del PlayerController, antes del render
public class VisionRangeController : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Tag del GameObject del player. Se busca en runtime porque vive en otra escena (gameplay aditivo).")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Asignación manual opcional. Si está vacío, se busca por tag.")]
    [SerializeField] private Transform playerOverride;

    [Tooltip("Cada cuántos frames re-buscar al player si todavía no apareció. Más bajo = más responsivo, más costoso.")]
    [SerializeField, Min(1)] private int searchEveryNFrames = 30;

    [Header("Rangos de visión (metros)")]
    [Tooltip("Distancia hasta la cual no hay niebla. Spec default: 5m.")]
    [SerializeField, Min(0f)] private float visionStart   = 5f;

    [Tooltip("Rango máximo en oscuridad total (lightLevel = 0). Cierra el fog cerca.")]
    [SerializeField, Min(0f)] private float visionEndDark = 6f;

    [Tooltip("Rango máximo en zona iluminada (lightLevel = 1). Fog casi imperceptible.")]
    [SerializeField, Min(0f)] private float visionEndLit  = 25f;

    [Header("Look del fog")]
    [SerializeField] private Color fogColor = Color.black;

    [Tooltip("Preservación de zonas brillantes. 0 = la niebla cubre todo. >1 = las luces 'perforan' la niebla. Spec recomienda 2.")]
    [SerializeField, Range(0f, 5f)] private float lightPreservation = 2f;

    [Header("Suavizado entre zonas")]
    [Tooltip("Velocidad de transición del rango al cambiar de zona oscura a iluminada (o viceversa).")]
    [SerializeField, Range(0.1f, 5f)] private float lerpSpeed = 2f;

    private Transform _player;
    private float _currentVisionEnd;

    private static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
    private static readonly int VStartId    = Shader.PropertyToID("_VisionStart");
    private static readonly int VEndId      = Shader.PropertyToID("_VisionEnd");
    private static readonly int FogColorId  = Shader.PropertyToID("_FogColor");
    private static readonly int LightPresId = Shader.PropertyToID("_LightPreservation");

    private void Start()
    {
        _currentVisionEnd = visionEndLit;
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
        float targetVisionEnd = Mathf.Lerp(visionEndDark, visionEndLit, lightLevel);
        _currentVisionEnd = Mathf.Lerp(_currentVisionEnd, targetVisionEnd, Time.deltaTime * lerpSpeed);

        Shader.SetGlobalVector(PlayerPosId, _player.position);
        Shader.SetGlobalFloat(VStartId, visionStart);
        Shader.SetGlobalFloat(VEndId, _currentVisionEnd);
        Shader.SetGlobalColor(FogColorId, fogColor);
        Shader.SetGlobalFloat(LightPresId, lightPreservation);
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
