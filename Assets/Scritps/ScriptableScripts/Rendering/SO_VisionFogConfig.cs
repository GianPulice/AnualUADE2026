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
