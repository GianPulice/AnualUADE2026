using System;
using UnityEngine;

/// <summary>
/// What the Nemesis's entrance in Zona 2 does and when (<see cref="NemesisGateEntrance"/>): the blows on
/// the gate, its fall, the eyes, the walk out. WHERE it happens is the scene's (the camera, the
/// marks, the points on the leaf the blows land on).
///
/// The default timings add up to ~10 s from the cut to the player getting control back: the blows,
/// the fall, and the Nemesis walking out past the camera.
/// </summary>
[CreateAssetMenu(fileName = "SO_NemesisGateEntranceConfig",
                 menuName = "Scriptable Objects/Nemesis/SO_NemesisGateEntranceConfig")]
public class SO_NemesisGateEntranceConfig : ScriptableObject
{
    /// <summary>One blow on the gate from behind.</summary>
    [Serializable]
    public class Blow
    {
        [Tooltip("Segundos desde el golpe anterior (el primero: desde que termina la espera inicial).")]
        [Min(0f)] public float delay = 1.2f;

        [Tooltip("Metros que salta la hoja hacia afuera con el golpe.")]
        [Min(0f)] public float push = 0.05f;

        [Tooltip("Grados que la hoja queda inclinada hacia afuera, para siempre, sumados a los anteriores.")]
        [Min(0f)] public float leanDegrees = 1f;

        [Tooltip("Radio (m) de la abolladura alrededor del punto del golpe.")]
        [Min(0f)] public float dentRadius = 1f;

        [Tooltip("Profundidad (m) de la abolladura en el centro. Se suma a las anteriores.")]
        [Min(0f)] public float dentDepth = 0.15f;

        [Tooltip("Sacudón de la cámara (m) y cuánto dura (s).")]
        [Min(0f)] public float shake = 0.04f;
        [Min(0.05f)] public float shakeSeconds = 0.35f;

        [Tooltip("Volumen del golpe (0..1).")]
        [Range(0f, 1f)] public float volume = 0.8f;
    }

    [Header("Cuándo arranca")]
    [Tooltip("Segundos después de agarrar el objeto de la caja antes de cortar a la cámara.")]
    [SerializeField, Min(0f)] private float startDelay = 0.6f;

    [Tooltip("Segundos máximos esperando que la pantalla esté libre (otra cinemática, pausa, " +
             "escondido…). Si no se libera, se saltea la cinemática y el Nemesis queda igual afuera.")]
    [SerializeField, Min(0f)] private float maxWaitForScreen = 10f;

    [Header("Saltear")]
    [SerializeField] private bool skippable = true;
    [SerializeField] private KeyCode skipKey = KeyCode.F;
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    [Header("Plano")]
    [Tooltip("Segundos de portón quieto, en silencio, antes del primer golpe.")]
    [SerializeField, Min(0f)] private float holdBefore = 1f;

    [Tooltip("Niebla mientras está el plano (opcional): una que abra la vista del vestíbulo. " +
             "Vacío = la niebla del gameplay, medida desde la cámara.")]
    [SerializeField] private SO_VisionFogConfig shotFog;

    [Header("Golpes")]
    [Tooltip("Los golpes, en orden. El último es el que tira el portón. Cada uno cae en el punto de " +
             "la hoja con el mismo número en la escena (Hit Points).")]
    [SerializeField] private Blow[] blows =
    {
        new Blow { delay = 0f,   push = 0.04f, leanDegrees = 0.6f, dentRadius = 0.9f, dentDepth = 0.12f, shake = 0.03f, shakeSeconds = 0.3f,  volume = 0.7f },
        new Blow { delay = 1.2f, push = 0.07f, leanDegrees = 1.4f, dentRadius = 1.1f, dentDepth = 0.2f,  shake = 0.05f, shakeSeconds = 0.35f, volume = 0.85f },
        new Blow { delay = 1.2f, push = 0.1f,  leanDegrees = 0f,   dentRadius = 1.3f, dentDepth = 0.3f,  shake = 0.07f, shakeSeconds = 0.35f, volume = 1f },
    };

    [Tooltip("El golpe metálico contra el portón.")]
    [SerializeField] private AudioClip blowClip;

