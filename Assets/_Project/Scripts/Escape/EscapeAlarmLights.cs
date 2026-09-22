using System;
using UnityEngine;

/// <summary>
/// The corridor's lamps turned into sirens for the chase: red and amber taking turns, the way a
/// patrol car's lights do. One job: make a set of ceiling <see cref="Light"/>s read as alarm lights
/// from anywhere down the corridor, through the escape's closed fog, without lighting the player.
///
/// ── HOW A LAMP READS THROUGH THE FOG ────────────────────────────────────────
///
/// Each lamp ends up carrying the same three pieces as the Light Base Switch prefab — the variant
/// the switch lamps use — so both lights are built the same way, with the escape's own numbers:
///   1. a <see cref="FogBeacon"/> ON THE LAMP, dropped <see cref="BeaconDrop"/> under it with its
///      centre offset (the lamps sit flush with the ceiling, and a beacon inside the ceiling's
///      depth is hidden by it). It is the only thing composited AFTER the fog's extinction, so it
///      is what shows from down the corridor in the closed fog. It carries the colour of the
///      moment, and scale 0 while its group is dark, which also frees its slot;
///   2. the lamp's <see cref="FogLightBypass"/> as a CLEAR-ONLY pool, like the variant's: a sphere
///      with overrideAppearance, intensity 0 and only its clearAmount left. The player and the
///      Nemesis are Unlit — the bypass's injected light is the only thing that brightens them, and
///      it was what burnt the player out under a Light Base. Clearing alone lets the lit floor read
///      a little further. With overrideAppearance the zone never reads the Light, so a lamp in its
///      dark half cannot fall back to the preset's defaults (the Resolve bug) either;
///   3. a <see cref="FogLightVolume"/>: the cone of its Spot drawn in the air, red or amber, the
///      "you can see where the light goes" look. It follows the Light, so a lamp in its dark half
///      shows no beam and takes no slot of the controller's eight.
/// Plus the Spot itself, for the walls and the floor — it does nothing to the Unlit characters.
///
/// A lamp in its dark half goes to intensity 0 with the Light still ENABLED, never disabled: an
/// enabled Light at zero is what tells the beam it is out without disturbing anything else.
///
/// ── THE PATTERN ─────────────────────────────────────────────────────────────
///
/// Lamps are sorted west to east and split by parity into two groups: one red, one amber. Each
/// period, the first half lights one group and the second half the other, with a short dark gap
/// between them. Neighbours are always in opposite groups, so the whole corridor carries both
/// colours at once and only half the beacons are lit at any time. The period is clamped so the
/// flashing stays under 3 Hz (photosensitivity).
///
/// Nothing is left behind: <see cref="Begin"/> remembers each lamp (active, enabled, colour,
/// intensity, everything it touches on the bypass, and any beacon or volume the lamp already had)
/// and <see cref="End"/> puts all of it back, removing what it added. The lamps' prefab (Light
/// Base) is never edited: everything is set at runtime, the same way EscapeGuideDoor does it.
///
/// How to use it: on the escape rig, drag the corridor's ceiling Lights into 'lamps' (order does
/// not matter). The director calls <see cref="Begin"/> as control comes back for the chase and
/// <see cref="End"/> when the escape ends.
/// </summary>
[DisallowMultipleComponent]
public class EscapeAlarmLights : MonoBehaviour
{
    [Tooltip("Las lámparas de techo del pasillo que se vuelven sirenas. El orden no importa: se " +
             "ordenan solas por X y se alternan rojo / ámbar. Pueden estar apagadas o inactivas en " +
             "la escena: se prenden al arrancar la persecución.")]
    [SerializeField] private Light[] lamps = Array.Empty<Light>();

    // Keeps the flashing under 3 Hz: two flashes per period.
    private const float MinPeriod = 0.67f;

    // How far under the lamp the beacon hangs (m): clear of the ceiling's depth, still on the lamp.
    private const float BeaconDrop = 0.3f;

    /// <summary>What a beacon the lamp already had looked like, to put it back at the end.</summary>
    private struct BeaconSave
    {
        public bool enabled;
        public Color color;
        public float intensity;
        public float worldRadius;
        public float minPixelRadius;
        public Vector3 centreOffset;
        public bool limitByFacing;
        public float nearFadeStart;
        public float nearFadeEnd;
    }

    private struct LampState
    {
        public Light light;
        public FogLightBypass bypass;
        public FogBeacon beacon;
        public bool addedBeacon;
        public BeaconSave beaconSave;
        public FogLightVolume volume;
        public bool addedVolume;
        public bool volumeWasEnabled;
        public float volumeIntensity;
        public int group;

        public bool wasActive;
        public bool wasEnabled;
        public Color color;
        public float intensity;

