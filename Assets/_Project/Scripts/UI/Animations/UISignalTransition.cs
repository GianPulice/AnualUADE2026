using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A change of channel on a panel: when its content changes, the panel catches the new signal a few
/// frames at a time instead of snapping to it — a stepped flicker, while a burst of static and a scan
/// line pass over it. Play() it when the content is swapped, in the same frame or just after.
///
/// A stepped flicker rather than a fade on purpose: a fade reads as a menu transition; the steps read
/// as a tube locking on.
///
/// The static and the scan line carry CanvasGroups that ignore the panel's, or the flicker would dim
/// them along with the content they are supposed to cover.
///
/// Runs on unscaled time: the pause menu can open over the inventory and must not freeze it half-lit.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[AddComponentMenu("WIRED/UI Animations/UI Signal Transition")]
public class UISignalTransition : MonoBehaviour
{
    [Tooltip("Stretched over the panel, no raycast. Its texture is generated at runtime.")]
    [SerializeField] private RawImage staticNoise;

    [Tooltip("Thin bar that sweeps the panel top to bottom. Its authored alpha is the peak.")]
    [SerializeField] private Graphic scanBar;

    [SerializeField] private float duration = 0.3f;
    [Range(0f, 1f)] [SerializeField] private float staticPeak = 0.45f;

    // (time fraction, panel alpha): the steps of the flicker. The last one must be alpha 1.
    private static readonly Vector2[] FlickerSteps =
    {
        new Vector2(0.00f, 0.10f),
        new Vector2(0.12f, 0.75f),
        new Vector2(0.22f, 0.25f),
        new Vector2(0.34f, 0.95f),
        new Vector2(0.46f, 0.60f),
        new Vector2(0.58f, 1.00f),
    };

    private CanvasGroup group;
    private Texture2D noise;
    private float scanBarPeak;
    private float elapsed;
    private bool playing;

    // -- Unity -------------------

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (scanBar != null) scanBarPeak = scanBar.color.a;
        if (staticNoise != null) staticNoise.texture = noise = CreateNoise(160, 90);
        Finish();
    }

    private void OnDisable() => Finish();

    private void OnDestroy()
    {
        if (noise != null) Destroy(noise);
    }

    private void Update()
    {
        if (!playing) return;

        elapsed += Time.unscaledDeltaTime;
        float t = duration > 0f ? elapsed / duration : 1f;

        if (t >= 1f) Finish();
        else Apply(t);
    }

    // -- Public API -------------------

    public void Play()
    {
        if (!isActiveAndEnabled) return;

        playing = true;
        elapsed = 0f;
        SetOverlaysVisible(true);
        Apply(0f);
    }

    // -- Core -------------------

    private void Apply(float t)
    {
        group.alpha = FlickerAt(t);

        if (staticNoise != null)
        {
            // A new grain every frame: static that holds still reads as a texture, not as noise.
            staticNoise.uvRect = new Rect(Random.value, Random.value, 1f, 1f);
            SetAlpha(staticNoise, staticPeak * (1f - t) * (1f - t));
        }

        if (scanBar != null)
        {
            RectTransform bar = scanBar.rectTransform;
            RectTransform area = (RectTransform)bar.parent;
            bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, -area.rect.height * t);
            SetAlpha(scanBar, scanBarPeak * (1f - t * t));
        }
    }

    private void Finish()
    {
        playing = false;
        if (group != null) group.alpha = 1f;
        SetOverlaysVisible(false);
    }

    private void SetOverlaysVisible(bool visible)
    {
        if (staticNoise != null) staticNoise.enabled = visible;
        if (scanBar != null) scanBar.enabled = visible;
    }

    private static float FlickerAt(float t)
    {
        float alpha = FlickerSteps[0].y;
        foreach (Vector2 step in FlickerSteps)
            if (t >= step.x) alpha = step.y;
        return alpha;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    /// <summary>Grey static, point-filtered so each cell stays a hard PSX-sized block.</summary>
    private static Texture2D CreateNoise(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Signal Static",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };

        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Random.Range(0, 256);
            pixels[i] = new Color32(v, v, v, 255);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
