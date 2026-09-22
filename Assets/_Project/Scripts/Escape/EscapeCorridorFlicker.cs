using System;
using UnityEngine;

/// <summary>
/// The corridor's own lamps failing while the escape runs (Paso 4/5). One job: make a set of
/// <see cref="Light"/>s flicker like dying fluorescent tubes, as a wave that travels down the
/// corridor. They flicker from the alarm to the end of the escape and nothing dims them: the fog is
/// what opens and closes (<see cref="EscapeFogCycle"/>), the lamps just keep failing under it.
///
/// Each lamp's flicker is three things on top of each other, none of them a clean sine:
///   1. one Perlin channel SHARED by the whole corridor, which every lamp reads with its own
///      delay — the nervous jitter of a bad tube, travelling — mixed with a lighter channel on a
///      seed of the lamp's own, so that no two tubes look like copies of each other;
///   2. a slow gate channel that, over a threshold, replaces the jitter with a hard square wave:
///      the brutal on / off stutter of a tube trying to strike. It rides the same delayed clock,
///      so the burst runs down the corridor instead of hitting every lamp at once;
///   3. a short blackout, rolled per lamp and per second: that one tube simply goes out.
/// The delay is the lamps' own X: they are sorted west to east in <see cref="Begin"/> and each one
/// gets (its x - the westmost x) / wave speed seconds, so the failure runs -X to +X, the same way
/// the player runs towards the gate. Nothing is random per frame except the blackout roll, so the
/// look is the same from run to run.
///
/// A lamp's base intensity is what it had when <see cref="Begin"/> was called, and <see cref="End"/>
/// puts intensity and enabled back to that. A lamp sitting at 0 (off in the scene until the escape)
/// falls back to the config's lamp intensity — the same number the guide doors use — because there
/// would be nothing to multiply otherwise.
///
/// How to use it: put it on the escape rig, drag the corridor Lights into 'lamps' (order does not
/// matter, it sorts them), and have the director call <see cref="Begin"/> when the escape starts
/// and <see cref="End"/> when it ends, next to <see cref="EscapeFogCycle"/>'s own Begin / End.
///
/// The fog cycle used to power them too, so they died every time the fog closed (WIR-038). They
/// read better failing on their own while the fog breathes, so that link is gone.
///
/// Left out on purpose: it does not decide when the escape starts (the director), does not touch
/// the fog or the guide doors, does not move or spawn anything, and does not touch the lamps'
/// colour — the corridor's colour is the scene's, and the emergency amber belongs to the guide doors.
/// </summary>
[DisallowMultipleComponent]
public class EscapeCorridorFlicker : MonoBehaviour
{
    [Tooltip("Las luces del pasillo que titilan. El orden no importa: se ordenan solas por X (de " +
             "oeste a este) para armar la ola. Vacío = no hace nada.")]
    [SerializeField] private Light[] lamps = Array.Empty<Light>();

    [Header("Titileo — A DEFINIR EN TESTEO")]
    [Tooltip("Velocidad del titileo: qué tan nervioso es el ruido de cada tubo. Bajo = respira, " +
             "alto = zumba.")]
    [SerializeField, Range(0.5f, 30f)] private float flickerSpeed = 9f;

    [Tooltip("Cuán marcado es el titileo. 0 = la lámpara queda fija; 1 = en los valles se apaga " +
             "del todo.")]
    [SerializeField, Range(0f, 1f)] private float flickerDepth = 0.85f;

    [Tooltip("Velocidad (m/s) a la que la ola de titileo recorre el pasillo, de la lámpara más al " +
             "oeste (-X) a la más al este (+X). Baja = se ve pasar lámpara por lámpara; muy alta = " +
             "titilan casi todas juntas.")]
    [SerializeField, Min(0.1f)] private float waveSpeed = 14f;

    [Header("Apagones")]
    [Tooltip("Probabilidad POR SEGUNDO de que una lámpara se apague del todo un instante. 0 = no " +
             "se apagan nunca.")]
    [SerializeField, Range(0f, 1f)] private float blackoutChance = 0.25f;

    [Tooltip("Cuánto dura ese apagón (s). Cada lámpara le suma una variación de ±40%, así no " +
             "vuelven todas igual.")]
    [SerializeField, Range(0.02f, 1.5f)] private float blackoutSeconds = 0.18f;

    // Perlin hugs 0.5 and mirrors around 0: the origin keeps every sample positive, and the two
    // bounds stretch the useful middle of its range out to a full 0..1 swing.
    private const float NoiseOrigin = 64f;
    private const float NoiseLow = 0.25f;
    private const float NoiseHigh = 0.75f;

    // The two shared lines of the noise field. Shared is the whole point: every lamp reads the
    // SAME pattern, only delayed, and that is what makes the failure travel instead of each tube
    // doing something of its own.
    private const float WaveSeed = 12.9f;
    private const float GateSeed = 71.3f;

    // How much of a lamp's own jitter is mixed into the travelling wave, and how much faster that
    // own channel runs. Enough to break the clone look, not enough to lose the wave.
    private const float OwnWeight = 0.35f;
    private const float OwnSpeedFactor = 1.7f;

    // The stutter channel: slow enough to decide WHEN the corridor starts striking, plus the rate
    // of the square wave it strikes at.
    private const float StutterChannelSpeed = 0.35f;
    private const float StutterThreshold = 0.72f;
    private const float StutterHz = 14f;

    private const float BlackoutSpread = 0.4f;
    private const float OffThreshold = 0.01f;

