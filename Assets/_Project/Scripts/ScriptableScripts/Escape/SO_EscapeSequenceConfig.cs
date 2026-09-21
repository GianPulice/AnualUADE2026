using UnityEngine;

/// <summary>
/// Every number of the escape sequence that is meant to be tuned in playtest, in one asset: the
/// skip key, the fog / guide-light cycle (Paso 4), the Nemesis's trot (Paso 6) and the audio
/// layers (Paso 7). What is NOT here is the cinematic's timing — shots and beats are clips and
/// markers in the Timeline (open the EscapeSequence object, Window ▸ Sequencing ▸ Timeline).
///
/// Read live: the fog cycle and the pursuit re-read this asset every frame, so dragging a slider
/// in Play mode is visible right away (and, as with any ScriptableObject, it sticks in the
/// editor — note the values you like before leaving Play). The two fog presets are the exception:
/// they are copied when the escape starts.
///
/// Tooltips in Spanish, same convention as SO_VisionFogConfig: this is the designer's file.
/// </summary>
[CreateAssetMenu(fileName = "SO_EscapeSequenceConfig", menuName = "Scriptable Objects/Escape/Escape Sequence Config")]
public class SO_EscapeSequenceConfig : ScriptableObject
{
    // ── Trigger ─────────────────────────────────────────────────────────────
    [Header("Disparo (Paso 1)")]
    [Tooltip("Puzzle que dispara la secuencia al completarse: el hub de los tres núcleos. Si ya " +
             "estaba completo al cargar la escena (partida cargada) la cinemática NO se repite.")]
    [SerializeField, PuzzleId] private string triggerPuzzleId = "puzzle_central_piso1";

    // ── Skip ────────────────────────────────────────────────────────────────
    [Header("Skip")]
    [Tooltip("Deja saltear la cinemática de apertura con la tecla de abajo. El cartel " +
             "'[Press F to skip]' se ve mientras dura.")]
    [SerializeField] private bool skippable = true;

    [SerializeField] private KeyCode skipKey = KeyCode.F;

    [Tooltip("{0} = la tecla de skip.")]
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    // ── Fog cycle (Paso 4) ──────────────────────────────────────────────────
    [Header("Ciclo de fog por puerta (Paso 4) — A DEFINIR EN TESTEO")]
    [Tooltip("Segundos que tarda la luz de la puerta en abrir la niebla, de la puerta hacia el " +
             "jugador.")]
    [SerializeField, Min(0.05f)] private float openSeconds = 1.5f;

    [Tooltip("Segundos que la luz se sostiene abierta (visibilidad y decisión).")]
    [SerializeField, Min(0f)] private float holdSeconds = 4f;

    [Tooltip("Segundos que tarda la niebla en cerrarse cuando la luz se apaga.")]
    [SerializeField, Min(0.05f)] private float closeSeconds = 2f;

    [Tooltip("Segundos con todo cerrado antes de que la luz de la puerta objetivo vuelva a " +
             "prenderse (o antes de que se prenda la próxima puerta, si el jugador ya cruzó la " +
             "actual). Constante en todo el recorrido: el ciclo no escala con la dificultad.")]
    [SerializeField, Min(0f)] private float darkGapSeconds = 1.5f;

    [Tooltip("Distancia horizontal (m) a la puerta objetivo a la que se la da por alcanzada y el " +
             "ciclo pasa a la próxima. En la última puerta (el portón) dispara el evento " +
             "'On Gate Reached' del director, y nada más.")]
    [SerializeField, Min(0.5f)] private float arrivalRadius = 2.5f;

    [Header("Luz que perfora el fog — forma")]
    [Tooltip("Apertura TOTAL del cono de luz (grados), desde la puerta hacia el jugador. Chico = " +
             "sólo el eje puerta-jugador se aclara y las zonas laterales quedan cerradas (lo que " +
             "pide el guion). 0 = esfera: aclara también a los costados.")]
    [SerializeField, Range(0f, 179f)] private float lightConeAngle = 40f;

