using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the player sees of the hiding spot from inside it: the slats of a locker, the underside and
/// legs of a table, the seam of a sealed container. One look per <see cref="EHidingSpotType"/>,
/// faded in on <see cref="HidingEvents.OnEntered"/> and out on <see cref="HidingEvents.OnExited"/>.
///
/// Why procedural and not three sprites: the looks are soft-edged shapes over black, and building
/// them here keeps their proportions tunable from the inspector (slat count, gap, table depth…)
/// without a round trip through an image editor. Each look is one full-screen RawImage with a small
/// alpha texture made in Awake; the colour comes from <see cref="shade"/>.
///
/// Sits under the rest of the HUD (the vignettes, the timer, the breath meter draw over it), and
/// never takes a click. The HUD's builder places it; see HidingHUDBuilder.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HidingOverlayView : MonoBehaviour
{
    [Tooltip("Colour of everything the spot blocks. Near black, faintly cold, like the rest of the " +
             "game's shadows. Never red: red is danger only.")]
    [SerializeField] private Color shade = new Color(0.015f, 0.015f, 0.02f, 1f);

    [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.35f;
    [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.25f;

    [Header("Locker")]
    [Tooltip("Horizontal slats across the view.")]
    [SerializeField, Range(3, 24)] private int lockerSlats = 9;

    [Tooltip("Fraction of each slat's height that is open, i.e. what the player sees through.")]
    [SerializeField, Range(0.05f, 0.9f)] private float lockerGap = 0.3f;

    [Tooltip("How soft the edges of each gap are, as a fraction of the gap.")]
    [SerializeField, Range(0f, 1f)] private float lockerSoftness = 0.45f;

    [Tooltip("Opacity of the metal between the slits. Just under 1 so the slats read as a door and " +
             "not as the screen switching off.")]
    [SerializeField, Range(0f, 1f)] private float lockerSolid = 0.96f;

    [Header("Under table")]
    [Tooltip("How far down the screen the underside of the table reaches.")]
    [SerializeField, Range(0f, 0.6f)] private float tableTop = 0.3f;

    [SerializeField, Range(0.01f, 0.3f)] private float tableEdgeSoftness = 0.06f;

    [Tooltip("Width of each table leg, as a fraction of the screen width. 0 = no legs.")]
    [SerializeField, Range(0f, 0.2f)] private float tableLegWidth = 0.05f;

    [Tooltip("How far in from each side the legs stand.")]
    [SerializeField, Range(0f, 0.4f)] private float tableLegInset = 0.07f;

    [Header("Container")]
    [Tooltip("Darkness everywhere but the door seam. Sealed: the player sees next to nothing.")]
    [SerializeField, Range(0f, 1f)] private float containerDarkness = 0.93f;

    [Tooltip("Width of the vertical seam of light between the doors, as a fraction of the screen.")]
    [SerializeField, Range(0.005f, 0.2f)] private float containerSeamWidth = 0.035f;

    [Header("All")]
    [Tooltip("Darkening towards the screen edges, added to every look.")]
    [SerializeField, Range(0f, 1f)] private float vignette = 0.85f;

    private const int TexWidth = 320;
    private const int TexHeight = 180;
    private const float Aspect = TexWidth / (float)TexHeight;

    private readonly RawImage[] layers = new RawImage[3];
    private readonly Texture2D[] textures = new Texture2D[3];
    private readonly float[] alphas = new float[3];

    private int activeType = -1;

    private void Awake()
    {
        for (int i = 0; i < layers.Length; i++)
        {
            textures[i] = BuildTexture((EHidingSpotType)i);
            layers[i] = CreateLayer(((EHidingSpotType)i).ToString(), textures[i]);
            SetAlpha(i, 0f);
        }
    }

    private void OnEnable()
    {
        HidingEvents.OnEntered += HandleEntered;
        HidingEvents.OnExited += HandleExited;
    }

    private void OnDisable()
    {
        HidingEvents.OnEntered -= HandleEntered;
        HidingEvents.OnExited -= HandleExited;

        activeType = -1;
        for (int i = 0; i < layers.Length; i++) SetAlpha(i, 0f);
    }

    private void OnDestroy()
    {
        foreach (Texture2D t in textures)
            if (t != null) Destroy(t);
    }

    private void HandleEntered(HidingSpot spot) => activeType = spot != null ? (int)spot.Type : -1;

    private void HandleExited(HidingSpot spot) => activeType = -1;

    private void Update()
    {
        // Unscaled: the fade belongs with the camera blend, which a pause frozen half way through
        // would leave as a half-dark screen under the menu.
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < layers.Length; i++)
        {
            float target = i == activeType ? 1f : 0f;
            if (Mathf.Approximately(alphas[i], target)) continue;

            float seconds = target > alphas[i] ? fadeInSeconds : fadeOutSeconds;
            SetAlpha(i, Mathf.MoveTowards(alphas[i], target, dt / seconds));
        }
    }

    private void SetAlpha(int index, float alpha)
    {
        alphas[index] = alpha;
        RawImage layer = layers[index];
        if (layer == null) return;

        layer.color = new Color(shade.r, shade.g, shade.b, shade.a * alpha);
        layer.enabled = alpha > 0f;
    }

    private RawImage CreateLayer(string layerName, Texture2D texture)
    {
        var go = new GameObject(layerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    // ── Looks ───────────────────────────────────────────────────────────────

    private Texture2D BuildTexture(EHidingSpotType type)
    {
        var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
        {
            name = "HidingOverlay_" + type,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[TexWidth * TexHeight];
        for (int y = 0; y < TexHeight; y++)
        {
            float v = (y + 0.5f) / TexHeight;
            for (int x = 0; x < TexWidth; x++)
            {
                float u = (x + 0.5f) / TexWidth;
                float a = Mathf.Max(ShapeAlpha(type, u, v), Vignette(u, v));
                pixels[y * TexWidth + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private float ShapeAlpha(EHidingSpotType type, float u, float v)
    {
        switch (type)
        {
            case EHidingSpotType.Locker:     return LockerAlpha(v);
            case EHidingSpotType.UnderTable: return TableAlpha(u, v);
            case EHidingSpotType.Container:  return ContainerAlpha(u, v);
            default:                         return 0f;
        }
    }

    /// <summary>Solid metal with a soft-edged slit in every slat.</summary>
    private float LockerAlpha(float v)
    {
        float phase = Mathf.Repeat(v * lockerSlats, 1f);
        float half = lockerGap * 0.5f;
        float fromCentre = Mathf.Abs(phase - half) / Mathf.Max(half, 1e-4f); // 0 centre of the slit, 1 its edge
        if (fromCentre >= 1f) return lockerSolid;

        return lockerSolid * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - lockerSoftness, 1f, fromCentre));
    }

    /// <summary>The underside across the top, a leg at each side.</summary>
    private float TableAlpha(float u, float v)
    {
        float fromTop = 1f - v;
        float top = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tableTop - tableEdgeSoftness, tableTop + tableEdgeSoftness, fromTop));

        float legs = 0f;
        if (tableLegWidth > 0f)
        {
            float halfLeg = tableLegWidth * 0.5f;
            float soft = halfLeg * 0.4f;
            float d = Mathf.Min(Mathf.Abs(u - tableLegInset), Mathf.Abs(u - (1f - tableLegInset)));
            legs = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfLeg - soft, halfLeg + soft, d));
        }

        return Mathf.Max(top, legs);
    }

    /// <summary>Near black, with the seam between the doors letting a thin line of the world in.</summary>
    private float ContainerAlpha(float u, float v)
    {
        float halfSeam = containerSeamWidth * 0.5f;
        float across = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfSeam * 0.3f, halfSeam, Mathf.Abs(u - 0.5f)));
        float along = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.18f, v))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.98f, 0.82f, v));

        return containerDarkness * (1f - across * along * 0.9f);
    }

    private float Vignette(float u, float v)
    {
        float dx = (u - 0.5f) * Aspect;
        float dy = v - 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy) / (0.5f * Mathf.Sqrt(Aspect * Aspect + 1f)); // 0 centre, 1 corner
        return vignette * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r));
    }
}
