using UnityEngine;

/// <summary>
/// Authoring data for <see cref="ModuleExplosionSequence"/>: what an exploding module looks and
/// sounds like, and how the defeat cinematic frames it when that explosion ends the run.
/// </summary>
[CreateAssetMenu(fileName = "SO_ModuleExplosionConfig", menuName = "Scriptable Objects/Modules/Module Explosion Config")]
public class SO_ModuleExplosionConfig : ScriptableObject
{
    [Header("Effect")]
    [Tooltip("Prefab with an ExplosionVFX on its root. Spawned on the body part of the module " +
             "that exploded. Empty = no visual (the sequence still runs).\n\n" +
             "Used by any module with no entry in Per Module below.")]
    [SerializeField] private ExplosionVFX vfxPrefab;

    /// <summary>
    /// How one module's explosion differs from the shared one.
    ///
    /// Two levels on purpose. Most of the difference asked for is "how bloody", which is one
    /// number and needs no extra prefab to maintain; a module that genuinely needs a different
    /// effect (the head) gets its own prefab and ignores the shared one.
    /// </summary>
    [System.Serializable]
    public struct ModuleVariant
    {
        [Tooltip("The module this applies to. Drag the ModuleData asset (M1/M2/M3).")]
        public ModuleData module;

        [Tooltip("Its own explosion prefab. Leave EMPTY to use the shared one above and only " +
                 "change how bloody / how big a bomb it is.")]
        public ExplosionVFX vfxPrefab;

        [Tooltip("\"Bomb\" multiplier for this module: Flash, Sparks, Smoke, Debris — everything " +
                 "except blood. 1 = as authored on the prefab. This is what makes M2/M3 read as a " +
                 "bigger blast, independently of how wet they are.")]
        [Range(0f, 4f)] public float explosionIntensity;

        [Tooltip("Blood multiplier for this module. 1 = as authored on the prefab. Higher is " +
                 "wetter without making the blast itself any bigger.")]
        [Range(0f, 4f)] public float goreIntensity;
    }

    [Tooltip("Per-module overrides. A module with no entry here uses the shared prefab at its " +
             "authored gore level. Modules listed twice resolve to the first entry.")]
    [SerializeField] private ModuleVariant[] perModule = System.Array.Empty<ModuleVariant>();

    [Tooltip("Explosion sound. Empty = silent (the sequence still runs, with a warning).")]
    [SerializeField] private AudioClip sfxClip;

    [Tooltip("Low on purpose: the explosion is meant to be SEEN more than heard.")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.35f;

    [System.Serializable]
    public struct SfxLayer
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
        [Tooltip("Seconds after the explosion. 0 = exactly with it.")]
        [Min(0f)] public float delay;
    }

    [Tooltip("Extra sounds played together with the explosion SFX (the headshot, for instance). " +
             "Each has its own volume and an optional delay. An empty clip is skipped.")]
    [SerializeField] private SfxLayer[] extraSfxLayers = System.Array.Empty<SfxLayer>();

    [Header("Camera shake")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.08f;
    [SerializeField, Min(0f)] private float shakeDuration = 0.35f;
    [SerializeField, Min(0f)] private float shakeFrequency = 28f;

    [Header("Cinematic")]
    [Tooltip("On = every module explosion plays the camera shot, not only the one that ends the run. " +
             "A penalty shot hands control back when it finishes. Off = penalties just play the " +
             "effect in place with a shake; the run-ending shot always plays.")]
    [SerializeField] private bool cinematicOnPenalty = true;

    [Tooltip("Seconds the camera takes to travel from the gameplay framing to the explosion shot.")]
    [SerializeField, Min(0f)] private float cameraMoveDuration = 0.8f;

    [Tooltip("Distance from the body part to the camera, in metres.")]
    [SerializeField, Min(0.3f)] private float cameraDistance = 2.2f;

    [Tooltip("Camera height relative to the body part, in metres.")]
    [SerializeField] private float cameraHeight = 0.35f;

    [Tooltip("Degrees the shot orbits away from dead-front of the player. 0 = straight in front, " +
             "35 = three-quarter view.")]
    [SerializeField, Range(-180f, 180f)] private float cameraYawOffset = 35f;

    [Tooltip("Seconds to hold on the shot after the camera arrives, before the explosion.")]
    [SerializeField, Min(0f)] private float preExplosionHold = 0.25f;

    [Tooltip("Seconds to hold after the effect has fully played, before the GameOver screen (or, " +
             "for a penalty, before the camera goes back to the player).")]
    [SerializeField, Min(0f)] private float postExplosionHold = 0.6f;

    [Tooltip("Upper bound for the wait on the effect, so a looping or misconfigured prefab can " +
             "never keep the GameOver screen from appearing.")]
    [SerializeField, Min(0.1f)] private float maxEffectWait = 4f;

    public ExplosionVFX VfxPrefab => vfxPrefab;

    /// <summary>
    /// The prefab to spawn for <paramref name="module"/>, how big a bomb to make it, and how
    /// bloody.
    ///
    /// Falls back to the shared prefab at 1/1 for a module with no entry, so adding a module never
    /// means remembering to add a variant for it.
    /// </summary>
    public void ResolveExplosion(ModuleData module, out ExplosionVFX prefab, out float explosion, out float gore)
    {
        prefab = vfxPrefab;
        explosion = 1f;
        gore = 1f;

        if (module == null || perModule == null) return;

        foreach (ModuleVariant variant in perModule)
        {
            if (variant.module != module) continue;

            if (variant.vfxPrefab != null) prefab = variant.vfxPrefab;
            explosion = variant.explosionIntensity;
            gore = variant.goreIntensity;
            return;
        }
    }

    public AudioClip SfxClip => sfxClip;
    public float SfxVolume => sfxVolume;
    public SfxLayer[] ExtraSfxLayers => extraSfxLayers;

    public float ShakeAmplitude => shakeAmplitude;
    public float ShakeDuration => shakeDuration;
    public float ShakeFrequency => shakeFrequency;

    public bool CinematicOnPenalty => cinematicOnPenalty;
    public float CameraMoveDuration => cameraMoveDuration;
    public float CameraDistance => cameraDistance;
    public float CameraHeight => cameraHeight;
    public float CameraYawOffset => cameraYawOffset;
    public float PreExplosionHold => preExplosionHold;
    public float PostExplosionHold => postExplosionHold;
    public float MaxEffectWait => maxEffectWait;
}
