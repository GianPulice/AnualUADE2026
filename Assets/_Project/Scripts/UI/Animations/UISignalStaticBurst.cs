using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dead-channel burst behind a menu: every so often the tube loses the signal for a fraction of a
/// second — static over the panel, the CRT's glitch bands torn wide open — and then picks it back up.
/// Nothing triggers it and nothing depends on it; it is the room tone of the main menu.
///
/// Ambient, not a transition. <see cref="UISignalTransition"/> covers a change of content and owns its
/// own "SignalStatic" overlay, which it switches off the moment the transition ends; this one owns a
/// separate RawImage and runs on its own timer, so the two never fight over the same node.
///
/// The level is STEPPED, never faded — the same reason the transition's flicker is stepped: a fade
/// reads as a menu effect, steps read as a tube. The grain is one small point-filtered texture whose
/// UV origin is thrown somewhere new every frame; static that holds still reads as a texture, not as
/// noise.
///
/// While a burst runs it also pushes <see cref="CanvasCRTPresenter"/>'s screen material, so the picture
/// tears at the same moment the noise arrives instead of the noise sitting on a perfectly steady image.
/// That material is a runtime copy (UIPSXSettingsApplier makes it), so the values written here never
/// reach the asset on disk — but they are still restored when the burst ends, and on disable, or a
/// burst cut short would leave the tube torn for good.
///
/// Runs on unscaled time (the menu may sit at timeScale 0) and obeys the player's VHS Glitch setting —
/// the same key <see cref="GlitchController"/> reads, so one toggle switches off every signal effect.
/// </summary>
[AddComponentMenu("WIRED/UI Animations/UI Signal Static Burst")]
public class UISignalStaticBurst : MonoBehaviour
{
    // Same key as GlitchController and SettingsModel.
    private const string KEY_GLITCH = "Settings_VHSGlitch";

    private static readonly int PropGlitchChance    = Shader.PropertyToID("_GlitchChance");
    private static readonly int PropGlitchAmount    = Shader.PropertyToID("_GlitchAmount");
    private static readonly int PropChromaticOffset = Shader.PropertyToID("_ChromaticOffset");

    [Header("Overlay")]
    [Tooltip("Stretched over the panel, no raycast. Its texture is generated at runtime.")]
    [SerializeField] private RawImage staticNoise;

    [Tooltip("Grain resolution. Small on purpose: stretched over the screen it keeps the cells PSX-chunky.")]
    [SerializeField] private Vector2Int noiseSize = new Vector2Int(256, 144);

    [Header("Timing (unscaled seconds)")]
    [Tooltip("Gap between bursts. Wide, so the menu is quiet most of the time and a burst still surprises.")]
    [SerializeField] private Vector2 intervalRange = new Vector2(6f, 22f);

    [SerializeField] private Vector2 durationRange = new Vector2(0.12f, 0.5f);

    [Header("Strength")]
    [Tooltip("Alpha the burst reaches. Rolled per burst, so some are a blink and some swallow the screen.")]
    [Range(0f, 1f)] [SerializeField] private float minPeak = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float maxPeak = 0.7f;

    [Tooltip("How many times a second the level jumps while the burst holds.")]
    [Range(1f, 60f)] [SerializeField] private float sputterRate = 20f;

    [Header("CRT tube (optional)")]
    [Tooltip("The tube this panel is shown through. Left empty, the burst is noise alone — the tube " +
             "keeps its own idle glitch and nothing is pushed.")]
    [SerializeField] private CanvasCRTPresenter tube;

    [Tooltip("Band chance while the burst runs. The material's idle value is 0.015.")]
    [Range(0f, 0.2f)] [SerializeField] private float burstGlitchChance = 0.12f;

    [Tooltip("Band offset while the burst runs, in pixels. Idle is 8.")]
    [Range(0f, 40f)] [SerializeField] private float burstGlitchAmount = 26f;

    [Tooltip("Chromatic aberration at the edge while the burst runs, in pixels. Idle is 1.5.")]
    [Range(0f, 6f)] [SerializeField] private float burstChromaticOffset = 4.5f;

    private Texture2D noise;

    private bool  allowed;
    private bool  bursting;
    private float burstStartedAt;
    private float burstDuration;
    private float burstPeak;
    private float sputter = 1f;
    private float nextSputterAt;
    private float nextBurstAt;

    private Material tubeMaterial;
    private float baseGlitchChance, baseGlitchAmount, baseChromaticOffset;

    // -- Unity -------------------

