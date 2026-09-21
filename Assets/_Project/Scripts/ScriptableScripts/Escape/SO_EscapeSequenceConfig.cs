using UnityEngine;

/// <summary>
/// Every number of the escape sequence that is meant to be tuned in playtest, in one asset: the
/// skip key, the white-light / fog cycle (Paso 4), the amber path lights (Paso 5), the Nemesis's
/// trot (Paso 6) and the audio layers (Paso 7). What is NOT here is the cinematic's timing — shots and beats are clips and
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
    [Header("Ciclo de luces blancas y fog (Paso 4) — A DEFINIR EN TESTEO")]
    [Tooltip("Segundos que tardan las luces blancas del pasillo en prenderse y la niebla en abrirse " +
             "(con lerp, no de golpe).")]
    [SerializeField, Min(0.05f)] private float openSeconds = 1.5f;

    [Tooltip("Segundos con las luces blancas prendidas y la niebla abierta: se ve el nivel, y " +
             "también al Nemesis. Ése es el costo.")]
    [SerializeField, Min(0f)] private float holdSeconds = 4f;

    [Tooltip("Segundos que tardan las luces blancas en apagarse y la niebla en volver a cerrarse.")]
    [SerializeField, Min(0.05f)] private float closeSeconds = 2f;

    [Tooltip("Segundos a oscuras con la niebla cerrada antes de que vuelvan las luces blancas. Lo " +
             "único que se ve son las luces ámbar del camino. Constante en todo el recorrido: el " +
             "ciclo no escala con la dificultad.")]
    [SerializeField, Min(0f)] private float darkGapSeconds = 1.5f;

    [Tooltip("Distancia horizontal (m) a la que una puerta del recorrido se da por alcanzada. En " +
             "la última (el portón) dispara el evento 'On Gate Reached' del director, y nada más.")]
    [SerializeField, Min(0.5f)] private float arrivalRadius = 2.5f;

    [Header("Luces ámbar del camino (Paso 5)")]
    [Tooltip("Color de las luces del camino al portón: fijas en cada puerta del recorrido, " +
             "prendidas todo el escape. ROJO NO: está reservado al Nemesis.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    [SerializeField] private Color lightColor = new Color(1f, 0.72f, 0.38f);

    [Tooltip("Radio (m) del halo de cada luz en la niebla. Chico: tiene que leerse como una " +
             "lámpara, no como un agujero en el fog.")]
    [SerializeField, Min(0f)] private float lightRadius = 3f;

    [Tooltip("Cuánta luz inyecta en la niebla (el halo). Ver FogLightBypass.intensity.")]
    [SerializeField, Range(0f, 8f)] private float lightFogIntensity = 2f;

    [Tooltip("Cuánta niebla disuelve alrededor de la lámpara. Bajo = el halo brilla y el entorno " +
             "sigue en niebla; 1 = limpia la esfera entera.")]
    [SerializeField, Range(0f, 1f)] private float lightFogClear = 0.35f;

    [Tooltip("Brillo del punto de la lámpara que atraviesa cualquier niebla, a cualquier distancia " +
             "(FogBeacon). Es lo que hace que el camino se vea con el fog cerrado. 0 = sin punto.")]
    [SerializeField, Min(0f)] private float beaconIntensity = 3f;

    [Tooltip("Intensidad de la Light real de cada puerta (lo que ilumina de verdad la geometría). " +
             "También es la de las lámparas del pasillo que arrancan apagadas en la escena.")]
    [SerializeField, Min(0f)] private float lampIntensity = 6f;

    [Header("Preset de niebla del escape (opcionales)")]
    [Tooltip("Niebla espesa que rige durante todo el escape, con las luces blancas apagadas. " +
             "Vacío = no toca la niebla global.")]
    [SerializeField] private SO_VisionFogConfig closedFog;

    [Tooltip("Niebla más baja que se aplica mientras las luces blancas están prendidas. Vacío = la " +
             "niebla no cambia con las luces.")]
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

    // ── Portón del final ────────────────────────────────────────────────────
    [Header("Portón del final — A DEFINIR EN TESTEO")]
    // El portón arranca a abrirse cuando volvés a tener el control, y tarda lo que tardarías en
    // llegar a él corriendo en línea recta desde Player_Spot con TU sprint de ese momento (módulos
    // incluidos). Este margen se suma a ese tiempo.
    [Tooltip("Segundos que se suman al tiempo de carrera hasta el portón. Positivo = termina de " +
             "abrirse un poco después de que llegás (la reacción al recuperar el control, la " +
             "aceleración, esquivar). Negativo = ya está abierto cuando llegás.")]
    [SerializeField] private float endGateSlackSeconds = 0.5f;

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

    public Color LightColor => lightColor;
    public float LightRadius => lightRadius;
    public float LightFogIntensity => lightFogIntensity;
    public float LightFogClear => lightFogClear;
    public float BeaconIntensity => beaconIntensity;
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

    public float EndGateSlackSeconds => endGateSlackSeconds;

    public AudioClip AlarmClip => alarmClip;
    public float AlarmVolume => alarmVolume;
    public AudioClip TensionClip => tensionClip;
    public float TensionMinVolume => tensionMinVolume;
    public float TensionMaxVolume => tensionMaxVolume;
    public float TensionSmoothing => tensionSmoothing;
    public string DoorLockSoundId => doorLockSoundId;
    public float DoorLockInterval => doorLockInterval;

}
