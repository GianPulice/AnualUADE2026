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
/// And one theatrical move that is not a lever but a scene: <see cref="StageEntranceAsync"/>, the
/// Mr. X entrance. See its own doc for why a teleport can be honest.
///
/// SETUP: one of these in the level (it is a Singleton), plus a NemesisPressureZone per area worth
/// naming. Nothing else has to know it exists — NemesisController asks it for an anchor through a
/// static, and answers itself when there is no Director in the scene.
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

    private NemesisPressureZone activeZone;
    private float activeIntensity;
    private float pressureEndsAt;

    private float nextEvaluationAt;
    private float nextNoiseAt;

    private bool isStagingEntrance;

    /// <summary>The routes whose weight this Director has touched, so exactly those get put back.
    /// A route the designer weighted for their own reasons must not be "restored" to 1 by us.
    /// </summary>
    private readonly List<NemesisRoute> boostedRoutes = new List<NemesisRoute>();

    private NemesisRoute[] allRoutes;
    private NemesisStateManager nemesis;

    /// <summary>
    /// The asset as the designer authored it, kept so the sensory boost can be built from it fresh
    /// every time and thrown away after. Never written to.
    /// </summary>
    private SO_NemesisData authoredData;

    /// <summary>The live copy carrying the boost, or null when no boost is installed.</summary>
    private SO_NemesisData boostedData;

    private void Awake() => CreateSingleton(false);

    private void OnEnable() => PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;

    private void OnDisable()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;

        // Everything this class does is a temporary edit to somebody else's state. A Director torn
        // down mid-request would otherwise leave boosted route weights and a cloned data asset
        // installed on the Nemesis for the rest of the run, with nothing left alive to undo them.
        ClearPressure();
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

        Instance.ApplyPressure(zoneId, intensity, duration);
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
        if (activeZone == null) return;

        if (Time.time >= pressureEndsAt)
        {
            ClearPressure();
            return;
        }

        ApplyRouteWeights();
        TryEmitNoise();
    }

    private void ApplyPressure(string zoneId, float intensity, float duration)
    {
        NemesisPressureZone zone = NemesisPressureZone.Find(zoneId);

        if (zone == null)
        {
            Debug.LogWarning($"[{nameof(NemesisDirector)}] No pressure zone called '{zoneId}'. " +
                             "Check the id against the NemesisPressureZone in the scene — nothing " +
                             "happens until they match.", this);
            return;
        }

        intensity = Mathf.Clamp01(intensity);

        if (intensity <= 0f || duration <= 0f)
        {
            ClearPressure();
            return;
        }

        // Whatever the last request left behind goes back first. Without this, a second request
        // over a first one would boost the new zone's routes without ever restoring the old
        // zone's — and the level would slowly turn into one where every route is boosted.
        ClearPressure();

        activeZone = zone;
        activeIntensity = intensity;
        pressureEndsAt = Time.time + duration;

        // Immediately, not on the next tick: a request made right after a puzzle is a beat in the
        // level's pacing, and up to evaluationInterval of nothing happening blunts it.
        nextNoiseAt = Time.time + Mathf.Max(0f, noiseInterval);
        ApplyRouteWeights();
        ApplySensoryBoost();

        Debug.Log($"[{nameof(NemesisDirector)}] Pressure on '{zone.ZoneId}' at {intensity:0.00} " +
                  $"for {duration:0}s.", this);
    }

    private void ClearPressure()
    {
        RestoreRouteWeights();
        RemoveSensoryBoost();

        activeZone = null;
        activeIntensity = 0f;
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

    /// <summary>A point inside the zone that the Nemesis could actually stand on.</summary>
    private bool TrySampleInZone(NemesisPressureZone zone, out Vector3 point)
    {
        point = zone.Center;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * zone.Radius;
            Vector3 candidate = zone.Center + new Vector3(offset.x, 0f, offset.y);

            if (NemesisNav.TrySnapToNavMesh(candidate, out point)) return true;
        }

        return NemesisNav.TrySnapToNavMesh(zone.Center, out point);
    }

    // ── Palanca 4: sentidos ─────────────────────────────────────────────────

    /// <summary>
    /// Installs a widened copy of the tuning asset.
    ///
    /// A COPY, AND A FRESH ONE EVERY TIME. Writing the boost into SO_NemesisData itself would be
    /// simpler by one line and wrong in a way that only shows up days later: ScriptableObject
    /// edits made in Play mode persist into the asset in the Editor, so a playtest that happened
    /// to run a pressure request would leave the boosted ranges in the project, and the next
    /// person to open the asset would find numbers nobody typed. Cloning from the authored asset
    /// at install time also means a designer's edits between two requests are picked up.
    /// </summary>
    private void ApplySensoryBoost()
    {
        if (sensoryBoost <= 1f) return;
        if (!TryResolveNemesis()) return;

        authoredData ??= nemesis.NemesisData;
        if (authoredData == null) return;

        boostedData = Instantiate(authoredData);
        boostedData.name = authoredData.name + " (director)";

        float multiplier = Mathf.Lerp(1f, sensoryBoost, activeIntensity);
        boostedData.ListenRange *= multiplier;
        boostedData.ViewRange *= multiplier;

        nemesis.OverrideData(boostedData);
    }

    private void RemoveSensoryBoost()
    {
        if (boostedData == null) return;

        if (nemesis != null && authoredData != null) nemesis.OverrideData(authoredData);

        Destroy(boostedData);
        boostedData = null;
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

        if (!TryFindEntrancePoint(player.position, zone, out Vector3 spot))
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

            float waited = 0f;
            while (waited < entranceStareSeconds)
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
                                      out Vector3 spot)
    {
        spot = Vector3.zero;

        FieldOfListening senses = nemesis.FieldOfListening;

        for (int attempt = 0; attempt < entranceSampleAttempts; attempt++)
        {
            Vector3 candidate = zone != null
                ? zone.Center + RandomFlatOffset(zone.Radius)
                : playerPosition + RandomFlatOffset(entranceMaxDistance);

            if (!NemesisNav.TrySnapToNavMesh(candidate, out Vector3 snapped)) continue;

            // Out of sight FIRST: it is the property that makes the whole thing fair, and it is
            // also the cheapest test of the two.
            if (arriveOutOfSightOnly && senses != null &&
                !senses.IsOccludedByWall(playerPosition, snapped)) continue;

            if (!NemesisNav.TryGetPathDistance(playerPosition, snapped, out float distance)) continue;
            if (distance < entranceMinDistance || distance > entranceMaxDistance) continue;

            spot = snapped;
            return true;
        }

        return false;
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
                ApplyPressure(trigger.zoneId, trigger.intensity, trigger.duration);

            if (trigger.stageEntrance)
                StageEntranceAsync(trigger.zoneId, this.GetCancellationTokenOnDestroy()).Forget();
        }
    }

    private bool TryResolveNemesis()
    {
        if (nemesis != null) return true;

        nemesis = FindFirstObjectByType<NemesisStateManager>(FindObjectsInactive.Include);
        return nemesis != null;
    }

    private void Start()
    {
        if (!TryResolveNemesis()) return;

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
}