    [Tooltip("Metros de más sobre la distancia puerta-jugador, para que la luz nunca termine " +
             "justo en el jugador.")]
    [SerializeField, Min(0f)] private float lightReachPadding = 3f;

    [Tooltip("Alcance mínimo de la luz (m), con la niebla abierta del todo.")]
    [SerializeField, Min(0f)] private float lightMinReach = 8f;

    [Tooltip("Alcance máximo de la luz (m). Un tramo más largo que esto queda cubierto sólo hasta acá.")]
    [SerializeField, Min(1f)] private float lightMaxReach = 40f;

    [Header("Luz que perfora el fog — aspecto (Paso 5)")]
    [Tooltip("Color de la luz de emergencia. El guion la nombra 'blanca' en el Paso 4 y el camino " +
             "'ámbar/naranja' en el Paso 5: arranca en ámbar cálido. ROJO NO: está reservado al " +
             "Nemesis.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    [SerializeField] private Color lightColor = new Color(1f, 0.72f, 0.38f);

    [Tooltip("Cuánta luz inyecta en la niebla (el halo). Ver FogLightBypass.intensity.")]
    [SerializeField, Range(0f, 8f)] private float lightFogIntensity = 2f;

    [Tooltip("Cuánta niebla disuelve en el eje de la luz. 1 = la limpia del todo.")]
    [SerializeField, Range(0f, 1f)] private float lightFogClear = 1f;

    [Tooltip("Intensidad de la Light real de la puerta a plena apertura (lo que ilumina de " +
             "verdad la geometría).")]
    [SerializeField, Min(0f)] private float lampIntensity = 6f;

    [Header("Preset de niebla del escape (opcionales)")]
    [Tooltip("Niebla espesa que rige durante todo el escape, entre pulsos de luz. Vacío = no toca " +
             "la niebla global (sólo actúa la luz de las puertas).")]
    [SerializeField] private SO_VisionFogConfig closedFog;

    [Tooltip("Niebla más baja que se aplica mientras la luz está prendida, además del claro que " +
             "abre la luz. Vacío = sólo el claro de la luz.")]
    [SerializeField] private SO_VisionFogConfig openFog;

    // ── Nemesis (Paso 6) ────────────────────────────────────────────────────
    [Header("Nemesis: ritmo de la persecución (Paso 6) — A DEFINIR EN TESTEO")]
    // El ritmo se mide CONTRA el sprint del jugador, no en m/s fijos: 1.0 = va exactamente a tu
    // velocidad de sprint de ese momento. Como es relativo, si un módulo te penaliza la velocidad
    // (M1 piernas, M2 pecho) el Nemesis afloja solo y la persecución se siente igual de justa.
    [Tooltip("Ritmo cuando lo tenés encima (ver 'Pace Near Distance'). Menor a 1 = podés despegarte " +
             "si corrés bien; es el aire para recuperarte de un tropezón.")]
    [SerializeField, Min(0.1f)] private float paceNear = 0.75f;

    [Tooltip("Ritmo cuando está lejos (ver 'Pace Far Distance'). Mayor a 1 = si aflojás, te alcanza.")]
    [SerializeField, Min(0.1f)] private float paceFar = 1.05f;

    [Tooltip("Por debajo de esta distancia (m) usa 'Pace Near'.")]
    [SerializeField, Min(0f)] private float paceNearDistance = 3f;

    [Tooltip("Por encima de esta distancia (m) usa 'Pace Far'. Entre las dos se interpola.")]
    [SerializeField, Min(0f)] private float paceFarDistance = 14f;

    [Tooltip("Piso de velocidad (m/s): por debajo de esto el Nemesis deja de dar miedo aunque el " +
             "jugador esté muy penalizado.")]
    [SerializeField, Min(0f)] private float minChaseSpeed = 1.2f;

    [Tooltip("El Nemesis siempre sabe dónde está el jugador (lo persigue con pathing hacia su " +
             "posición real, sin ruta prefijada) y no baja de Chasing. Apagado: sólo manda lo que " +
             "ve y oye, y puede perderlo en los tramos de fog denso.")]
    [SerializeField] private bool perfectTracking = true;

    [Tooltip("Cada cuántos segundos actualiza lo que sabe de la posición del jugador.")]
    [SerializeField, Min(0.02f)] private float trackingRefreshSeconds = 0.25f;

    // ── Audio (Paso 7) ──────────────────────────────────────────────────────
    [Header("Audio (Paso 7)")]
    [Tooltip("Alarma de instalación. Loop desde que arranca la secuencia hasta que termina.")]
    [SerializeField] private AudioClip alarmClip;
    [SerializeField, Range(0f, 1f)] private float alarmVolume = 0.6f;

    [Tooltip("Capa musical de tensión. Loop desde el mismo momento que la alarma.")]
    [SerializeField] private AudioClip tensionClip;

    [Tooltip("Volumen de la capa de tensión con el Nemesis lejos.")]
    [SerializeField, Range(0f, 1f)] private float tensionMinVolume = 0.25f;

    [Tooltip("Volumen de la capa de tensión con el Nemesis encima (el escalado por cercanía usa " +
             "la misma señal 0..1 que la viñeta roja).")]
    [SerializeField, Range(0f, 1f)] private float tensionMaxVolume = 0.9f;

    [Tooltip("Segundos que tarda la capa de tensión en seguir a la cercanía del Nemesis.")]
    [SerializeField, Min(0.05f)] private float tensionSmoothing = 1.5f;

    [Tooltip("Sonido de traba de cada puerta del pasillo (id de AudioManager).")]
    [SerializeField, SoundId] private string doorLockSoundId = "sfx_interaction_puerta_bloqueada";

    [Tooltip("Segundos entre una puerta y la siguiente al trabarse (secuencia rápida).")]
    [SerializeField, Min(0f)] private float doorLockInterval = 0.12f;

    public string TriggerPuzzleId => triggerPuzzleId;

    public bool Skippable => skippable;
    public KeyCode SkipKey => skipKey;
    public string SkipPromptText => string.Format(skipPromptFormat, skipKey);

    public float OpenSeconds => openSeconds;
    public float HoldSeconds => holdSeconds;
    public float CloseSeconds => closeSeconds;
    public float DarkGapSeconds => darkGapSeconds;
    public float ArrivalRadius => arrivalRadius;

    public float LightConeAngle => lightConeAngle;
    public float LightReachPadding => lightReachPadding;
    public float LightMinReach => lightMinReach;
    public float LightMaxReach => Mathf.Max(lightMaxReach, lightMinReach);
    public Color LightColor => lightColor;
    public float LightFogIntensity => lightFogIntensity;
    public float LightFogClear => lightFogClear;
    public float LampIntensity => lampIntensity;
    public SO_VisionFogConfig ClosedFog => closedFog;
    public SO_VisionFogConfig OpenFog => openFog;

    public float PaceNear => paceNear;
    public float PaceFar => paceFar;
    public float PaceNearDistance => paceNearDistance;
    public float PaceFarDistance => Mathf.Max(paceFarDistance, paceNearDistance + 0.1f);
    public float MinChaseSpeed => minChaseSpeed;
    public bool PerfectTracking => perfectTracking;
    public float TrackingRefreshSeconds => trackingRefreshSeconds;

    public AudioClip AlarmClip => alarmClip;
    public float AlarmVolume => alarmVolume;
    public AudioClip TensionClip => tensionClip;
    public float TensionMinVolume => tensionMinVolume;
    public float TensionMaxVolume => tensionMaxVolume;
    public float TensionSmoothing => tensionSmoothing;
    public string DoorLockSoundId => doorLockSoundId;
    public float DoorLockInterval => doorLockInterval;

}
