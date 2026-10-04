using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisData", menuName = "Scriptable Objects/SO_NemesisData")]
public class SO_NemesisData : ScriptableObject
{
    [SerializeField] private float investigationTimeOut;
    [SerializeField] private float searchTimeOut;
    [SerializeField] private float visionLossGracePeriod;
    [SerializeField] private float patrolWaypointWaitTime;

    [Tooltip("Segundos de variación, hacia arriba y hacia abajo, sobre la espera en cada " +
             "waypoint. La espera real se sortea entre (espera - esto) y (espera + esto), sin " +
             "bajar de 0.\n\n" +
             "En 0 el Nemesis espera exactamente lo mismo en todos los waypoints, siempre, y eso " +
             "es justo lo que lo hace leerse como un metrónomo: una vez que le tomaste el tiempo a " +
             "una ronda, le tomaste el tiempo a todas. Con variación, quedarte quieto esperando " +
             "que se vaya deja de ser una cuenta y pasa a ser una apuesta.\n\n" +
             "Es una variación y no un par mín/máx a propósito: el valor autorado de la espera " +
             "sigue siendo el centro, así que subir esto no puede retunear sin querer cuánto tarda " +
             "una ronda entera.")]
    [SerializeField, Min(0f)] private float patrolWaitVariance = 0f;

    [SerializeField] private float noiseUpdateCooldown;

    // Tuneable detection parameters live here and not on the sensor components so that a
    // designer changes them in one asset, and so Tier 3.3 can scale difficulty by handing the
    // sensors a runtime copy of this SO. The LayerMasks stay on the components: those are scene
    // wiring, not design values.
    //
    // Defaults match what the Nemesis prefab had before the migration (viewRange 10,
    // viewAngle 90, listenRange 10) so moving them here did not retune the enemy.

    [Header("Vision")]
    [Tooltip("Hasta qué distancia te NOTA, en metros: el rango de visión de un Nemesis que todavía " +
             "no te vio (patrullando, o yendo a un ruido). Es el número sobre el que se balancea el " +
             "sigilo.\n\n" +
             "Desde el 04/10 no es el único rango: una vez que te vio te SOSTIENE más lejos (View " +
             "Hold Scale) y, si te pierde y te caza, puede volver a verte desde más lejos (View Hunt " +
             "Scale). Los dos se multiplican sobre este. Lo que distingue a través de un escondite, " +
             "lo que siente atrás y el 'acá no está' del mapa de búsqueda siguen saliendo de este.")]
    [SerializeField] private float viewRange = 10f;

    [Tooltip("Full width of the vision cone in degrees. Halved when tested against the " +
             "forward vector.")]
    [Range(0, 360)]
    [SerializeField] private float viewAngle = 90f;

    [Tooltip("Ancho TOTAL, en grados, del cono interno de foco: lo que el Nemesis mira de verdad.\n\n" +
             "Adentro de este cono te detecta al instante, igual que siempre. Entre este ángulo y " +
             "View Angle está la visión PERIFÉRICA: ahí no te detecta de una, sino que le va " +
             "subiendo la sospecha (ver Awareness Build Time). Eso es lo que hace que asomarse a " +
             "una esquina te dé un instante en vez de arrancar la persecución en el mismo frame.\n\n" +
             "Si lo ponés igual o mayor que View Angle no hay banda periférica y el sistema entero " +
             "queda desactivado: todo vuelve a ser detección instantánea. El inspector del asset " +
             "te avisa cuando pasa eso.")]
    [Range(0, 360)]
    [SerializeField] private float focusAngle = 60f;

    [Tooltip("Segundos de exposición continua en la periferia, en el BORDE del rango de visión, " +
             "para que la sospecha llegue al máximo y te detecte.\n\n" +
             "Escala con la distancia: pegado al Nemesis es casi inmediato, en el límite del rango " +
             "tarda esto. Muy bajo y la periferia es lo mismo que el foco; muy alto y podés " +
             "cruzarle el campo visual caminando sin que reaccione nunca.")]
    [SerializeField, Min(0.05f)] private float awarenessBuildTime = 1.2f;

    [Tooltip("Cuánta sospecha se le baja por segundo cuando deja de verte.\n\n" +
             "0.5 la vacía en dos segundos desde el máximo. Que decaiga y no se corte de golpe es " +
             "lo que hace que asomarse dos veces seguidas sea más peligroso que asomarse una: la " +
             "segunda arranca desde donde quedó la primera.")]
    [SerializeField, Min(0.01f)] private float awarenessDecayRate = 0.5f;

    [Tooltip("A partir de qué nivel de sospecha (0 a 1) el Nemesis empieza a moverse hacia vos, " +
             "sin haberte detectado del todo.\n\n" +
             "Es el umbral que lee la regla nueva de la escalera de prioridades: por encima de " +
             "esto pide Investigating. Al llegar a 1 pasa a ser detección completa y manda la " +
             "regla 'lo está viendo'.")]
    [SerializeField, Range(0f, 1f)] private float awarenessTriggerThreshold = 0.4f;

    [Tooltip("Hard detection radius, measured FLAT from the Nemesis's feet and only on its own " +
             "floor (height gap under Catch Max Vertical Offset). Inside it the Nemesis notices the " +
             "player no matter what cone or hiding says — only a wall in between stops it, and only " +
             "with Proximity Detection Respects Walls on; the shell of the spot the player hides in " +
             "never does. A player out in the open is SEEN; one inside a hiding spot makes the spot " +
             "KNOWN, and the Nemesis goes to open it. Keep it well under viewRange — this is 'it is " +
             "literally on top of me', not a second vision range. NOT the same as proximityRadius " +
             "below, which only drives the HUD vignette and detects nothing. 0 disables it.")]
    [SerializeField] private float proximityDetectionRange = 3f;

    [Tooltip("The view range in force is multiplied by this while the player is crouching — the " +
             "base one, and the longer ones too (View Hold Scale, View Hunt Scale): a crouch is worth " +
             "the same fraction against a Nemesis that is holding you in sight or hunting you. " +
             "1 = crouching does not help at all, 0.5 = spotted at half the distance.")]
    [SerializeField, Range(0f, 1f)] private float crouchVisionMultiplier = 0.6f;

    [Header("Vision — zonas y cómo se suman")]
    //
    // Tres zonas de distinto peso (VisionZones): el FOCO te ve al instante; la PERIFERIA "cree que vio
    // algo" y llena el medidor de sospecha; ATRÁS "siente que hay alguien", mucho más débil, y sola
    // nunca llega a avistamiento: a lo sumo se da vuelta a mirar, y ahí deciden los ojos.
    // Y se SUMAN con lo que ya cree: lo que ve de reojo donde hace poco te sintió no es "algo", sos vos.

    [Tooltip("Segundos desde la última vez que te sintió (te vio o te oyó) durante los que un vistazo " +
             "de reojo que cae dentro de la creencia (su radio, en tu piso) cuenta como verte: pasa " +
             "directo a perseguirte, sin la sospecha ni 'vio algo de reojo' en el medio. Lo mismo que " +
             "ya pasaba mientras te perseguía (el medidor estaba lleno), extendido a la búsqueda y a la " +
             "investigación de un ruido tuyo. Lo que siente atrás, corroborado, pesa como un vistazo " +
             "(sigue sin ser avistamiento). 0 lo apaga: la periferia vuelve a arrancar de cero siempre.")]
    [SerializeField, Min(0f)] private float glimpseCorroborationWindow = 8f;

    [Tooltip("Hasta qué distancia siente a alguien ATRÁS suyo, fuera del cono de visión, en metros " +
             "(en plano, en su mismo piso, sin paredes en el medio). Agachado se acorta igual que la " +
             "vista (Crouch Vision Multiplier). Lo que quede adentro de Proximity Detection Range no " +
             "cuenta: ahí ya te detecta igual. 0 apaga la zona.")]
    [SerializeField, Min(0f)] private float rearSenseRange = 3f;

    [Tooltip("Cuánto pesa lo que siente atrás, como fracción de la velocidad con que lo de reojo llena " +
             "el medidor de sospecha a la misma cercanía. 0.25: cuatro veces más lento que un vistazo. " +
             "Nunca pasa de Noise Only Suspicion Cap: sola no es un avistamiento. Al cruzar Awareness " +
             "Trigger Threshold se da vuelta a mirar ('vio algo de reojo').")]
    [SerializeField, Range(0f, 1f)] private float rearSenseStrength = 0.25f;

    [Header("Vision — rango que se adapta y mirada en la persecución")]
    //
    // View Range es hasta dónde te NOTA. Verte no es alcanzarte: al que ya está viendo lo sigue viendo
    // más lejos (sostener), y al que perdió mientras lo perseguía o lo buscaba lo puede volver a ver
    // desde más lejos cuanto más tiempo lleva sin verlo (cazar). Patrullando sin haberte visto, o
    // yendo a un ruido, vale View Range a secas: el sigilo no cambia. Agachado parte a la mitad el
    // rango que esté valiendo. Lo que distingue a través de un escondite, lo que siente atrás, la
    // detección dura y el "acá no está" del mapa de búsqueda se quedan en View Range.
    // La regla está en AdaptiveViewRange y la de la mirada en ChaseGaze (WIRED.Nemesis.Logic).

    [Tooltip("SOSTENER. Por cuánto se multiplica View Range mientras te ESTÁ VIENDO: una vez que te " +
             "vio, para que deje de verte tenés que alejarte hasta View Range × esto (o salir del " +
             "cono, o tapar la línea de vista). Con View Range 7 y esto en 2, te nota a 7 m y te " +
             "sostiene hasta 14 m.\n\n" +
             "Es lo que arregla 'en línea recta me pierde': corriendo a su misma velocidad quedabas " +
             "justo en el borde de los 7 m, y cualquier barrido que fallaba cortaba la persecución. " +
             "Verte no es alcanzarte: no lo hace más rápido, solo le cuesta más perderte.\n\n" +
             "Romper la línea de vista sigue valiendo: apenas deja de verte el rango vuelve a View " +
             "Range y lo tiene que recuperar cazando (View Hunt Scale). Se multiplica encima de lo " +
             "que ya le suman la escalada y el Director. 1 lo apaga: como antes.")]
    [SerializeField, Min(1f)] private float viewHoldScale = 2f;

    [Tooltip("CAZAR. Hasta cuánto crece View Range cuando PERDIÓ a un jugador que venía viendo y " +
             "todavía lo persigue o lo busca (Chasing sin vista, Searching, yendo al montacargas): " +
             "'un rango mayor si no lo llega a ver'. Arranca en ×1 al perderte y llega a esto en View " +
             "Hunt Grow Time. Es el rango con el que te puede VOLVER a ver; el cono y la línea de " +
             "vista hacen falta igual.\n\n" +
             "Se lo gana viéndote: una búsqueda que nació de un ruido, sin haberte visto, se queda en " +
             "View Range, y yendo a investigar un ruido (Investigating) tampoco crece. Pasarle por al " +
             "lado a un Nemesis que solo te oyó cuesta lo mismo que antes. 1 lo apaga.")]
    [SerializeField, Min(1f)] private float viewHuntScale = 2f;

