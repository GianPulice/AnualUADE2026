using UnityEngine;

/// <summary>
/// How an interactable answers the crosshair: the far and near values of the highlight, how fast it
/// moves between them, and in which colour. Read by <see cref="ItemProximityHighlight"/>.
///
/// Shared on purpose. Every Father prefab points its highlight at one of these, so a whole family —
/// and every variant of it — lights up the same way, and changing the colour of "things you can
/// use" is one edit here instead of one per prefab. The values used to live on the component
/// itself, which is how the new fuse ended up with scene overrides of its own that made its near
/// state almost identical to its far one.
///
/// Two assets are expected (Tools ▸ Interactables ▸ Set Up Highlights creates both):
///   • SO_Highlight_Items — inventory pickups. Category Config set, so each pickup takes its
///     colours from its item's category.
///   • SO_Highlight_Interactables — puzzle props and devices. Category Config empty, so they all
///     share the single colour below.
/// </summary>
[CreateAssetMenu(fileName = "SO_HighlightProfile", menuName = "Scriptable Objects/SO_HighlightProfile")]
public class SO_HighlightProfile : ScriptableObject
{
    [Header("Far state (not under the crosshair)")]
    [Tooltip("Tint at rest. Only shaders that declare _TintIntensity use it (ItemPSX_Outline, " +
             "PSXIndustrial); plain URP/Lit materials get emission only.")]
    [SerializeField, Range(0f, 1f)] private float farTint;

    [Tooltip("Emission at rest. Leave at 0 unless the object should glow even when ignored.")]
    [SerializeField, Range(0f, 2f)] private float farEmission;

    [Header("Near state (under the crosshair)")]
    [SerializeField, Range(0f, 1f)] private float nearTint;

    [SerializeField, Range(0f, 2f)] private float nearEmission = 0.15f;

    [Tooltip("Multiplies the emission on plain URP/Lit materials only (the highlight shaders are " +
             "left alone). The same number reads very differently on the two: measured on screen, " +
             "0.15 lifts a PSXIndustrial valve by well over 100% but a URP/Lit socket by a few " +
             "percent, which is why the core and regulator sockets and the electric panel looked " +
             "like they had no highlight at all. Raise it until a URP/Lit prop answers the " +
             "crosshair about as clearly as the valves do.")]
    [SerializeField, Min(0f)] private float litEmissionScale = 1f;

    [Header("Transition")]
    [Tooltip("Seconds between far and near, SmoothStep-eased.")]
    [SerializeField, Min(0.01f)] private float lerpDuration = 0.3f;

    [Header("Colour")]
    [Tooltip("Written to _TintColor. Ignored when Category Config is set and the owner is a pickup.")]
    [SerializeField] private Color tintColor = Color.white;

    [Tooltip("Written to _EmissionColor (or added to it, on URP/Lit). Ignored when Category Config " +
             "is set and the owner is a pickup.\n\nDefault #E0E0E0, the UI's 'selected' white: the " +
             "colour spec reserves red for danger, amber #FFC850 for the player's device modules " +
             "and cold blue #8AB4D4 for monitors.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color emissionColor = new Color(0.878f, 0.878f, 0.878f);

    [Tooltip("Set it on the items profile only: a pickup then takes its tint and emission colour " +
             "from its item's category instead of the two colours above.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    public float FarTint      => farTint;
    public float FarEmission  => farEmission;
    public float NearTint     => nearTint;
    public float NearEmission => nearEmission;
    public float LerpDuration => lerpDuration;
    public float LitEmissionScale => litEmissionScale;

    /// <summary>
    /// The colours for <paramref name="owner"/>: its item category's when this profile follows
    /// categories and the owner is a pickup with an item, this profile's own otherwise.
    /// </summary>
    public void ResolveColors(IInteractable owner, out Color tint, out Color emission)
    {
        if (categoryConfig != null && owner is PickupInteractable pickup && pickup.Item != null)
        {
            CategoryVisuals visuals = categoryConfig.Get(pickup.Item.Category);
            tint     = visuals.shaderTintColor;
            emission = visuals.shaderEmissionColor;
            return;
        }

        tint     = tintColor;
        emission = emissionColor;
    }
}
