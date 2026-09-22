using UnityEngine;

/// <summary>What a capture during the chase means. Stored as a number in the asset: only append.</summary>
public enum EscapeCaptureOutcome
{
    /// <summary>The run ends: the defeat screen.</summary>
    GameOver = 0,

    /// <summary>The chase starts over from Player_Spot.</summary>
    RestartChase = 1,
}

/// <summary>
/// Every number of the escape sequence that is meant to be tuned in playtest, in one asset: the
/// skip key, the slam as the player steps out into the corridor, the pan, the Nemesis's eyes and
/// its charge, the fog cycle (Paso 4), the corridor's sirens, the amber path lights (Paso 5, off
/// by default now), its trot (Paso 6), what a capture means, the gate slamming shut at the end,
/// and the audio layers (Paso 7).
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
    [Tooltip("Puzzle que arma la secuencia al completarse: el hub de los tres núcleos. No corta el " +
             "juego: la cinemática arranca cuando el jugador sale al pasillo. Si ya estaba " +
             "completo al cargar la escena (partida cargada) la secuencia NO se repite.")]
    [SerializeField, PuzzleId] private string triggerPuzzleId = "puzzle_central_piso1";

    // ── Skip ────────────────────────────────────────────────────────────────
    [Header("Skip")]
    [Tooltip("Deja saltear las cinemáticas del escape (del portazo a la carga del Nemesis, y el " +
             "portón del final) con la tecla de abajo. El cartel '[Press F to skip]' se ve " +
             "mientras duran.")]
    [SerializeField] private bool skippable = true;

    [SerializeField] private KeyCode skipKey = KeyCode.F;

    [Tooltip("{0} = la tecla de skip.")]
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    // ── The slam ────────────────────────────────────────────────────────────
    [Header("Portazo al salir al pasillo — A DEFINIR EN TESTEO")]
    // Después del último núcleo se sigue jugando. Cuando el jugador sale por la puerta del centro
    // y ya dio unos pasos en el pasillo (RevealTrigger, fuera del barrido de la hoja): corte seco
    // al plano general, todas las puertas abiertas se cierran de golpe a la vez (la del centro,
    // atrás suyo, incluida: no hay vuelta) y se traban. Después, la tensión y el paneo.
    [Tooltip("Segundos que tarda cada puerta en cerrarse de golpe. Acelera hasta el marco.")]
    [SerializeField, Min(0.01f)] private float doorSlamSeconds = 0.15f;

    [Tooltip("Sonido del portazo (id de AudioManager), en cada puerta. Vacío = el sonido de cerrar " +
             "de cada puerta.")]
    [SerializeField, SoundId] private string doorSlamSoundId = "";

    [Tooltip("En el corte, el jugador se acomoda en Player_CorridorMark (a lo sumo un par de " +
             "metros, el corte lo tapa) para que el plano general lo encuadre siempre igual. " +
             "Apagado = queda donde estaba.")]
    [SerializeField] private bool snapPlayerToMark = true;

    [Tooltip("Segundos del plano fijo después del portazo: silencio, el jugador encerrado. 0 = " +
             "pasa directo al paneo.")]
    [SerializeField, Min(0f)] private float tensionSeconds = 3f;

    [Tooltip("Al poner el último núcleo se cierran y traban TODAS las puertas del hub, la del " +
             "centro incluida. Estos son los segundos que pasan hasta que la del centro se abre " +
             "sola: la única salida. 0 = se abre en el acto.")]
    [SerializeField, Min(0f)] private float safeDoorOpensAfter = 1.5f;

    // ── Pan, eyes, charge ───────────────────────────────────────────────────
    [Header("Paneo, ojos y carga del Nemesis — A DEFINIR EN TESTEO")]
    // La misma cámara del portazo gira hacia el fondo del pasillo, donde espera el Nemesis
    // (Nemesis_CorridorEnd). Corte al detalle de los ojos: la niebla cerrada se come el cuerpo.
    // Corte al plano general: arranca a correr hacia el jugador y, cuando recorrió unos metros,
    // corte al jugador mirándolo: vuelve el control y arranca la persecución.
    [Tooltip("El giro de 180° hacia el fondo lo hace el JUGADOR, en su propia cámara: gira solo, " +
             "ve el pasillo como lo va a ver cuando le devuelvan el control, y no se desorienta al " +
             "recuperarlo. Apagado = lo hace la cámara del portazo (Cam_Slam) con un paneo.")]
    [SerializeField] private bool panOnPlayerView = true;

    [Tooltip("La carga del Nemesis también se ve desde el jugador, así el control vuelve sin " +
             "ningún corte. Apagado = corta al plano general (Cam_Charge).")]
    [SerializeField] private bool chargeOnPlayerView = true;

    [Tooltip("Segundos que tarda el giro de 180° hacia el fondo del pasillo.")]
    [SerializeField, Min(0.05f)] private float panSeconds = 2.2f;

    [Tooltip("Forma del paneo (0..1 en los dos ejes). Vacía = arranca y frena suave.")]
    [SerializeField] private AnimationCurve panCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("SÓLO para el paneo de cámara (con 'Pan On Player View' apagado): para qué lado gira " +
             "Cam_Slam. -1 = izquierda, +1 = derecha, 0 = por el lado corto. Con Cam_Slam como " +
             "viene, +1 pasa por la puerta del centro recién cerrada antes de llegar al fondo.\n\n" +
             "El giro del jugador va siempre por el lado corto: saliendo por la puerta, el fondo " +
             "del pasillo le queda a un cuarto de vuelta a la izquierda.")]
    [SerializeField, Range(-1, 1)] private int panTurnSign = 1;

    [Tooltip("Segundos del plano detalle de los ojos.")]
    [SerializeField, Min(0f)] private float eyesHoldSeconds = 1.6f;

    [Tooltip("Metros que corre el Nemesis hacia el jugador en el plano general antes de que vuelva " +
             "el control.")]
    [SerializeField, Min(0.5f)] private float chargeDistance = 6f;

    [Tooltip("Tope (s) de la carga: si por lo que sea no recorre la distancia, el control vuelve " +
             "igual.")]
    [SerializeField, Min(0.5f)] private float chargeMaxSeconds = 4f;

    // ── Fog cycle (Paso 4) ──────────────────────────────────────────────────
    // Sólo la niebla: las luces del pasillo titilan todo el escape por su cuenta
    // (EscapeCorridorFlicker) y no se apagan con ella. Un ciclo corto y amplio se siente como
    // entrar y salir de la luz de una Light Base una y otra vez.
    [Header("Ciclo de la niebla (Paso 4) — A DEFINIR EN TESTEO")]
    [Tooltip("Segundos que tarda la niebla en expandirse / abrirse (con lerp, no de golpe).")]
    [SerializeField, Min(0.05f)] private float openSeconds = 1f;

    [Tooltip("Segundos con la niebla abierta: se ve el nivel, y también al Nemesis. Ése es el costo.")]
    [SerializeField, Min(0f)] private float holdSeconds = 1.5f;

    [Tooltip("Segundos que tarda la niebla en contraerse / volver a cerrarse.")]
    [SerializeField, Min(0.05f)] private float closeSeconds = 1.2f;

    [Tooltip("Segundos con la niebla cerrada antes de que se vuelva a abrir. Lo único que se ve de " +
             "lejos son las luces ámbar del camino. Constante en todo el recorrido: el ciclo no " +
             "escala con la dificultad.")]
    [SerializeField, Min(0f)] private float darkGapSeconds = 1f;

    [Tooltip("Distancia horizontal (m) a la que una puerta del recorrido se da por alcanzada. En " +
             "la última (el portón) dispara el evento 'On Gate Reached' del director, y nada más.")]
    [SerializeField, Min(0.5f)] private float arrivalRadius = 2.5f;

    [Header("Luces ámbar del camino (Paso 5)")]
    [Tooltip("Prende las luces ámbar del camino durante el escape. Apagado (lo normal ahora) = el " +
             "pasillo lo marcan las sirenas; el recorrido igual cuenta las puertas alcanzadas.")]
    [SerializeField] private bool guideLightsEnabled = false;

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

    [Tooltip("Intensidad del spot ámbar de cada puerta del camino. Cuelga del techo, arriba de la " +
             "puerta, y apunta al piso: lo que se ve es el charco de luz abajo.")]
    [SerializeField, Min(0f)] private float guideLampIntensity = 20f;

    [Tooltip("Apertura (grados) del spot ámbar. Más = charco más ancho y más suave.")]
    [SerializeField, Range(10f, 160f)] private float guideSpotAngle = 70f;

    [Tooltip("Intensidad de las lámparas blancas del pasillo que arrancan apagadas en la escena " +
             "(las que titilan).")]
    [SerializeField, Min(0f)] private float lampIntensity = 6f;

    // ── Sirens ──────────────────────────────────────────────────────────────
    [Header("Sirenas del pasillo — A DEFINIR EN TESTEO")]
    // Las lámparas del pasillo se vuelven sirenas cuando vuelve el control: rojo y ámbar
    // alternando, estilo patrullero. Misma receta que las Light Base Switch: un punto que
    // atraviesa cualquier niebla (FogBeacon), el haz del cono en el aire (FogLightVolume), un
    // charco que sólo aclara la niebla sin inyectar luz (el jugador y el Nemesis son Unlit: la luz
    // inyectada los quemaba) y el Spot para las paredes.
    [Tooltip("Color de un grupo de sirenas. Rojo peligro #CC1A1A.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    [SerializeField] private Color alarmRed = new Color(0.8f, 0.102f, 0.102f);

    [Tooltip("Color del otro grupo. El ámbar del escape (el de las luces del camino).")]
    [ColorUsage(showAlpha: false, hdr: false)]
    [SerializeField] private Color alarmAmber = new Color(1f, 0.72f, 0.38f);

    [Tooltip("Segundos de una vuelta completa: la mitad prende un grupo, la otra mitad el otro. " +
             "Más de 0.66 s = menos de 3 destellos por segundo (fotosensibilidad).")]
    [SerializeField, Min(0.34f)] private float alarmPeriod = 0.8f;

    [Tooltip("Fracción de cada mitad que queda apagada entre un grupo y el otro. 0 = cambia de " +
             "color sin apagarse.")]
    [SerializeField, Range(0f, 0.8f)] private float alarmGap = 0.15f;

    [Tooltip("Intensidad del Spot de cada sirena prendida (lo que pinta paredes y piso).")]
    [SerializeField, Min(0f)] private float alarmLampIntensity = 12f;

    [Tooltip("Brillo del punto de la sirena que atraviesa cualquier niebla (FogBeacon). 0 = sin " +
             "punto: no se ven de lejos.")]
    [SerializeField, Min(0f)] private float alarmBeaconIntensity = 3f;

    [Tooltip("Tamaño real (m) del punto de la sirena. Más grande que un ojo del Nemesis.")]
    [SerializeField, Min(0.001f)] private float alarmBeaconRadius = 0.3f;

    [Tooltip("Radio mínimo (px) del punto. Más grande que el de los ojos (5.5) para no " +
             "confundirlos.")]
    [SerializeField, Min(0f)] private float alarmBeaconMinPixels = 10f;

    [Tooltip("Distancia (m) por debajo de la cual el punto de la sirena se apaga: de cerca no " +
             "encandila.")]
    [SerializeField, Min(0f)] private float alarmBeaconNearFadeStart = 1.5f;

    [Tooltip("Distancia (m) a partir de la cual el punto está a brillo completo.")]
    [SerializeField, Min(0f)] private float alarmBeaconNearFadeEnd = 4f;

    [Tooltip("Cuánta niebla aclara el charco de cada sirena. Sólo aclara: no mete luz, así no " +
             "quema al jugador que pasa por abajo. 0 = sin charco.")]
    [SerializeField, Range(0f, 1f)] private float alarmPoolClear = 0f;

    [Tooltip("Brillo del haz de cada sirena en el aire (FogLightVolume): el cono rojo / ámbar que " +
             "se ve bajar del techo a través de la niebla. 0.3 = sutil, 1 = exagerado. 0 = sin haz.")]
    [SerializeField, Min(0f)] private float alarmBeamIntensity = 0.9f;

    [Header("Preset de niebla del escape (opcionales)")]
    [Tooltip("Niebla espesa que rige durante todo el escape: entra con el portazo (del Nemesis se " +
             "ven los ojos a través de ella) y es a la que vuelve el ciclo cada vez que se " +
             "cierra. Vacío = no toca la niebla global.")]
    [SerializeField] private SO_VisionFogConfig closedFog;

    [Tooltip("Niebla más baja que se aplica mientras el ciclo está abierto, en la persecución. " +
             "Vacío = la niebla no respira.")]
    [SerializeField] private SO_VisionFogConfig openFog;

    // ── The cinematic's fog and the handover ────────────────────────────────
    [Header("Niebla de la cinemática y vuelta del control")]
    [Tooltip("Otra niebla para la cinemática del portazo a la carga, sólo si hace falta. Vacío (lo " +
             "normal) = la niebla cerrada del escape: del Nemesis se ven sólo los ojos y el cuerpo " +
             "aparece recién cerca de la cámara.")]
    [SerializeField] private SO_VisionFogConfig revealShotFog;

    [Tooltip("Niebla del plano de la cámara de seguridad del portazo (Cam_Slam), sólo mientras está " +
             "al aire: con más alcance que la cerrada, así el feed muestra el pasillo. Al salir del " +
             "plano vuelve la de arriba, para los ojos y la carga. Vacío = la de arriba también acá.")]
    [SerializeField] private SO_VisionFogConfig slamShotFog;

    [Tooltip("Cuánto (grados) se corre la cámara del jugador de la línea hacia el Nemesis al " +
             "volver el control, para que la cabeza del jugador no le tape los ojos.")]
    [SerializeField, Range(0f, 45f)] private float revealCameraSideAngle = 12f;

    [Tooltip("Altura de la cámara del jugador al volver el control, en unidades del eje vertical " +
             "del rig: 0 = anillo del medio; negativo = más baja, mira más a lo largo del pasillo; " +
             "positivo = más alta, mira más al piso (el 17.5 por defecto del rig ya mira al piso).")]
    [SerializeField, Range(-40f, 40f)] private float revealCameraVertical = -5f;

    [Tooltip("Sonido cuando arranca a correr (id de AudioManager), en la posición del Nemesis. " +
             "Vacío = ninguno.")]
    [SerializeField, SoundId] private string revealSoundId = "sfx_nemesis_activacion";

    // ── Capture during the chase ────────────────────────────────────────────
    [Header("Si te agarra en la persecución")]
    [Tooltip("Game Over = se termina la partida (pantalla de derrota). Restart Chase = volvés a " +
             "Player_Spot (un checkpoint), el portón se vuelve a cerrar, la niebla arranca cerrada " +
             "y el Nemesis espera detrás tuyo en Nemesis_ApproachStart.")]
    [SerializeField] private EscapeCaptureOutcome captureOutcome = EscapeCaptureOutcome.GameOver;

    [Tooltip("Sólo con Restart Chase: segundos desde que te levantás con el control hasta que el " +
             "Nemesis vuelve a correr.")]
    [SerializeField, Min(0f)] private float restartNemesisDelay = 1f;

    // ── Ending: the gate slams shut ─────────────────────────────────────────
    [Header("Final: el portón se cae — A DEFINIR EN TESTEO")]
    // Al cruzar el WinTrigger: plano del portón (sin el jugador), el Nemesis corre hacia él, el
    // portón cae en su cara con polvo y queda trabado del otro lado. Después, la victoria.
    [Tooltip("Segundos desde el corte al plano del portón hasta que empieza a caer. Va de la mano " +
             "con dónde arranca el Nemesis (Nemesis_GateStart): el portón tiene que tocar el piso " +
             "un instante antes de que él llegue.")]
    [SerializeField, Min(0f)] private float gateDropDelay = 1f;

    [Tooltip("Segundos que tarda en caer. Acelera como algo pesado: arranca lento y llega a fondo.")]
    [SerializeField, Min(0.05f)] private float gateSlamSeconds = 0.35f;

    [Tooltip("Sonido del golpe contra el piso (id de AudioManager). Vacío = sin sonido: todavía no " +
             "hay uno de portón cayendo.")]
    [SerializeField, SoundId] private string gateSlamSoundId = "sfx_interaction_cerrar_puerta";

    [Tooltip("Cuánto (m) se sacude la cámara con el golpe.")]
    [SerializeField, Min(0f)] private float gateShakeAmplitude = 0.06f;

    [Tooltip("Cuánto dura la sacudida (s).")]
    [SerializeField, Min(0.05f)] private float gateShakeSeconds = 0.45f;

    [Tooltip("Niebla del plano del portón. En esta sección el CENTRO de la niebla es el Nemesis, " +
             "no la cámara: él queda en una burbuja clara y todo lo demás en niebla, así se lo ve " +
             "aunque sea Unlit. Tiene que cerrarse poco después del portón, así del otro lado no se " +
             "ve nada (ni el jugador). Vacío = la niebla cerrada del escape.")]
    [SerializeField] private SO_VisionFogConfig gateShotFog;

    [Tooltip("Después del golpe, corta al plano medio del Nemesis entre el polvo (Cam_Dust). " +
             "Apagado = todo el final queda en la cámara de seguridad del portón.")]
    [SerializeField] private bool cutToDustShot = false;

    [Tooltip("Segundos después del golpe hasta el corte al plano medio del Nemesis entre el polvo.")]
    [SerializeField, Min(0f)] private float dustShotDelay = 0.35f;

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
    [Tooltip("Alarma de instalación. Loop desde que vuelve el control (arranca la persecución, " +
             "con las sirenas) hasta que termina el escape.")]
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

    [Tooltip("Sonido de traba de cada puerta (id de AudioManager): las otras salidas del hub al " +
             "poner el último núcleo.")]
    [SerializeField, SoundId] private string doorLockSoundId = "sfx_interaction_puerta_bloqueada";

    [Tooltip("Segundos entre una puerta y la siguiente cuando se traban de a una (EscapeCorridorLock.LockAll).")]
    [SerializeField, Min(0f)] private float doorLockInterval = 0.12f;

    public string TriggerPuzzleId => triggerPuzzleId;

    public bool Skippable => skippable;
    public KeyCode SkipKey => skipKey;
    public string SkipPromptText => string.Format(skipPromptFormat, skipKey);

    public float DoorSlamSeconds => doorSlamSeconds;
    public string DoorSlamSoundId => doorSlamSoundId;
    public bool SnapPlayerToMark => snapPlayerToMark;
    public float TensionSeconds => tensionSeconds;
    public float SafeDoorOpensAfter => safeDoorOpensAfter;
    public bool PanOnPlayerView => panOnPlayerView;
    public bool ChargeOnPlayerView => chargeOnPlayerView;

    public float PanSeconds => panSeconds;
    public AnimationCurve PanCurve => panCurve;
    public int PanTurnSign => panTurnSign;
    public float EyesHoldSeconds => eyesHoldSeconds;
    public float ChargeDistance => chargeDistance;
    public float ChargeMaxSeconds => chargeMaxSeconds;

    public float OpenSeconds => openSeconds;
    public float HoldSeconds => holdSeconds;
    public float CloseSeconds => closeSeconds;
    public float DarkGapSeconds => darkGapSeconds;
    public float ArrivalRadius => arrivalRadius;

    public bool GuideLightsEnabled => guideLightsEnabled;
    public Color LightColor => lightColor;
    public float LightRadius => lightRadius;
    public float LightFogIntensity => lightFogIntensity;
    public float LightFogClear => lightFogClear;
    public float BeaconIntensity => beaconIntensity;
    public float GuideLampIntensity => guideLampIntensity;
    public float GuideSpotAngle => guideSpotAngle;
    public float LampIntensity => lampIntensity;
    public SO_VisionFogConfig ClosedFog => closedFog;
    public SO_VisionFogConfig OpenFog => openFog;

    public Color AlarmRed => alarmRed;
    public Color AlarmAmber => alarmAmber;
    public float AlarmPeriod => alarmPeriod;
    public float AlarmGap => alarmGap;
    public float AlarmLampIntensity => alarmLampIntensity;
    public float AlarmBeaconIntensity => alarmBeaconIntensity;
    public float AlarmBeaconRadius => alarmBeaconRadius;
    public float AlarmBeaconMinPixels => alarmBeaconMinPixels;
    public float AlarmBeaconNearFadeStart => alarmBeaconNearFadeStart;
    public float AlarmBeaconNearFadeEnd => alarmBeaconNearFadeEnd;
    public float AlarmPoolClear => alarmPoolClear;
    public float AlarmBeamIntensity => alarmBeamIntensity;

    public SO_VisionFogConfig RevealShotFog => revealShotFog;
    public SO_VisionFogConfig SlamShotFog => slamShotFog;
    public float RevealCameraSideAngle => revealCameraSideAngle;
    public float RevealCameraVertical => revealCameraVertical;
    public string RevealSoundId => revealSoundId;

    public EscapeCaptureOutcome CaptureOutcome => captureOutcome;
    public float RestartNemesisDelay => restartNemesisDelay;

    public SO_VisionFogConfig GateShotFog => gateShotFog;
    public bool CutToDustShot => cutToDustShot;
    public float DustShotDelay => dustShotDelay;
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
