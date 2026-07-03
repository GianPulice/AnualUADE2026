using UnityEngine;

/// <summary>
/// Preset de configuración del vision fog. Asignable al <see cref="VisionRangeController"/>
/// como default o usado por un <c>LightZone</c> trigger para definir el "feeling" de
/// una zona puntual del nivel.
///
/// El diseñador no piensa en cantidad de luz física — piensa en qué atmósfera
/// debe tener esta zona. Ejemplos típicos:
///   - Default_Dark        — pasillos sin destino, fog cierra cerca
///   - SafeRoom            — sala segura, visión amplia aunque haya poca luz
///   - PuzzleRoom_Lit      — sala con monitores, visión media-amplia
///   - BossArena           — atmósfera tensa, visión corta aunque haya antorchas
///   - SilentHill_Foggy    — niebla densa estilo Silent Hill, gris medio
///
/// Para crear: Project window → click derecho → Create → Rendering → Vision Fog Config.
/// </summary>
[CreateAssetMenu(fileName = "SO_VisionFog_", menuName = "Rendering/Vision Fog Config")]
public class SO_VisionFogConfig : ScriptableObject
{
    [Header("Rango de visión (metros)")]
    [Tooltip("Distancia hasta la cual no hay niebla.")]
    [Min(0f)] public float visionStart = 5f;

    [Tooltip("Distancia donde la niebla cubre 100%. Valores chicos = más opresivo, " +
             "valores altos = visión amplia.")]
    [Min(0f)] public float visionEnd = 20f;

    [Header("Look")]
    [Tooltip("Color base de la niebla. Negro = oscuridad pura. Gris medio = niebla densa estilo Silent Hill.")]
    public Color fogColor = Color.black;

    [Tooltip("Preservación de zonas brillantes. 0 = la niebla cubre todo. >1 = las luces 'perforan' la niebla.")]
    [Range(0f, 5f)] public float lightPreservation = 0f;

    [Tooltip("Curva de densidad. 1 = lineal (comportamiento base). >1 acelera el cierre — refuerza " +
             "la sensación de falta de visibilidad sin cambiar el rango. <1 la suaviza.")]
    [Range(0.25f, 4f)] public float densityPower = 1f;

    [Header("Linterna del player (perfora la niebla)")]
    [Tooltip("Radio (metros) donde la linterna del player disuelve la niebla. 0 = feature apagado. " +
             "Requiere un componente FogLightSource sobre la linterna del player.")]
    [Range(0f, 30f)] public float playerLightRange = 8f;

    [Tooltip("Cuán fuerte es el 'hueco' que la linterna hace en la niebla.")]
    [Range(0f, 3f)] public float playerLightIntensity = 1f;

    [Tooltip("Tinte que la linterna del player agrega al color de escena en la zona iluminada.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    public Color playerLightColor = new Color(1f, 0.85f, 0.6f);

    [Header("Transición al activarse")]
    [Tooltip("Segundos que tarda el fog en interpolar de la config anterior a esta. " +
             "0 = cambio instantáneo.")]
    [Min(0f)] public float transitionDuration = 1f;

    // ── GD PENDIENTE §3.7 ──────────────────────────────────────────────────────
    // Qué objetos muestran silueta a través de la niebla.
    // Default = None hasta acordar con GD cuándo aplica (playtesting).
    [Header("Siluetas en niebla — GD pendiente §3.7")]
    [Tooltip("None hasta validación con GD. Items = silueta solo en ítems recogibles. " +
             "Puzzles = silueta en interactuables. All = ambos.")]
    public SilhouetteMode silhouetteMode = SilhouetteMode.None;
}

public enum SilhouetteMode
{
    None,
    Items,
    Puzzles,
    All
}
