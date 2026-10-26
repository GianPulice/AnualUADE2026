using System;
using UnityEngine;

public enum SparkIntensity
{
    Low,
    Medium,
    High
}

/// <summary>
/// Drives the ElectricSparks prefab: one intensity selector (Low / Medium / High) and one colour,
/// applied to every particle system of the effect so none of them has to be tuned by hand.
///
/// The effect is a chain of strikes. A strike fires the radial sparks (with bounce, trails and impact
/// splashes), the flash with its light, the embers and the smoke at the same instant, so they always
/// belong together. Strikes come at irregular times with their own chance of happening, from a random
/// point of the loop per instance, so two cables never crackle in unison and the loop never repeats
/// exactly. <see cref="Strike"/> fires one on demand, for a puzzle that shorts a panel.
///
/// The intensity changes how much each strike throws (counts, speed, size, brightness, trails, light,
/// smoke); the colour is the hue of the sparks and their glow. Both are applied in the editor as soon
/// as they change, so the Particle System preview follows the Inspector. Strikes themselves only run
/// in play mode.
///
/// What it owns: counts, speed and size ranges, colours, trail ratio, flash light and the strike
/// schedule. What it leaves to the systems: shapes, gravity, drag, noise, collision and the rest of
/// the look; tune those on the child systems. The sparks fly along the +Z of this object.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ElectricSparks : MonoBehaviour
{
    [Serializable]
    public struct Preset
    {
        [Tooltip("Scales the chance of every strike in the pattern (1 = the pattern as written).")]
        [Range(0f, 1f)] public float strikeChance;

        [Header("Sparks")]
        [Tooltip("Sparks thrown per strike (random between the two).")]
        public Vector2Int sparkCount;
        [Tooltip("Launch speed in m/s (random between the two).")]
        public Vector2 sparkSpeed;
        [Tooltip("Spark width in metres (random between the two).")]
        public Vector2 sparkSize;
        [Tooltip("HDR multiplier on the spark colour. Above 1 the sparks burn brighter than the screen.")]
        [Min(0f)] public float brightness;
        [Tooltip("Share of the sparks that leave a ribbon trail. 0 turns trails off.")]
        [Range(0f, 1f)] public float trailRatio;
        [Tooltip("Chance that a spark landing on a surface splashes into tiny sparks.")]
        [Range(0f, 1f)] public float impactChance;

        [Header("Flash")]
        [Tooltip("Flash sprites per strike (random between the two).")]
        public Vector2Int flashCount;
        [Tooltip("Size of the flash sprites, relative to the base size.")]
        [Min(0f)] public float flashSize;
        [Tooltip("Intensity of the real light each flash lights up. 0 turns the light off.")]
        [Min(0f)] public float lightIntensity;

        [Header("Atmosphere")]
        [Tooltip("Slow glowing embers per strike. 0 turns them off.")]
        public Vector2Int emberCount;
        [Tooltip("Smoke puffs per strike. 0 turns smoke off.")]
        public Vector2Int smokeCount;
    }

    [Serializable]
    public struct StrikeSlot
    {
        [Tooltip("Seconds into the loop.")]
        [Min(0f)] public float time;
        [Tooltip("Chance this strike happens, before the intensity scales it.")]
        [Range(0f, 1f)] public float probability;
    }

    [Tooltip("How much each strike throws: Low is a mild crackle, High is a shower.")]
    [SerializeField] private SparkIntensity intensity = SparkIntensity.Medium;

    [Tooltip("Hue of the sparks and their glow. Brightness comes from the intensity, not from this swatch.")]
    [ColorUsage(false)]
    [SerializeField] private Color color = new Color(1f, 0.45f, 0.08f);

    [Tooltip("How white the fastest sparks burn before cooling to the colour. 0 = colour only, 1 = white hot.")]
    [Range(0f, 1f)]
    [SerializeField] private float hotness = 0.7f;

    [Tooltip("The three levels, in order: Low, Medium, High.")]
    [SerializeField] private Preset[] presets = DefaultPresets();

    [Header("Strikes")]
    [Tooltip("Length of the strike loop in seconds.")]
    [Min(0.5f)]
    [SerializeField] private float loopSeconds = 5f;

    [Tooltip("When the strikes happen in the loop, and how likely each is. Uneven on purpose.")]
    [SerializeField] private StrikeSlot[] strikePattern = DefaultStrikes();

    [Tooltip("Strikes stop while the main camera is farther than this, so nothing is simulated out of " +
             "sight. 0 = always.")]
    [Min(0f)]
    [SerializeField] private float activeDistance = 30f;

    [Tooltip("Strike on its own once the scene is running. Turn off to fire it only through Strike().")]
    [SerializeField] private bool playOnAwake = true;

    [Header("Systems (wired in the prefab)")]
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private ParticleSystem flash;
    [SerializeField] private ParticleSystem embers;
    [SerializeField] private ParticleSystem smoke;
    [SerializeField] private ParticleSystem impact;

    // Base sizes of the flash sprites in metres; the preset's Flash Size scales them.
    private static readonly Vector2 FlashWidth = new Vector2(0.10f, 0.28f);
    private static readonly Vector2 FlashLength = new Vector2(0.35f, 0.9f);

    // Speed range, in m/s, over which a spark cools from hot to the plain colour.
    private static readonly Vector2 CoolingSpeed = new Vector2(0.5f, 7f);

    private float _loopTime;
    private bool _running;

    public SparkIntensity Intensity
    {
        get => intensity;
        set { intensity = value; Apply(); }
    }

    public Color Color
    {
        get => color;
        set { color = value; Apply(); }
    }

    public bool IsRunning => _running;

    public void SetIntensity(SparkIntensity value) => Intensity = value;

    public void SetColor(Color value) => Color = value;

    /// <summary>Starts the strike schedule (it also starts by itself on awake unless turned off).</summary>
    public void Play() => _running = true;

    /// <summary>Stops new strikes; what is already flying finishes its arc.</summary>
    public void Stop() => _running = false;

    /// <summary>Fires one strike now, whatever the schedule says.</summary>
    [ContextMenu("Strike")]
    public void Strike()
    {
        if (!HasAllSystems()) return;

        Preset preset = presets[(int)intensity];
        Throw(sparks, preset.sparkCount);
        Throw(flash, preset.flashCount);
        Throw(embers, preset.emberCount);
        Throw(smoke, preset.smokeCount);
    }

    private void OnEnable()
    {
        Apply();
        if (!Application.isPlaying) return;

        // A random point of the loop per instance: cables put in the same frame must not crackle together.
        _loopTime = UnityEngine.Random.Range(0f, loopSeconds);
        _running = playOnAwake;
    }

    private void OnDisable() => _running = false;

#if UNITY_EDITOR
    // The Particle System modules cannot be touched from inside OnValidate itself.
    private void OnValidate() => UnityEditor.EditorApplication.delayCall += () =>
    {
        if (this != null) Apply();
    };
#endif

    private void Update()
    {
        if (!_running || !Application.isPlaying) return;

        float previous = _loopTime;
        _loopTime += Time.deltaTime;
        bool wrapped = _loopTime >= loopSeconds;
        if (wrapped) _loopTime -= loopSeconds;

        if (!InRange()) return;

        float chance = presets[(int)intensity].strikeChance;
        for (int i = 0; i < strikePattern.Length; i++)
        {
            float time = strikePattern[i].time;
            bool crossed = wrapped ? (time > previous || time <= _loopTime) : (time > previous && time <= _loopTime);
            if (crossed && UnityEngine.Random.value < strikePattern[i].probability * chance)
                Strike();
        }
    }

    private bool InRange()
    {
        if (activeDistance <= 0f) return true;
        Camera main = Camera.main;
        return main == null || (main.transform.position - transform.position).sqrMagnitude <= activeDistance * activeDistance;
    }

    private bool HasAllSystems() =>
        presets != null && presets.Length == 3 && sparks != null && flash != null &&
        embers != null && smoke != null && impact != null;

    private void Apply()
    {
        if (!HasAllSystems())
        {
            Debug.LogWarning($"[{nameof(ElectricSparks)}] '{name}' needs three presets and all five child " +
                             "systems wired; the effect is left as it is.", this);
            return;
        }

        Preset preset = presets[(int)intensity];
        Color hot = Color.Lerp(color, Color.white, hotness);

        ApplySparks(preset, hot);
        ApplyFlash(preset, hot);
        ApplyEmbers(preset);
        ApplySmoke();
        ApplyImpact(preset, hot);
    }

    private void ApplySparks(Preset preset, Color hot)
    {
        var main = sparks.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(preset.sparkSpeed.x, preset.sparkSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(preset.sparkSize.x, preset.sparkSize.y);
        // Neutral and HDR: the hue lives in Color by Speed, so it can cool from white to the colour.
        main.startColor = new Color(preset.brightness, preset.brightness, preset.brightness, 1f);

        var cooling = sparks.colorBySpeed;
        cooling.enabled = true;
        cooling.range = CoolingSpeed;
        cooling.color = new ParticleSystem.MinMaxGradient(TwoColourGradient(color, hot));

        var trails = sparks.trails;
        trails.enabled = preset.trailRatio > 0f;
        trails.ratio = preset.trailRatio;

        // Sub-emitter 0 of the sparks is the impact splash, fired when a spark lands.
        var subEmitters = sparks.subEmitters;
        if (subEmitters.subEmittersCount > 0)
            subEmitters.SetSubEmitterEmitProbability(0, preset.impactChance);
    }

    private void ApplyFlash(Preset preset, Color hot)
    {
        var main = flash.main;
        main.startSizeX = new ParticleSystem.MinMaxCurve(FlashWidth.x * preset.flashSize, FlashWidth.y * preset.flashSize);
        main.startSizeY = new ParticleSystem.MinMaxCurve(FlashLength.x * preset.flashSize, FlashLength.y * preset.flashSize);
        Color flashColor = Color.Lerp(color, hot, 0.6f) * (preset.brightness * 1.3f);
        flashColor.a = 1f;
        main.startColor = flashColor;

        var lights = flash.lights;
        lights.enabled = preset.lightIntensity > 0f;
        lights.intensityMultiplier = preset.lightIntensity;
    }

    private void ApplyEmbers(Preset preset)
    {
        var main = embers.main;
        Color emberColor = color * (preset.brightness * 0.55f);
        emberColor.a = 1f;
        main.startColor = emberColor;
    }

    private void ApplySmoke()
    {
        var main = smoke.main;
        // Dark smoke with a faint hint of the glow it is lit by.
        Color smokeColor = Color.Lerp(new Color(0.18f, 0.18f, 0.18f), color * 0.5f, 0.2f);
        smokeColor.a = 0.45f;
        main.startColor = smokeColor;
    }

    private void ApplyImpact(Preset preset, Color hot)
    {
        var main = impact.main;
        main.startColor = new Color(hot.r * preset.brightness, hot.g * preset.brightness, hot.b * preset.brightness, 1f);
    }

    // Zero in the upper bound turns the system off for this level.
    private static void Throw(ParticleSystem system, Vector2Int count)
    {
        if (count.y <= 0) return;
        system.Emit(UnityEngine.Random.Range(count.x, count.y + 1));
    }

    // Slow end first: Color by Speed reads the gradient left to right as the speed rises.
    private static Gradient TwoColourGradient(Color slow, Color fast)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(slow, 0f), new GradientColorKey(fast, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    private static Preset[] DefaultPresets() => new[]
    {
        new Preset // Low: a mild crackle.
        {
            strikeChance = 0.55f,
            sparkCount = new Vector2Int(14, 24), sparkSpeed = new Vector2(2f, 6f), sparkSize = new Vector2(0.014f, 0.034f),
            brightness = 2f, trailRatio = 0f, impactChance = 0f,
            flashCount = new Vector2Int(1, 1), flashSize = 0.7f, lightIntensity = 0f,
            emberCount = Vector2Int.zero, smokeCount = Vector2Int.zero,
        },
        new Preset // Medium: the default, a proper shower.
        {
            strikeChance = 0.85f,
            sparkCount = new Vector2Int(40, 70), sparkSpeed = new Vector2(2.5f, 8f), sparkSize = new Vector2(0.016f, 0.04f),
            brightness = 3f, trailRatio = 0.25f, impactChance = 0.06f,
            flashCount = new Vector2Int(1, 2), flashSize = 1f, lightIntensity = 1.2f,
            emberCount = new Vector2Int(6, 10), smokeCount = new Vector2Int(1, 2),
        },
        new Preset // High: a short circuit going off.
        {
            strikeChance = 1f,
            sparkCount = new Vector2Int(90, 140), sparkSpeed = new Vector2(3f, 10f), sparkSize = new Vector2(0.018f, 0.05f),
            brightness = 4.5f, trailRatio = 0.4f, impactChance = 0.12f,
            flashCount = new Vector2Int(2, 3), flashSize = 1.5f, lightIntensity = 2.5f,
            emberCount = new Vector2Int(14, 24), smokeCount = new Vector2Int(3, 5),
        },
    };

    private static StrikeSlot[] DefaultStrikes() => new[]
    {
        new StrikeSlot { time = 0.0f, probability = 1.0f },
        new StrikeSlot { time = 0.5f, probability = 0.6f },
        new StrikeSlot { time = 1.1f, probability = 0.8f },
        new StrikeSlot { time = 1.5f, probability = 0.5f },
        new StrikeSlot { time = 2.3f, probability = 0.9f },
        new StrikeSlot { time = 2.9f, probability = 0.6f },
        new StrikeSlot { time = 3.6f, probability = 0.7f },
        new StrikeSlot { time = 4.2f, probability = 0.5f },
    };
}
