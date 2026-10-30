using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Puts the Nemesis where the tension is, without ever taking hold of it.
///
/// THE ONE RULE THIS CLASS IS BUILT AROUND: it does not touch the FSM. It cannot put the Nemesis
/// into Chasing, cannot hand it the player's position, cannot skip a detection range or a grace
/// period. Everything it does is something the game world could have done on its own — a noise, a
/// patrol that happens to swing this way, a monster that walks in through the door. That is not
/// squeamishness about layering: an AI the player can read is the entire product here, and a
/// director that reaches into the FSM produces a monster whose behaviour has no explanation on
/// screen. The player cannot tell "it heard something over there" from "the game decided", and
/// once they suspect the second, every genuine piece of tracking reads as cheating too.
///
/// So it leans on four levers, in ascending order of how much the player notices:
///
///   1. THE PATROL ANCHOR. NemesisController already biases which zone it patrols next towards an
///      anchor (SO_NemesisData.ZoneBiasUsesRealPlayer, the player's own position). The Director
///      overrides that anchor with the pressure zone's centre while a request is live. Coarse by
///      construction — it weights whole zones, it is a roll and not a choice, and the falloff says
///      "this side of the level" — so it reads as the monster being around, never as it knowing.
///   2. ROUTE WEIGHTS. The routes covering the zone get their authored weight multiplied while the
///      pressure lasts. Same mechanism the designer uses to say "patrol here more often", borrowed
///      for a few seconds.
///   3. SYNTHETIC NOISE. FieldOfListening does not care who made a sound — it sweeps for colliders
///      on its listen layer and reads loudness off the collider's radius. So a noise from the
///      Director is the same object a thrown bottle would be, arriving down the same channel, and
///      it pushes the Nemesis to Investigating exactly as a real one would. This is the loudest
///      lever and the one on the longest cooldown.
///   4. SENSES. A runtime COPY of SO_NemesisData with wider hearing and sight, installed while the
///      pressure lasts and thrown away after. The copy is not an optimisation: mutating the asset
///      itself would write the boost into the project — in the Editor those writes persist — and a
///      designer would find their tuning silently changed by a playtest.
///
/// And a fifth that belongs to the pacing rather than to any pressure (plan §18.5 C1):
///
///   5. PERSISTENCE. How much silence a search tolerates before it cools down, and its hard cap —
///      SO_NemesisData.SearchQuietWindow and SearchHardCap — scaled by the pacing state: Relax gives
///      up sooner and leaves, which is what makes the retreat visible; rising sensitivity insists.
///      It never makes the Nemesis ignore what it senses — fresh evidence renews a search exactly as
///      before — it only moves how long the quiet has to last. Same channel as the senses: the
///      Searching state reads its SO every frame, and the Director hands it a different one.
///
/// Levers 4 and 5 travel in ONE borrowed copy (<see cref="RefreshLoan"/>). Two copies would each be
/// built from the baseline, and whichever was installed second would silently undo the other.
///
/// And one theatrical move that is not a lever but a scene: <see cref="StageEntranceAsync"/>, the
/// Mr. X entrance. See its own doc for why a teleport can be honest.
///
/// SETUP: one of these in the level (it is a Singleton), plus a NemesisPressureZone per area worth
/// naming. Nothing else has to know it exists — NemesisController asks it for an anchor through a
/// static, and answers itself when there is no Director in the scene.
///
/// Pacing (plan §6): with an SO_DirectorPacing assigned, <see cref="NemesisTension"/> measures the
/// encounter and the Director retreats in Relax, ramps pressure after a long silence, lends the search
/// its persistence, and comes back past a search that ended empty ("vuelve a pasar", plan §18.5 C2).
/// No lever may act within <see cref="NemesisSafeZones.Clearance"/> of the Hub (C5), and none at all
/// while the Nemesis is suspended — asleep, in a cinematic, in the escape — puzzle beats included.
/// </summary>
public class NemesisDirector : Singleton<NemesisDirector>
{
    [Header("Evaluación")]
    [Tooltip("Cada cuántos segundos el Director revisa qué está pasando.\n\n" +
             "Lento a propósito. Nada de lo que hace es una reacción a algo que pasó este frame: " +
             "empuja una patrulla que tarda decenas de segundos en cambiar de zona. Evaluar " +
             "seguido no lo haría más preciso, sólo más caro.")]
    [SerializeField, Range(0.5f, 10f)] private float evaluationInterval = 3f;

    [Header("Palanca 1-2: patrulla")]
    [Tooltip("Cuánto se multiplica el peso de las rutas que tocan la zona, con la presión al " +
             "máximo. 1 desactiva esta palanca.\n\n" +
             "Se aplica sobre el peso que puso el diseñador, no lo reemplaza: una ruta que él " +
             "bajó a 0.2 porque es un rincón feo sigue siendo menos frecuente que las demás, " +
             "incluso bajo presión.")]
    [SerializeField, Min(1f)] private float routeWeightBoost = 3f;

    [Header("Palanca 3: ruido")]
    [Tooltip("Segundos entre ruidos sintéticos mientras hay presión. 0 apaga la palanca.\n\n" +
             "Largo a propósito: el ruido es lo único que el Director hace que el jugador " +
             "escucha, y un goteo constante de ruidos deja de leerse como el mundo y empieza a " +
             "leerse como un sistema.")]
    [SerializeField, Min(0f)] private float noiseInterval = 9f;

    [Tooltip("Radio del emisor de ruido, en metros — es lo que FieldOfListening lee como " +
             "volumen. Referencia del jugador: agachado 1, caminando 2, corriendo 6.")]
    [SerializeField, Min(0.5f)] private float noiseLoudness = 4f;

    [Tooltip("Segundos que vive el emisor. Tiene que superar cómodamente el intervalo con el que " +
             "el Nemesis barre en busca de ruidos, o el barrido puede caer entre dos y no oír nada.")]
    [SerializeField, Min(0.2f)] private float noiseLifetime = 0.8f;

    [Tooltip("Capa del emisor de ruido. TIENE que estar dentro del Listen Mask del Nemesis o el " +
             "ruido no existe para nadie — es la misma capa donde vive el emisor del jugador " +
             "(DetectableAudio). Se avisa por consola si no coinciden.")]
    [SerializeField] private int noiseLayer = 8;

    [Header("Palanca 4: sentidos")]
    [Tooltip("Cuánto se multiplican los rangos de oído y vista con la presión al máximo. 1 " +
             "desactiva la palanca.\n\n" +
             "Modesto a propósito. Esta es la única palanca que el jugador no puede ver venir, " +
             "así que es la que más rápido se siente injusta: subir el oído un cuarto hace que " +
             "una habitación grande cuente como una, y con eso alcanza.")]
    [SerializeField, Range(1f, 2f)] private float sensoryBoost = 1.25f;

    [Header("Entrada en escena")]
    [Tooltip("Distancia mínima al jugador a la que puede aparecer. Es la garantía de que una " +
             "entrada nunca es una emboscada: por debajo de esto el jugador no tendría tiempo de " +
             "reaccionar, y el Director dejaría de ser algo que el mundo podría haber hecho.")]
    [SerializeField, Min(4f)] private float entranceMinDistance = 10f;

    [Tooltip("Distancia máxima. Más allá, la entrada no se nota y es lo mismo que no haberla hecho.")]
    [SerializeField, Min(5f)] private float entranceMaxDistance = 22f;

    [Tooltip("Segundos que se queda quieto mirando al jugador antes de moverse.\n\n" +
             "Es la pausa de Mr. X, y es un regalo al jugador: durante esa ventana el Nemesis no " +
             "avanza aunque ya te haya visto. Sirve para que la aparición se lea como una " +
             "amenaza y no como un salto de susto, y para que haya tiempo de decidir por dónde " +
             "salir.")]
    [SerializeField, Min(0f)] private float entranceStareSeconds = 2.5f;

    [Tooltip("Cuántos puntos prueba antes de rendirse buscando un lugar válido para aparecer.")]
    [SerializeField, Min(4)] private int entranceSampleAttempts = 32;

    [Tooltip("Sólo aparecer donde el jugador NO lo esté viendo.\n\n" +
             "Encendido, el jugador nunca ve materializarse al Nemesis: dobla una esquina y ya " +
             "está ahí, mirándolo. Con el rango de visión corto de este juego eso igual cae a " +
             "diez metros, así que se lee como una aparición y no como una teletransportación.\n\n" +
             "Apagado, puede aparecer a la vista. Es más brutal y más barato: el jugador ve el " +
             "truco, y a partir de ahí cada cosa que el Nemesis haga bien parece trampa también. " +
             "Si lo apagás, la ventana de 'se queda mirando' pasa a ser lo único que hace justa " +
             "la entrada, así que no la bajes a cero.")]
    [SerializeField] private bool arriveOutOfSightOnly = true;

    [Tooltip("La entrada se hace pegado a una pared, con una mano apoyada en ella y la otra estirada " +
             "hacia el jugador (NemesisPeekRig). Con esto prendido busca primero un umbral: un paso " +
             "angosto de hasta ~2.8 m, el lugar donde está por entrar a la habitación. Si no hay " +
             "ninguno acepta cualquier pared.")]
    [SerializeField] private bool entranceAtDoorway = true;

    [Tooltip("Hasta dónde busca una pared desde el punto sorteado, en metros. Más allá se descarta.")]
    [SerializeField, Min(0.5f)] private float entranceWallSearchRadius = 2.5f;

