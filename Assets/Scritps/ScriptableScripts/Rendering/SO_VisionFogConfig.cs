using UnityEngine;

/// <summary>
/// Preset de configuración del vision fog. Asignable al <see cref="VisionRangeController"/>
/// para tener distintas atmósferas por nivel/zona sin tocar código.
///
/// Para crear un preset nuevo:
///   Project window → click derecho → Create → Rendering → Vision Fog Config.
///
/// Ejemplos típicos de presets:
///   - Interior_Dark        — pasillos sin luz, fog cierra cerca (visionEndDark 4, color negro)
///   - Interior_Lit         — sala con luces, fog se aleja (visionEndDark 8, lightPreservation 0.5)
///   - Exterior_Foggy       — Silent Hill style (gris medio, rangos chicos, lightPreservation 0)
///   - Boss_Arena           — atmósfera tensa (color rojizo sutil, rangos custom)
/// </summary>
[CreateAssetMenu(fileName = "SO_VisionFog_", menuName = "Rendering/Vision Fog Config")]
public class SO_VisionFogConfig : ScriptableObject
{
    [Header("Rangos de visión (metros)")]
    [Tooltip("Distancia hasta la cual no hay niebla.")]
    [Min(0f)] public float visionStart = 5f;

    [Tooltip("Rango máximo en oscuridad total (ambient light = 0). Cierra el fog cerca.")]
    [Min(0f)] public float visionEndDark = 6f;

    [Tooltip("Rango máximo en zona iluminada (ambient light = 1). Fog casi imperceptible.")]
    [Min(0f)] public float visionEndLit = 25f;

    [Header("Look")]
    [Tooltip("Color base de la niebla. Negro = oscuridad pura. Gris medio = niebla densa.")]
    public Color fogColor = Color.black;

    [Tooltip("Preservación de zonas brillantes. 0 = la niebla cubre todo. >1 = las luces 'perforan' la niebla.")]
    [Range(0f, 5f)] public float lightPreservation = 0f;

    [Header("Transición")]
    [Tooltip("Velocidad de transición al cambiar de zona oscura a iluminada (o viceversa).")]
    [Range(0.1f, 5f)] public float lerpSpeed = 2f;
}
