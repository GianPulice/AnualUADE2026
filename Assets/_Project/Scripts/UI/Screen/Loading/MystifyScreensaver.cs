using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The loading screen's background: the Windows 3.1 / 95 "Mystify" screensaver, drawn by
/// MystifyScreensaver.shader and shown on this RawImage.
///
/// Every time it is enabled — every time the loading screen comes up — it rolls a new seed: new
/// starting corners, directions, speeds and colours — these last always inside the material's hue
/// band, the reds — so like the real screensaver it never plays the same pattern twice. Everything
/// else (polygons, echoes, speeds, colour rate, the band itself) is tuned on the material asset,
/// which is re-read on every show: tweak it in Play mode and the next load picks it up.
///
/// The shader draws into a small render texture rather than straight onto the canvas: one-texel lines
/// at <see cref="rows"/> rows, point-filtered up to the screen, are the aliased staircase of a VGA
/// screensaver, and the canvas's CRT tube then curves and scans them like the rest of the UI. It also
/// keeps the cost flat at any resolution — the shader tests every edge of every echo on every texel.
///
/// Unscaled clock: a scene change can start from the pause or result screen while Time.timeScale is
/// still 0, and the screensaver must move from its first frame.
/// </summary>
[RequireComponent(typeof(RawImage))]
[AddComponentMenu("WIRED/UI/Mystify Screensaver")]
public class MystifyScreensaver : MonoBehaviour
{
    private static readonly int PropSeed       = Shader.PropertyToID("_Seed");
    private static readonly int PropClock      = Shader.PropertyToID("_Clock");
    private static readonly int PropTargetSize = Shader.PropertyToID("_TargetSize");

    // Its own generator, not UnityEngine.Random: anything in the game may re-seed that one, and a
    // fixed seed there would give every loading screen the same pattern.
    private static readonly System.Random Rng = new System.Random();

    [Tooltip("Material with the WIRED/UI/Mystify Screensaver shader. Never written to: the screensaver " +
             "draws with a runtime copy.")]
    [SerializeField] private Material material;

    [Tooltip("Height of the render texture, in texels. The lines are one texel wide, so this sets how " +
             "thick they look: 360 is 3 px at 1080p. The width follows the RawImage's aspect.")]
    [SerializeField, Range(120, 1080)] private int rows = 360;

    [Header("Random look per show")]
    [Tooltip("On every show, the shape / trail / motion / line / colour-rate values are rolled inside " +
             "the ranges below on the runtime copy. The hue band (Hue Centre / Hue Spread), Brightness, " +
             "Saturation, Background, the render queue and the GI flags are never touched: they stay as " +
             "authored on the material, so the screensaver keeps its colours inside the band whatever " +
             "is rolled. Off = the material's values are used as they are, only the seed changes.")]
    [SerializeField] private bool randomizeLook = true;

    [SerializeField] private Vector2Int polygonsRange = new Vector2Int(1, 4);
    [SerializeField] private Vector2Int cornersRange = new Vector2Int(3, 6);
    [SerializeField] private Vector2Int echoesRange = new Vector2Int(3, 12);
    [SerializeField] private Vector2 echoSpacingRange = new Vector2(0.03f, 0.15f);
    [SerializeField] private Vector2 echoFadeRange = new Vector2(0f, 0.8f);
    [Tooltip("Rolled min corner speed. The max speed is min + a roll of Speed Spread.")]
    [SerializeField] private Vector2 speedMinRange = new Vector2(0.06f, 0.2f);
    [SerializeField] private Vector2 speedSpreadRange = new Vector2(0.05f, 0.2f);
    [SerializeField] private Vector2 marginRange = new Vector2(0f, 0.05f);
    [SerializeField] private Vector2 lineWidthRange = new Vector2(1f, 2f);
    [SerializeField] private Vector2 colorCycleRange = new Vector2(1f, 6f);

    private static readonly int PropPolygons    = Shader.PropertyToID("_Polygons");
    private static readonly int PropCorners     = Shader.PropertyToID("_Corners");
    private static readonly int PropEchoes      = Shader.PropertyToID("_Echoes");
    private static readonly int PropEchoSpacing = Shader.PropertyToID("_EchoSpacing");
    private static readonly int PropEchoFade    = Shader.PropertyToID("_EchoFade");
    private static readonly int PropSpeedMin    = Shader.PropertyToID("_SpeedMin");
    private static readonly int PropSpeedMax    = Shader.PropertyToID("_SpeedMax");
    private static readonly int PropMargin      = Shader.PropertyToID("_Margin");
    private static readonly int PropLineWidth   = Shader.PropertyToID("_LineWidth");
    private static readonly int PropColorCycle  = Shader.PropertyToID("_ColorCycle");