    [Tooltip("Segundos cazándote sin verte que tarda el rango en crecer de View Range a View Range × " +
             "View Hunt Scale. Es la ventana que te da romper la línea de vista: recién perdido te " +
             "busca con el rango de siempre. 0: salta al máximo apenas te pierde.")]
    [SerializeField, Min(0f)] private float viewHuntGrowTime = 2f;

    [Tooltip("Segundos que tarda el rango en volver a View Range cuando la cacería termina (vuelve a " +
             "patrullar, o se va a mirar otra cosa). Baja de a poco en vez de cortarse. 0: de golpe.")]
    [SerializeField, Min(0f)] private float viewHuntSettleTime = 2f;

    [Tooltip("ESQUINAS. Mientras te persigue, la MIRADA deja de estar pegada al cuerpo: si te ve, te " +
             "sigue con los ojos; si te pierde, mira hacia donde te vio irte. Apunta a un punto a " +
             "estos metros de donde te perdió, en el rumbo que te vio llevar (velocidad OBSERVADA, " +
             "nunca tu posición real). De lejos eso es casi derecho; a medida que se acerca la mirada " +
             "se mete en la esquina, y al llegar ya está mirando el pasillo por el que te fuiste en " +
             "vez de la pared.\n\n" +
             "Más alto: empieza a girar antes y deja de cubrir el frente antes. Si no te vio moverte " +
             "(o ibas muy lento) no hay rumbo y mira al frente. 0 apaga las dos cosas: la mirada " +
             "queda pegada al cuerpo durante toda la persecución, como antes.")]
    [SerializeField, Min(0f)] private float lostSightLookAhead = 6f;

    [Tooltip("Grados por segundo a los que gira la mirada en la persecución (hacia vos, hacia donde " +
             "te fuiste, y de vuelta al frente del cuerpo al terminar). El barrido de las pausas " +
             "tiene el suyo (Scan Speed). Muy bajo y llega a la esquina sin haber terminado de " +
             "girar; muy alto y la mirada salta.")]
    [SerializeField, Min(1f)] private float gazeTurnSpeed = 200f;

    [Header("Hearing")]
    [Tooltip("Hard ceiling on hearing, and the radius of the broadphase OverlapSphere. How loud " +
             "the player actually is decides the real range — see Noise Range Scale.")]
    [SerializeField] private float listenRange = 10f;

    [Tooltip("Metres of hearing per metre of the player's own noise emitter radius.\n\n" +
             "This is the knob that makes moving quietly WORTH something. The player's emitter " +
             "(crouch 1 / walk 2 / run 6) used to decide only whether the OverlapSphere caught " +
             "the collider at all: once a wall was in the way the test collapsed to " +
             "'listenRange * multiplier', identical for a sprint and a crouch. Sneaking behind " +
             "cover bought you literally nothing, which is where it mattered most.\n\n" +
             "At 2.5 the three gaits become 2.5 / 5 / 15 m, capped by Listen Range. Keep Listen " +
             "Range above loudness * this for the loudest gait, or the cap flattens the top of " +
             "the scale and running stops being louder than walking.")]
    [SerializeField, Min(0.1f)] private float noiseRangeScale = 2.5f;

    [Tooltip("Whether a wall between the Nemesis and a noise attenuates it.")]
    [SerializeField] private bool wallOcclusionEnabled = true;

    [Tooltip("Effective range through a wall = listenRange * this. Spec default: 0.6.")]
    [SerializeField, Range(0f, 1f)] private float wallOcclusionMultiplier = 0.6f;

    [Tooltip("Effective range through a FLOOR = listenRange * this.\n\n" +
             "Deliberately more generous than the wall multiplier: a floor slab is the one thing " +
             "the Nemesis can never see through, so hearing is its only channel to the storey " +
             "above. Set this too low and it can never work out that you are up there; set it to " +
             "1 and it tracks you between floors as if the slab were not there.\n\n" +
             "Combined with the player's own noise radii (crouch 1 / walk 2 / run 6) this is the " +
             "knob that decides how much running upstairs costs you.")]
    [SerializeField, Range(0f, 1f)] private float floorOcclusionMultiplier = 0.75f;

    [Tooltip("Measure how far a noise is along the NavMesh instead of in a straight line.\n\n" +
             "This is what makes a player directly overhead read as the 12 metres they are on " +
             "foot rather than the 5 they are as the crow flies — so hearing them does not make " +
             "the Nemesis behave as though they were within arm's reach. Costs one " +
             "NavMesh.CalculatePath every NoiseUpdateCooldown seconds.")]
    [SerializeField] private bool hearingUsesPathDistance = true;

    [Header("Navigation")]
    [Tooltip("Seconds between recalculations of the route verdict — reachable, how far, which " +
             "floor, whether the lift is on the way.\n\n" +
             "Each one is a NavMesh.CalculatePath, so this is a real cost knob. It is also a " +
             "STABILITY knob: the verdict flipping frame to frame is what made the Nemesis " +
             "oscillate between Chasing and Searching while standing under the player. Do not " +
             "drop it near zero to make it feel sharper.")]
    [SerializeField, Min(0.05f)] private float routeVerdictInterval = 0.4f;

    [Tooltip("Height difference, in metres, past which a target counts as being on another " +
             "floor. Roughly one storey; below a full storey it starts firing on ramps and " +
             "crates.")]
    [SerializeField, Min(0.5f)] private float floorHeightThreshold = 2.5f;

    [Tooltip("Seconds the Nemesis keeps walking to the freight elevator after it has stopped " +
             "seeing or hearing the player.\n\n" +
             "This is what makes the lift trip a decision instead of an accident. At 0 it turns " +
             "around the instant you break line of sight — which, since a floor slab breaks it " +
             "the moment it starts climbing, means it never gets there at all.")]
    [SerializeField, Min(0f)] private float elevatorCommitTime = 12f;

    [Tooltip("Radio, en metros, del sorteo de último recurso de la búsqueda: cuando el mapa de " +
             "búsqueda no tiene ningún valor (entró directo de una captura, o solo sabe de un " +
             "escondite y no cree nada más), camina a puntos al azar a esta distancia de donde estaba " +
             "al empezar a buscar.\n\n" +
             "Chico, se queda dando pasitos en el lugar; grande, se dispersa tanto que deja de leerse " +
             "como una búsqueda. Con valor en el mapa esto no se usa: decide el mapa (Mapa de " +
             "búsqueda, más abajo).")]
    [SerializeField, Min(1f)] private float searchSweepRadius = 5f;

    [Header("Search - donde busca")]
    //
    // ADÓNDE va lo decide el mapa de búsqueda (más abajo; Plan Búsqueda, Fase 2b): la búsqueda ya no
    // barre un disco alrededor de la creencia, va adonde todavía hay valor. No guarda memoria de "ya
    // barrí": lo que mira queda en cero en el mapa, y esa es toda la memoria. Acá queda la pausa de
    // mirar en cada lugar al que llega.

    [Tooltip("Segundos que el Nemesis se queda quieto en cada lugar de la busqueda al que LLEGA, " +
             "antes de elegir el siguiente.\n\n" +
             "Es lo que convierte la busqueda en algo LEGIBLE. Sin esta pausa encadena destinos " +
             "sin parar y desde afuera no se distingue de una patrulla rara: no hay forma de saber " +
             "que esta buscando, ni donde, ni si ya se fue de la zona. Con la pausa se planta, " +
             "mira alrededor (ver Scan Half Angle) y sigue - y desde un escondite se puede leer si " +
             "te esta por encontrar o si ya paso de largo. Ademas, lo que barre con la mirada en esa " +
             "pausa es lo que el mapa de busqueda da por vacio.\n\n" +
             "Al ENTRAR a buscar no hace esta pausa: sale enseguida hacia donde pudiste ir. Si un " +
             "lugar queda vacio antes de llegar (lo vio de lejos) tampoco: elige otro sin parar.\n\n" +
             "0 la desactiva y vuelve al encadenado de antes.")]
    [SerializeField, Min(0f)] private float searchPauseTime = 1.2f;

    [Header("Search — área de la búsqueda")]
    //
    // The disc the search used to sweep is gone (Plan-Busqueda-Nemesis Fase 2b: the possibility map
    // decides where it goes). What is left of "the area being searched" is this one radius, for the
    // two things that still ask how far around a search something counts as inside it.

    [Tooltip("Radio, en metros, de lo que cuenta como \"la zona que está buscando\", para dos cosas:\n" +
             "• los escondites que usaste a esta distancia del lugar al que va tienen su tirada por " +
             "hábito (una por búsqueda cada uno);\n" +
             "• un señuelo que suena a esta distancia de donde cree que estás (más Lead Sums Margin) " +
             "no compite con vos: suma, y la búsqueda lo cubre.\n\n" +
             "Ya NO limita adónde busca. Eso lo decide el mapa de búsqueda (más abajo), que no tiene " +
             "tope de distancia ni se recorta por paredes: te sigue por la puerta.")]
    [SerializeField, Min(1f)] private float roomSweepRadius = 7f;

    [Header("Chase - pursuit")]
    //
    // La persecucion era Seek puro: destination = ultima posicion sentida. Correr hacia donde el
    // jugador ESTUVO significa llegar siempre un paso tarde, y contra un jugador que corre en
    // linea recta el Nemesis nunca acorta distancia. Estos knobs son la prediccion temporal y la
    // eleccion de ruta que la reemplazan.

    [Tooltip("Segundos que el Nemesis proyecta hacia adelante la posicion del jugador mientras lo " +
             "persigue.\n\n" +
             "Mas alto = corta mas por delante. Demasiado alto y adivina: apunta a donde el " +
             "jugador habria estado si nunca hubiera doblado, y eso se lee como que el juego hace " +
             "trampa, no como que el monstruo es vivo.\n\n" +
             "La velocidad que extrapola es OBSERVADA (FieldOfView.LastKnownVelocity), no leida " +
             "del jugador, asi que cambiar de direccion apenas rompes la linea de vision siempre " +
             "funciona. 0 desactiva la prediccion y vuelve al Seek de antes.\n\n" +
             "Solo la persecucion predice: la busqueda barre alrededor de la creencia (plan §18) " +
             "y no adelanta nada. Perseguir puede permitirse adivinar porque te esta viendo o te " +
             "acaba de ver.")]
    [SerializeField, Range(0f, 1.5f)] private float chaseTimePrediction = 0.45f;

