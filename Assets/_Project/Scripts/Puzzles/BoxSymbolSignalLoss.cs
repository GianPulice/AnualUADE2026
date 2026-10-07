using UnityEngine;

/// <summary>
/// Hides a push box's basket symbol behind a plain black disc unless the player looks down on it
/// from above or the box stands in the light of a reveal lamp.
///
/// The symbol (the "Plane" under the box's "Model") tells the player which basket the box belongs
/// to. It lies flat on the box's top face, and the puzzle wants it readable only from a higher
/// vantage point — a second floor, a ledge — or under the UV lamp, never from the same floor
/// standing in front of the box. So while the player's feet are at or below the Plane and no lamp
/// lights it, the Plane shows a solid black disc the size of the symbol's circular badge.
///
/// The disc is a solid mask and not the symbol's own alpha: the symbol has small cut-outs (the
/// skull's eyes, the badge's ragged rim) that would let the box's top show through and give the
/// figure away. Its material is a runtime copy of the Plane's own, with only the texture and the
/// colour changed, so it keeps the same lighting and fog as the symbol and the asset on disk is
/// never touched.
///
/// THE UV LAMP. While the Plane is inside a lit <see cref="BoxSymbolRevealLight"/> the symbol fades
/// in from the black over <see cref="revealSeconds"/> (a linear lerp, 0 → 1), and fades back out to
/// black over the same time once the box leaves the light or the lamp goes off. It also glows in its
/// own colours while it is lit (<see cref="revealGlow"/>), like fluorescent paint under a blacklight:
/// the lamp can be any colour, and under a saturated violet the red and the yellow badges would
/// otherwise go nearly black. The glow fades in and out with the symbol.
///
/// Looking from above is a separate way in and stays immediate: the plain symbol, exactly as it
/// always looked, the moment the player's feet clear the Plane. Each rule keeps its own hysteresis.
///
/// THE MATERIAL SLOTS. While the symbol is hidden or fading the Plane carries several materials (an
/// extra material draws the same mesh again, on top): a decoy in slot 0, the black disc in slot 1
/// and, under the lamp, the symbol over them in slot 2. The disc stays under the symbol so the fade
/// never shows the cut-outs.
///
/// The decoy is there because <see cref="ItemProximityHighlight"/> owns slot 0 of every renderer
/// under the box: from Awake on it pins that slot's _EmissionColor with a MaterialPropertyBlock, and
/// raises it to a light grey whenever the box is targeted or being pushed. Anything visible in slot 0
/// would glow with it: the black disc turned white in the player's hands, and a symbol's own glow
/// was overwritten. So slot 0 holds a copy of the symbol's material that is fully transparent (its
/// alpha clip discards every pixel), taking the highlight's block where it shows nothing; the disc
/// and the symbol live in slots the highlight never touches. The plain symbol read from above stays
/// in slot 0, as it always was, and lights up with the box. The Plane is left with ONE material on
/// Awake, so the highlight never collects a second slot.
///
/// While the game is paused the fade stops where it is, because it runs on scaled time.
/// Unlike the UI static this ignores the VHS Glitch setting: it hides puzzle information, it is not
/// a cosmetic effect.
/// </summary>
public class BoxSymbolSignalLoss : MonoBehaviour
{
    private static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int PropEmissionMap = Shader.PropertyToID("_EmissionMap");
    private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");

    // Pixels across the disc mask. The material's alpha clip turns the bilinear ramp at the rim
    // into a clean circle, so this only has to be fine enough not to look polygonal.
    private const int DiscSize = 128;

    [Tooltip("Renderer showing the basket symbol. Left empty, 'Model/Plane' under this box is used.")]
    [SerializeField] private Renderer symbolRenderer;

    [Tooltip("Radius of the black disc, as a fraction of the Plane's width (0.5 = touches the " +
             "edges). The symbols' circular badge is about 0.44.")]
    [Range(0.05f, 0.5f)] [SerializeField] private float circleRadius = 0.44f;

    [Tooltip("Seconds the symbol takes to fade in from the black while a reveal lamp lights it, " +
             "and to fade back out to black once it leaves the light. Does not apply to looking " +
             "from above, which is immediate.")]
    [Min(0.01f)] [SerializeField] private float revealSeconds = 1.5f;

    [Tooltip("Metres the feet must clear the Plane by before the symbol shows, and drop back by " +
             "before it is hidden again, so standing right at the edge height does not flicker.")]
    [Min(0f)] [SerializeField] private float heightHysteresis = 0.05f;