    [Header("Ritmo (Fase 5)")]
    [Tooltip("Tensión y ritmo: retirada en Relax y sensibilidad creciente tras mucho silencio. " +
             "Vacío = el Director sólo responde a puzzles y a la API, como antes de la Fase 5.\n\n" +
             "Los disparadores por puzzle le ganan siempre al ritmo.")]
    [SerializeField] private SO_DirectorPacing pacing;

    [Header("Disparadores por puzzle")]
    [Tooltip("Presión que se dispara sola al completarse un puzzle. Es el gancho del que habla " +
             "el diseño: el alivio de resolver algo dura lo que tarda el Nemesis en aparecer.")]
    [SerializeField] private List<PuzzleTrigger> puzzleTriggers = new List<PuzzleTrigger>();

    [Serializable]
    private class PuzzleTrigger
    {
        [PuzzleId]
        [Tooltip("Puzzle que dispara esto, al completarse.")]
        public string puzzleId;

        [PressureZoneId]
        [Tooltip("Zona donde se aplica la presión. Vacío = sólo la entrada en escena, sin zona.")]
        public string zoneId;

        [Range(0f, 1f)]
        [Tooltip("Cuánta presión. 1 es todo lo que el Director sabe hacer sin hacer trampa.")]
        public float intensity = 1f;

        [Min(0f)]
        [Tooltip("Cuántos segundos dura.")]
        public float duration = 60f;

        [Tooltip("Además de la presión, hacer la entrada en escena tipo Mr. X.")]
        public bool stageEntrance;
    }

    // ── Estado ──────────────────────────────────────────────────────────────

    /// <summary>Who asked for the pressure in flight. Pacing only ever replaces or clears its own.</summary>
    private enum EPressureSource
    {
        Scripted,
        RisingSensitivity,
        Retreat,

        /// <summary>"Vuelve a pasar": back past where a search ended empty (plan §18.5 C2).</summary>
        Revisit,
    }

    [Flags]
    private enum ELevers
    {
        None = 0,
        Anchor = 1 << 0,
        RouteWeights = 1 << 1,
        Noise = 1 << 2,
        Senses = 1 << 3,
        All = Anchor | RouteWeights | Noise | Senses,
    }

    /// <summary>Retreat and the revisit move the patrol and nothing else: no noise, and its senses stay
    /// as they are. A bias on where it walks, never a reason to notice anyone.</summary>
    private const ELevers PatrolLevers = ELevers.Anchor | ELevers.RouteWeights;

    /// <summary>Seconds a revisit that falls due mid-hunt waits before it asks again. See
    /// <see cref="TickRevisit"/>.</summary>
    private const float RevisitRetrySeconds = 10f;

    private NemesisPressureZone activeZone;
    private float activeIntensity;
    private float pressureEndsAt;
    private EPressureSource activeSource;
    private ELevers activeLevers;

    private NemesisTension tension;
    private int risingStep;
    private float nextRisingAt;

    // "Vuelve a pasar": one pending at a time, from the LAST search that ended empty.
    private bool revisitPending;
    private Vector3 revisitArea;
    private float revisitDueAt;

    private float nextEvaluationAt;
    private float nextNoiseAt;

    private bool isStagingEntrance;

    // Presence (SO_DirectorPacing "Presencia"): one pending vanish, and the clock of the ambient
    // entrance. Both are moves of the body that wait for the player not to be looking.
    private bool vanishPending;
    private float vanishDueAt;
    private float vanishGiveUpAt;
    private float nextAmbientEntranceAt = -1f;
    private float lastSawNemesisAt = -1f;
    private float nextUnseenRollAt;
    private bool returnPending;
    private float returnDueAt;
    private float returnGiveUpAt;

    /// <summary>The routes whose weight this Director has touched, so exactly those get put back.
    /// A route the designer weighted for their own reasons must not be "restored" to 1 by us.
    /// </summary>
    private readonly List<NemesisRoute> boostedRoutes = new List<NemesisRoute>();

    private NemesisRoute[] allRoutes;
    private NemesisStateManager nemesis;

    // The baseline the loan is built from is NOT cached here. It lives on the Nemesis as
    // NemesisStateManager.BaselineData and is read fresh at both ends of the loan — see
    // RefreshLoan for why holding onto it broke difficulty escalation.

    /// <summary>The live copy carrying every borrowed number (senses and persistence), or null
    /// when nothing is lent.</summary>
    private SO_NemesisData loanedData;

    // What the copy above was built with: 1 and 1 when nothing is lent. The persistence is also
    // what F9 shows, so it is the number the Searching state is actually reading.
    private float loanedSenses = 1f;
    private float loanedPersistence = 1f;
    private string loanedPersistenceReason;

    /// <summary>The baseline the live copy was cloned from. Compared, never restored: when an
    /// escalation installs a new baseline mid-loan, the copy is rebuilt on top of it.</summary>
    private SO_NemesisData loanedFrom;

    private void Awake()
    {
        CreateSingleton(false);

        // Explicit null check: GetComponent's fake null in the Editor defeats '??'.
        tension = GetComponent<NemesisTension>();
        if (tension == null) tension = gameObject.AddComponent<NemesisTension>();

        tension.PacingStateChanged += HandlePacingStateChanged;
        tension.SuspensionChanged += HandleSuspensionChanged;

        // Awake/OnDestroy and not OnEnable/OnDisable (docs/CLAUDE.md): static events outlive the
        // component's enabled state. The handlers check it themselves.
        NemesisEvents.OnSearchEnded += HandleSearchEnded;
        NemesisEvents.OnBaselineChanged += HandleBaselineChanged;
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
    }

    private void OnDestroy()
    {
        if (tension != null)
        {
            tension.PacingStateChanged -= HandlePacingStateChanged;
            tension.SuspensionChanged -= HandleSuspensionChanged;
        }

        NemesisEvents.OnSearchEnded -= HandleSearchEnded;
        NemesisEvents.OnBaselineChanged -= HandleBaselineChanged;
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
    }

    /// <summary>An escalation tier landed mid-loan (plan Fase 7): the copy was cloned from the old
    /// baseline, so it is rebuilt on the new one instead of waiting for the loan to end.</summary>
    private void HandleBaselineChanged()
    {
        if (isActiveAndEnabled && loanedData != null) RefreshLoan();
    }

    private void OnEnable()
    {
        PuzzleCompletionAftermath.Settled += HandlePuzzleCompleted;

        // Back from a disable that returned everything: the rhythm may still want its persistence.
        RefreshLoan();
    }

    private void OnDisable()
    {
        PuzzleCompletionAftermath.Settled -= HandlePuzzleCompleted;

        // Everything this class does is a temporary edit to somebody else's state. A Director torn
        // down mid-request would otherwise leave boosted route weights and a cloned data asset
        // installed on the Nemesis for the rest of the run, with nothing left alive to undo them.
        // The persistence goes back too, not only what a pressure lent, and a pending revisit is
        // dropped rather than left to fire whenever this is enabled again.
        EndPressure();
        ReturnLoan();
        CancelRevisit();
        CancelVanish();
    }