    private struct LampState
    {
        public Light light;
        public float anchorX;
        public float baseIntensity;
        public bool wasEnabled;
        public bool wasActive;
        public float waveDelay;
        public float seed;
        public float blackoutLeft;
    }

    private LampState[] states = Array.Empty<LampState>();
    private float elapsed;

    /// <summary>The corridor is flickering.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Starts the flicker, remembering how every lamp was. Safe to call twice.</summary>
    public void Begin(SO_EscapeSequenceConfig config)
    {
        // A second Begin without an End would capture an already flickering intensity as the base.
        if (IsRunning) End();

        int count = CountLamps();
        if (count == 0)
        {
            Debug.LogWarning($"[{nameof(EscapeCorridorFlicker)}] No lamps assigned: the corridor " +
                             "stays as dark as the guide door leaves it.", this);
            return;
        }

        float fallback = config != null ? config.LampIntensity : 0f;

        states = new LampState[count];
        int slot = 0;
        for (int i = 0; i < lamps.Length; i++)
        {
            Light lamp = lamps[i];
            if (lamp == null) continue;

            states[slot++] = new LampState
            {
                light = lamp,
                anchorX = lamp.transform.position.x,
                baseIntensity = lamp.intensity > OffThreshold ? lamp.intensity : fallback,
                wasEnabled = lamp.enabled,
                wasActive = lamp.gameObject.activeSelf,
            };
        }

        // They sleep as inactive objects outside the escape (see LampState.wasActive): wake them.
        for (int i = 0; i < states.Length; i++)
            if (states[i].light != null) states[i].light.gameObject.SetActive(true);

        // West to east: the failure travels the way the player runs, towards the gate.
        Array.Sort(states, (a, b) => a.anchorX.CompareTo(b.anchorX));

        float westmost = states[0].anchorX;
        for (int i = 0; i < states.Length; i++)
        {
            states[i].waveDelay = (states[i].anchorX - westmost) / waveSpeed;

            // Its own place in the noise field, so no two tubes ever fail alike.
            states[i].seed = i * 17.37f + 3.11f;
            states[i].blackoutLeft = 0f;
        }

        elapsed = 0f;
        IsRunning = true;
    }

    /// <summary>Stops the flicker: every lamp back to the intensity and enabled it had.</summary>
    public void End()
    {
        IsRunning = false;

        for (int i = 0; i < states.Length; i++)
        {
            Light lamp = states[i].light;
            if (lamp == null) continue;

            lamp.intensity = states[i].baseIntensity;
            lamp.enabled = states[i].wasEnabled;
            lamp.gameObject.SetActive(states[i].wasActive);
        }

        states = Array.Empty<LampState>();
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!IsRunning) return;

        float dt = Time.deltaTime;
        elapsed += dt;

        for (int i = 0; i < states.Length; i++)
        {
            Light lamp = states[i].light;
            if (lamp == null) continue;

            float factor = Tick(ref states[i], dt);
            bool on = factor > OffThreshold && states[i].baseIntensity > OffThreshold;

            lamp.enabled = on;
            if (on) lamp.intensity = states[i].baseIntensity * factor;
        }
    }

    /// <summary>Advances one lamp's blackout and returns its 0..1 factor for this frame.</summary>
    private float Tick(ref LampState state, float dt)
    {
        if (state.blackoutLeft > 0f)
        {
            // A blackout wins over the noise: the tube is simply out.
            state.blackoutLeft -= dt;
            return 0f;
        }

        if (blackoutChance > 0f && UnityEngine.Random.value < blackoutChance * dt)
        {
            state.blackoutLeft = blackoutSeconds *
                                 UnityEngine.Random.Range(1f - BlackoutSpread, 1f + BlackoutSpread);
            return 0f;
        }

        // The wave: this lamp reads the corridor's shared pattern that many seconds late.
        float local = elapsed - state.waveDelay;
        float t = local * flickerSpeed + NoiseOrigin;

        float wave = Mathf.PerlinNoise(WaveSeed, t);
        wave = Mathf.InverseLerp(NoiseLow, NoiseHigh, wave);

        // Its own channel, on its own seed and NOT delayed, so no two tubes look like copies.
        float own = Mathf.PerlinNoise(state.seed, elapsed * flickerSpeed * OwnSpeedFactor + NoiseOrigin);
        own = Mathf.InverseLerp(NoiseLow, NoiseHigh, own);

        float jitter = Mathf.Lerp(wave, own, OwnWeight);

        float gate = Mathf.PerlinNoise(GateSeed, t * StutterChannelSpeed);
        if (gate > StutterThreshold)
        {
            // Striking: hard on / off instead of a dip, which is what sells a failing tube. It
            // rides the delayed clock too, so the burst runs down the corridor lamp by lamp.
            jitter = Mathf.Repeat(local * StutterHz, 1f) < 0.5f ? 1f : 0f;
        }

        return Mathf.Lerp(1f - flickerDepth, 1f, jitter);
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

        // The same order Begin() will use, so the labels ARE the order of the wave.
        Light[] sorted = new Light[count];
        int slot = 0;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] != null) sorted[slot++] = lamps[i];
        }
        Array.Sort(sorted, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        Gizmos.color = new Color(1f, 0.92f, 0.65f, 0.9f);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = sorted[i].transform.position;

            Gizmos.DrawWireSphere(position, 0.25f);
            UnityEditor.Handles.Label(position + Vector3.up * 0.4f, $"{i + 1}");

            // The line is the path of the wave, west to east.
            if (i > 0) Gizmos.DrawLine(sorted[i - 1].transform.position, position);
        }
    }
#endif
}
