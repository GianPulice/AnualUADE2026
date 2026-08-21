using UnityEngine;

/// <summary>
/// Vision fog configuration preset. Assignable to the <see cref="VisionRangeController"/>
/// as the default, or used by a <c>LightZone</c> trigger to define the "feeling" of a
/// specific area of the level.
///
/// The designer does not think in terms of physical light amount — they think about what
/// atmosphere this area should have. Typical examples:
///   - Default_Dark        — dead-end corridors, fog closes in early
///   - SafeRoom            — safe room, wide vision even with little light
///   - PuzzleRoom_Lit      — room with monitors, medium-wide vision
///   - BossArena           — tense atmosphere, short vision even with torches
///   - SilentHill_Foggy    — dense Silent Hill style fog, mid grey
///
/// To create: Project window → right click → Create → Rendering → Vision Fog Config.
/// </summary>
[CreateAssetMenu(fileName = "SO_VisionFog_", menuName = "Rendering/Vision Fog Config")]
public class SO_VisionFogConfig : ScriptableObject
{
    [Header("Vision range (metres)")]
    [Tooltip("Distance up to which there is no fog.")]
    [Min(0f)] public float visionStart = 5f;

    [Tooltip("Distance where the fog covers 100%. Small values = more oppressive, " +
             "high values = wide vision.")]
    [Min(0f)] public float visionEnd = 20f;

    [Header("Look")]
    [Tooltip("Base fog color. Black = pure darkness. Mid grey = dense Silent Hill style fog.")]
    public Color fogColor = Color.black;

    [Tooltip("Preservation of bright areas. 0 = the fog covers everything. >1 = lights 'punch through' the fog.")]
    [Range(0f, 5f)] public float lightPreservation = 0f;

    [Tooltip("Density curve. 1 = linear (base behaviour). >1 accelerates the closing — reinforces " +
             "the sense of poor visibility without changing the range. <1 softens it.")]
    [Range(0.01f, 1f)] public float densityPower = 1f;

    [Tooltip("Optical blur (not just darkening) towards visionEnd. 0 = no blur, " +
             "only the usual fade to fogColor. Typical values: 0.005–0.02.")]
    [Range(0f, 0.05f)] public float blurStrength = 0f;

    // Not a flashlight: these are the module LEDs stuck to the character's body (head, leg),
    // so the opening is a sphere around the player rather than a cone he aims.
    [Header("Player module lights (punch through the fog)")]
    [Tooltip("Radius (metres) where the player's body lights dissolve the fog. 0 = feature off. " +
             "Optionally driven by a FogLightSource component on the light itself.")]
    [Range(0f, 30f)] public float playerLightRange = 8f;

    [Tooltip("How strong the 'hole' the body lights make in the fog is.")]
    [Range(0f, 3f)] public float playerLightIntensity = 1f;

    [Tooltip("Multiplies the scene color inside the lit radius. This is not just a tint: a " +
             "saturated colour also DARKENS (red = luminance down to ~34%), and that is what " +
             "keeps the environment dark inside the opening. Near-white washes the darkness out.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    public Color playerLightColor = new Color(1f, 0.85f, 0.6f);

    [Header("Transition on activation")]
    [Tooltip("Seconds the fog takes to interpolate from the previous config to this one. " +
             "0 = instant change.")]
    [Min(0f)] public float transitionDuration = 1f;

    // ── GD PENDING §3.7 ────────────────────────────────────────────────────────
    // Which objects show a silhouette through the fog.
    // Default = None until it is agreed with GD when it applies (playtesting).
    [Header("Fog silhouettes — GD pending §3.7")]
    [Tooltip("None until validated with GD. Items = silhouette on pickable items only. " +
             "Puzzles = silhouette on interactables. All = both.")]
    public SilhouetteMode silhouetteMode = SilhouetteMode.None;
}

public enum SilhouetteMode
{
    None,
    Items,
    Puzzles,
    All
}