    private RawImage image;
    private Material runtime;
    private RenderTexture target;
    private float startedAt;

    // -- Unity -------------------

    private void Awake()
    {
        image = GetComponent<RawImage>();
    }

    private void OnEnable()
    {
        if (material == null)
        {
            Debug.LogWarning($"[MystifyScreensaver] '{name}' has no material; it shows nothing.", this);
            image.enabled = false;
            enabled = false;
            return;
        }

        if (runtime == null) runtime = new Material(material) { name = material.name + " (Runtime)" };
        else runtime.CopyPropertiesFromMaterial(material);

        // Hidden until the first frame is drawn: with no texture a RawImage draws plain white.
        image.enabled = false;
        Reroll();
    }

    private void OnDisable()
    {
        if (image != null) image.enabled = false;
        ReleaseTarget();
    }

    private void OnDestroy()
    {
        ReleaseTarget();
        if (runtime != null) Destroy(runtime);
    }

    private void LateUpdate()
    {
        EnsureTarget();
        runtime.SetFloat(PropClock, Time.unscaledTime - startedAt);

        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(null, target, runtime);
        RenderTexture.active = previous;

        image.enabled = true;
    }

    // -- Public API -------------------

    /// <summary>A new pattern, never seen before, starting again from its first line.</summary>
    [ContextMenu("Reroll")]
    public void Reroll()
    {
        if (runtime == null) return;

        // Below 2^24: the shader reads the seed back out of a float, exact only up to there.
        runtime.SetFloat(PropSeed, Rng.Next(1, 1 << 24));
        if (randomizeLook) RandomizeLook();
        startedAt = Time.unscaledTime;
    }

    /// <summary>
    /// Rolls the tunable look on the runtime copy, clamped to the shader's own property ranges.
    /// Only the properties listed here change: the hue band, Brightness, Saturation, Background,
    /// render queue and GI stay exactly as the material asset has them.
    /// </summary>
    private void RandomizeLook()
    {
        runtime.SetFloat(PropPolygons, RollInt(polygonsRange, 1, 4));
        runtime.SetFloat(PropCorners, RollInt(cornersRange, 3, 6));
        runtime.SetFloat(PropEchoes, RollInt(echoesRange, 1, 16));
        runtime.SetFloat(PropEchoSpacing, Roll(echoSpacingRange, 0.01f, 0.3f));
        runtime.SetFloat(PropEchoFade, Roll(echoFadeRange, 0f, 1f));

        float speedMin = Roll(speedMinRange, 0f, 1f);
        runtime.SetFloat(PropSpeedMin, speedMin);
        runtime.SetFloat(PropSpeedMax, Mathf.Clamp(speedMin + Roll(speedSpreadRange, 0f, 1f), speedMin, 1f));

        runtime.SetFloat(PropMargin, Roll(marginRange, 0f, 0.2f));
        runtime.SetFloat(PropLineWidth, Roll(lineWidthRange, 0.5f, 4f));
        runtime.SetFloat(PropColorCycle, Roll(colorCycleRange, 0.25f, 20f));
    }

    private static float Roll(Vector2 range, float min, float max)
    {
        float lo = Mathf.Clamp(Mathf.Min(range.x, range.y), min, max);
        float hi = Mathf.Clamp(Mathf.Max(range.x, range.y), min, max);
        return lo + (float)Rng.NextDouble() * (hi - lo);
    }

    private static int RollInt(Vector2Int range, int min, int max)
    {
        int lo = Mathf.Clamp(Mathf.Min(range.x, range.y), min, max);
        int hi = Mathf.Clamp(Mathf.Max(range.x, range.y), min, max);
        return Rng.Next(lo, hi + 1);   // inclusive
    }

    // -- Render target -------------------

    private void EnsureTarget()
    {
        Rect rect = image.rectTransform.rect;
        float aspect = rect.height > 0f ? rect.width / rect.height : 16f / 9f;
        int height = Mathf.Max(1, rows);
        int width = Mathf.Max(1, Mathf.RoundToInt(height * aspect));
        if (target != null && target.width == width && target.height == height) return;

        ReleaseTarget();

        target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = "Mystify Screensaver",
            filterMode = FilterMode.Point,   // one texel = one hard-edged block on screen
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
        };
        target.Create();

        runtime.SetVector(PropTargetSize, new Vector4(width, height, 0f, 0f));
        image.texture = target;
    }

    private void ReleaseTarget()
    {
        if (target == null) return;

        if (image != null) image.texture = null;
        target.Release();
        Destroy(target);
        target = null;
    }
}
