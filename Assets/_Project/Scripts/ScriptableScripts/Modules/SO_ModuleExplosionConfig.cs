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
             "that exploded. Empty = no visual (the sequence still runs).")]
    [SerializeField] private ExplosionVFX vfxPrefab;

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