    [Header("El portón sale volando")]
    [Tooltip("Segundos entre el último golpe (la abolladura final) y que la hoja salga volando.")]
    [SerializeField, Min(0f)] private float knockOffDelay = 0.2f;

    [Tooltip("La hoja contra el piso. Suena en el primer golpe fuerte de la caída horneada " +
             "(SO_GateFallTrack, en GateKnockOff).")]
    [SerializeField] private AudioClip landClip;
    [SerializeField, Range(0f, 1f)] private float landVolume = 1f;

    [Tooltip("Sacudón de la cámara (m) cuando cae, y cuánto dura (s).")]
    [SerializeField, Min(0f)] private float landShake = 0.12f;
    [SerializeField, Min(0.05f)] private float landShakeSeconds = 0.6f;

    [Header("El Nemesis")]
    [Tooltip("Segundos desde que cae la hoja (y aparece él, entre el polvo) hasta que se le prenden " +
             "los ojos.")]
    [SerializeField, Min(0f)] private float settleSeconds = 0.8f;

    [Tooltip("Id del SO_SoundData que suena cuando se le prenden los ojos.")]
    [SerializeField] private string appearSoundId = "sfx_nemesis_activacion";

    [Tooltip("Segundos entre los ojos y que empiece a caminar.")]
    [SerializeField, Min(0f)] private float eyesToWalkSeconds = 0.2f;

    [Tooltip("Segundos máximos de la caminata; si para entonces no pasó la cámara, corta igual.")]
    [SerializeField, Min(0.5f)] private float walkMaxSeconds = 6f;

    [Tooltip("Segundos después de que pasa al lado de la cámara antes de cortar de vuelta al jugador.")]
    [SerializeField, Min(0f)] private float holdEnd = 0.6f;

    [Header("Ojos (flare)")]
    [Tooltip("Más chico que el del escape: ahí es un primer plano con teleobjetivo, acá el Nemesis " +
             "está a unos metros en un plano general, y con esos valores los ojos eran una bola blanca.")]
    [SerializeField, Min(1f)] private float eyesFlarePower = 2.5f;
    [SerializeField, Min(0f)] private float eyesFlareCoreRadius = 12f;
    [SerializeField, Min(0f)] private float eyesFlareHaloIntensity = 0.5f;
    [SerializeField, Min(0f)] private float eyesFlareHaloRadius = 45f;
    [SerializeField, Min(0f)] private float eyesFlareRiseSeconds = 0.25f;
    [SerializeField, Min(0f)] private float eyesFlareHoldSeconds = 0.6f;
    [SerializeField, Min(0f)] private float eyesFlareFallSeconds = 0.7f;

    public float StartDelay => startDelay;
    public float MaxWaitForScreen => maxWaitForScreen;

    public bool Skippable => skippable;
    public KeyCode SkipKey => skipKey;
    public string SkipPromptText => string.Format(skipPromptFormat, skipKey);

    public float HoldBefore => holdBefore;
    public SO_VisionFogConfig ShotFog => shotFog;

    public Blow[] Blows => blows ?? Array.Empty<Blow>();
    public AudioClip BlowClip => blowClip;

    public float KnockOffDelay => knockOffDelay;
    public AudioClip LandClip => landClip;
    public float LandVolume => landVolume;
    public float LandShake => landShake;
    public float LandShakeSeconds => landShakeSeconds;

    public float SettleSeconds => settleSeconds;
    public string AppearSoundId => appearSoundId;
    public float EyesToWalkSeconds => eyesToWalkSeconds;
    public float WalkMaxSeconds => walkMaxSeconds;
    public float HoldEnd => holdEnd;

    public float EyesFlarePower => eyesFlarePower;
    public float EyesFlareCoreRadius => eyesFlareCoreRadius;
    public float EyesFlareHaloIntensity => eyesFlareHaloIntensity;
    public float EyesFlareHaloRadius => eyesFlareHaloRadius;
    public float EyesFlareRiseSeconds => eyesFlareRiseSeconds;
    public float EyesFlareHoldSeconds => eyesFlareHoldSeconds;
    public float EyesFlareFallSeconds => eyesFlareFallSeconds;
}