    private void Awake()
    {
        if (staticNoise == null)
        {
            Debug.LogWarning($"[UISignalStaticBurst] '{name}' has no static overlay; it does nothing.", this);
            enabled = false;
            return;
        }

        staticNoise.texture = noise = CreateNoise(Mathf.Max(2, noiseSize.x), Mathf.Max(2, noiseSize.y));
        staticNoise.raycastTarget = false;
        staticNoise.enabled = false;
    }

    private void OnEnable()
    {
        SettingsModel.OnSettingsApplied += ApplySettings;
        ApplySettings();
        ScheduleNext();
    }

    private void OnDisable()
    {
        SettingsModel.OnSettingsApplied -= ApplySettings;
        EndBurst();
    }

    private void OnDestroy()
    {
        if (noise != null) Destroy(noise);
    }

    private void Update()
    {
        if (!allowed) return;

        float now = Time.unscaledTime;

        if (bursting)
        {
            float t = burstDuration > 0f ? (now - burstStartedAt) / burstDuration : 1f;
            if (t >= 1f) EndBurst();
            else Hold(now, t);
            return;
        }

        // The same flag that holds the world's glitch back during inventory / skill check / examine.
        if (GlitchController.SuspendTriggering) return;
        if (now < nextBurstAt) return;

        BeginBurst(now);
    }

    // -- Core -------------------

    private void BeginBurst(float now)
    {
        bursting       = true;
        burstStartedAt = now;
        burstDuration  = Random.Range(durationRange.x, durationRange.y);
        burstPeak      = Random.Range(Mathf.Min(minPeak, maxPeak), Mathf.Max(minPeak, maxPeak));
        nextSputterAt  = now;

        staticNoise.enabled = true;
        PushTube();
        Hold(now, 0f);
    }

    private void Hold(float now, float t)
    {
        if (now >= nextSputterAt)
        {
            sputter = Random.Range(0.45f, 1f);
            nextSputterAt = now + 1f / Mathf.Max(sputterRate, 1f);
        }

        // Holds, then falls away over the tail: the signal comes back faster than it left.
        float decay = 1f - t * t;

        // A new grain every frame — see the class note.
        staticNoise.uvRect = new Rect(Random.value, Random.value, 1f, 1f);
        SetAlpha(staticNoise, burstPeak * sputter * decay);
    }

    private void EndBurst()
    {
        bool wasBursting = bursting;
        bursting = false;

        if (staticNoise != null)
        {
            SetAlpha(staticNoise, 0f);
            staticNoise.enabled = false;
        }

        RestoreTube();
        if (wasBursting) ScheduleNext();
    }

    private void ScheduleNext()
    {
        nextBurstAt = Time.unscaledTime + Random.Range(intervalRange.x, intervalRange.y);
    }

    private void ApplySettings()
    {
        allowed = PlayerPrefs.GetInt(KEY_GLITCH, 1) != 0;
        if (!allowed) EndBurst();
    }

    // -- The tube -------------------

    /// <summary>
    /// Finds the tube's runtime material, once it exists. Resolved lazily rather than cached up front:
    /// the presenter builds that material in its own Awake, execution order between the two is not
    /// pinned, and the material is replaced if the presenter is ever rebuilt.
    /// </summary>
    private bool TryResolveTube()
    {
        if (tubeMaterial != null) return true;
        if (tube == null) return false;

        Material material = tube.ScreenMaterial;
        if (material == null) return false;

        tubeMaterial        = material;
        baseGlitchChance    = material.GetFloat(PropGlitchChance);
        baseGlitchAmount    = material.GetFloat(PropGlitchAmount);
        baseChromaticOffset = material.GetFloat(PropChromaticOffset);
        return true;
    }

    private void PushTube()
    {
        if (!TryResolveTube()) return;

        tubeMaterial.SetFloat(PropGlitchChance,    burstGlitchChance);
        tubeMaterial.SetFloat(PropGlitchAmount,    burstGlitchAmount);
        tubeMaterial.SetFloat(PropChromaticOffset, burstChromaticOffset);
    }

    private void RestoreTube()
    {
        if (tubeMaterial == null) return;

        tubeMaterial.SetFloat(PropGlitchChance,    baseGlitchChance);
        tubeMaterial.SetFloat(PropGlitchAmount,    baseGlitchAmount);
        tubeMaterial.SetFloat(PropChromaticOffset, baseChromaticOffset);
    }

    // -- Helpers -------------------

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    /// <summary>
    /// Grey static, point-filtered so each cell stays a hard PSX-sized block, repeat-wrapped so the UV
    /// origin can be thrown anywhere without the edges showing.
    /// </summary>
    private static Texture2D CreateNoise(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Signal Static Burst",
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
