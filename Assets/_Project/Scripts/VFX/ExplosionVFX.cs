using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root of an explosion prefab (Prefabs/VFX/ModuleExplosion). Every piece of the effect is a
/// child Particle System with a readable name (Flash, Sparks, Smoke, Debris) plus an optional
/// point Light, and this component scales the whole thing from a handful of knobs so tuning it
/// does not mean opening each Particle System module by hand.
///
/// What each knob touches:
///   - Intensity: every burst count and the flash light's peak. 0.5 = half the particles.
///   - Size:      the root's scale. Children use Hierarchy scaling, so shapes, speeds and
///                particle sizes all follow it.
///   - Tint:      multiplies each child's authored Start Color (Color and Two Colors modes).
///   - Sim speed: playback speed of every child. Above 1 = faster, punchier.
///
/// Per-piece tweaks (spark count vs smoke count, colours per layer) are still done on the child
/// itself: the knobs read what the child has authored and multiply on top of it.
///
/// The knobs are applied to a spawned COPY, never to the prefab: the runtime always plays a fresh
/// instance, and the editor Preview (right-click the component > Preview) builds a hidden
/// throwaway clone and simulates it in the Scene view. Tuning the knobs can therefore never
/// overwrite the authored burst counts or colours.
/// </summary>
[DisallowMultipleComponent]
public class ExplosionVFX : MonoBehaviour
{
    [Header("Global knobs")]
    [Tooltip("Multiplies every burst count and the flash light's intensity.")]
    [SerializeField, Range(0f, 3f)] private float intensity = 1f;

    [Tooltip("Multiplies the burst counts of the blood layers ONLY (BloodSpray, BloodMist, " +
             "BloodChunks), on top of Intensity. 1 = as authored.\n\n" +
             "Separate from Intensity because the two mean different things: Intensity is how big " +
             "the blast is, and raising it for more blood drags sparks, smoke and debris up with " +
             "it. This is how WET it is, and it is what the per-module variants move so the head " +
             "reads as worse than the legs without the explosion itself changing size.")]
    [SerializeField, Range(0f, 3f)] private float goreIntensity = 1f;

    [Tooltip("Uniform scale of the whole effect.")]
    [SerializeField, Range(0.1f, 5f)] private float size = 1f;

    [Tooltip("Multiplies the authored Start Color of every child. White = as authored.")]
    [SerializeField] private Color tint = Color.white;

    [Tooltip("Playback speed of every child system.")]
    [SerializeField, Range(0.25f, 3f)] private float simulationSpeed = 1f;

    [Header("Flash light")]
    [Tooltip("Optional point light that pops on at Play and dies out. Leave empty for none.")]
    [SerializeField] private Light flashLight;

    [SerializeField, Min(0f)] private float flashPeakIntensity = 6f;

    [Tooltip("Seconds the light takes to die out.")]
    [SerializeField, Min(0.01f)] private float flashDuration = 0.12f;

    [Header("Lifecycle")]
    [Tooltip("Destroys the object once every child has finished. Leave on for spawned instances.")]
    [SerializeField] private bool destroyWhenDone = true;

    private ParticleSystem[] systems;
    private float flashElapsed = -1f;
    private float finishTime = -1f;

