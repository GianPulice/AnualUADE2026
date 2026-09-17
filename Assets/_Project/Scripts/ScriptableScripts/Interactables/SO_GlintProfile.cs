using UnityEngine;

/// <summary>
/// How pickups glint from a distance: the look, timing and range of <see cref="ItemGlint"/>.
///
/// Shared like <see cref="SO_HighlightProfile"/>: every pickup points at SO_Glint_Items (Tools ▸
/// Interactables ▸ Set Up Item Glints creates it), so how often and how far items glint is one edit
/// here, not one per prefab.
///
/// The star's SHAPE (ray thickness, core, pixel grid, pull toward the camera) is not here: it lives on
/// the material, where the inspector preview shows it while it is tuned.
/// </summary>
[CreateAssetMenu(fileName = "SO_GlintProfile", menuName = "Scriptable Objects/SO_GlintProfile")]
public class SO_GlintProfile : ScriptableObject
{
    [Header("Look")]
    [Tooltip("mat_item_glint, shader WIRED/Items/Item Glint. The star's shape is tuned on it.")]
    [SerializeField] private Material material;

    [Tooltip("Colour of the star before the category tint below. Default white.\n\nThe colour spec " +
             "reserves amber #FFC850 for the player's device and cold blue #8AB4D4 for monitors — keep " +
             "this base out of both.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color color = Color.white;

    [Tooltip("Where each pickup's category colour comes from: ItemCategory.asset, its 3D Shader " +
             "Emission Color — the same colour the crosshair highlight uses. Leave empty and every " +
             "star is plain Colour.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    [Tooltip("How much of its category's colour a pickup's star takes. 0 = plain Colour, 1 = the " +
             "category's hue at full brightness. Only the hue is taken: a dark category colour (the " +
             "notes' blue) tints the star as much as a bright one. A category with no colour (Other) " +
             "keeps the plain Colour.")]
    [SerializeField, Range(0f, 1f)] private float categoryTint = 0.45f;

    [Tooltip("Diameter of the star at its peak, in world metres.")]
    [SerializeField, Min(0.01f)] private float size = 0.25f;

    [Tooltip("Smallest the star gets on screen, as a fraction of the screen height, however far away " +
             "the item is. The PS1 filter keeps one texel per block (256 rows), so a star only a few " +
             "rows tall shimmers in and out as the camera moves. 0.045 ≈ 11 rows.")]
    [SerializeField, Range(0f, 0.2f)] private float minScreenHeight = 0.045f;

    [Tooltip("Metres the star sits above the centre of the item's renderers.")]
    [SerializeField] private float heightOffset = 0.05f;

    [Header("Timing")]
    [Tooltip("Seconds from the start of one glint to the start of the next.")]
    [SerializeField, Min(0.1f)] private float interval = 2.8f;

    [Tooltip("Random ± seconds added to every interval. The first glint of each item is also spread " +
             "over a whole interval, so items placed side by side never flash in unison.")]
    [SerializeField, Min(0f)] private float intervalJitter = 0.8f;

    [Tooltip("Seconds one glint lasts.")]
    [SerializeField, Min(0.05f)] private float flashDuration = 0.55f;

    [Tooltip("Size of the star over one glint, as a fraction of Size. Time runs 0–1.")]
    [SerializeField] private AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 5f),
        new Keyframe(0.3f, 1f),
        new Keyframe(1f, 0f, -1.6f, 0f));

    [Tooltip("Opacity of the star over one glint. Time runs 0–1.")]
    [SerializeField] private AnimationCurve alphaCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 8f),
        new Keyframe(0.2f, 1f),
        new Keyframe(1f, 0f, -1.4f, 0f));

    [Tooltip("Degrees the star turns during one glint.")]
    [SerializeField] private float spinDegrees = 45f;

    [Header("Distance (from the player)")]
    [Tooltip("Closer than this, the glint is gone and ItemProximityHighlight takes over. Keep it at or " +
             "above SO_InteractionManager's Interaction Distance (2.5), so the star never shows while " +
             "the crosshair can already pick the item up.")]
    [SerializeField, Min(0f)] private float hideWithin = 2.5f;

    [Tooltip("Farther than this, the glint is gone too.\n\nThe vision fog lets bright pixels through " +
             "(Light Preservation), so a glint past the zone's Vision End can still read as a speck in " +
             "the dark. Lower this if that gives items away too early.")]
    [SerializeField, Min(0f)] private float maxDistance = 12f;

    [Tooltip("Metres over which the glint fades in past Hide Within and out before Max Distance, " +
             "instead of popping.")]
    [SerializeField, Min(0.01f)] private float fadeBand = 1f;

    [Tooltip("Seconds the glint takes to fade out when the crosshair lands on the item, and back in " +
             "when it leaves.")]
    [SerializeField, Min(0f)] private float targetFadeTime = 0.12f;

    public Material Material           => material;
    public float Size                  => size;
    public float MinScreenHeight       => minScreenHeight;
    public float HeightOffset          => heightOffset;
    public float Interval              => interval;
    public float FlashDuration         => flashDuration;
    public AnimationCurve ScaleCurve   => scaleCurve;
    public AnimationCurve AlphaCurve   => alphaCurve;
    public float SpinDegrees           => spinDegrees;
    public float HideWithin            => hideWithin;
    public float MaxDistance           => maxDistance;
    public float FadeBand              => fadeBand;
    public float TargetFadeTime        => targetFadeTime;

    /// <summary>
    /// The star's colour for <paramref name="owner"/>: Colour, blended toward its item category's hue
    /// when this profile follows categories and the owner is a pickup with an item.
    /// </summary>
    public Color ResolveColor(IInteractable owner)
    {
        if (categoryConfig == null || categoryTint <= 0f) return color;
        if (!(owner is PickupInteractable pickup) || pickup.Item == null) return color;

        Color category = categoryConfig.Get(pickup.Item.Category).shaderEmissionColor;
        float peak = Mathf.Max(category.r, Mathf.Max(category.g, category.b));
        if (peak <= 0.0001f) return color;

        // Brightest component to 1: keeps the hue, drops how dark the category colour is authored.
        var hue = new Color(category.r / peak, category.g / peak, category.b / peak);
        return Color.Lerp(color, hue, categoryTint);
    }

    /// <summary>Seconds until the next glint, jitter included. Never shorter than a glint.</summary>
    public float RollInterval() =>
        Mathf.Max(flashDuration, interval + Random.Range(-intervalJitter, intervalJitter));
}
