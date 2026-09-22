using UnityEngine;

/// <summary>
/// Every number of the escape sequence that is meant to be tuned in playtest, in one asset: the
/// skip key, the fog cycle (Paso 4), the amber path lights (Paso 5), the Nemesis's reveal in the
/// corridor, its trot (Paso 6), the chase restart after a capture, the gate slamming shut at the
/// end, and the audio layers (Paso 7). What is NOT here is the opening's timing — its shot and
/// beats are a clip and markers in the Timeline (open the EscapeSequence object, Window ▸
/// Sequencing ▸ Timeline).
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
    [Tooltip("Deja saltear las cinemáticas del escape (la apertura, la aparición del Nemesis en el " +
             "pasillo y el portón del final) con la tecla de abajo. El cartel '[Press F to skip]' " +
             "se ve mientras duran.")]
    [SerializeField] private bool skippable = true;

    [SerializeField] private KeyCode skipKey = KeyCode.F;

    [Tooltip("{0} = la tecla de skip.")]
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    // ── Fog cycle (Paso 4) ──────────────────────────────────────────────────
    // Sólo la niebla: las luces del pasillo titilan todo el escape por su cuenta
    // (EscapeCorridorFlicker) y no se apagan con ella.
    [Header("Ciclo de la niebla (Paso 4) — A DEFINIR EN TESTEO")]
    [Tooltip("Segundos que tarda la niebla en expandirse / abrirse (con lerp, no de golpe).")]
    [SerializeField, Min(0.05f)] private float openSeconds = 1.5f;

    [Tooltip("Segundos con la niebla abierta: se ve el nivel, y también al Nemesis. Ése es el costo.")]
    [SerializeField, Min(0f)] private float holdSeconds = 4f;

    [Tooltip("Segundos que tarda la niebla en contraerse / volver a cerrarse.")]
    [SerializeField, Min(0.05f)] private float closeSeconds = 2f;

    [Tooltip("Segundos con la niebla cerrada antes de que se vuelva a abrir. Lo único que se ve de " +
             "lejos son las luces ámbar del camino. Constante en todo el recorrido: el ciclo no " +
             "escala con la dificultad.")]
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
    [Tooltip("Niebla espesa que rige durante todo el escape: entra con la aparición del Nemesis (se " +
             "le ven los ojos a través de ella) y es a la que vuelve el ciclo cada vez que se " +
             "cierra. Vacío = no toca la niebla global.")]
    [SerializeField] private SO_VisionFogConfig closedFog;

    [Tooltip("Niebla más baja que se aplica mientras el ciclo está abierto. También es la que se " +
             "abre cuando el Nemesis carga en la aparición. Vacío = la niebla no respira.")]
    [SerializeField] private SO_VisionFogConfig openFog;

    // ── Nemesis reveal in the corridor ──────────────────────────────────────
    [Header("Aparición del Nemesis en el pasillo — A DEFINIR EN TESTEO")]
    // Se dispara al salir por la puerta del centro (entrar al RevealTrigger y alejarse de la hoja
    // de la puerta). La puerta de la zona segura se cierra y se traba atrás tuyo: no hay vuelta
    // atrás. La niebla se cierra, el jugador se da vuelta despacio con su propia cámara y ve los
    // ojos del Nemesis en la niebla; el Nemesis carga mientras la niebla se abre, y el control
    // vuelve con él todavía corriendo, el jugador mirándolo.
    [Tooltip("Segundos entre el portazo de la zona segura y que el jugador empiece a darse vuelta.")]
    [SerializeField, Min(0f)] private float revealTurnDelay = 0.5f;

    [Tooltip("Segundos que tarda el jugador en darse vuelta hacia el Nemesis. Más = más lento, más " +
             "tenso.")]
    [SerializeField, Min(0.1f)] private float revealTurnSeconds = 2.2f;

    [Tooltip("Segundos mirándole los ojos en la niebla, ya dado vuelta, antes de que arranque a " +
             "correr hacia vos.")]
    [SerializeField, Min(0f)] private float revealStareSeconds = 1f;

    [Tooltip("A esta distancia (m) del jugador se devuelve el control, con el Nemesis corriendo. " +
             "Recuperás el control mirándolo, así que hay que darse vuelta para correr: no la " +
             "bajes demasiado.")]
    [SerializeField, Min(1f)] private float revealHandoffDistance = 8f;

    [Tooltip("Tope (s) de toda la escena: si por lo que sea no llega a esa distancia, el control " +
             "vuelve igual.")]
    [SerializeField, Min(0.5f)] private float revealMaxSeconds = 9f;

    [Tooltip("Sonido al abrir el plano (id de AudioManager), en la posición del Nemesis. Vacío = " +
             "ninguno.")]
    [SerializeField, SoundId] private string revealSoundId = "sfx_nemesis_activacion";

    // ── Capture during the chase ────────────────────────────────────────────
    [Header("Si te agarra: la persecución vuelve a empezar")]
    // No es game over: volvés a Player_Spot (un checkpoint), el portón se vuelve a cerrar, la
    // niebla arranca cerrada y el Nemesis espera detrás tuyo en Nemesis_ApproachStart.
    [Tooltip("Segundos desde que te levantás con el control hasta que el Nemesis vuelve a correr.")]
    [SerializeField, Min(0f)] private float restartNemesisDelay = 1f;

    // ── Ending: the gate slams shut ─────────────────────────────────────────
    [Header("Final: el portón se cae — A DEFINIR EN TESTEO")]
    // Al cruzar el WinTrigger: plano del portón (sin el jugador), el Nemesis corre hacia él, el
    // portón cae en su cara con polvo y queda trabado del otro lado. Después, la victoria.
    [Tooltip("Segundos desde el corte al plano del portón hasta que empieza a caer. Va de la mano " +
             "con dónde arranca el Nemesis (Nemesis_GateStart): el portón tiene que tocar el piso " +
             "un instante antes de que él llegue.")]
    [SerializeField, Min(0f)] private float gateDropDelay = 0.55f;

    [Tooltip("Segundos que tarda en caer. Acelera como algo pesado: arranca lento y llega a fondo.")]
    [SerializeField, Min(0.05f)] private float gateSlamSeconds = 0.35f;

    [Tooltip("Sonido del golpe contra el piso (id de AudioManager). Vacío = sin sonido: todavía no " +
             "hay uno de portón cayendo.")]
    [SerializeField, SoundId] private string gateSlamSoundId = "";

    [Tooltip("Cuánto (m) se sacude la cámara con el golpe.")]
    [SerializeField, Min(0f)] private float gateShakeAmplitude = 0.06f;

    [Tooltip("Cuánto dura la sacudida (s).")]
    [SerializeField, Min(0.05f)] private float gateShakeSeconds = 0.45f;

    [Tooltip("Segundos con el Nemesis trabado atrás del portón antes de la pantalla de victoria.")]
    [SerializeField, Min(0f)] private float endingHoldSeconds = 2.5f;

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
    // El portón se abre durante la persecución y termina de abrirse cuando llegarías a él
    // corriendo en línea recta desde donde recuperás el control (o desde Player_Spot, si la
    // persecución volvió a empezar) con TU sprint de ese momento (módulos incluidos). Este margen
    // se suma a ese tiempo.
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

    public float RevealTurnDelay => revealTurnDelay;
    public float RevealTurnSeconds => revealTurnSeconds;
    public float RevealStareSeconds => revealStareSeconds;
    public float RevealHandoffDistance => revealHandoffDistance;

    /// <summary>Never shorter than the turn and the stare: the cap is for a charge that stalls.</summary>
    public float RevealMaxSeconds =>
        Mathf.Max(revealMaxSeconds, revealTurnDelay + revealTurnSeconds + revealStareSeconds + 1f);
    public string RevealSoundId => revealSoundId;

    public float RestartNemesisDelay => restartNemesisDelay;

    public float GateDropDelay => gateDropDelay;
    public float GateSlamSeconds => gateSlamSeconds;
    public string GateSlamSoundId => gateSlamSoundId;
    public float GateShakeAmplitude => gateShakeAmplitude;
    public float GateShakeSeconds => gateShakeSeconds;
    public float EndingHoldSeconds => endingHoldSeconds;

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
