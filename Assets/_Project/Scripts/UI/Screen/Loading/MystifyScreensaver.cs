using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The loading screen's background: the Windows 3.1 / 95 "Mystify" screensaver, drawn by
/// MystifyScreensaver.shader and shown on this RawImage.
///
/// Every time it is enabled — every time the loading screen comes up — it rolls a new seed: new
/// starting corners, directions, speeds and colours, so like the real screensaver it never plays the
/// same pattern twice. Everything else (polygons, echoes, speeds, colour rate) is tuned on the material
/// asset, which is re-read on every show: tweak it in Play mode and the next load picks it up.
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
        startedAt = Time.unscaledTime;
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