    /// <summary>Seconds from Play until the last particle is gone, at the current speed.</summary>
    public float Duration
    {
        get
        {
            ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);

            float longest = flashLight != null ? flashDuration : 0f;
            foreach (ParticleSystem ps in all)
            {
                ParticleSystem.MainModule main = ps.main;
                float life = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
                longest = Mathf.Max(longest, life);
            }

            return longest / Mathf.Max(simulationSpeed, 0.01f);
        }
    }

    /// <summary>
    /// Names of the child systems <see cref="goreIntensity"/> applies to. They are built under
    /// exactly these names by Tools/VFX/Module Explosion — renaming one there means renaming it
    /// here, and the effect fails silently (no blood scaling) rather than erroring if they drift.
    /// </summary>
    private static readonly string[] GoreSystems = { "BloodSpray", "BloodMist", "BloodChunks" };

    /// <summary>
    /// Overrides <see cref="goreIntensity"/> before <see cref="Play"/>. Meant for a spawned
    /// instance, so the per-module variant can be dialled in without one prefab per module.
    /// Does nothing once the knobs have been applied.
    /// </summary>
    public void SetGore(float value)
    {
        goreIntensity = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Overrides <see cref="intensity"/> before <see cref="Play"/> — the "how big a bomb" knob:
    /// Flash, Sparks, Smoke and Debris, everything except the blood layers. Same use as
    /// <see cref="SetGore"/>: the per-module variant dials this in on a spawned instance.
    /// Unclamped above the inspector's 0-3 preview range on purpose — a module variant is allowed
    /// to ask for more punch than the slider shows, same as SetGore.
    /// </summary>
    public void SetIntensity(float value)
    {
        intensity = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Applies the knobs to THIS object and plays it. Meant for a spawned instance: calling it on
    /// the prefab asset itself would bake the multipliers into the authored values.
    /// </summary>
    public void Play()
    {
        ApplyKnobs();

        foreach (ParticleSystem ps in systems)
        {
            ps.Clear(false);
            ps.Play(false);
        }

        StartFlash();
        finishTime = Time.time + Duration;
    }

    private void Update()
    {
        TickFlash(Time.deltaTime);

        if (destroyWhenDone && finishTime > 0f && Time.time >= finishTime)
        {
            Destroy(gameObject);
        }
    }

    // Guarded so a second Play on the same instance does not multiply twice.
    private bool knobsApplied;

    private void ApplyKnobs()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        if (knobsApplied) return;
        knobsApplied = true;

        transform.localScale *= size;

        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpeed *= simulationSpeed;

            // Hierarchy so the root's scale reaches shapes and particle sizes, not just positions.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            float burstScale = intensity * (IsGoreSystem(ps) ? goreIntensity : 1f);

            ParticleSystem.EmissionModule emission = ps.emission;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                ParticleSystem.MinMaxCurve count = burst.count;
                // constantMin and constantMax only. MinMaxCurve.constant is not a third value —
                // it is an alias of constantMax — so also scaling it squared every multiplier:
                // the authored Intensity of 2 was really multiplying by 4, and a gore dial of 1.8
                // was landing at 3.24. The authored values are compensated for the fix, so what
                // is on screen today does not change.
                count.constantMin *= burstScale;
                count.constantMax *= burstScale;
                burst.count = count;
                emission.SetBurst(i, burst);
            }

            ParticleSystem.MinMaxGradient color = main.startColor;
            switch (color.mode)
            {
                case ParticleSystemGradientMode.Color:
                    color.color *= tint;
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    color.colorMin *= tint;
                    color.colorMax *= tint;
                    break;
            }
            main.startColor = color;
        }
    }

    private static bool IsGoreSystem(ParticleSystem ps)
    {
        foreach (string gore in GoreSystems)
        {
            if (ps.name == gore) return true;
        }

        return false;
    }

    private void StartFlash()
    {
        if (flashLight == null) return;
        flashLight.enabled = true;
        flashLight.intensity = flashPeakIntensity * intensity;
        flashElapsed = 0f;
    }

    private void TickFlash(float deltaTime)
    {
        if (flashLight == null || flashElapsed < 0f) return;

        flashElapsed += deltaTime * simulationSpeed;
        float t = Mathf.Clamp01(flashElapsed / flashDuration);

        // Squared falloff: bright pop, quick death. A linear fade reads as a lamp dimming.
        flashLight.intensity = flashPeakIntensity * intensity * (1f - t) * (1f - t);

        if (t >= 1f)
        {
            flashLight.enabled = false;
            flashElapsed = -1f;
        }
    }

#if UNITY_EDITOR
    // ── Editor preview ───────────────────────────────────────────────────────────────────

    private static ExplosionVFX previewClone;
    private static double previewLastTime;
    private static float previewElapsed;

    [ContextMenu("Preview")]
    private void Preview()
    {
        if (Application.isPlaying)
        {
            ExplosionVFX runtimeCopy = Instantiate(this, transform.position, transform.rotation);
            runtimeCopy.destroyWhenDone = true;
            runtimeCopy.Play();
            return;
        }

        StopPreview();

        previewClone = Instantiate(this, transform.position, transform.rotation);
        previewClone.gameObject.hideFlags = HideFlags.HideAndDontSave;
        previewClone.destroyWhenDone = false;
        previewClone.ApplyKnobs();
        previewClone.StartFlash();

        foreach (ParticleSystem ps in previewClone.systems)
            ps.Simulate(0f, false, true);

        previewElapsed = 0f;
        previewLastTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update += TickPreview;
    }

    private static void TickPreview()
    {
        if (previewClone == null)
        {
            StopPreview();
            return;
        }

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = (float)(now - previewLastTime);
        previewLastTime = now;
        previewElapsed += dt;

        // Incremental, restart = false: re-simulating from zero every tick would reseed the
        // random values and the particles would jitter from frame to frame.
        foreach (ParticleSystem ps in previewClone.systems)
            if (ps != null) ps.Simulate(dt, false, false);

        previewClone.TickFlash(dt);
        UnityEditor.SceneView.RepaintAll();

        if (previewElapsed >= previewClone.Duration + 0.1f) StopPreview();
    }

    private static void StopPreview()
    {
        UnityEditor.EditorApplication.update -= TickPreview;
        if (previewClone != null) DestroyImmediate(previewClone.gameObject);
        previewClone = null;
    }
#endif
}