    // ── API ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks for pressure in a zone. The single entry point for everything outside this class —
    /// the module system, a narrative beat, the console.
    /// </summary>
    /// <param name="zoneId">Id of a <see cref="NemesisPressureZone"/> in the level.</param>
    /// <param name="intensity">0..1. Scales every lever together; 0 clears.</param>
    /// <param name="duration">Seconds. A new request replaces the one in flight rather than
    /// stacking with it: two pressure zones at once is the same as none, since the patrol can only
    /// be pulled towards one place at a time.</param>
    public static void RequestPressure(string zoneId, float intensity, float duration)
    {
        if (!Exists)
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] Pressure requested for '{zoneId}' but " +
                             "there is no Director in the scene. Ignored.");
            return;
        }

        Instance.ApplyPressure(zoneId, intensity, duration, EPressureSource.Scripted, ELevers.All);
    }

    /// <summary>Ends whatever is in flight and puts everything it touched back.</summary>
    public static void ReleasePressure()
    {
        if (Exists) Instance.ClearPressure();
    }

    /// <summary>
    /// Stages the Mr. X entrance: the Nemesis appears near the player, out of sight, and waits.
    /// See <see cref="StageEntranceAsync"/>.
    /// </summary>
    /// <param name="zoneId">Optional. Confines the arrival to a zone; empty means "anywhere near
    /// the player that qualifies".</param>
    public static void RequestEntrance(string zoneId = null)
    {
        if (!Exists)
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] Entrance requested but there is no " +
                             "Director in the scene. Ignored.");
            return;
        }

        Instance.StageEntranceAsync(zoneId, Instance.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// The anchor the patrol should be pulled towards, or false to let
    /// <see cref="NemesisController"/> fall back to its own answer (the player's position).
    ///
    /// Static and null-safe so the controller does not need a reference, and so a level with no
    /// Director behaves exactly as it did before this class existed.
    /// </summary>
    public static bool TryGetPressureAnchor(out Vector3 anchor)
    {
        anchor = Vector3.zero;

        if (!Exists) return false;

        NemesisDirector director = Instance;
        if (director.activeZone == null || director.activeIntensity <= 0f) return false;
        if ((director.activeLevers & ELevers.Anchor) == 0) return false;

        anchor = director.activeZone.Center;
        return true;
    }

    /// <summary>Current pressure on a zone, 0 when none. For the zone's own gizmo.</summary>
    public static float IntensityOf(string zoneId)
    {
        if (!Exists || string.IsNullOrWhiteSpace(zoneId)) return 0f;

        NemesisDirector director = Instance;
        if (director.activeZone == null) return 0f;

        return string.Equals(director.activeZone.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase)
            ? director.activeIntensity
            : 0f;
    }

    // ── Ciclo ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Time.time < nextEvaluationAt) return;
        nextEvaluationAt = Time.time + Mathf.Max(0.5f, evaluationInterval);

        Evaluate();
    }

    /// <summary>
    /// One low-frequency tick: expire what is over, and top up what is still running.
    ///
    /// The levers are re-applied rather than set once because the world moves underneath them: a
    /// route can be unlocked mid-request, and the noise is a periodic event by nature.
    /// </summary>
    private void Evaluate()
    {
        // Suspended: the tension says so on the frame it happens (HandleSuspensionChanged), and this
        // is the net under it. Checked before the pacing, which may not exist: the guard is about the
        // escape, not about the rhythm.
        if (tension != null && tension.IsSuspended)
        {
            StandDown();
            return;
        }

        if (activeZone != null)
        {
            if (Time.time >= pressureEndsAt)
            {
                ClearPressure();
            }
            else
            {
                if ((activeLevers & ELevers.RouteWeights) != 0) ApplyRouteWeights();
                if ((activeLevers & ELevers.Noise) != 0) TryEmitNoise();
            }
        }

        TickPacing();
    }

    private void ApplyPressure(string zoneId, float intensity, float duration,
                               EPressureSource source, ELevers levers)
    {
        NemesisPressureZone zone = NemesisPressureZone.Find(zoneId);

        if (zone == null)
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] No pressure zone called '{zoneId}'. " +
                             "Check the id against the NemesisPressureZone in the scene — nothing " +
                             "happens until they match.", this);
            return;
        }

        ApplyPressure(zone, intensity, duration, source, levers);
    }

    private void ApplyPressure(NemesisPressureZone zone, float intensity, float duration,
                               EPressureSource source, ELevers levers)
    {
        if (!NemesisSafeZones.IsCentreClear(zone.Center))
        {
            Debug.LogError($"[{nameof(NemesisDirector)}] Refused pressure on '{zone.ZoneId}': its centre is " +
                           $"{NemesisSafeZones.FlatDistance(zone.Center):0.0} m from the Hub (minimum " +
                           $"{NemesisSafeZones.Clearance:0} m). The anchor would park the Nemesis at the " +
                           "Hub's door (cheese C5). Move the zone.", zone);
            return;
        }

        intensity = Mathf.Clamp01(intensity);

        if (intensity <= 0f || duration <= 0f)
        {
            ClearPressure();
            return;
        }

        // Nothing leans on a Nemesis the escape or a cinematic owns, a puzzle beat included: the
        // pressure would ride into the chase with its senses boost on (plan §14.1). Before the first
        // wake-up the tension is not suspended, so Zona1's puzzle triggers still land on the sleeping
        // Nemesis as before — and are cleared the moment it wakes into the escape.
        if (tension != null && tension.IsSuspended)
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] Pressure on '{zone.ZoneId}' ignored: the Nemesis " +
                             $"is suspended ({tension.SuspendReason}).", this);
            return;
        }

        // Whatever the last request left behind goes back first. Without this, a second request
        // over a first one would boost the new zone's routes without ever restoring the old
        // zone's — and the level would slowly turn into one where every route is boosted.
        EndPressure();

        activeZone = zone;
        activeIntensity = intensity;
        pressureEndsAt = Time.time + duration;
        activeSource = source;
        activeLevers = levers;

        // Immediately, not on the next tick: a request made right after a puzzle is a beat in the
        // level's pacing, and up to evaluationInterval of nothing happening blunts it.
        nextNoiseAt = Time.time + Mathf.Max(0f, noiseInterval);
        if ((levers & ELevers.RouteWeights) != 0) ApplyRouteWeights();

        // Senses from this request, persistence from the rhythm (a rising-sensitivity request also
        // moves the persistence, through its intensity): one copy for both.
        RefreshLoan();

        Debug.Log($"[{nameof(NemesisDirector)}] Pressure on '{zone.ZoneId}' at {intensity:0.00} " +
                  $"for {duration:0}s ({LabelOf(source)}).", this);
    }

    private void ClearPressure()
    {
        EndPressure();

        // Settled again without this pressure: its senses go back, and the persistence drops to
        // what the rhythm alone asks for (a rising-sensitivity request was holding it up).
        RefreshLoan();
    }

    /// <summary>Puts back what the pressure in flight touched, and forgets it. Does NOT settle the
    /// loan: the callers do, once, after deciding what replaces it.</summary>
    private void EndPressure()
    {
        RestoreRouteWeights();

        activeZone = null;
        activeIntensity = 0f;
    }

    /// <summary>
    /// The Nemesis is off-limits (asleep, in a cinematic, in the escape): every pressure goes,
    /// scripted included, and a pending revisit with it. The pacing only ever cleared its own, and a
    /// puzzle trigger fired less than its duration before the escape woke the Nemesis into the chase
    /// with the Director's senses on (plan §14.1, §18.5 C4).
    /// </summary>
    private void StandDown()
    {
        if (activeZone != null) ClearPressure();
        CancelRevisit();
    }

    // ── Palanca 1-2: pesos de ruta ──────────────────────────────────────────

    /// <summary>
    /// Multiplies the authored weight of every route that reaches into the zone.
    ///
    /// "Reaches into" and not "is centred on": a route is a line through the level, and one that
    /// merely passes through the pressured area is exactly the one worth making more frequent —
    /// it brings the Nemesis through and out again, which is the shape of a patrol rather than of
    /// a guard posting.
    ///
    /// Only unlocked routes are touched. Boosting a locked one is not wrong so much as pointless
    /// (nothing rolls it) and it would then need restoring later for no reason.
    /// </summary>
    private void ApplyRouteWeights()
    {
        if (routeWeightBoost <= 1f || activeZone == null) return;

        allRoutes ??= FindObjectsByType<NemesisRoute>(FindObjectsInactive.Include);

        float multiplier = Mathf.Lerp(1f, routeWeightBoost, activeIntensity);

        for (int i = 0; i < allRoutes.Length; i++)
        {
            NemesisRoute route = allRoutes[i];
            if (route == null || !route.IsUnlocked) continue;
            if (!RouteTouchesZone(route, activeZone)) continue;

            route.SetPressureMultiplier(multiplier);

            if (!boostedRoutes.Contains(route)) boostedRoutes.Add(route);
        }
    }

    private static bool RouteTouchesZone(NemesisRoute route, NemesisPressureZone zone)
    {
        IReadOnlyList<Transform> waypoints = route.Waypoints;

        for (int i = 0; i < waypoints.Count; i++)
        {
            Transform waypoint = waypoints[i];
            if (waypoint != null && zone.Contains(waypoint.position)) return true;
        }

        return false;
    }

    private void RestoreRouteWeights()
    {
        for (int i = 0; i < boostedRoutes.Count; i++)
        {
            if (boostedRoutes[i] != null) boostedRoutes[i].SetPressureMultiplier(1f);
        }

        boostedRoutes.Clear();
    }

    // ── Palanca 3: ruido sintético ──────────────────────────────────────────

    /// <summary>
    /// Drops a noise inside the zone, through the channel the player's own footsteps use.
    ///
    /// The emitter is a plain trigger collider on the listen layer with a lifetime, because that
    /// is all a noise is in this project: FieldOfListening sweeps for colliders on its mask and
    /// reads loudness off the collider's radius. Nothing about it knows who made it, which is the
    /// entire point — the Nemesis investigating this is running the same code path it runs for a
    /// player who stepped on glass, so it cannot behave differently, and neither can the player
    /// tell the difference.
    ///
    /// Placed on the NavMesh rather than anywhere in the sphere: a noise inside a wall sends the
    /// Nemesis to investigate a place it cannot stand, and Searching then spends its whole timeout
    /// pacing around the nearest reachable point to a lie.
    /// </summary>
    private void TryEmitNoise()
    {
        if (noiseInterval <= 0f || Time.time < nextNoiseAt) return;
        nextNoiseAt = Time.time + noiseInterval;

        if (!TrySampleInZone(activeZone, out Vector3 point)) return;

        GameObject emitter = new GameObject($"DirectorNoise ({activeZone.ZoneId})")
        {
            layer = noiseLayer,
        };
        emitter.transform.position = point;

        SphereCollider collider = emitter.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = noiseLoudness;

        Destroy(emitter, noiseLifetime);
    }

    /// <summary>A point inside the zone that the Nemesis could actually stand on, and not at the Hub's door (C5).</summary>
    private bool TrySampleInZone(NemesisPressureZone zone, out Vector3 point)
    {
        point = zone.Center;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * zone.Radius;
            Vector3 candidate = zone.Center + new Vector3(offset.x, 0f, offset.y);

            if (NemesisNav.TrySnapToNavMesh(candidate, out point) && NemesisSafeZones.IsClear(point))
                return true;
        }

        return NemesisNav.TrySnapToNavMesh(zone.Center, out point) && NemesisSafeZones.IsClear(point);
    }

    // ── Palancas 4-5: el préstamo (sentidos y persistencia) ─────────────────

    /// <summary>
    /// Settles what is lent to the Nemesis: hearing and sight (lever 4, from the pressure in flight)
    /// and the search's persistence (lever 5, from the rhythm), in ONE copy of its tuning asset.
    /// Called whenever either could have changed: a pressure applied or cleared (which covers every
    /// rising-sensitivity renewal, since its intensity is what moves the persistence), the pacing
    /// state changing, and the Nemesis becoming suspended or not.
    ///
    /// A COPY, AND A FRESH ONE EVERY TIME. Writing the boost into SO_NemesisData itself would be
    /// simpler by one line and wrong in a way that only shows up days later: ScriptableObject
    /// edits made in Play mode persist into the asset in the Editor, so a playtest that happened
    /// to run a pressure request would leave the boosted ranges in the project, and the next
    /// person to open the asset would find numbers nobody typed. Cloning from the authored asset
    /// at install time also means a designer's edits between two requests are picked up.
    ///
    /// ONE COPY FOR BOTH (plan §18.8). Each lever with its own copy, each cloned from the baseline,
    /// and whichever went in second would throw the other away without a trace — the Relax
    /// persistence silently dropped by the next puzzle's senses boost, or the other way round.
    /// Rebuilt from the baseline with every multiplier applied at once instead, and when every
    /// multiplier is 1 there is no copy at all: the Nemesis gets its baseline back.
    /// </summary>
    private void RefreshLoan()
    {
        float senses = SensesMultiplier();
        float persistence = PersistenceMultiplier(out string reason);

        if (Mathf.Approximately(senses, 1f) && Mathf.Approximately(persistence, 1f))
        {
            ReturnLoan();
            return;
        }

        if (!TryResolveNemesis()) return;

        // Already out with these very numbers and still the one installed (the pacing handler and
        // the pressure it just applied both ask): nothing to rebuild.
        if (loanedData != null && nemesis.NemesisData == loanedData && loanedFrom == nemesis.BaselineData &&
            Mathf.Approximately(loanedSenses, senses) && Mathf.Approximately(loanedPersistence, persistence))
        {
            loanedPersistenceReason = reason;
            return;
        }

        // Read fresh every time, never cached with '??='. This is a LOAN and the baseline is what it
        // has to give back — but difficulty escalation replaces what the Nemesis's tuning IS
        // between one pressure request and the next. Caching the first asset ever seen would make
        // ReturnLoan restore a baseline that stopped being current several puzzles ago, quietly
        // undoing the whole escalation. Nothing would look broken: the monster keeps behaving, just
        // at the difficulty of the opening room.
        SO_NemesisData baseline = nemesis.BaselineData;
        if (baseline == null) return;

        SO_NemesisData copy = Instantiate(baseline);
        copy.name = baseline.name + " (director)";

        // Hearing is min(ListenRange, loudness × NoiseRangeScale): ListenRange is only the CAP (15 m),
        // and walking is heard at 4 × 2.5 = 10 m, under it. Scaling the cap alone made the boost reach
        // a running player and nobody else (plan D32). Both go up together, as the escalation does.
        copy.ListenRange *= senses;
        copy.NoiseRangeScale *= senses;
        copy.ViewRange *= senses;
        copy.SearchQuietWindow *= persistence;
        copy.SearchHardCap *= persistence;

        nemesis.OverrideData(copy);

        // The old copy goes only once the new one is installed: the Nemesis never points at a
        // destroyed asset, not even between two lines.
        if (loanedData != null) Destroy(loanedData);

        loanedData = copy;
        loanedFrom = baseline;
        loanedSenses = senses;
        loanedPersistence = persistence;
        loanedPersistenceReason = reason;
    }

    /// <summary>Hands the baseline back and throws the copy away. Nothing lent afterwards.</summary>
    private void ReturnLoan()
    {
        loanedSenses = 1f;
        loanedPersistence = 1f;
        loanedPersistenceReason = null;
        loanedFrom = null;

        if (loanedData == null) return;

        // Read now rather than remembered from the install, for the same reason: a puzzle solved
        // DURING the loan has to survive the restore.
        if (nemesis != null && nemesis.BaselineData != null) nemesis.OverrideData(nemesis.BaselineData);

        Destroy(loanedData);
        loanedData = null;
    }

    /// <summary>Lever 4: wider hearing and sight while the pressure in flight carries the Senses lever.
    /// Retreat and the revisit never do.</summary>
    private float SensesMultiplier()
    {
        if (activeZone == null || activeIntensity <= 0f || (activeLevers & ELevers.Senses) == 0) return 1f;

        return Mathf.Lerp(1f, sensoryBoost, activeIntensity);
    }

    /// <summary>
    /// Lever 5: what the search's quiet window and hard cap are multiplied by, by pacing state (plan
    /// §18.5 C1). BuildUp as authored, rising towards RisingMaxPersistence with the intensity of a live
    /// rising-sensitivity request — it has been quiet for a long time, so once it does find the player
    /// it insists (Mr. X, C1). PeakFade shorter, which helps the encounter end on its own, and that is
    /// what PeakFade is waiting for. Relax shortest: it gives up sooner and leaves, and the retreat
    /// stops being invisible. 1 with no pacing, before the Nemesis wakes, and while it is suspended.
    ///
    /// Rising is lerped from the BuildUp value rather than from 1, which is the same thing with the
    /// defaults, and keeps a rising request from ever lowering the persistence of a designer who
    /// raised BuildUp's.
    /// </summary>
    private float PersistenceMultiplier(out string reason)
    {
        reason = null;

        if (pacing == null || tension == null || !tension.IsRunning || tension.IsSuspended) return 1f;

        NemesisTension.EPacingState state = tension.State;

        switch (state)
        {
            case NemesisTension.EPacingState.BuildUp:
                if (activeZone != null && activeSource == EPressureSource.RisingSensitivity)
                {
                    reason = LabelOf(EPressureSource.RisingSensitivity);
                    return Mathf.Lerp(pacing.BuildUpPersistence, pacing.RisingMaxPersistence, activeIntensity);
                }

                reason = state.ToString();
                return pacing.BuildUpPersistence;

            case NemesisTension.EPacingState.SustainPeak:
                reason = state.ToString();
                return pacing.SustainPeakPersistence;

            case NemesisTension.EPacingState.PeakFade:
                reason = state.ToString();
                return pacing.PeakFadePersistence;

            case NemesisTension.EPacingState.Relax:
                reason = state.ToString();
                return pacing.RelaxPersistence;

            default:
                return 1f;
        }
    }

    // ── La entrada en escena ────────────────────────────────────────────────

    /// <summary>
    /// The Mr. X entrance: the Nemesis turns up near the player, out of sight, and stands there
    /// for a moment before it starts moving.
    ///
    /// WHY A TELEPORT IS ALLOWED HERE, when the rest of this class will not go near one. The rule
    /// worth keeping is not "never move the Nemesis" — it is that the player must never lose to
    /// something they had no way to see coming. Two properties buy that back, and both are
    /// enforced below rather than trusted:
    ///
    ///   - IT ARRIVES OUT OF SIGHT, and never closer than <see cref="entranceMinDistance"/>. The
    ///     player does not watch it appear; they turn a corner and it is there, which is a
    ///     different feeling and a fair one. Arriving inside their view would be the cheat.
    ///   - IT WAITS. The stare window is time the player gets for free, with the monster stood
    ///     still in front of them. Every other system in this project can only ever make the
    ///     Nemesis faster; this is the one that makes it slower, on purpose.
    ///
    /// The player's short sight range is what makes it work at all: "out of sight" is a couple of
    /// rooms in this level, not half a map, so an arrival that respects it is still an arrival
    /// that lands close enough to matter.
    ///
    /// It never enters the Hub, and that guarantee costs nothing to maintain here: the warp goes
    /// through NemesisStateManager.WarpTo, which refuses any point that is not on walkable
    /// NavMesh, and the Hub is a Not Walkable volume. There is no code path from this method to a
    /// safe zone, and there cannot be one added by accident.
    /// </summary>
    private async UniTaskVoid StageEntranceAsync(string zoneId, CancellationToken token)
    {
        if (isStagingEntrance) return;
        if (!TryResolveNemesis()) return;

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return;

        // Dormant, mid-capture, riding the lift: all cases where the body is not ours to move.
        if (!nemesis.IsActive || nemesis.IsUsingElevator) return;

        NemesisPressureZone zone = NemesisPressureZone.Find(zoneId);

        if (!TryFindEntrancePoint(player.position, zone, out Vector3 spot, out Vector3 wallPoint,
                                  out Vector3 wallNormal))
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] No spot to make an entrance at: " +
                             $"nothing on the NavMesh between {entranceMinDistance}m and " +
                             $"{entranceMaxDistance}m of the player" +
                             (arriveOutOfSightOnly ? " that is also out of their sight" : "") +
                             ". Most likely the player is standing in the open, or the two " +
                             "distances leave no room between them. The entrance is skipped " +
                             "rather than made somewhere that would be unfair.", this);
            return;
        }

        await HoldPeekAsync(spot, wallPoint, wallNormal, player, entranceStareSeconds, token);
    }

    /// <summary>
    /// Stages the peek at a FIXED place instead of a sampled one: the Nemesis is put at
    /// <paramref name="position"/> (snapped to the NavMesh and to the nearest wall), facing the player,
    /// holds the peek for <paramref name="stareSeconds"/> and is let go. For
    /// <see cref="NemesisPeekTrigger"/>, which exists so the pose can be SEEN on demand and tuned,
    /// where the entrance of the rhythm is random and only happens when the player is not looking.
    /// Skips every fairness rule on purpose: whoever places it decides where.
    /// </summary>
    /// <returns>false when it could not be staged: no Director, no Nemesis, nothing under the point.
    /// </returns>
    public static async UniTask<bool> PeekAtAsync(Vector3 position, float stareSeconds)
    {
        if (!Exists || !Instance.TryResolveNemesis()) return false;

        NemesisDirector director = Instance;
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null || director.isStagingEntrance || !director.nemesis.IsActive) return false;

        if (!NemesisNav.TrySnapToNavMesh(position, out Vector3 snapped)) return false;

        // The wall it leans on: the nearest one. Without one the hand has nothing to touch, and the
        // peek would be the clip alone, so it is reported rather than staged half.
        if (!director.TryFindWallEdge(snapped, director.nemesis.FieldOfListening, false, out Vector3 spot,
                                      out Vector3 wallPoint, out Vector3 wallNormal))
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] PeekAt: no wall within " +
                             $"{director.entranceWallSearchRadius}m of {position}. Move the spot against one.",
                             director);
            return false;
        }

        await director.HoldPeekAsync(spot, wallPoint, wallNormal, player, stareSeconds,
                                     director.GetCancellationTokenOnDestroy());
        return true;
    }

    /// <summary>
    /// Puts the Nemesis at the spot, held, and plays the peek for the length of the stare. Shared by
    /// the Director's own entrance and <see cref="PeekAtAsync"/>.
    /// </summary>
    private async UniTask HoldPeekAsync(Vector3 spot, Vector3 wallPoint, Vector3 wallNormal, Transform player,
                                        float stareSeconds, CancellationToken token)
    {
        isStagingEntrance = true;

        // The watchdog reads a body that is deliberately standing still while it has a path as
        // being wedged, and would repath it and then warp it away mid-stare.
        nemesis.PushStuckSuppression();

        try
        {
            if (!nemesis.WarpTo(spot)) return;

            FacePlayer(player);

            // Held rather than dormant: dormant would switch its senses off too, and the point of
            // the beat is that it has ALREADY seen you. It stands there knowing.
            nemesis.SetExternalHold(true);

            // The peek: the model's unused E_First_Contact clip, one hand on the wall it is standing
            // against and the other stretched at the player. Handed back in the finally.
            nemesis.PlayPeek(wallPoint, wallNormal, player);

            float waited = 0f;
            while (waited < stareSeconds)
            {
                waited += Time.deltaTime;

                // Kept facing you for the whole window. The FSM may well have entered Chasing by
                // now and would normally turn the body with the agent, but the agent is stopped,
                // so nothing else is writing rotation.
                FacePlayer(player);

                await UniTask.Yield(token);
            }
        }
        finally
        {
            nemesis.EndPeek();
            nemesis.SetExternalHold(false);
            nemesis.PopStuckSuppression();
            isStagingEntrance = false;
        }
    }


    private void FacePlayer(Transform player)
    {
        if (player == null) return;

        Vector3 direction = player.position - nemesis.transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f) return;

        nemesis.transform.rotation = Quaternion.LookRotation(direction);
    }

    /// <summary>
    /// Somewhere on the NavMesh, near the player, that the player cannot see.
    ///
    /// Distance is measured over the NavMesh and not through the air, because "ten metres away"
    /// through a wall is not ten metres away — it is a room the Nemesis would have to leave the
    /// floor to reach, and an entrance made there never arrives.
    /// </summary>
    private bool TryFindEntrancePoint(Vector3 playerPosition, NemesisPressureZone zone,
                                      out Vector3 spot, out Vector3 wallPoint, out Vector3 wallNormal)
    {
        spot = wallPoint = wallNormal = Vector3.zero;

        FieldOfListening senses = nemesis.FieldOfListening;

        // First only doorways (where it is about to walk into the room), then any wall.
        int passes = entranceAtDoorway ? 2 : 1;

        for (int pass = 0; pass < passes; pass++)
        {
            bool doorwayOnly = entranceAtDoorway && pass == 0;

            for (int attempt = 0; attempt < entranceSampleAttempts; attempt++)
            {
                Vector3 candidate = zone != null
                    ? zone.Center + RandomFlatOffset(zone.Radius)
                    : playerPosition + RandomFlatOffset(entranceMaxDistance);

                if (!NemesisNav.TrySnapToNavMesh(candidate, out Vector3 snapped)) continue;

                // Against a wall, always: the hand on it is part of the entrance. The spot moves to
                // the wall.
                if (!TryFindWallEdge(snapped, senses, doorwayOnly, out Vector3 edgeSpot,
                                     out Vector3 hitPoint, out Vector3 hitNormal)) continue;

                // Never on the Hub's doormat: arriving there is C5 staged by the Director.
                if (!NemesisSafeZones.IsClear(edgeSpot)) continue;

                // Out of sight FIRST: it is the property that makes the whole thing fair, and it is
                // also the cheapest test of the two.
                if (arriveOutOfSightOnly && senses != null &&
                    !senses.IsOccludedByWall(playerPosition, edgeSpot)) continue;

                if (!NemesisNav.TryGetPathDistance(playerPosition, edgeSpot, out float distance)) continue;
                if (distance < entranceMinDistance || distance > entranceMaxDistance) continue;

                // A wall straight between it and the player is not a wall to lean on, it is the one
                // the arm would have to reach through.
                Vector3 toPlayer = playerPosition - edgeSpot;
                toPlayer.y = 0f;
                Vector3 intoWall = -hitNormal;
                intoWall.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f && intoWall.sqrMagnitude > 0.01f &&
                    Vector3.Dot(toPlayer.normalized, intoWall.normalized) > 0.85f) continue;

                spot = edgeSpot;
                wallPoint = hitPoint;
                wallNormal = hitNormal;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The closest edge of the NavMesh to a point, which is where a wall is (the mesh is inset from it
    /// by the agent's radius), and the wall itself under a chest-high ray. With
    /// <paramref name="doorwayOnly"/> the opening across from that edge has to be under 2.8 m: a
    /// doorway, or a corridor, and not the side of an open hall.
    /// </summary>
    private bool TryFindWallEdge(Vector3 from, FieldOfListening senses, bool doorwayOnly, out Vector3 spot,
                                 out Vector3 wallPoint, out Vector3 wallNormal)
    {
        spot = wallPoint = wallNormal = Vector3.zero;
        if (senses == null) return false;

        if (!NavMesh.FindClosestEdge(from, out NavMeshHit edge, NemesisNav.AreaMask)) return false;
        if (edge.distance > entranceWallSearchRadius) return false;

        Vector3 inward = edge.normal;
        inward.y = 0f;
        if (inward.sqrMagnitude < 0.01f) return false;
        inward.Normalize();

        if (!NavMesh.SamplePosition(edge.position + inward * 0.02f, out NavMeshHit onMesh, 0.5f,
                                    NemesisNav.AreaMask)) return false;
        spot = onMesh.position;

        // The wall, not a ledge: a ray into it at chest height, and a short one.
        if (!senses.TryRaycastWall(spot + Vector3.up * 1.3f, -inward, 1.2f, out RaycastHit wall)) return false;
        wallPoint = wall.point;
        wallNormal = wall.normal;

        if (!doorwayOnly) return true;

        // Across from the wall: the other edge has to be close.
        return NavMesh.Raycast(spot, spot + inward * 3f, out NavMeshHit across, NemesisNav.AreaMask) &&
               across.distance <= 2.8f;
    }

    private static Vector3 RandomFlatOffset(float radius)
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }

    // ── Ganchos ─────────────────────────────────────────────────────────────

    private void HandlePuzzleCompleted(string puzzleId)
    {
        for (int i = 0; i < puzzleTriggers.Count; i++)
        {
            PuzzleTrigger trigger = puzzleTriggers[i];

            if (trigger == null) continue;
            if (!string.Equals(trigger.puzzleId, puzzleId, StringComparison.OrdinalIgnoreCase)) continue;

            if (!string.IsNullOrWhiteSpace(trigger.zoneId))
                ApplyPressure(trigger.zoneId, trigger.intensity, trigger.duration,
                              EPressureSource.Scripted, ELevers.All);

            if (trigger.stageEntrance)
                StageEntranceAsync(trigger.zoneId, this.GetCancellationTokenOnDestroy()).Forget();
        }
    }

    private bool TryResolveNemesis()
    {
        if (nemesis != null) return true;

        // FindAnyObjectByType and not FindFirstObjectByType: the latter is deprecated because it
        // orders by instance ID, and that ordering is worth nothing here — a scene only ever has
        // one Nemesis, so "any" is "the one".
        //
        // FindObjectsInactive.Include is load-bearing and must stay: the Nemesis sits in the scene
        // as a DISABLED GameObject until the first puzzle activates it, so the default (active
        // only) would find nothing for the whole first stretch of the run.
        nemesis = FindAnyObjectByType<NemesisStateManager>(FindObjectsInactive.Include);
        return nemesis != null;
    }

    private void Start()
    {
        TryResolveNemesis();
        tension.Configure(pacing, nemesis);

        ReportMissingSafeZones();
        ReportZonesAtTheHub();
        ReportTriggersWithoutZone();
        ReportNoiseLayerMismatch();
    }

    private void ReportMissingSafeZones()
    {
        foreach (Unity.AI.Navigation.NavMeshModifierVolume _ in NemesisSafeZones.Volumes) return;

        Debug.LogWarning($"[{nameof(NemesisDirector)}] No baked Not Walkable volume carries a SafeZoneMarker, so " +
                         "the Director cannot tell where the Hub is and the C5 guard is off. Add a SafeZoneMarker " +
                         "next to the Hub's NavMeshModifierVolume.", this);
    }

    private void ReportNoiseLayerMismatch()
    {
        if (nemesis == null) return;

        FieldOfListening senses = nemesis.FieldOfListening;
        if (senses == null) return;

        // A noise on a layer the Nemesis does not listen to is not a quiet noise, it is no noise
        // at all — and nothing about it looks wrong in the inspector. Reported once, on load,
        // rather than discovered by a designer wondering why pressure does nothing.
        if ((senses.ListenMask.value & (1 << noiseLayer)) != 0) return;

        Debug.LogError($"[{nameof(NemesisDirector)}] Noise Layer is '{LayerMask.LayerToName(noiseLayer)}' " +
                       $"({noiseLayer}), which is not in the Nemesis's Listen Mask. Synthetic " +
                       "noises will be inaudible. Use the same layer the player's noise emitter " +
                       "is on (DetectableAudio).", this);
    }

    private static void ReportZonesAtTheHub()
    {
        IReadOnlyList<NemesisPressureZone> zones = NemesisPressureZone.Active;

        for (int i = 0; i < zones.Count; i++)
        {
            NemesisPressureZone zone = zones[i];
            if (zone == null || NemesisSafeZones.IsCentreClear(zone.Center)) continue;

            Debug.LogError($"[{nameof(NemesisDirector)}] Pressure zone '{zone.ZoneId}' is centred " +
                           $"{NemesisSafeZones.FlatDistance(zone.Center):0.0} m from the Hub (minimum " +
                           $"{NemesisSafeZones.Clearance:0} m). Every request on it will be refused.", zone);
        }
    }

    private void ReportTriggersWithoutZone()
    {
        for (int i = 0; i < puzzleTriggers.Count; i++)
        {
            PuzzleTrigger trigger = puzzleTriggers[i];
            if (trigger == null || string.IsNullOrWhiteSpace(trigger.zoneId)) continue;
            if (NemesisPressureZone.Find(trigger.zoneId) != null) continue;

            Debug.LogWarning($"[{nameof(NemesisDirector)}] The trigger for '{trigger.puzzleId}' asks for zone " +
                             $"'{trigger.zoneId}', which is not in the scene. That puzzle will apply no pressure.", this);
        }
    }

    // ── Ritmo (Fase 5) ──────────────────────────────────────────────────────

    private void HandlePacingStateChanged(NemesisTension.EPacingState state)
    {
        if (!isActiveAndEnabled) return;

        if (state != NemesisTension.EPacingState.BuildUp)
        {
            risingStep = 0;

            // "Vuelve a pasar" is a BuildUp move: a peak on the way cancels it (plan §18.5 C2).
            CancelRevisit();
        }

        TickPacing();

        // Lever 5 follows the rhythm. If TickPacing just applied or cleared a pressure, the loan is
        // already settled for this state and this is a no-op.
        RefreshLoan();
    }

    /// <summary>
    /// The Nemesis became suspended (asleep, in a cinematic, in the escape) or stopped being so, or
    /// the tension just started. Raised by NemesisTension on the frame it happens — on waking, from
    /// inside NemesisEvents.OnActivated — rather than found on the next evaluation tick, up to
    /// <see cref="evaluationInterval"/> later: the escape wakes the Nemesis already inside its
    /// cinematic, and it must not take a single step with a puzzle's senses boost on.
    ///
    /// Taken through the tension and not by subscribing to OnActivated here: the tension has to have
    /// started and measured the suspension first, and two listeners on one static event run in
    /// whatever order they happened to subscribe.
    /// </summary>
    private void HandleSuspensionChanged()
    {
        if (!isActiveAndEnabled) return;

        if (tension.IsSuspended) StandDown();

        // Suspended, the persistence is 1; back from it, whatever the rhythm says.
        RefreshLoan();
    }

    /// <summary>
    /// Turns the pacing state into levers. Puzzle beats and API requests outrank it: while one is live
    /// the rhythm waits, and it only ever replaces or clears its own requests.
    /// </summary>
    private void TickPacing()
    {
        if (pacing == null || tension == null || !tension.IsRunning) return;

        if (tension.IsSuspended)
        {
            StandDown();
            return;
        }

        // Before the scripted early-out: a revisit falling due under a puzzle's pressure is dropped,
        // not held back until that pressure lapses.
        TickRevisit();
        TickPresence();

        bool ownsPressure = activeZone != null && activeSource != EPressureSource.Scripted;

        if (activeZone != null && activeSource == EPressureSource.Scripted) return;

        switch (tension.State)
        {
            case NemesisTension.EPacingState.BuildUp:
                TickBuildUp(ownsPressure);
                break;

            case NemesisTension.EPacingState.SustainPeak:
                break;

            // "Corta el ruido sintético y el boost" and waits for the encounter to end on its own.
            case NemesisTension.EPacingState.PeakFade:
                if (ownsPressure) ClearPressure();
                break;

            case NemesisTension.EPacingState.Relax:
                if (activeZone == null) ApplyRetreat();
                break;
        }
    }

    private void TickBuildUp(bool ownsPressure)
    {
        if (ownsPressure && activeSource == EPressureSource.Retreat) ClearPressure();

        // C5: no pressure following the player into the Hub.
        if (tension.IsPlayerInSafeZone)
        {
            if (activeZone != null && activeSource == EPressureSource.RisingSensitivity) ClearPressure();
            risingStep = 0;
            return;
        }

        // An encounter resets the ramp; a live request lapses on its own or is cleared at PeakFade.
        if (!tension.IsQuiet)
        {
            risingStep = 0;
            return;
        }

        if (Time.time < nextRisingAt) return;

        ApplyRisingSensitivity();
    }

    /// <summary>
    /// Mr. X's anti-stall: pressure where the player is, a step stronger every renewal. Each renewal
    /// also raises the search persistence with it (lever 5, through the loan the pressure settles).
    /// </summary>
    private void ApplyRisingSensitivity()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return;

        NemesisPressureZone zone = FindZoneAt(player.position);
        if (zone == null) return;

        float intensity = Mathf.Min(pacing.RisingMaxIntensity,
                                    pacing.RisingStartIntensity + pacing.RisingIntensityStep * risingStep);

        // Outlives the next renewal, so the pressure never drops out between two of them.
        float duration = pacing.RisingRepeatSeconds + Mathf.Max(0.5f, evaluationInterval) + 1f;

        ApplyPressure(zone, intensity, duration, EPressureSource.RisingSensitivity, ELevers.All);

        risingStep++;
        nextRisingAt = Time.time + pacing.RisingRepeatSeconds;
    }

    /// <summary>Relax: the patrol leans towards the zone farthest from the player, by NavMesh.</summary>
    private void ApplyRetreat()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return;

        NemesisPressureZone zone = FindFarthestZone(player.position);
        if (zone == null) return;

        float duration = Mathf.Max(1f, tension.StateTimeRemaining);
        ApplyPressure(zone, pacing.RetreatIntensity, duration, EPressureSource.Retreat, PatrolLevers);
    }

    // ── Vuelve a pasar (plan §18.5 C2) ──────────────────────────────────────

    /// <summary>
    /// A search ended. Empty, on a quiet BuildUp: schedule the patrol to come back past it later —
    /// the version of C1's "it never comes back on purpose" that needs no habits (Fase 6 turns it into
    /// an ambush). Found (it left for Chasing or Catch): whatever was pending belonged to an older
    /// search the Nemesis has since moved past, so it goes.
    ///
    /// The LAST empty end wins, replacing the pending one. With Investigating escalating into a short
    /// search (D26), and Searching → Investigating counting as an end too, one hunt can end several
    /// searches in a row; the revisit belongs to where it gave up last. A search interrupted by the
    /// Nemesis being switched off is reported when it next leaves Searching (NemesisEvents), by which
    /// time the rhythm has moved on or not — the BuildUp check below is made then, which is what counts.
    /// </summary>
    private void HandleSearchEnded(Vector3 area, bool found)
    {
        if (!isActiveAndEnabled) return;

        if (found)
        {
            CancelRevisit();
            CancelVanish();
            return;
        }

        if (pacing == null || tension == null || !tension.IsRunning || tension.IsSuspended) return;

        RollVanish();

        // In Relax it does not come back (plan §18.5 C2): that is the retreat, and coming back would
        // undo it. SustainPeak and PeakFade are an encounter still resolving.
        if (tension.State != NemesisTension.EPacingState.BuildUp) return;

        revisitPending = true;
        revisitArea = area;
        revisitDueAt = Time.time + pacing.RollRevisitDelay();
    }

    /// <summary>A capture is the encounter, not a search it gave up on: nothing to come back to.</summary>
    private void HandlePlayerCaptured(PlayerStateManager player)
    {
        CancelRevisit();
        CancelVanish();
    }

    private void CancelRevisit() => revisitPending = false;

    // ── Presencia: desaparece y aparece cerca ───────────────────────────────

    private void CancelVanish()
    {
        vanishPending = false;
        returnPending = false;
    }

    /// <summary>
    /// A search ended empty: roll whether the Nemesis goes away instead of patrolling on from where it
    /// gave up. Not in SustainPeak or PeakFade, which are an encounter still resolving. The move itself
    /// waits in <see cref="TickPresence"/> until the player cannot see it go.
    /// </summary>
    private void RollVanish()
    {
        if (pacing.VanishChance <= 0f) return;

        NemesisTension.EPacingState state = tension.State;
        if (state != NemesisTension.EPacingState.BuildUp && state != NemesisTension.EPacingState.Relax) return;

        if (UnityEngine.Random.value >= pacing.VanishChance) return;

        vanishPending = true;
        vanishDueAt = Time.time + pacing.RollVanishDelay();
        vanishGiveUpAt = vanishDueAt + pacing.VanishRetrySeconds;
    }

    /// <summary>
    /// The two presence moves, from <see cref="TickPacing"/>. Never over a puzzle's scripted pressure,
    /// and never with the Nemesis doing anything but patrolling: a hunt in flight is not ours to cut.
    /// </summary>
    private void TickPresence()
    {
        // The clock of "how long since the player saw it" only runs while there is something to see:
        // asleep, away in a hunt or with the player in the Hub it is held at zero.
        if (!TryResolveNemesis() || !nemesis.IsActive || isStagingEntrance ||
            nemesis.CurrentStateKey != NemesisStateManager.ENemesisState.Patrolling ||
            tension.IsPlayerInSafeZone || tension.PlayerSeesNemesis)
        {
            lastSawNemesisAt = Time.time;
        }

        if (!TryResolveNemesis() || !nemesis.IsActive || isStagingEntrance) return;
        if (activeZone != null && activeSource == EPressureSource.Scripted) return;
        if (nemesis.CurrentStateKey != NemesisStateManager.ENemesisState.Patrolling) return;

        TickVanish();
        TickReturn();
        TickAmbientEntrance();
        TickUnseenPeek();
    }

    /// <summary>
    /// Goes through NemesisController.ChooseSpawnPoint, which is already the fairness rule: far enough
    /// over the NavMesh, outside the player's cone and behind geometry, or it returns null and nothing
    /// moves. On top of that the Nemesis has to be out of the player's sight where it stands NOW, and
    /// at least <see cref="SO_DirectorPacing.VanishMinPlayerDistance"/> away, so nobody watches it go.
    /// </summary>
    private void TickVanish()
    {
        if (!vanishPending || Time.time < vanishDueAt) return;

        if (Time.time > vanishGiveUpAt || tension.State == NemesisTension.EPacingState.SustainPeak ||
            tension.State == NemesisTension.EPacingState.PeakFade)
        {
            CancelVanish();
            return;
        }

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null || nemesis.NemesisController == null) return;

        if (tension.PlayerSeesNemesis) return;

        FieldOfListening senses = nemesis.FieldOfListening;
        if (senses != null && !senses.IsOccludedByWall(player.position, nemesis.transform.position)) return;

        if (NemesisNav.TryGetPathDistance(player.position, nemesis.transform.position, out float distance) &&
            distance < pacing.VanishMinPlayerDistance)
        {
            return;
        }

        // Retried on the next tick if nothing is safe yet: the player walking on usually fixes it.
        if (nemesis.NemesisController.ChooseSpawnPoint() == null) return;

        CancelVanish();

        // It is gone. A while later it may be back at the door of the room the player is in, when
        // they have stopped looking over their shoulder (TickReturn).
        if (UnityEngine.Random.value < pacing.VanishReturnChance)
        {
            returnPending = true;
            returnDueAt = Time.time + pacing.RollVanishReturnDelay();
            returnGiveUpAt = returnDueAt + pacing.VanishReturnRetrySeconds;
        }
    }

    /// <summary>
    /// The scare the vanish sets up: standing at a doorway, one hand on the wall and the other
    /// reaching round it, after the player thought it had gone. It is the Mr. X entrance, so it keeps
    /// every guarantee of that one (out of sight, never closer than its minimum, never in the Hub);
    /// what this adds is only the WHEN.
    /// </summary>
    private void TickReturn()
    {
        if (!returnPending || Time.time < returnDueAt) return;

        if (Time.time > returnGiveUpAt || tension.State != NemesisTension.EPacingState.BuildUp)
        {
            returnPending = false;
            return;
        }

        if (tension.IsPlayerInSafeZone) return;

        returnPending = false;
        StageEntranceAsync(null, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// The longer the player goes without seeing the Nemesis, the likelier it is to lean out of a doorway
    /// ahead of them: coming out of another room into a corridor, or about to walk into one. Nothing
    /// before <c>UnseenPeekAfterSeconds</c>, then a chance per roll that climbs to its maximum. The clock
    /// starts over whenever the player sees it, and after every peek this makes.
    /// </summary>
    private void TickUnseenPeek()
    {
        if (pacing.UnseenPeekAfterSeconds <= 0f || tension.State != NemesisTension.EPacingState.BuildUp) return;
        if (Time.time < nextUnseenRollAt) return;
        nextUnseenRollAt = Time.time + Mathf.Max(0.5f, evaluationInterval);

        float chance = pacing.UnseenPeekChance(Time.time - lastSawNemesisAt);
        if (chance <= 0f || UnityEngine.Random.value >= chance) return;

        if (!TryStagePeekAhead()) return;

        // Counts as seen: the next one is a whole wait away.
        lastSawNemesisAt = Time.time;
    }

    private bool TryStagePeekAhead()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null || nemesis.IsUsingElevator) return false;

        // Where the player is LOOKING, not where the body faces: the camera is what the doorway has to be
        // in front of.
        Camera view = Camera.main;
        Vector3 forward = view != null ? view.transform.forward : player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return false;
        forward.Normalize();

        if (!TryFindPeekAhead(player.position, forward, out Vector3 spot, out Vector3 wallPoint,
                              out Vector3 wallNormal)) return false;

        HoldPeekAsync(spot, wallPoint, wallNormal, player, entranceStareSeconds,
                      this.GetCancellationTokenOnDestroy()).Forget();
        return true;
    }

    /// <summary>
    /// A doorway or corridor mouth in the cone ahead of the player: against a wall, on their floor, out of
    /// sight (the body is behind the frame; it is the arm that shows), and between the pacing's minimum and
    /// maximum distance over the NavMesh. Doorways first, any wall second, as for the entrance. Returns
    /// false when the level has no such room ahead, which is the normal answer in a dead end.
    /// </summary>
    private bool TryFindPeekAhead(Vector3 playerPosition, Vector3 forward, out Vector3 spot,
                                  out Vector3 wallPoint, out Vector3 wallNormal)
    {
        spot = wallPoint = wallNormal = Vector3.zero;

        FieldOfListening senses = nemesis.FieldOfListening;
        Vector2 range = pacing.UnseenPeekDistance;
        float half = pacing.UnseenPeekConeDegrees;
        float floor = nemesis.NemesisData != null ? nemesis.NemesisData.FloorHeightThreshold : 2.5f;

        for (int pass = 0; pass < 2; pass++)
        {
            bool doorwayOnly = pass == 0;

            for (int attempt = 0; attempt < entranceSampleAttempts; attempt++)
            {
                float angle = UnityEngine.Random.Range(-half, half);
                float distance = UnityEngine.Random.Range(range.x, range.y);
                Vector3 candidate = playerPosition + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;

                if (!NemesisNav.TrySnapToNavMesh(candidate, out Vector3 snapped)) continue;
                if (!TryFindWallEdge(snapped, senses, doorwayOnly, out Vector3 edgeSpot, out Vector3 hitPoint,
                                     out Vector3 hitNormal)) continue;

                if (Mathf.Abs(edgeSpot.y - playerPosition.y) > floor) continue;
                if (!NemesisSafeZones.IsClear(edgeSpot)) continue;

                // Still ahead after the snap to the wall, not just at the sampled point.
                Vector3 offset = edgeSpot - playerPosition;
                offset.y = 0f;
                if (Vector3.Angle(forward, offset) > half + 15f) continue;

                if (senses != null && !senses.IsOccludedByWall(playerPosition + Vector3.up * 1.5f,
                                                              edgeSpot + Vector3.up * 1.2f)) continue;

                if (!NemesisNav.TryGetPathDistance(playerPosition, edgeSpot, out float path)) continue;
                if (path < range.x || path > range.y * 1.5f) continue;

                spot = edgeSpot;
                wallPoint = hitPoint;
                wallNormal = hitNormal;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every so often, the Mr. X entrance with no puzzle behind it. The roll is made on a clock rather
    /// than on an event so it does not need anyone to have lost the player. Not in the Hub, and the
    /// entrance itself still refuses anything in sight or closer than its minimum.
    /// </summary>
    private void TickAmbientEntrance()
    {
        if (!pacing.AmbientEntranceEnabled) return;

        if (nextAmbientEntranceAt < 0f)
        {
            nextAmbientEntranceAt = Time.time + pacing.RollAmbientEntranceInterval();
            return;
        }

        if (Time.time < nextAmbientEntranceAt) return;
        nextAmbientEntranceAt = Time.time + pacing.RollAmbientEntranceInterval();

        if (tension.State != NemesisTension.EPacingState.BuildUp) return;
        if (tension.IsPlayerInSafeZone) return;
        if (UnityEngine.Random.value >= pacing.AmbientEntranceChance) return;

        StageEntranceAsync(null, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// Applies the pending revisit once it falls due: anchor and route weights on the zone of that
    /// search (the one containing it, else the nearest by NavMesh; never one at the Hub's door), at
    /// RevisitIntensity for RevisitDuration. A patrol bias, not an order: it rolls towards the area in
    /// a cycle or two, and never learns anything about where the player is.
    ///
    /// Only onto a Nemesis that is PATROLLING, in BuildUp, with no other pressure live. A scripted or
    /// rising-sensitivity pressure wins and the revisit is dropped; an older revisit still running is
    /// replaced, since the newer one comes from a later search. Leaving BuildUp and a capture cancel it
    /// before it gets here.
    ///
    /// Mid-hunt when it falls due (Searching, Investigating, Chasing, Catch or Traversing), it is
    /// RETRIED <see cref="RevisitRetrySeconds"/> later rather than dropped. Either the hunt ends a
    /// search — and that end replaces it (empty) or cancels it (found) on its own — or it does not: a
    /// lead investigated and let go, which says nothing about the search the player got away from,
    /// and dropping the revisit there would make it depend on a decoy. A hunt that turns into an
    /// encounter peaks the tension, and leaving BuildUp cancels it anyway.
    /// </summary>
    private void TickRevisit()
    {
        if (!revisitPending || Time.time < revisitDueAt) return;

        if (tension.State != NemesisTension.EPacingState.BuildUp ||
            (activeZone != null && activeSource != EPressureSource.Revisit))
        {
            CancelRevisit();
            return;
        }

        NemesisStateManager.ENemesisState? state = nemesis != null ? nemesis.CurrentStateKey : null;

        if (state != NemesisStateManager.ENemesisState.Patrolling)
        {
            // Null is a dormant Nemesis, which the suspension should already have caught.
            if (state.HasValue) revisitDueAt = Time.time + RevisitRetrySeconds;
            else CancelRevisit();
            return;
        }

        CancelRevisit();

        NemesisPressureZone zone = FindZoneAt(revisitArea);
        if (zone == null) return;

        ApplyPressure(zone, pacing.RevisitIntensity, pacing.RevisitDuration, EPressureSource.Revisit, PatrolLevers);
    }

    /// <summary>The zone containing a point (flat, nearest centre), else the nearest one by NavMesh.
    /// The player's position for rising sensitivity; where a search ended for the revisit.</summary>
    private static NemesisPressureZone FindZoneAt(Vector3 point)
    {
        IReadOnlyList<NemesisPressureZone> zones = NemesisPressureZone.Active;

        NemesisPressureZone best = null;
        float bestFlat = float.PositiveInfinity;

        for (int i = 0; i < zones.Count; i++)
        {
            NemesisPressureZone zone = zones[i];
            if (!IsUsable(zone) || !zone.Contains(point)) continue;

            Vector3 offset = point - zone.Center;
            offset.y = 0f;

            if (offset.sqrMagnitude >= bestFlat) continue;

            bestFlat = offset.sqrMagnitude;
            best = zone;
        }

        if (best != null) return best;

        float bestPath = float.PositiveInfinity;

        for (int i = 0; i < zones.Count; i++)
        {
            NemesisPressureZone zone = zones[i];
            if (!IsUsable(zone)) continue;

            float distance = NemesisNav.PathDistanceOrInfinity(point, zone.Center);
            if (distance >= bestPath) continue;

            bestPath = distance;
            best = zone;
        }

        return best;
    }

    /// <summary>Farthest by NavMesh; farthest in a straight line when nothing is reachable.</summary>
    private static NemesisPressureZone FindFarthestZone(Vector3 playerPosition)
    {
        IReadOnlyList<NemesisPressureZone> zones = NemesisPressureZone.Active;

        NemesisPressureZone byPath = null;
        NemesisPressureZone byLine = null;
        float bestPath = -1f;
        float bestLine = -1f;

        for (int i = 0; i < zones.Count; i++)
        {
            NemesisPressureZone zone = zones[i];
            if (!IsUsable(zone)) continue;

            float line = Vector3.Distance(playerPosition, zone.Center);
            if (line > bestLine)
            {
                bestLine = line;
                byLine = zone;
            }

            float path = NemesisNav.PathDistanceOrInfinity(playerPosition, zone.Center);
            if (!float.IsPositiveInfinity(path) && path > bestPath)
            {
                bestPath = path;
                byPath = zone;
            }
        }

        return byPath != null ? byPath : byLine;
    }

    private static bool IsUsable(NemesisPressureZone zone) =>
        zone != null && NemesisSafeZones.IsCentreClear(zone.Center);

    private static string LabelOf(EPressureSource source) => source switch
    {
        EPressureSource.RisingSensitivity => "sensibilidad",
        EPressureSource.Retreat => "retirada",
        EPressureSource.Revisit => "vuelta",
        _ => "puzzle/API",
    };

    // ── Lectura para debug (F9, F10, gizmos) ────────────────────────────────

    public static NemesisTension Tension => Exists ? Instance.tension : null;

    public static string ActiveZoneId =>
        Exists && Instance.activeZone != null ? Instance.activeZone.ZoneId : null;

    public static float ActiveIntensity => Exists && Instance.activeZone != null ? Instance.activeIntensity : 0f;

    public static float ActiveTimeRemaining =>
        Exists && Instance.activeZone != null ? Mathf.Max(0f, Instance.pressureEndsAt - Time.time) : 0f;

    public static string ActiveSourceLabel =>
        Exists && Instance.activeZone != null ? LabelOf(Instance.activeSource) : null;

    /// <summary>Rising-sensitivity renewals so far in this silence (0 when the ramp is idle).</summary>
    public static int RisingStep => Exists ? Instance.risingStep : 0;

    /// <summary>
    /// What the search's quiet window and hard cap are multiplied by right now (lever 5): the number
    /// actually lent to the Nemesis, so the one its Searching state is reading. 1 with nothing lent,
    /// and with no Director in the scene.
    /// </summary>
    public static float SearchPersistence => Exists ? Instance.loanedPersistence : 1f;

    /// <summary>Why <see cref="SearchPersistence"/> is what it is: the pacing state, or "sensibilidad"
    /// while a rising-sensitivity request drives it. Null with nothing lent.</summary>
    public static string SearchPersistenceReason => Exists ? Instance.loanedPersistenceReason : null;

    /// <summary>Seconds until the pending "vuelve a pasar" falls due, or -1 when none is pending.
    /// </summary>
    public static float RevisitTimeRemaining =>
        Exists && Instance.revisitPending ? Mathf.Max(0f, Instance.revisitDueAt - Time.time) : -1f;

#if UNITY_EDITOR
    private static readonly Vector3[] FootprintScratch = new Vector3[4];

    /// <summary>Selecting the Director shows coverage: waypoints outside every zone, and the Hub's no-go band.</summary>
    private void OnDrawGizmosSelected()
    {
        NemesisPressureZone[] zones = FindObjectsByType<NemesisPressureZone>(FindObjectsInactive.Exclude);

        int total = 0;
        int covered = 0;

        foreach (NemesisRoute route in FindObjectsByType<NemesisRoute>(FindObjectsInactive.Exclude))
        {
            for (int i = 0; i < route.transform.childCount; i++)
            {
                Transform waypoint = route.transform.GetChild(i);
                if (!waypoint.CompareTag(NemesisRoute.WaypointTag)) continue;

                total++;

                bool inAny = false;
                for (int z = 0; z < zones.Length && !inAny; z++)
                    inAny = zones[z].isActiveAndEnabled && zones[z].Contains(waypoint.position);

                if (inAny)
                {
                    covered++;
                    continue;
                }

                Gizmos.color = UncoveredColor;
                Gizmos.DrawWireCube(waypoint.position + Vector3.up * 0.5f, Vector3.one * 0.8f);
                UnityEditor.Handles.color = UncoveredColor;
                UnityEditor.Handles.Label(waypoint.position + Vector3.up * 1.4f, $"sin zona · {waypoint.name}");
            }
        }

        foreach (Unity.AI.Navigation.NavMeshModifierVolume volume in NemesisSafeZones.Volumes)
            DrawSafeZoneBand(volume);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                                  $"Director · cobertura {covered}/{total} waypoints · {zones.Length} zonas");
    }

    private static void DrawSafeZoneBand(Unity.AI.Navigation.NavMeshModifierVolume volume)
    {
        NemesisSafeZones.GetFootprint(volume, FootprintScratch);

        Vector3 p0 = FootprintScratch[0];
        Vector3 a = (FootprintScratch[1] - p0).normalized;
        Vector3 b = (FootprintScratch[3] - p0).normalized;
        float clearance = NemesisSafeZones.Clearance;

        UnityEditor.Handles.color = SafeZoneColor;
        UnityEditor.Handles.DrawAAPolyLine(3f, FootprintScratch[0], FootprintScratch[1],
                                           FootprintScratch[2], FootprintScratch[3], FootprintScratch[0]);

        Vector3 e0 = FootprintScratch[0] - a * clearance - b * clearance;
        Vector3 e1 = FootprintScratch[1] + a * clearance - b * clearance;
        Vector3 e2 = FootprintScratch[2] + a * clearance + b * clearance;
        Vector3 e3 = FootprintScratch[3] - a * clearance + b * clearance;

        UnityEditor.Handles.color = new Color(SafeZoneColor.r, SafeZoneColor.g, SafeZoneColor.b, 0.5f);
        UnityEditor.Handles.DrawDottedLines(new[] { e0, e1, e1, e2, e2, e3, e3, e0 }, 4f);
        UnityEditor.Handles.Label((e2 + e3) * 0.5f + Vector3.up,
                                  $"{volume.name}: sin centros de zona a menos de {clearance:0} m");
    }

    private static readonly Color UncoveredColor = new Color(1f, 0.35f, 0.25f, 0.9f);
    private static readonly Color SafeZoneColor = new Color(0.35f, 0.85f, 0.55f, 0.9f);
#endif
}
