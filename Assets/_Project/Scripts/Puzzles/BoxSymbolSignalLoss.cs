using UnityEngine;

/// <summary>
/// Hides a push box's basket symbol behind TV static unless the player looks down on it from above.
///
/// The symbol (the "Plane" under the box's "Model") tells the player which basket the box belongs
/// to. It lies flat on the box's top face, and the puzzle wants it readable only from a higher
/// vantage point — a second floor, a ledge — never from the same floor standing in front of the box.
/// So while the player's feet are at or below the Plane, the Plane shows dead-channel static instead.
///
/// The static is the same look the pause menu and the UI transitions use: grey noise in hard,
/// point-filtered cells, with a new grain every frame (static that holds still reads as a texture,
/// not as noise). It fills a solid disc the size of the symbol's circular badge — not the symbol's
/// own alpha, whose cut-outs would give the figure away, and not the whole square Plane either.
/// Because the disc must stay put, the grain is redrawn in place each frame rather than scrolled.
///
/// While the game is paused the grain stops being redrawn, so the static freezes with everything
/// else and picks up again on unpause.
///
/// The static material is a runtime copy of the Plane's own material with only the texture swapped,
/// so it keeps the same lighting and fog as the symbol and the asset on disk is never touched.
/// Unlike the UI static this ignores the VHS Glitch setting: it hides puzzle information, it is not
/// a cosmetic effect.
/// </summary>
public class BoxSymbolSignalLoss : MonoBehaviour
{
    private static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int PropEmissionMap = Shader.PropertyToID("_EmissionMap");
    private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");

    // Texture pixels per grain cell. The cells stay chunky; the extra pixels only smooth the disc's
    // edge so it does not step as coarsely as the grain.
    private const int PixelsPerCell = 8;

    [Tooltip("Renderer showing the basket symbol. Left empty, 'Model/Plane' under this box is used.")]
    [SerializeField] private Renderer symbolRenderer;

    [Tooltip("Grain cells across the Plane. Small on purpose so the cells stay PSX-chunky.")]
    [SerializeField] private int noiseResolution = 24;

    [Tooltip("Radius of the static disc, as a fraction of the Plane's width (0.5 = touches the " +
             "edges). The symbols' circular badge is about 0.44.")]
    [Range(0.05f, 0.5f)] [SerializeField] private float circleRadius = 0.44f;

    [Tooltip("How much the static glows on its own, like a lit screen. 0 = lit by the scene only.")]
    [Range(0f, 2f)] [SerializeField] private float emissionStrength = 0.35f;

    [Tooltip("Metres the feet must clear the Plane by before the symbol shows, and drop back by " +
             "before it is hidden again, so standing right at the edge height does not flicker.")]
    [Min(0f)] [SerializeField] private float heightHysteresis = 0.05f;

    private Material symbolMaterial;
    private Material staticMaterial;
    private Texture2D noise;
    private Color32[] pixels;
    private bool[] inDisc;
    private int cells;
    private int size;
    private bool showingStatic;

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

        CreateNoise();
        symbolMaterial = symbolRenderer.sharedMaterial;
        staticMaterial = CreateStaticMaterial(symbolMaterial);

        // Decide before the first frame renders, so a box on the player's floor never flashes its
        // symbol on load.
        showingStatic = !IsPlayerAbove(false);
        symbolRenderer.sharedMaterial = showingStatic ? staticMaterial : symbolMaterial;
    }

    private void OnDestroy()
    {
        if (symbolRenderer != null && symbolMaterial != null) symbolRenderer.sharedMaterial = symbolMaterial;
        if (staticMaterial != null) Destroy(staticMaterial);
        if (noise != null) Destroy(noise);
    }

    private void Update()
    {
        bool wantStatic = !IsPlayerAbove(!showingStatic);
        if (wantStatic != showingStatic)
        {
            showingStatic = wantStatic;
            symbolRenderer.sharedMaterial = showingStatic ? staticMaterial : symbolMaterial;
        }

        // Paused: keep the last grain on screen — the static freezes with the rest of the game.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // A new grain every frame — see the class note.
        if (showingStatic) RedrawGrain();
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

    private Material CreateStaticMaterial(Material source)
    {
        Material material = new Material(source) { name = source.name + " (Signal Static)" };
        material.SetTexture(PropBaseMap, noise);
        material.SetTextureScale(PropBaseMap, Vector2.one);
        material.SetTextureOffset(PropBaseMap, Vector2.zero);
        material.SetColor(PropBaseColor, Color.white);

        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        material.SetTexture(PropEmissionMap, noise);
        material.SetTextureScale(PropEmissionMap, Vector2.one);
        material.SetTextureOffset(PropEmissionMap, Vector2.zero);
        material.SetColor(PropEmissionColor, Color.white * emissionStrength);
        return material;
    }

    /// <summary>
    /// Builds this box's grain texture and the disc mask. Alpha is 255 inside the disc and 0 outside,
    /// so the source material's alpha clip cuts the static to the circle. Point-filtered so each
    /// cell stays a hard PSX-sized block.
    /// </summary>
    private void CreateNoise()
    {
        cells = Mathf.Max(2, noiseResolution);
        size = cells * PixelsPerCell;

        noise = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Box Symbol Static",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        pixels = new Color32[size * size];
        inDisc = new bool[size * size];
        float radiusSqr = circleRadius * circleRadius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f;
                float v = (y + 0.5f) / size - 0.5f;
                inDisc[y * size + x] = u * u + v * v <= radiusSqr;
            }
        }

        RedrawGrain();
    }

    /// <summary>Rolls a new grey level for every cell and writes it, masked by the disc.</summary>
    private void RedrawGrain()
    {
        for (int cy = 0; cy < cells; cy++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                byte level = (byte)Random.Range(0, 256);
                for (int py = 0; py < PixelsPerCell; py++)
                {
                    int row = (cy * PixelsPerCell + py) * size + cx * PixelsPerCell;
                    for (int px = 0; px < PixelsPerCell; px++)
                    {
                        int i = row + px;
                        pixels[i] = new Color32(level, level, level, inDisc[i] ? (byte)255 : (byte)0);
                    }
                }
            }
        }

        noise.SetPixels32(pixels);
        noise.Apply(false);
    }
}