    [Tooltip("How much the symbol glows in its own colours while a reveal lamp lights it, like " +
             "fluorescent paint under a blacklight. 1 = as bright as under plain white light. " +
             "0 = no glow: only the lamp's own colour lights it, and under a violet lamp the red " +
             "and yellow badges go nearly black. Does not change the look from above.")]
    [Range(0f, 3f)] [SerializeField] private float revealGlow = 1f;

    private Material symbolMaterial;
    private Material decoyMaterial;
    private Material blackMaterial;
    private Material revealMaterial;
    private Texture2D disc;
    private Color symbolBaseColor;

    // The looks of the Plane, as the material lists it is switched between.
    private Material[] plainSet;    // { symbol }                 from above, no lamp
    private Material[] startSet;    // { disc }                   what Awake leaves: one material
    private Material[] blackSet;    // { decoy, disc }            hidden
    private Material[] revealSet;   // { decoy, disc, symbol }    under the lamp: fading in, out or full
    private Material[] shownSet;

    // The two ways the symbol becomes readable, each remembered on its own: the hysteresis of one
    // must not be steered by the answer of the other.
    private bool playerAbove;
    private bool inRevealLight;

    // How far the lamp has revealed the symbol: 0 = black, 1 = fully read. Moves at 1 / revealSeconds.
    private float lampReveal;

    // What was last written to the reveal material, so it is only touched while it changes.
    private float appliedSeen = -1f;
    private float appliedGlow = -1f;

    private void Awake()
    {
        if (symbolRenderer == null)
        {
            Transform plane = transform.Find("Model/Plane");
            if (plane != null) symbolRenderer = plane.GetComponent<Renderer>();
        }

        if (symbolRenderer == null || symbolRenderer.sharedMaterial == null)
        {
            Debug.LogWarning($"[{nameof(BoxSymbolSignalLoss)}] '{name}' has no symbol renderer " +
                             "(Model/Plane). The symbol will not be hidden.", this);
            enabled = false;
            return;
        }

        symbolMaterial = symbolRenderer.sharedMaterial;
        symbolBaseColor = symbolMaterial.GetColor(PropBaseColor);
        disc = CreateDisc();
        decoyMaterial = CreateDecoyMaterial(symbolMaterial);
        blackMaterial = CreateBlackMaterial(symbolMaterial);
        revealMaterial = CreateRevealMaterial(symbolMaterial);

        plainSet = new[] { symbolMaterial };
        startSet = new[] { blackMaterial };
        blackSet = new[] { decoyMaterial, blackMaterial };
        revealSet = new[] { decoyMaterial, blackMaterial, revealMaterial };

        // Decide before the first frame renders, so a box on the player's floor never flashes its
        // symbol on load. A box already under a lit lamp starts revealed: nothing fades on load.
        playerAbove = IsPlayerAbove(false);
        inRevealLight = BoxSymbolRevealLight.IsLit(symbolRenderer.transform.position, false);
        lampReveal = inRevealLight ? 1f : 0f;

        // One material only, whatever the state: see the class note on the slots. The first Update
        // puts the decoy and the symbol around it, before the first frame is drawn.
        Show(playerAbove ? plainSet : startSet);
    }

    private void OnDestroy()
    {
        if (symbolRenderer != null && plainSet != null) symbolRenderer.sharedMaterials = plainSet;
        if (decoyMaterial != null) Destroy(decoyMaterial);
        if (blackMaterial != null) Destroy(blackMaterial);
        if (revealMaterial != null) Destroy(revealMaterial);
        if (disc != null) Destroy(disc);
    }

    private void Update()
    {
        // Both are asked every time, so each one's remembered answer stays current for its own
        // hysteresis.
        playerAbove = IsPlayerAbove(playerAbove);
        inRevealLight = BoxSymbolRevealLight.IsLit(symbolRenderer.transform.position, inRevealLight);

        // Scaled time on purpose: the pause (timeScale 0) holds the fade where it is.
        lampReveal = Mathf.MoveTowards(lampReveal, inRevealLight ? 1f : 0f, Time.deltaTime / revealSeconds);

        Apply();
    }

    /// <summary>
    /// Puts the Plane in the look the current state asks for. <c>seen</c> is how much of the symbol
    /// shows (from above it is all of it at once, under the lamp it follows the fade); <c>glow</c>
    /// is how much of the blacklight glow is on, which only the lamp gives.
    /// </summary>
    private void Apply()
    {
        float seen = playerAbove ? 1f : lampReveal;
        float glow = lampReveal;

        if (seen == appliedSeen && glow == appliedGlow) return;
        appliedSeen = seen;
        appliedGlow = glow;

        // Out of the lamp's reach the symbol is either fully there or not at all.
        if (glow <= 0f)
        {
            Show(seen >= 1f ? plainSet : blackSet);
            return;
        }

        Color baseColor = symbolBaseColor * seen;
        baseColor.a = symbolBaseColor.a;
        revealMaterial.SetColor(PropBaseColor, baseColor);
        revealMaterial.SetColor(PropEmissionColor, GlowColor() * glow);
        Show(revealSet);
    }