        public FogLightBypass.BypassShape bypassShape;
        public bool bypassOverride;
        public Color bypassColor;
        public float bypassIntensity;
        public float bypassClear;
        public float bypassScale;
    }

    private LampState[] states = Array.Empty<LampState>();
    private SO_EscapeSequenceConfig config;
    private float elapsed;

    /// <summary>The sirens are going.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Starts the sirens, remembering how every lamp was. Safe to call twice.</summary>
    public void Begin(SO_EscapeSequenceConfig escapeConfig)
    {
        if (IsRunning) End();

        config = escapeConfig;
        if (config == null) return;

        int count = CountLamps();
        if (count == 0)
        {
            Debug.LogWarning($"[{nameof(EscapeAlarmLights)}] No lamps assigned: the chase has no " +
                             "sirens.", this);
            return;
        }

        states = new LampState[count];
        int slot = 0;
        for (int i = 0; i < lamps.Length; i++)
        {
            Light lamp = lamps[i];
            if (lamp == null) continue;

            FogLightBypass bypass = lamp.GetComponent<FogLightBypass>();
            states[slot++] = new LampState
            {
                light = lamp,
                bypass = bypass,
                wasActive = lamp.gameObject.activeSelf,
                wasEnabled = lamp.enabled,
                color = lamp.color,
                intensity = lamp.intensity,
                bypassShape = bypass != null ? bypass.shape : FogLightBypass.BypassShape.Sphere,
                bypassOverride = bypass != null && bypass.overrideAppearance,
                bypassColor = bypass != null ? bypass.color : Color.white,
                bypassIntensity = bypass != null ? bypass.intensity : 0f,
                bypassClear = bypass != null ? bypass.clearAmount : 0f,
                bypassScale = bypass != null ? bypass.LightIntensityScale : 1f,
            };
        }

        // West to east, so parity alternates between neighbours down the corridor.
        Array.Sort(states, (a, b) => a.light.transform.position.x.CompareTo(b.light.transform.position.x));

        for (int i = 0; i < states.Length; i++)
        {
            ref LampState s = ref states[i];
            s.group = i % 2;

            // Awake first: they sleep as inactive objects outside the escape, and a beacon added to
            // an inactive object would not register until it wakes.
            s.light.gameObject.SetActive(true);
            s.light.enabled = true;

            // Clear-only pool, set up the same way as the Light Base Switch variant: a sphere that
            // never reads the Light, so it only ever dissolves fog.
            if (s.bypass != null)
            {
                s.bypass.shape = FogLightBypass.BypassShape.Sphere;
                s.bypass.overrideAppearance = true;
                s.bypass.intensity = 0f;
                s.bypass.LightIntensityScale = 0f;
                s.bypass.clearAmount = config.AlarmPoolClear;
            }

            // The beacon goes on the lamp, like the variant's. One the lamp already carries is
            // reused and put back at the end.
            s.addedBeacon = !s.light.TryGetComponent(out s.beacon);
            if (s.addedBeacon) s.beacon = s.light.gameObject.AddComponent<FogBeacon>();
            else s.beaconSave = Save(s.beacon);

            s.beacon.enabled = true;
            // Local, so it follows the lamp: the Spot points down, so its own forward is "down".
            s.beacon.centreOffset = s.light.transform.InverseTransformVector(Vector3.down * BeaconDrop);
            ConfigureBeacon(s.beacon);

            // The beam in the air. Reads its shape, colour and on / off from the Spot on its own.
            s.addedVolume = !s.light.TryGetComponent(out s.volume);
            if (s.addedVolume) s.volume = s.light.gameObject.AddComponent<FogLightVolume>();
            else
            {
                s.volumeWasEnabled = s.volume.enabled;
                s.volumeIntensity = s.volume.intensity;
            }
            s.volume.enabled = true;
        }

        elapsed = 0f;
        IsRunning = true;
        Apply();

        // The sirens are easy to lose: a lamp that never woke, a pool with no bypass on it, or the
        // fog's light-volume slots taken by the lamps of another room. Say what was actually set up.
        int pools = 0, beams = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].bypass != null) pools++;
            if (states[i].volume != null) beams++;
        }
        Debug.Log($"[{nameof(EscapeAlarmLights)}] {states.Length} sirens on, {pools} pools, {beams} beams, " +
                  $"lamp intensity {config.AlarmLampIntensity}. Beams show up to " +
                  $"{VisionRangeController.MaxLightVolumes} at a time across the whole level; the fog's " +
                  "arrays keep the size of their first upload, so a raised cap needs an editor restart.", this);
    }

    /// <summary>Stops the sirens: every lamp back to how <see cref="Begin"/> found it.</summary>
    public void End()
    {
        IsRunning = false;

        for (int i = 0; i < states.Length; i++)
        {
            LampState s = states[i];
            if (s.light == null) continue;

            if (s.beacon != null)
            {
                if (s.addedBeacon) Destroy(s.beacon);
                else Restore(s.beacon, s.beaconSave);
            }

            if (s.volume != null)
            {
                if (s.addedVolume) Destroy(s.volume);
                else
                {
                    s.volume.intensity = s.volumeIntensity;
                    s.volume.enabled = s.volumeWasEnabled;
                }
            }

            if (s.bypass != null)
            {
                s.bypass.shape = s.bypassShape;
                s.bypass.overrideAppearance = s.bypassOverride;
                s.bypass.color = s.bypassColor;
                s.bypass.intensity = s.bypassIntensity;
                s.bypass.clearAmount = s.bypassClear;
                s.bypass.LightIntensityScale = s.bypassScale;
            }

            s.light.color = s.color;
            s.light.intensity = s.intensity;
            s.light.enabled = s.wasEnabled;
            s.light.gameObject.SetActive(s.wasActive);
        }

        states = Array.Empty<LampState>();
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!IsRunning) return;

        elapsed += Time.deltaTime;
        Apply();
    }

    /// <summary>Which group is lit right now (0 or 1), or -1 in the dark gap between them.</summary>
    private int LitGroup()
    {
        float period = Mathf.Max(MinPeriod, config.AlarmPeriod);
        float phase = Mathf.Repeat(elapsed, period) / period;

        int group = phase < 0.5f ? 0 : 1;
        float withinHalf = Mathf.Repeat(phase, 0.5f) * 2f;
        return withinHalf < 1f - config.AlarmGap ? group : -1;
    }

    private void Apply()
    {
        int lit = LitGroup();

        for (int i = 0; i < states.Length; i++)
        {
            LampState s = states[i];
            if (s.light == null) continue;

            // Re-read every frame, like the rest of the config: tuning shows live in Play mode.
            Color color = s.group == 0 ? config.AlarmRed : config.AlarmAmber;
            bool on = s.group == lit;

            s.light.color = color;
            s.light.intensity = on ? config.AlarmLampIntensity : 0f;

            if (s.bypass != null) s.bypass.clearAmount = config.AlarmPoolClear;
            if (s.volume != null) s.volume.intensity = config.AlarmBeamIntensity;

            if (s.beacon != null)
            {
                s.beacon.color = color;
                s.beacon.IntensityScale = on ? 1f : 0f;
                ConfigureBeacon(s.beacon);
            }
        }
    }

    private static BeaconSave Save(FogBeacon b) => new BeaconSave
    {
        enabled = b.enabled,
        color = b.color,
        intensity = b.intensity,
        worldRadius = b.worldRadius,
        minPixelRadius = b.minPixelRadius,
        centreOffset = b.centreOffset,
        limitByFacing = b.limitByFacing,
        nearFadeStart = b.nearFadeStart,
        nearFadeEnd = b.nearFadeEnd,
    };

    private static void Restore(FogBeacon b, BeaconSave s)
    {
        b.color = s.color;
        b.intensity = s.intensity;
        b.worldRadius = s.worldRadius;
        b.minPixelRadius = s.minPixelRadius;
        b.centreOffset = s.centreOffset;
        b.limitByFacing = s.limitByFacing;
        b.nearFadeStart = s.nearFadeStart;
        b.nearFadeEnd = s.nearFadeEnd;
        b.IntensityScale = 1f;
        b.enabled = s.enabled;
    }

    private void ConfigureBeacon(FogBeacon beacon)
    {
        beacon.intensity = config.AlarmBeaconIntensity;
        beacon.worldRadius = Mathf.Max(0.001f, config.AlarmBeaconRadius);
        beacon.minPixelRadius = config.AlarmBeaconMinPixels;
        beacon.limitByFacing = false;
        beacon.nearFadeStart = config.AlarmBeaconNearFadeStart;
        beacon.nearFadeEnd = config.AlarmBeaconNearFadeEnd;
        beacon.maxDistance = 0f;
    }

    private int CountLamps()
    {
        int count = 0;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] != null) count++;
        }
        return count;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (lamps == null) return;

        int count = CountLamps();
        if (count == 0) return;

        // The same order and grouping Begin() will use: the colour of each gizmo IS its group.
        Light[] sorted = new Light[count];
        int slot = 0;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] != null) sorted[slot++] = lamps[i];
        }
        Array.Sort(sorted, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        for (int i = 0; i < count; i++)
        {
            Gizmos.color = i % 2 == 0 ? new Color(0.8f, 0.1f, 0.1f, 0.9f) : new Color(1f, 0.72f, 0.38f, 0.9f);
            Gizmos.DrawWireSphere(sorted[i].transform.position, 0.3f);
        }
    }
#endif
}