    [Tooltip("Cada cuantos segundos el Nemesis re-evalua por que waypoint conviene ir mientras " +
             "persigue.\n\n" +
             "Cada re-evaluacion cuesta consultas de camino sobre varios candidatos, asi que esto " +
             "es un knob de costo real. No lo bajes a cero para que se sienta mas agil: la " +
             "prediccion se recalcula igual todos los frames, esto es solo la eleccion de ruta.")]
    [SerializeField, Min(0.1f)] private float chaseRouteReplanInterval = 0.75f;

    [Tooltip("Metros que se tiene que mover la creencia para forzar una re-evaluacion de ruta " +
             "antes de que venza el intervalo.\n\n" +
             "Es lo que hace que volver a verte del otro lado del pasillo se sienta inmediato en " +
             "vez de esperar hasta tres cuartos de segundo.")]
    [SerializeField, Min(0.5f)] private float chaseBeliefMoveThreshold = 3f;

    [Tooltip("Cuanto mas puede tardar un waypoint de flanqueo respecto de ir derecho, y aun asi " +
             "valer la pena.\n\n" +
             "1 = solo acepta desvios que no cuesten nada. 1.25 acepta llegar un 25% mas tarde a " +
             "cambio de terminar en un lugar desde donde te VE. Muy alto y deja de perseguirte " +
             "para irse a pasear por posiciones bonitas.\n\n" +
             "No se aplica cuando no hay camino completo hasta la creencia: ahi cualquier waypoint " +
             "alcanzable con linea de vision es mejor que quedarse pegado contra una pared, que es " +
             "lo que hacia antes.")]
    [SerializeField, Min(1f)] private float chaseDetourTolerance = 1.25f;

    [Header("Belief trace, memory and the lift")]
    //
    // The interception that lived under this header ("cut the player off at a waypoint ahead of
    // them") was taken out on 27/09 (plan D24): it almost never won the race, and when it did it
    // sent the Nemesis to a waypoint instead of after the player.

    [Tooltip("How far from a detection a waypoint may sit and still be marked as 'this is where " +
             "I sensed them'. Roughly the spacing between neighbouring waypoints. Read by the " +
             "pursuit's trail penalty and the patrol's bias, not by the search.")]
    [SerializeField, Min(0.5f)] private float beliefTraceRadius = 3f;

    [Tooltip("Seconds over which a sighting or a noise stops steering the patrol.\n\n" +
             "At 0 seconds old the player bias applies at full RoutePlayerBiasStrength; by this " +
             "many seconds it is gone and the roll falls back to the route weights you authored. " +
             "It is what stops the Nemesis orbiting the room it lost you in for the rest of the " +
             "run.\n\n" +
             "Only has any effect while BiasUsesLastKnownPosition is on — with it off the bias " +
             "reads the player's live position, which is never stale.")]
    [SerializeField, Min(1f)] private float beliefMemoryTime = 45f;

    [Tooltip("Maximum seconds the Nemesis waits at a landing for the freight elevator to free up " +
             "or finish a trip.\n\n" +
             "This is the safety net for 'the player is riding the lift right now': once it runs " +
             "out, the Nemesis abandons the link and paths whatever other way it can, instead of " +
             "standing at the doors forever. Keep it comfortably above one full ride, or it gives " +
             "up on trips that were about to work.")]
    [SerializeField, Min(1f)] private float elevatorWaitTimeout = 20f;

    [Tooltip("Seconds the Nemesis ignores a freight elevator after giving up on it.\n\n" +
             "Without it, abandoning the link and re-evaluating it are the same frame: the agent " +
             "is still standing on the link, so the next Update starts the whole wait over, times " +
             "out again, and the Nemesis spends the rest of the run cycling at the landing " +
             "without ever moving. The stuck watchdog cannot save it either — a body held at a " +
             "landing on purpose is not one it counts as stuck.\n\n" +
             "Keep it long enough for the agent to actually walk away and commit to another " +
             "route, or it steps off the link and immediately steps back on.")]
    [SerializeField, Min(0f)] private float elevatorAbandonCooldown = 10f;

    [Header("Stuck detection")]
    [Tooltip("How long the Nemesis has to make no progress before it counts as stuck and warps " +
             "itself out.")]
    [SerializeField, Min(0.5f)] private float stuckCheckInterval = 3f;

    [Tooltip("Distance it has to cover within stuckCheckInterval to count as making progress.")]
    [SerializeField, Min(0.05f)] private float stuckMinDistance = 0.5f;

    [Tooltip("Segundos que le da al Nemesis para arrancar después de recalcularle el camino, " +
             "antes de teletransportarlo.\n\n" +
             "El teleport tapa el síntoma; casi siempre el problema real es un path corrupto, no " +
             "un camino bloqueado — y a un path corrupto lo arregla pedirlo de nuevo. Por eso el " +
             "watchdog primero le hace ResetPath + SetDestination al mismo destino y le da esta " +
             "ventana; recién si tampoco así se mueve, lo saca de ahí.\n\n" +
             "Corta a propósito (menor que stuckCheckInterval): ya se comió el intervalo entero " +
             "sin moverse, así que esto es una segunda oportunidad, no otra espera igual de " +
             "larga. Muy corta y el agente no llega ni a terminar de calcular el camino nuevo.")]
    [SerializeField, Min(0.2f)] private float stuckRepathGrace = 1.5f;

    [Tooltip("Waypoints closer than this to the player are not eligible when repositioning after " +
             "a capture, so the Nemesis does not warp on top of the player it just respawned.")]
    [SerializeField, Min(0f)] private float repositionMinPlayerDistance = 15f;

    [Tooltip("Spawn points closer than this to the player are never used when the Nemesis first " +
             "spawns in. Reuses the reposition distance above so there is one answer to \"too " +
             "close\", not two that can drift apart.\n\n" +
             "This one is a HARD floor: unlike the occlusion and angle tests, it is never relaxed. " +
             "A spawn that is merely visible is survivable; one that lands in the player's lap is " +
             "not.")]
    [SerializeField, Min(0f)] private float spawnMinPlayerDistance = 15f;

    [Tooltip("Half-angle of the cone in front of the player that counts as \"in view\", in " +
             "degrees. A spawn point inside it is rejected even with clear distance, because " +
             "occlusion alone does not stop the Nemesis popping into existence down an open " +
             "corridor the player happens to be facing.\n\n" +
             "90 = the whole front hemisphere. Lower it to allow spawns closer to the edge of " +
             "vision; 0 disables the test.")]
    [SerializeField, Range(0f, 180f)] private float spawnSafeHalfAngle = 90f;

    [Header("Player feedback")]
    [Tooltip("Distance at which the proximity vignette starts to show. Independent of the " +
             "vision range: tension has to rise even if the Nemesis has never seen you. " +
             "A bit larger than the FieldOfView's viewRange (10 in the prefab) works well.")]
    [SerializeField] private float proximityRadius = 12f;

    [Header("Patrol routes - mirar alrededor")]
    [Tooltip("Cuántos grados hacia cada lado barre la MIRADA mientras el Nemesis espera parado en " +
             "un waypoint.\n\n" +
             "La mirada es independiente del cuerpo: el NavMeshAgent rota al Nemesis hacia donde " +
             "camina, así que sin esto la pausa en un waypoint es el monstruo clavado mirando el " +
             "pasillo por el que vino. Con esto, la espera pasa a leerse como que está revisando.\n\n" +
             "0 lo apaga y la mirada queda pegada al frente del cuerpo, como antes.")]
    [SerializeField, Range(0f, 180f)] private float scanHalfAngle = 50f;

    [Tooltip("Grados por segundo a los que gira la mirada durante ese barrido.\n\n" +
             "Rápido se lee como nervioso y además hace la periferia inútil (te barre por encima " +
             "antes de que la sospecha llegue a subir). Lento se lee pesado y deliberado, y le da " +
             "tiempo a la sospecha a acumularse - que es lo que querés que pase.")]
    [SerializeField, Min(1f)] private float scanSpeed = 60f;