    // Swaps the Plane's materials only when the look actually changes, not every frame.
    private void Show(Material[] set)
    {
        if (ReferenceEquals(shownSet, set)) return;

        shownSet = set;
        symbolRenderer.sharedMaterials = set;
    }

    /// <summary>
    /// True if the player's feet are above the Plane. <paramref name="currentlyAbove"/> is the
    /// current answer, used to apply the hysteresis in the direction that keeps it.
    /// No player resolved counts as "not above", so the symbol stays hidden by default.
    /// </summary>
    private bool IsPlayerAbove(bool currentlyAbove)
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return false;

        // The player's pivot is at its feet.
        float feetY = player.position.y;
        float planeY = symbolRenderer.transform.position.y;
        float margin = currentlyAbove ? -heightHysteresis : heightHysteresis;
        return feetY > planeY + margin;
    }

    /// <summary>
    /// Slot 0 while the symbol is hidden or fading: a runtime copy of the symbol's material with its
    /// alpha at 0, so the alpha clip discards every pixel and it draws nothing. It exists only to
    /// take <see cref="ItemProximityHighlight"/>'s emission block, see the class note.
    /// </summary>
    private Material CreateDecoyMaterial(Material source)
    {
        Material material = new Material(source) { name = source.name + " (Highlight Decoy)" };
        Color clear = symbolBaseColor;
        clear.a = 0f;
        material.SetColor(PropBaseColor, clear);
        material.renderQueue = Mathf.Max(0, source.renderQueue - 2);
        return material;
    }

    /// <summary>
    /// The solid black disc that hides the symbol: a runtime copy of the symbol's material with the
    /// disc mask as its texture and black as its colour. Everything else (shader, alpha clip, queue,
    /// specular) is the symbol's own, so a black disc and a black figure light up identically and
    /// the disc gives nothing away. It draws just before the symbol, so the symbol lands on top.
    /// </summary>
    private Material CreateBlackMaterial(Material source)
    {
        Material material = new Material(source) { name = source.name + " (Signal Black)" };
        material.SetTexture(PropBaseMap, disc);
        material.SetTextureScale(PropBaseMap, Vector2.one);
        material.SetTextureOffset(PropBaseMap, Vector2.zero);
        material.SetColor(PropBaseColor, Color.black);
        material.SetColor(PropEmissionColor, Color.black);
        material.renderQueue = Mathf.Max(0, source.renderQueue - 1);
        return material;
    }

    /// <summary>
    /// A runtime copy of the symbol's material that also emits the symbol's own picture, so its
    /// colours show whatever colour the lamp is. The figure stays black: black emits nothing. The
    /// lamp still lights it on top of the glow. Its colour and its glow are driven by
    /// <see cref="Apply"/>; the asset on disk is never touched.
    /// </summary>
    private Material CreateRevealMaterial(Material source)
    {
        Material material = new Material(source) { name = source.name + " (Reveal)" };
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        // No scale or offset of its own: the shader samples the emission map with the base map's UV.
        material.SetTexture(PropEmissionMap, source.GetTexture(PropBaseMap));
        material.SetColor(PropEmissionColor, Color.black);
        return material;
    }

    // The glow at full strength; Apply scales it by how far the lamp has revealed the symbol.
    private Color GlowColor() => symbolBaseColor * revealGlow;

#if UNITY_EDITOR
    // The materials take their colours when the look changes. This re-applies them when a slider
    // moves in Play mode, so the glow can be tuned while looking at it. Outside Play there are no
    // runtime materials.
    private void OnValidate() => appliedSeen = -1f;
#endif

    /// <summary>
    /// Builds the disc mask: white, with alpha 255 inside the disc and 0 outside, so the source
    /// material's alpha clip cuts the black to the circle.
    /// </summary>
    private Texture2D CreateDisc()
    {
        var texture = new Texture2D(DiscSize, DiscSize, TextureFormat.RGBA32, false)
        {
            name = "Box Symbol Disc",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[DiscSize * DiscSize];
        float radiusSqr = circleRadius * circleRadius;
        for (int y = 0; y < DiscSize; y++)
        {
            for (int x = 0; x < DiscSize; x++)
            {
                float u = (x + 0.5f) / DiscSize - 0.5f;
                float v = (y + 0.5f) / DiscSize - 0.5f;
                byte alpha = u * u + v * v <= radiusSqr ? (byte)255 : (byte)0;
                pixels[y * DiscSize + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
        return texture;
    }
}