    [Header("Patrol routes (Tier 3.1)")]
    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of walking the active " +
             "route in the opposite direction for that cycle.")]
    [SerializeField, Range(0f, 1f)] private float routeReverseChance = 0.15f;

    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of skipping the very " +
             "next waypoint on the first hop of that cycle (advances two waypoints instead of " +
             "one). Rolled independently from routeReverseChance, so both can land together.")]
    [SerializeField, Range(0f, 1f)] private float routeSkipWaypointChance = 0.15f;

    [Header("Patrol routes — player bias")]
    [Tooltip("How often, in seconds, the Nemesis re-picks its route without having left " +
             "Patrolling. Without this the roll only runs once, on entering the state, and a long " +
             "patrol feels dead: the bias was computed three minutes ago.\n\n" +
             "0 disables periodic replanning (the old behaviour).")]
    [SerializeField, Min(0f)] private float routeReplanInterval = 25f;

    [Tooltip("How many times more likely a zone becomes when the player is on top of it. " +
             "1 = no bias, the roll uses only the inspector weight.\n\n" +
             "It stays a roll: never 'always the nearest zone', just more tickets in the draw. " +
             "Distance is measured over the NavMesh and not in a straight line, so a zone one " +
             "floor up counts as far even when it is 4 metres away.")]
    [SerializeField, Min(1f)] private float routePlayerBiasStrength = 3f;

    [Tooltip("Metres of path beyond which the player bias no longer applies. Smaller = the " +
             "Nemesis only prioritises when it is genuinely close.")]
    [SerializeField, Min(1f)] private float routePlayerBiasFalloff = 40f;

    [Tooltip("Bias against the last known position (what the Nemesis saw or heard) instead of the " +
             "player's real position.\n\n" +
             "On is the fair option and what Mr. X does: it chases the memory, not the truth. Off " +
             "makes it omniscient and stops the patrol feeling like a patrol — it starts feeling " +
             "remote-controlled.")]
    [SerializeField] private bool biasUsesLastKnownPosition = true;

    [Header("Patrol routes — cúmulos (clusters)")]
    [Tooltip("Patrol by ZONE instead of by waypoint: the Nemesis picks a cúmulo of nearby " +
             "waypoints, sweeps it, and only then moves on to another one — preferring a cúmulo " +
             "next door, so it migrates through the level instead of jumping across it.\n\n" +
             "Off restores the old behaviour: one waypoint picked at a time out of the whole " +
             "merged set, with Cross Route Transfer Chance rolled on every arrival. That is what " +
             "made the patrol read as teleporting — two consecutive waypoints of a route in this " +
             "level can be thirty metres apart.\n\n" +
             "With this on, Cross Route Transfer Chance is ignored: a cúmulo is spatial, so it " +
             "already mixes whatever routes cover that corner of the level.")]
    [SerializeField] private bool clusterPatrolEnabled = true;

    [Tooltip("Metres. How far from a cúmulo's centre a waypoint may sit and still belong to it — " +
             "i.e. how big a 'zone' is.\n\n" +
             "Tune it against the level, not against a feeling: it should be about the size of a " +
             "room or a stretch of corridor. Too small and every waypoint becomes its own cúmulo, " +
             "which is the old behaviour with extra steps (the graph warns when that happens). " +
             "Too large and one cúmulo swallows the floor, and the Nemesis never appears to leave.")]
    [SerializeField, Min(1f)] private float clusterRadius = 12f;

    [Tooltip("Ceiling on how many waypoints one cúmulo may hold, so a densely marked room does " +
             "not absorb everything within the radius.")]
    [SerializeField, Range(2, 12)] private int maxClusterSize = 5;

    [Tooltip("Fewest waypoints of a cúmulo the Nemesis visits before moving on. A cúmulo with " +
             "fewer members than this is simply swept whole.")]
    [SerializeField, Min(1)] private int clusterMinWaypoints = 3;

    [Tooltip("Most waypoints of a cúmulo the Nemesis visits before moving on. Rolled between the " +
             "minimum and this on each cúmulo, so it does not spend the same amount of time in " +
             "every zone.")]
    [SerializeField, Min(1)] private int clusterMaxWaypoints = 6;

    [Tooltip("How many times more likely the NEXT cúmulo is when it is right next door.\n\n" +
             "This is the knob that turns the patrol into a walk through the level instead of a " +
             "series of jumps: at 1 the next zone is drawn from anywhere on the island with no " +
             "preference at all. It only applies when finishing a cúmulo — entering Patrolling " +
             "fresh (after a chase, say) is deliberately free to relocate anywhere.")]
    [SerializeField, Min(1f)] private float clusterNeighbourBias = 4f;

    [Tooltip("Metres of path beyond which a cúmulo stops counting as 'next door'. Measured over " +
             "the NavMesh, so a zone one floor up is as far as the walk to the lift makes it.")]
    [SerializeField, Min(1f)] private float clusterNeighbourFalloff = 25f;

    [Tooltip("Cuánto se le recorta el peso a una zona por haber sido barrida hace poco. " +
             "1 = sin penalización, 0.25 = le quedan la cuarta parte de los boletos.\n\n" +
             "Excluir sólo la zona recién terminada no alcanza para que el Nemesis deje de hacer " +
             "A-B-A-B entre dos vecinas: apenas se va de B, A vuelve a ser candidata a peso " +
             "completo, y encima el sesgo de vecindad la favorece justamente por estar al lado. " +
             "Esto es lo que convierte 'andá a otro lado' en 'andá a donde no acabás de estar'.")]
    [SerializeField, Range(0f, 1f)] private float clusterRecencyPenalty = 0.25f;

    [Tooltip("A cuántas zonas recién barridas se les aplica la penalización.\n\n" +
             "0 la apaga. Subirlo cerca de la cantidad de cúmulos de la isla deja al Nemesis sin " +
             "candidatas sin penalizar, y ahí la penalización se anula sola — todas pesan poco, " +
             "que es lo mismo que si ninguna pesara poco.")]
    [SerializeField, Min(0)] private int clusterRecencyMemory = 2;

    [Header("Patrol routes — auto-generated sweep points")]
    //
    // WHAT THESE FIX. A cúmulo's sweep is its member waypoints and nothing else, so a room marked
    // with ONE waypoint produces a tour of one stop: the Nemesis walks to it, the tour runs out
    // immediately, and it leaves. It never prowls that room, however much the cluster settings say
    // it should. Getting a room actually swept meant hand-placing four or five waypoints in it,
    // which is authoring work that says nothing a single marker did not already say.
    //
    // These satellites are generated on the NavMesh around each authored waypoint at graph build
    // time, and they join the cúmulo's TOUR only. They deliberately do NOT become graph nodes:
    // they never enter the cluster's centroid or its weight (which would quietly re-aim the zone
    // bias), never enter the per-waypoint patrol roll, the pursuit's detour candidates, the
    // search, or the sensed trail. One authored waypoint still means one waypoint everywhere the
    // Nemesis reasons about the level — it just means a small AREA when it comes to walking it.

    [Tooltip("How many extra sweep points to generate around each patrol waypoint.\n\n" +
             "0 turns the feature off and restores the behaviour where a cúmulo's sweep is its " +
             "authored waypoints and nothing else. At 2-3 a single waypoint marks a room the " +
             "Nemesis will actually prowl instead of clipping the corner of. Raising it does not " +
             "make it sweep longer — that is Cluster Max Waypoints, which caps the stops per " +
             "visit — it makes the stops it does take more varied.")]
    [SerializeField, Range(0, 6)] private int waypointSatellites = 3;

    [Tooltip("How far, in metres, generated points may sit from the waypoint they belong to.\n\n" +
             "Think of it as how big a room one marker is claiming. Too small and the satellites " +
             "cluster on top of the waypoint and the sweep looks like pacing; too large and they " +
             "leak into the next room and the Nemesis appears to wander off mid-sweep. Points are " +
             "snapped to the NavMesh and dropped if they cannot be reached from the waypoint, so " +
             "an over-large radius wastes generation attempts rather than producing bad stops.")]
    [SerializeField, Min(1f)] private float waypointSatelliteRadius = 4f;

    [Header("Patrol routes — zone gravitation (director bias)")]
    //
    // READ THIS BEFORE TUNING IT. Everything else in this asset is measured off what the Nemesis
    // SENSED. This one is not: it reads the player's live transform, so it is knowledge the
    // Nemesis did not earn, and it is here on purpose.
    //
    // The reason is that RoutePlayerBiasStrength below could never do what it was asked to. It is
    // gated on TryGetPlayerBeliefPosition, which returns false until the player has been seen or
    // heard AT LEAST ONCE, and it is then scaled by BeliefFreshness, which decays to nothing over
    // BeliefMemoryTime. On a cold patrol — the whole first stretch of a run — the bias was exactly
    // zero and the Nemesis wandered by route weight alone.
    //
    // What keeps this honest is WHERE it applies. It biases the choice of CÚMULO and nothing
    // finer: which corner of the level to prowl, never which waypoint to stand on. Combined with a
    // wide falloff it reads as the monster drifting your way, which is the intent. Pushed too high
    // it reads as the monster seeing through walls, which is the failure. The per-waypoint roll
    // stays on the belief, and should.

    [Tooltip("Let the patrol's ZONE choice gravitate towards where the player actually is, " +
             "instead of only towards what the Nemesis has sensed.\n\n" +
             "Off restores the shipped behaviour exactly: with no sighting and no noise, the " +
             "patrol is an unbiased roll over your authored route weights. On, it drifts your way " +
             "from the first second of the run. This is a director bias — it is deliberately " +
             "unfair, and deliberately coarse.")]
    [SerializeField] private bool zoneBiasUsesRealPlayer = true;

    [Tooltip("How many times more likely the cúmulo containing the player is to be drawn.\n\n" +
             "It has to compete with Cluster Neighbour Bias, which pulls towards whatever zone is " +
             "next door — at equal strength the two roughly cancel and the drift is invisible. " +
             "1 disables the gravitation without touching the toggle above.")]
    [SerializeField, Min(1f)] private float zonePlayerBiasStrength = 4f;

    [Tooltip("Metres of path beyond which the zone gravitation no longer applies.\n\n" +
             "Deliberately wide. This is gravitation, not aim: a narrow falloff turns a drift " +
             "towards your side of the level into a beeline for your room. Distance is measured " +
             "over the NavMesh, so a zone one floor up counts as far even when it is 4 metres " +
             "away.")]
    [SerializeField, Min(1f)] private float zonePlayerBiasFalloff = 40f;

    [Tooltip("Segundos después de una caza (Chasing, Searching o Catch) durante los que la gravitación " +
             "de arriba NO mira tu posición real (D40). La patrulla de después la maneja lo que " +
             "percibió. Tampoco la mira mientras estás escondido. Por defecto, lo que dura la " +
             "memoria de la creencia (Belief Memory Time): mientras cree saber dónde estás, no hace " +
             "trampa. 0 la deja volver apenas termina la caza.")]
    [SerializeField, Min(0f)] private float huntGraceSeconds = 45f;

    [Header("Patrol routes — cross-route transfer")]
    [Tooltip("Chance, on each waypoint arrival, of jumping to a waypoint on ANOTHER unlocked " +
             "route instead of following the current route in order.\n\n" +
             "This is what lets it change floor without waiting for the route roll to hand it the " +
             "upper one: if another route has a reachable waypoint on level 1, it can borrow it " +
             "and adopt that route from there. 0 locks it inside its own route, as before.\n\n" +
             "IGNORED while Cluster Patrol Enabled is on — see that field.")]
    [SerializeField, Range(0f, 1f)] private float crossRouteTransferChance = 0.3f;

    [Tooltip("How many candidate waypoints are evaluated with real path distance on each pick. " +
             "The rest are discarded by a straight-line prefilter, which is free.\n\n" +
             "Each candidate costs two path queries. 8 is generous for a level this size; raising " +
             "it is only needed with a great many waypoints packed close together.")]
    [SerializeField, Range(2, 24)] private int waypointBiasSampleCount = 8;

    [Header("Capture")]
    [Tooltip("Real horizontal distance at which the Nemesis can grab the player.\n\n" +
             "Checked in addition to the agent's path because remainingDistance lies when the " +
             "path is partial: with the player sealed off behind a wall, the agent reaches the " +
             "closest point it can and remainingDistance drops to zero. Without this check that " +
             "fires the capture through the wall.")]
    [SerializeField, Min(0.5f)] private float catchMaxReach = 2f;

    [Tooltip("Maximum height difference allowed for a grab. Prevents capture between floors when " +
             "the player is directly above or below the Nemesis.")]
    [SerializeField, Min(0.5f)] private float catchMaxVerticalOffset = 1.5f;

    [Tooltip("Require a clear line of sight (no wall in between) to grab. Uses the " +
             "FieldOfListening's obstacleMask, the same one that already filters sound.")]
    [SerializeField] private bool catchRequiresLineOfSight = true;

    [Header("Extreme proximity detection")]
    [Tooltip("Whether hard proximity detection (proximityDetectionRange) also respects walls.\n\n" +
             "Off, the Nemesis detects you through a thin wall just by standing on the other side, " +
             "which is how it ends up grabbing you without ever having seen you. On, it still " +
             "ignores the vision cone and still defeats Hidden — it only asks that there be no " +
             "geometry in between.")]
    [SerializeField] private bool proximityDetectionRespectsWalls = true;

    [Header("Proximity vignette (HUD)")]
    [Tooltip("Measure HUD proximity over the NavMesh instead of in a straight line.\n\n" +
             "This is the fix for 'the threat UI shows up when it is on another floor': in a " +
             "straight line the Nemesis one floor below is 4 metres away and lights the vignette " +
             "up to maximum, when it is really half a storey of walking away.")]
    [SerializeField] private bool proximityUsesPathDistance = true;

    [Tooltip("How often that path distance is recomputed. It does not need to be per frame: the " +
             "vignette interpolates between measurements.")]
    [SerializeField, Min(0.05f)] private float proximityRecalcInterval = 0.2f;

    [Header("Chase - progress (el loop de la mesa)")]
    //
    // At the end rather than beside the other chase knobs, so this change is a pure addition to
    // both the inspector and the asset file. The initialisers are the plan's starting values
    // (docs/Plan-IA-Stalker.md §12) and they matter: they are what an asset saved before these
    // fields existed deserialises to. Without them it would get zeros — a trail penalty of 0 is a
    // veto, not the plan's x0.2 — and nothing would say so.
    //
    // Corriendo, el jugador (4.5 m/s) siempre le gana al Nemesis (3.0 m/s), así que dar vueltas
    // alrededor de una mesa es una persecución que no puede terminar. Estos knobs dicen cuándo el
    // Nemesis se da cuenta de que no está acortando distancia (NemesisChaseProgress) y qué hace
    // NemesisPursuit mientras tanto: marcar el camino por donde vino el jugador y aceptar
    // desvíos más largos, para que la ruta salga por el otro lado. Nunca lo hace más rápido.

    [Tooltip("Segundos que tiene una persecución para acortar Chase Min Progress antes de " +
             "contar como estancada.\n\n" +
             "Solo corre mientras está en Chasing y lo vio hace menos de Vision Loss Grace " +
             "Period; sin vista se PAUSA (F9 dice 'pausada') y no avanza. Cada ventana que vence " +
             "sin progreso mantiene el estancamiento, pero UNA persecución suma un solo " +
             "'ChaseStalled' a los hábitos: el de la primera ventana (ver Chase Stall Regroup " +
             "Time). F9 muestra cuántas ventanas van y que ya se contó.\n\n" +
             "Más corto y cualquier persecución con una esquina de por medio se marca como " +
             "estancada; más largo y el loop de la mesa dura eso de más antes de que reaccione.")]
    [SerializeField, Min(0.5f)] private float chaseProgressWindow = 4f;

    [Tooltip("Metros, medidos por NavMesh (no en línea recta: hay pisos), que la distancia " +
             "hasta el jugador tiene que bajar dentro de la ventana para que cuente como " +
             "progreso.\n\n" +
             "Se compara contra la distancia al ABRIR la ventana, no contra la mejor lectura: " +
             "en un loop se acerca de un lado y se aleja del otro, y eso no es progreso. Llegar " +
             "al alcance de captura (Catch Max Reach) cuenta siempre como progreso.")]
    [SerializeField, Min(0.05f)] private float chaseMinProgress = 1.5f;

    [Tooltip("Metros que puede cambiar la distancia por NavMesh entre dos mediciones seguidas " +
             "(cada Route Verdict Interval) para que se la tome por movimiento de verdad. Si " +
             "cambia más que esto, lo que cambió es el CAMINO (el jugador pisó una pasarela cuyo " +
             "acceso queda lejos, se selló una puerta, se activó un vínculo), no el terreno que " +
             "ganó o perdió: esa medición no se juzga y pasa a ser la nueva referencia de la " +
             "ventana.\n\n" +
             "Caso que lo motivó: jugador a 2.9 m en línea recta y a 23.2 m por NavMesh en una " +
             "sola medición, leído como 'se alejó 20 m' y contado como estancamiento. Tiene que " +
             "ser bastante más que lo que recorren los dos en un intervalo (unos 3 m a 0.4 s). " +
             "Más chico y una carrera normal se toma por salto; más grande y un cambio de camino " +
             "chico vuelve a pasar por progreso o por estancamiento.")]
    [SerializeField, Min(1f)] private float chasePathJumpDistance = 10f;

    [Tooltip("Segundos fuera de Chasing que cierran una persecución a efectos de los hábitos: " +
             "una persecución suma UN solo 'ChaseStalled', por larga que sea, y recién cuenta " +
             "otro cuando pasa más que esto sin que esté persiguiendo (o cuando te agarra).\n\n" +
             "Existe para el titileo Chasing / Searching: perderte de vista un segundo y volver a " +
             "verte no es otra persecución. Tiene que pasar una búsqueda de verdad entre dos " +
             "loops para que cuenten dos. 0 = cada vez que sale de Chasing cuenta como una nueva.")]
    [SerializeField, Min(0f)] private float chaseStallRegroupTime = 10f;

    [Tooltip("Reemplaza a Chase Detour Tolerance mientras la persecución está estancada: cuánto " +
             "más puede tardar un desvío por un waypoint respecto de ir derecho.\n\n" +
             "Sube para que 'el otro lado' del obstáculo entre en el presupuesto; con la " +
             "tolerancia normal casi nunca entra, porque ir derecho al jugador que da vueltas " +
             "siempre parece corto. Nunca baja la normal: si ponés menos, se usa la normal.")]
    [SerializeField, Min(1f)] private float chaseStagnantDetourTolerance = 2.5f;

    [Tooltip("Multiplicador del peso de los waypoints de desvío que están sobre el rastro " +
             "sensado (por donde se lo sintió pasar al jugador), mientras la persecución está " +
             "estancada.\n\n" +
             "0.2 = esos waypoints tienen 5 veces menos chances en el sorteo, y lo que queda es " +
             "el otro lado del obstáculo. 1 apaga la contra-jugada. 0 los veta del todo, y " +
             "entonces si el único waypoint con vista al jugador está sobre el rastro, sigue " +
             "persiguiéndolo por atrás.")]
    [SerializeField, Range(0f, 1f)] private float chaseTrailPenalty = 0.2f;

    [Tooltip("Metros (en planta, sin contar pisos) alrededor de cada waypoint del rastro dentro " +
             "de los cuales un waypoint de desvío cuenta como 'por donde vino'.\n\n" +
             "Del orden de la distancia entre waypoints vecinos (Belief Trace Radius). Si es más " +
             "grande que el obstáculo, marca los DOS lados y ya no queda un 'otro lado' que " +
             "elegir. Los gizmos lo dibujan alrededor del rastro durante la persecución.")]
    [SerializeField, Min(0f)] private float chaseTrailPenaltyRadius = 3f;

    [Header("Investigation - revisar el ruido (DIS-002 / WIR-006)")]
    //
    // Al final y no junto a InvestigationTimeOut, igual que el bloque de arriba: así el cambio es
    // un agregado puro al inspector y al asset.
    [Tooltip("Segundos que se queda mirando alrededor cuando llega a donde escuchó el ruido, antes " +
             "de volver a patrullar.\n\n" +
             "Antes llegar era terminar: con un ruido a menos de ~4 m llegaba en un segundo y se " +
             "iba (DIS-002). Mientras dura, barre la mirada como en una pausa de búsqueda.")]
    [SerializeField, Min(0f)] private float investigationDwellTime = 4f;

    [Tooltip("Cada cuánto, como mucho, cambia de destino si sigue escuchando mientras camina.\n\n" +
             "Investigar es ir a DONDE lo escuchó, no seguirlo en vivo: re-apuntar en cada " +
             "barrido del oído lo convertía en una persecución sin feedback (WIR-006).")]
    [SerializeField, Min(0.1f)] private float investigationRetargetInterval = 1.5f;

    [Tooltip("Metros que tiene que estar el ruido nuevo del destino actual para que valga la pena " +
             "cambiar. Por debajo, sigue yendo al mismo punto.")]
    [SerializeField, Min(0f)] private float investigationRetargetDistance = 3f;

    [Header("Escondites - lo que sabe el Nemesis (Fase 2)")]
    [Tooltip("Cuánto ve debajo de una MESA, como fracción de View Range. La mesa no ciega: acorta " +
             "la vista (spec de escondites §3). Nunca es instantáneo: pasa por el acumulador de la " +
             "periferia, y si se llena no arranca una persecución, marca el escondite como conocido.")]
    [SerializeField, Range(0f, 1f)] private float underTableVisionMultiplier = 0.5f;

    [Tooltip("Segundos antes de esconderse en los que, si te vio, sabe en qué escondite te metiste " +
             "(plan §3.4, Nivel A: \"te vi entrar\"). Se cuenta desde el FINAL de la subida, así " +
             "que tiene que ser al menos la subida (SO_HidingData.EnterDuration, 0.6) más un barrido " +
             "de la vista (0.1): con 0.6 justo, visto sólo en el primer barrido de la subida no " +
             "cuenta. Además tiene que ver la puerta del escondite (línea de vista).")]
    [SerializeField, Min(0f)] private float seenEnteringWindow = 0.75f;

    [Tooltip("Segundos que tarda en sacarte de un escondite antes de la captura: abrir el locker, " +
             "agacharse bajo la mesa. Es el golpe de efecto de ver al monstruo en la puerta, NO una " +
             "ventana para escapar: salir en ese momento te deja en sus manos (plan D1, captura). " +
             "El margen real del jugador es antes, mientras lo ve acercarse.")]
    [SerializeField, Min(0f)] private float hiddenPullOutTime = 0.8f;

    [Header("Creencia (plan §17)")]
    [Tooltip("Radio de la creencia cuando la ancla una vista, en metros. Un avistamiento es una " +
             "posición: casi cero.")]
    [SerializeField, Min(0.05f)] private float beliefSightRadius = 0.5f;

    [Tooltip("Radio base de la creencia cuando la ancla un ruido del jugador, en metros, antes de " +
             "sumar la distancia y lo que el ruido tuvo que atravesar. Un ruido es 'por ahí'.")]
    [SerializeField, Min(0.05f)] private float beliefNoiseBaseRadius = 1f;

    [Tooltip("Cuánto crece el radio de un ruido por cada metro de distancia (medida por NavMesh): " +
             "un paso a tu lado es un punto; el mismo paso desde la otra punta de la sala, una zona.")]
    [SerializeField, Min(0f)] private float beliefNoiseRadiusPerMetre = 0.15f;

    [Tooltip("Multiplicador del radio de un ruido que atravesó una pared (o la carcasa de un " +
             "escondite): amortiguado, se sabe peor de dónde vino.")]
    [SerializeField, Min(1f)] private float beliefNoiseWallFactor = 1.5f;

    [Tooltip("Multiplicador del radio de un ruido que atravesó un piso.")]
    [SerializeField, Min(1f)] private float beliefNoiseFloorFactor = 1.3f;

    [Tooltip("A qué velocidad crece el radio de la creencia sin evidencia nueva, en m/s: hasta dónde " +
             "pudo haber llegado el jugador. La velocidad del jugador corriendo (4.5). Con evidencia " +
             "que coincide el radio se achica: así suman los sentidos.")]
    [SerializeField, Min(0f)] private float beliefGrowthSpeed = 4.5f;

    [Tooltip("Multiplicador del radio de un ruido del jugador que sale de un escondite (plan D22): " +
             "la respiración, un suspiro, la exhalación después de aguantar. Amortiguado por el " +
             "mueble, marca la zona y no la puerta. 1 lo apaga y vuelve a ir derecho al escondite.")]
    [SerializeField, Min(1f)] private float beliefNoiseHidingSpotFactor = 2f;

    [Tooltip("Cuánto se equivoca el oído al ubicar un ruido tuyo, en fracciones del radio de esa " +
             "evidencia (Plan Búsqueda, Fase 1, D39). El sensor nunca entrega tu posición real: " +
             "entrega la real más un desvío de hasta esto × el radio, pegado al NavMesh de tu piso. " +
             "Al lado suyo (radio ~1.5 m) sigue siendo preciso; a 10 m a través de una pared, ±2–3 m. " +
             "0 vuelve al oído que sabe exactamente dónde estás (WIR-057).")]
    [SerializeField, Range(0f, 1f)] private float hearingLocalizationError = 0.6f;

    [Tooltip("Cada cuántos segundos cambia, más o menos, hacia dónde se equivoca el oído. El desvío " +
             "se mueve suave: el mismo ruido oído dos veces seguidas suena desde casi el mismo lugar " +
             "equivocado. Más corto, el error se promedia solo al oírte muchas veces.")]
    [SerializeField, Min(0.1f)] private float hearingErrorDriftTime = 4f;

    [Header("Búsqueda: el punto o la zona de una evidencia")]
    //
    // La búsqueda ya no barre un disco (Plan Búsqueda, Fase 2b): va adonde el mapa de búsqueda tiene
    // valor. Lo que queda acá es la regla de la Fase 1: cuándo la búsqueda puede pararse en el punto
    // mismo de la evidencia y cuándo ese punto es solo "por ahí".

    [Tooltip("Radio de evidencia (m) hasta el que un ruido tuyo cuenta como preciso: lo oyó al lado " +
             "suyo. Solo entonces la búsqueda puede pararse en ese punto mismo, como hace con una " +
             "vista. Con un ruido más vago el punto es solo \"por ahí\": busca los lugares de la zona " +
             "que marca el mapa y nunca camina al último paso, que suele ser la puerta del escondite " +
             "(Plan Búsqueda, Fase 1).")]
    [SerializeField, Min(0f)] private float searchPreciseNoiseRadius = 1.5f;

    [Header("Bajadas entre pisos (plan §15)")]
    [Tooltip("Segundos que una bajada queda fuera de las rutas después de usarla (o de abandonarla " +
             "porque el jugador estaba arriba). Evita el loop \"sube por la escalera, se tira, sube, " +
             "se tira\" si el jugador da vueltas entre pisos. El tiempo, la velocidad y el costo de " +
             "cada fase están en SO_NemesisMovement.")]
    [SerializeField, Min(0f)] private float dropLinkCooldown = 8f;

    [Header("Búsqueda que se enfría (plan §18.5 B, Fase 2B parte 3)")]
    //
    // La búsqueda ya no dura Search Time Out fijo: sigue mientras el SILENCIO (segundos desde la
    // última evidencia del jugador, sin contar pistas ni lo que se oye desde el Hub) sea menor que
    // la ventana × la calidad de esa evidencia, con un mínimo y un tope. El Director estira o acorta
    // la ventana y el tope según el ritmo (persistencia, préstamo de números): por eso se leen de este
    // SO y no de constantes.
    //
    // También termina, pasado el mínimo, cuando "revisó todo". Desde la Fase 2b del Plan Búsqueda eso
    // ya no es "barrió el disco entero": es que ningún lugar vale la caminata (el umbral está en Mapa
    // de búsqueda, Search Map Worth Threshold).

    [Tooltip("Segundos que la búsqueda dura siempre, pase lo que pase: siempre mira un poco. Tiene " +
             "que quedar por debajo de Max Hold Seconds (SO_HidingData, 8 s): si no, quedarse sin aire " +
             "en el escondite te delata siempre (D21).")]
    [SerializeField, Min(0f)] private float searchMinTime = 6f;

    [Tooltip("Segundos de silencio que tolera antes de dejar de buscar. Cada paso o exhalación tuya " +
             "que oye lo vuelve a cero. El Director lo multiplica según el ritmo (Relax lo acorta, la " +
             "sensibilidad creciente lo estira).")]
    [SerializeField, Min(0.5f)] private float searchQuietWindow = 8f;

    [Tooltip("Tope de la búsqueda en segundos, aunque te siga oyendo. 0 = SIN TOPE, y así viene " +
             "(03/10): busca mientras le siga llegando evidencia tuya y termina cuando el silencio pasa " +
             "la ventana, o cuando revisó todo. Con un tope mayor que 0, un jugador que hace ruido sin " +
             "dejarse ver deja de tenerlo buscando al llegar al tope (y si te oye, va a investigar). El " +
             "Director y la escalada lo escalan.")]
    [SerializeField, Min(0f)] private float searchHardCap = 0f;

    [Tooltip("Cuánto estira la ventana de silencio una evidencia de VISTA: te vio, insiste más.")]
    [SerializeField, Min(0.1f)] private float searchQualitySight = 1.25f;

    [Tooltip("Cuánto la acorta un ruido que atravesó una pared, un piso o un escondite: te oyó " +
             "amortiguado, sabe menos.")]
    [SerializeField, Min(0.1f)] private float searchQualityMuffled = 0.75f;

    [Tooltip("Escala del tope cuando la búsqueda viene de investigar un ruido tuyo sin encontrarte " +
             "(D26): una búsqueda corta, no una entera. Sin tope (Search Hard Cap en 0) no hace nada: " +
             "la búsqueda dura lo que dure la evidencia.")]
    [SerializeField, Range(0.1f, 1f)] private float searchEscalatedCapScale = 0.5f;

    [Header("Elección: a qué le presta atención (plan §17.4, Fase 2B parte 4)")]
    //
    // Cada vez que llega algo nuevo (tu evidencia, una pista, un vistazo) NemesisChoice le pregunta a
    // FocusArbiter si vale más que lo que está persiguiendo. Valor = base × confianza × frescura ×
    // costo de llegar × habituación. El nuevo tiene que ganarle al actual por el margen, y lo recién
    // elegido tiene una ventaja que decae. Los casos 25 y 28–33 del plan están calibrados con esto.

    [Tooltip("Valor base de la radio (plan §17.5). Si la elige, se compromete hasta romperla.")]
    [SerializeField, Range(0f, 1f)] private float leadValueRadio = 0.6f;

    [Tooltip("Valor base de la alarma de incendio. Se oye desde cualquier lado: el costo de llegar pesa.")]
    [SerializeField, Range(0f, 1f)] private float leadValueFireAlarm = 0.7f;

    [Tooltip("Valor base de las cadenas. Usos infinitos: la habituación las gasta.")]
    [SerializeField, Range(0f, 1f)] private float leadValueChains = 0.45f;

    [Tooltip("Valor base de cualquier otro ruido que no sos vos (un pulso del Director).")]
    [SerializeField, Range(0f, 1f)] private float leadValueOther = 0.4f;

    [Tooltip("Valor base de un vistazo de reojo (el medidor subiendo, sin llegar a 1).")]
    [SerializeField, Range(0f, 1f)] private float glimpseValue = 0.5f;

    [Tooltip("Multiplicador por cada vez que un señuelo lo hizo ir sin encontrar nada (×0.6: a la " +
             "tercera, unas cadenas del otro lado del nivel ya no lo mueven). Por sesión.")]
    [SerializeField, Range(0.1f, 1f)] private float leadHabituation = 0.6f;

    [Tooltip("Debajo de este valor, una pista o un vistazo no merecen atención (vos sí, siempre).")]
    [SerializeField, Range(0f, 1f)] private float focusAttentionFloor = 0.12f;

    [Tooltip("Cuánto más tiene que valer lo nuevo que lo actual para cambiar.")]
    [SerializeField, Range(0f, 1f)] private float focusMargin = 0.05f;

    [Tooltip("Ventaja de lo recién elegido contra algo de su tipo o menor. Decae a cero en " +
             "Focus Commitment Decay segundos. Con poco titubea; con mucho, no reacciona.")]
    [SerializeField, Range(0f, 1f)] private float focusCommitmentBonus = 0.3f;

    [SerializeField, Min(0.1f)] private float focusCommitmentDecay = 3f;

    [Tooltip("Segundos en que la frescura de algo sentido cae a la mitad.")]
    [SerializeField, Min(0.1f)] private float focusFreshnessHalfLife = 6f;

    [Tooltip("Metros de camino a los que el costo de llegar deja el valor a la mitad.")]
    [SerializeField, Min(1f)] private float focusCostDistance = 60f;

    [Tooltip("Segundos después de cambiar a un tipo durante los que no cambia a otro del mismo tipo: " +
             "dos ruidos alternados no lo hacen ir y venir.")]
    [SerializeField, Min(0f)] private float focusAntiDither = 2f;

    [Tooltip("Metros alrededor de donde cree que estás (su radio, con tope en Room Sweep Radius) en " +
             "los que un señuelo no compite: suma, y la búsqueda lo cubre (caso 33).")]
    [SerializeField, Min(0f)] private float leadSumsMargin = 3f;

    [Header("Sospecha compartida (plan §17.3, Fase 2B parte 4)")]
    //
    // Un ruido SUAVE tuyo (agachado) sube el mismo medidor que un vistazo de reojo: un paso suave y
    // un vistazo juntos lo ponen en sospecha más rápido que cualquiera de los dos solo (caso 26). Un
    // ruido solo nunca llega a ser un avistamiento: el medidor se queda por debajo de 1 sin vista.

    [Tooltip("Radio de emisión (m) hasta el que un ruido tuyo cuenta como suave. El agachado emite 1, " +
             "caminando 4. La respiración desde un escondite no cuenta: tiene sus reglas (D21, D22).")]
    [SerializeField, Min(0f)] private float softNoiseLoudness = 1.5f;

    [Tooltip("Cuánto sube el medidor de sospecha por segundo con un ruido suave, en fracciones de " +
             "Awareness Build Time (un vistazo va de 0.35 a 2 según la distancia).")]
    [SerializeField, Min(0f)] private float softNoiseSuspicionRate = 0.6f;

    [Tooltip("Tope del medidor con ruido solo, sin vistazo: por debajo de 1, para que un ruido nunca se " +
             "vuelva un avistamiento.")]
    [SerializeField, Range(0f, 0.99f)] private float noiseOnlySuspicionCap = 0.9f;

    [Header("Mapa de búsqueda (Plan Búsqueda, Fase 2)")]
    //
    // Una grilla sobre el NavMesh del Nemesis que guarda "qué tan posible es que estés acá". Verte o
    // oírte pone el valor; con el tiempo se esparce por donde se camina, a tu velocidad; lo que está
    // mirando queda en cero. Desde la Fase 2b la BÚSQUEDA DECIDE CON ESTO: va adonde todavía hay
    // valor (Search Map Zone Radius, Candidates, Repick Share y Worth Threshold), y se ve en el gizmo
    // y en F9.

    [Tooltip("Separación entre nodos del mapa, en metros. Más chico es más fino y más caro: el armado " +
             "y cada tick escalan con la cantidad de nodos (≈ área caminable / separación²).")]
    [SerializeField, Range(1f, 4f)] private float searchMapNodeSpacing = 2f;

    [Tooltip("Segundos entre actualizaciones del mapa. No hace falta cada frame.")]
    [SerializeField, Range(0.05f, 1f)] private float searchMapTickInterval = 0.25f;

    [Tooltip("A qué velocidad se esparce el valor por el NavMesh, en m/s. Tu velocidad corriendo " +
             "(4.5, la misma que Belief Growth Speed): el valor corre como correrías vos y nunca " +
             "atraviesa paredes.")]
    [SerializeField, Min(0.1f)] private float searchMapSpreadSpeed = 4.5f;

    [Tooltip("Cuánto más rápido se esparce hacia donde te vio moverte, recién perdido. 1: el doble " +
             "hacia adelante y casi nada para atrás. 0 lo apaga: se esparce parejo.")]
    [SerializeField, Min(0f)] private float searchMapHeadingBias = 1f;

    [Tooltip("Segundos en que el sesgo de rumbo se apaga solo después de la última vista. Después se " +
             "esparce parejo: cuanto más tiempo pasó, menos sabe hacia dónde ibas.")]
    [SerializeField, Min(0f)] private float searchMapHeadingDuration = 3f;

    [Tooltip("Hasta dónde lo que está mirando queda en cero (\"acá no está\"), como fracción de View " +
             "Range, dentro de su cono de visión y con línea de vista. 1 es todo su rango de vista (7 m " +
             "en el asset), y sigue a la escalada y al Director cuando lo agrandan. Un escondite " +
             "cerrado no se limpia mirándolo: solo al abrirlo.")]
    [SerializeField, Range(0f, 1.5f)] private float searchMapClearRangeScale = 1f;

    [Tooltip("Altura sobre el piso a la que apunta el rayo del \"acá no está\", en metros: la de " +
             "alguien agachado. Una caja que tapa a alguien agachado deja ese lugar con valor.")]
    [SerializeField, Min(0.05f)] private float searchMapProbeHeight = 0.6f;

    [Tooltip("Radio de un LUGAR del mapa, en metros. La búsqueda no elige un nodo suelto: junta el " +
             "valor que hay dentro de este radio de un nodo y camina al medio de esa zona. Con nodos " +
             "cada 2 m, 2.5 es un nodo y sus vecinos directos. Más chico, muchos lugares de poco " +
             "valor cada uno (un ruido vago se busca en más puntos); más grande, pocos lugares " +
             "grandes (un ruido vago es casi un solo lugar: va derecho al medio). Conviene que quede " +
             "bien por debajo de lo que limpia mirando (View Range × Search Map Clear Range Scale): " +
             "así, al llegar, ve la zona entera.")]
    [SerializeField, Range(1f, 8f)] private float searchMapZoneRadius = 2.5f;

    [Tooltip("Cuántos lugares compara en cada elección: los de más valor. Cada uno cuesta una " +
             "consulta de camino, y elige una vez por llegada o cuando el lugar al que iba se quedó " +
             "sin valor, nunca por frame. Entre ellos TIRA por valor ÷ (1 + segundos de caminata): " +
             "no va siempre al mejor. No son candidatos un escondite, el Hub, donde ya está parado " +
             "ni un lugar al que solo se llega por el montacargas.")]
    [SerializeField, Range(2, 16)] private int searchMapCandidates = 8;

    [Tooltip("Cuánto del valor que tenía un lugar al elegirlo le tiene que quedar para seguir yendo, " +
             "como fracción. Si baja de esto —lo vio vacío de lejos, o evidencia nueva movió el " +
             "valor— elige otro en vez de caminar hasta un lugar que ya sabe vacío. 0: va siempre " +
             "hasta el final. Cerca de 1: cambia de idea con cada tick del mapa y titubea.")]
    [SerializeField, Range(0f, 0.9f)] private float searchMapRepickShare = 0.35f;

    [Tooltip("Umbral de \"revisé todo\": por debajo de este valor ÷ (1 + segundos de caminata), un " +
             "lugar no vale la caminata. Cuando ningún lugar al que puede llegar a pie lo alcanza —el " +
             "valor quedó muy repartido, o lejos, o adentro de escondites, o se fue al Hub— no camina " +
             "más: mira alrededor donde está, y la búsqueda se enfría en cuanto pasó Search Min Time.\n\n" +
             "Con 0.015 están justo en el límite un lugar con 6 % del valor a 3 s, o con 10 % a 6 s. " +
             "Más alto abandona antes; 0 lo apaga (termina solo por silencio). Si más de la mitad del " +
             "valor se fue al Hub, no camina sea cual sea este número: nunca acampa esa puerta.")]
    [SerializeField, Range(0f, 0.2f)] private float searchMapWorthThreshold = 0.015f;

    public float InvestigationTimeOut { get => investigationTimeOut; set => investigationTimeOut = value; }
    public float SearchTimeOut { get => searchTimeOut; set => searchTimeOut = value; }
    public float VisionLossGracePeriod { get => visionLossGracePeriod; set => visionLossGracePeriod = value; }
    public float PatrolWaypointWaitTime { get => patrolWaypointWaitTime; set => patrolWaypointWaitTime = value; }
    public float PatrolWaitVariance { get => patrolWaitVariance; set => patrolWaitVariance = value; }

    /// <summary>Shortest the Nemesis may wait at a waypoint. Floored at 0: a variance wider than
    /// the base wait would otherwise ask for a negative delay.</summary>
    public float PatrolWaitMin => Mathf.Max(0f, patrolWaypointWaitTime - patrolWaitVariance);

    /// <summary>Longest the Nemesis may wait at a waypoint.</summary>
    public float PatrolWaitMax => Mathf.Max(PatrolWaitMin, patrolWaypointWaitTime + patrolWaitVariance);
    public float NoiseUpdateCooldown { get => noiseUpdateCooldown; set => noiseUpdateCooldown = value; }
    public float ViewRange { get => viewRange; set => viewRange = value; }
    public float ViewAngle { get => viewAngle; set => viewAngle = value; }
    public float FocusAngle { get => focusAngle; set => focusAngle = value; }
    public float AwarenessBuildTime { get => awarenessBuildTime; set => awarenessBuildTime = value; }
    public float AwarenessDecayRate { get => awarenessDecayRate; set => awarenessDecayRate = value; }
    public float AwarenessTriggerThreshold { get => awarenessTriggerThreshold; set => awarenessTriggerThreshold = value; }
    public float ScanHalfAngle { get => scanHalfAngle; set => scanHalfAngle = value; }
    public float ScanSpeed { get => scanSpeed; set => scanSpeed = value; }

    /// <summary>Whether the peripheral band exists at all. False when the designer has widened the
    /// focus cone to (or past) the full view cone, which switches gradual detection off and
    /// restores the old instant-detection behaviour everywhere.</summary>
    public bool HasPeripheralVision => focusAngle < viewAngle;
    public float ProximityDetectionRange { get => proximityDetectionRange; set => proximityDetectionRange = value; }
    public float CrouchVisionMultiplier { get => crouchVisionMultiplier; set => crouchVisionMultiplier = value; }
    public float RearSenseRange { get => rearSenseRange; set => rearSenseRange = value; }
    public float RearSenseStrength { get => rearSenseStrength; set => rearSenseStrength = value; }
    public float GlimpseCorroborationWindow { get => glimpseCorroborationWindow; set => glimpseCorroborationWindow = value; }
    public float ViewHoldScale { get => viewHoldScale; set => viewHoldScale = value; }
    public float ViewHuntScale { get => viewHuntScale; set => viewHuntScale = value; }
    public float ViewHuntGrowTime { get => viewHuntGrowTime; set => viewHuntGrowTime = value; }
    public float ViewHuntSettleTime { get => viewHuntSettleTime; set => viewHuntSettleTime = value; }
    public float LostSightLookAhead { get => lostSightLookAhead; set => lostSightLookAhead = value; }
    public float GazeTurnSpeed { get => gazeTurnSpeed; set => gazeTurnSpeed = value; }

    /// <summary>How far it keeps seeing a standing player it is already seeing, in metres: the view
    /// range as lent right now, times <see cref="ViewHoldScale"/> (never under the view range).
    /// </summary>
    public float ViewHoldRange => viewRange * Mathf.Max(1f, viewHoldScale);

    /// <summary>The furthest it can see again a standing player it lost and is still hunting, in
    /// metres: the view range as lent right now, times <see cref="ViewHuntScale"/>.</summary>
    public float ViewHuntRange => viewRange * Mathf.Max(1f, viewHuntScale);
    public float ListenRange { get => listenRange; set => listenRange = value; }
    public float NoiseRangeScale { get => noiseRangeScale; set => noiseRangeScale = value; }
    public bool WallOcclusionEnabled { get => wallOcclusionEnabled; set => wallOcclusionEnabled = value; }
    public float WallOcclusionMultiplier { get => wallOcclusionMultiplier; set => wallOcclusionMultiplier = value; }
    public float FloorOcclusionMultiplier { get => floorOcclusionMultiplier; set => floorOcclusionMultiplier = value; }
    public float RouteVerdictInterval { get => routeVerdictInterval; set => routeVerdictInterval = value; }
    public float FloorHeightThreshold { get => floorHeightThreshold; set => floorHeightThreshold = value; }
    public float ElevatorCommitTime { get => elevatorCommitTime; set => elevatorCommitTime = value; }
    public float SearchSweepRadius { get => searchSweepRadius; set => searchSweepRadius = value; }
    public float SearchPauseTime { get => searchPauseTime; set => searchPauseTime = value; }
    public float RoomSweepRadius { get => roomSweepRadius; set => roomSweepRadius = value; }
    public float ChaseTimePrediction { get => chaseTimePrediction; set => chaseTimePrediction = value; }
    public float ChaseRouteReplanInterval { get => chaseRouteReplanInterval; set => chaseRouteReplanInterval = value; }
    public float ChaseBeliefMoveThreshold { get => chaseBeliefMoveThreshold; set => chaseBeliefMoveThreshold = value; }
    public float ChaseDetourTolerance { get => chaseDetourTolerance; set => chaseDetourTolerance = value; }
    public float BeliefTraceRadius { get => beliefTraceRadius; set => beliefTraceRadius = value; }
    public float BeliefMemoryTime { get => beliefMemoryTime; set => beliefMemoryTime = value; }
    public float ElevatorWaitTimeout { get => elevatorWaitTimeout; set => elevatorWaitTimeout = value; }
    public float ElevatorAbandonCooldown { get => elevatorAbandonCooldown; set => elevatorAbandonCooldown = value; }
    public bool HearingUsesPathDistance { get => hearingUsesPathDistance; set => hearingUsesPathDistance = value; }
    public float StuckCheckInterval { get => stuckCheckInterval; set => stuckCheckInterval = value; }
    public float StuckMinDistance { get => stuckMinDistance; set => stuckMinDistance = value; }
    public float StuckRepathGrace { get => stuckRepathGrace; set => stuckRepathGrace = value; }
    public float RepositionMinPlayerDistance { get => repositionMinPlayerDistance; set => repositionMinPlayerDistance = value; }
    public float SpawnMinPlayerDistance { get => spawnMinPlayerDistance; set => spawnMinPlayerDistance = value; }
    public float SpawnSafeHalfAngle { get => spawnSafeHalfAngle; set => spawnSafeHalfAngle = value; }
    public float ProximityRadius { get => proximityRadius; set => proximityRadius = value; }
    public float RouteReverseChance { get => routeReverseChance; set => routeReverseChance = value; }
    public float RouteSkipWaypointChance { get => routeSkipWaypointChance; set => routeSkipWaypointChance = value; }
    public float RouteReplanInterval { get => routeReplanInterval; set => routeReplanInterval = value; }
    public float RoutePlayerBiasStrength { get => routePlayerBiasStrength; set => routePlayerBiasStrength = value; }
    public float RoutePlayerBiasFalloff { get => routePlayerBiasFalloff; set => routePlayerBiasFalloff = value; }
    public bool BiasUsesLastKnownPosition { get => biasUsesLastKnownPosition; set => biasUsesLastKnownPosition = value; }
    public bool ClusterPatrolEnabled { get => clusterPatrolEnabled; set => clusterPatrolEnabled = value; }
    public float ClusterRadius { get => clusterRadius; set => clusterRadius = value; }
    public int MaxClusterSize { get => maxClusterSize; set => maxClusterSize = value; }
    public int ClusterMinWaypoints { get => clusterMinWaypoints; set => clusterMinWaypoints = value; }

    /// <summary>Clamped against the minimum rather than trusted: the two are independent fields
    /// and a max typed below the min would make the roll's range empty.</summary>
    public int ClusterMaxWaypoints
    {
        get => Mathf.Max(clusterMinWaypoints, clusterMaxWaypoints);
        set => clusterMaxWaypoints = value;
    }

    public float ClusterRecencyPenalty { get => clusterRecencyPenalty; set => clusterRecencyPenalty = value; }
    public int ClusterRecencyMemory { get => clusterRecencyMemory; set => clusterRecencyMemory = value; }
    public int WaypointSatellites { get => waypointSatellites; set => waypointSatellites = value; }
    public float WaypointSatelliteRadius { get => waypointSatelliteRadius; set => waypointSatelliteRadius = value; }
    public bool ZoneBiasUsesRealPlayer { get => zoneBiasUsesRealPlayer; set => zoneBiasUsesRealPlayer = value; }
    public float ZonePlayerBiasStrength { get => zonePlayerBiasStrength; set => zonePlayerBiasStrength = value; }
    public float ZonePlayerBiasFalloff { get => zonePlayerBiasFalloff; set => zonePlayerBiasFalloff = value; }
    public float ClusterNeighbourBias { get => clusterNeighbourBias; set => clusterNeighbourBias = value; }
    public float ClusterNeighbourFalloff { get => clusterNeighbourFalloff; set => clusterNeighbourFalloff = value; }
    public float CrossRouteTransferChance { get => crossRouteTransferChance; set => crossRouteTransferChance = value; }
    public int WaypointBiasSampleCount { get => waypointBiasSampleCount; set => waypointBiasSampleCount = value; }
    public float CatchMaxReach { get => catchMaxReach; set => catchMaxReach = value; }
    public float CatchMaxVerticalOffset { get => catchMaxVerticalOffset; set => catchMaxVerticalOffset = value; }
    public bool CatchRequiresLineOfSight { get => catchRequiresLineOfSight; set => catchRequiresLineOfSight = value; }
    public bool ProximityDetectionRespectsWalls { get => proximityDetectionRespectsWalls; set => proximityDetectionRespectsWalls = value; }
    public bool ProximityUsesPathDistance { get => proximityUsesPathDistance; set => proximityUsesPathDistance = value; }
    public float ProximityRecalcInterval { get => proximityRecalcInterval; set => proximityRecalcInterval = value; }
    public float ChaseProgressWindow { get => chaseProgressWindow; set => chaseProgressWindow = value; }
    public float ChaseMinProgress { get => chaseMinProgress; set => chaseMinProgress = value; }
    public float ChasePathJumpDistance { get => chasePathJumpDistance; set => chasePathJumpDistance = value; }
    public float ChaseStallRegroupTime { get => chaseStallRegroupTime; set => chaseStallRegroupTime = value; }
    public float ChaseStagnantDetourTolerance { get => chaseStagnantDetourTolerance; set => chaseStagnantDetourTolerance = value; }
    public float ChaseTrailPenalty { get => chaseTrailPenalty; set => chaseTrailPenalty = value; }
    public float ChaseTrailPenaltyRadius { get => chaseTrailPenaltyRadius; set => chaseTrailPenaltyRadius = value; }
    public float InvestigationDwellTime { get => investigationDwellTime; set => investigationDwellTime = value; }
    public float InvestigationRetargetInterval { get => investigationRetargetInterval; set => investigationRetargetInterval = value; }
    public float InvestigationRetargetDistance { get => investigationRetargetDistance; set => investigationRetargetDistance = value; }
    public float UnderTableVisionMultiplier { get => underTableVisionMultiplier; set => underTableVisionMultiplier = value; }
    public float SeenEnteringWindow { get => seenEnteringWindow; set => seenEnteringWindow = value; }
    public float HiddenPullOutTime { get => hiddenPullOutTime; set => hiddenPullOutTime = value; }
    public float BeliefSightRadius { get => beliefSightRadius; set => beliefSightRadius = value; }
    public float BeliefNoiseBaseRadius { get => beliefNoiseBaseRadius; set => beliefNoiseBaseRadius = value; }
    public float BeliefNoiseRadiusPerMetre { get => beliefNoiseRadiusPerMetre; set => beliefNoiseRadiusPerMetre = value; }
    public float BeliefNoiseWallFactor { get => beliefNoiseWallFactor; set => beliefNoiseWallFactor = value; }
    public float BeliefNoiseFloorFactor { get => beliefNoiseFloorFactor; set => beliefNoiseFloorFactor = value; }
    public float BeliefGrowthSpeed { get => beliefGrowthSpeed; set => beliefGrowthSpeed = value; }
    public float BeliefNoiseHidingSpotFactor { get => beliefNoiseHidingSpotFactor; set => beliefNoiseHidingSpotFactor = value; }
    public float DropLinkCooldown { get => dropLinkCooldown; set => dropLinkCooldown = value; }
    public float SearchMinTime { get => searchMinTime; set => searchMinTime = value; }
    public float SearchQuietWindow { get => searchQuietWindow; set => searchQuietWindow = value; }
    public float SearchHardCap { get => searchHardCap; set => searchHardCap = value; }
    public float SearchQualitySight { get => searchQualitySight; set => searchQualitySight = value; }
    public float SearchQualityMuffled { get => searchQualityMuffled; set => searchQualityMuffled = value; }
    public float SearchEscalatedCapScale { get => searchEscalatedCapScale; set => searchEscalatedCapScale = value; }
    public float LeadValueRadio { get => leadValueRadio; set => leadValueRadio = value; }
    public float LeadValueFireAlarm { get => leadValueFireAlarm; set => leadValueFireAlarm = value; }
    public float LeadValueChains { get => leadValueChains; set => leadValueChains = value; }
    public float LeadValueOther { get => leadValueOther; set => leadValueOther = value; }
    public float GlimpseValue { get => glimpseValue; set => glimpseValue = value; }
    public float LeadHabituation { get => leadHabituation; set => leadHabituation = value; }
    public float FocusAttentionFloor { get => focusAttentionFloor; set => focusAttentionFloor = value; }
    public float FocusMargin { get => focusMargin; set => focusMargin = value; }
    public float FocusCommitmentBonus { get => focusCommitmentBonus; set => focusCommitmentBonus = value; }
    public float FocusCommitmentDecay { get => focusCommitmentDecay; set => focusCommitmentDecay = value; }
    public float FocusFreshnessHalfLife { get => focusFreshnessHalfLife; set => focusFreshnessHalfLife = value; }
    public float FocusCostDistance { get => focusCostDistance; set => focusCostDistance = value; }
    public float FocusAntiDither { get => focusAntiDither; set => focusAntiDither = value; }
    public float LeadSumsMargin { get => leadSumsMargin; set => leadSumsMargin = value; }
    public float SoftNoiseLoudness { get => softNoiseLoudness; set => softNoiseLoudness = value; }
    public float SoftNoiseSuspicionRate { get => softNoiseSuspicionRate; set => softNoiseSuspicionRate = value; }
    public float NoiseOnlySuspicionCap { get => noiseOnlySuspicionCap; set => noiseOnlySuspicionCap = value; }
    public float HearingLocalizationError { get => hearingLocalizationError; set => hearingLocalizationError = value; }
    public float HearingErrorDriftTime { get => hearingErrorDriftTime; set => hearingErrorDriftTime = value; }
    public float SearchPreciseNoiseRadius { get => searchPreciseNoiseRadius; set => searchPreciseNoiseRadius = value; }
    public float HuntGraceSeconds { get => huntGraceSeconds; set => huntGraceSeconds = value; }
    public float SearchMapNodeSpacing { get => searchMapNodeSpacing; set => searchMapNodeSpacing = value; }
    public float SearchMapTickInterval { get => searchMapTickInterval; set => searchMapTickInterval = value; }
    public float SearchMapSpreadSpeed { get => searchMapSpreadSpeed; set => searchMapSpreadSpeed = value; }
    public float SearchMapHeadingBias { get => searchMapHeadingBias; set => searchMapHeadingBias = value; }
    public float SearchMapHeadingDuration { get => searchMapHeadingDuration; set => searchMapHeadingDuration = value; }
    public float SearchMapClearRangeScale { get => searchMapClearRangeScale; set => searchMapClearRangeScale = value; }

    /// <summary>How far the map's "not here" reaches, in metres: the view range as lent right now,
    /// times <see cref="SearchMapClearRangeScale"/>.</summary>
    public float SearchMapClearRange => viewRange * searchMapClearRangeScale;
    public float SearchMapProbeHeight { get => searchMapProbeHeight; set => searchMapProbeHeight = value; }
    public float SearchMapZoneRadius { get => searchMapZoneRadius; set => searchMapZoneRadius = value; }
    public int SearchMapCandidates { get => searchMapCandidates; set => searchMapCandidates = value; }
    public float SearchMapRepickShare { get => searchMapRepickShare; set => searchMapRepickShare = value; }
    public float SearchMapWorthThreshold { get => searchMapWorthThreshold; set => searchMapWorthThreshold = value; }
}
