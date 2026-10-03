using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// On-screen readout of what the Nemesis currently believes and is doing.
///
/// WHY THIS EXISTS
///
/// Every instrument the Nemesis had was spatial: the gizmos draw ranges, the route gizmos draw
/// polylines, the validator checks masks. All of it answers "how far" and none of it answers
/// "how long" — and the numbers that actually decide how the monster FEELS are the temporal ones.
/// How long from losing sight to being hunted again. How long a search lasts before it gives up.
/// How stale the belief steering a patrol is. Those were tuned by playing and guessing, because
/// nothing in the project could show them.
///
/// The one number worth the whole component is <see cref="lastSafeTime"/>: seconds from the last
/// detection to the Nemesis going back to Patrolling. That is "how long until you feel safe", and
/// it is the number to tune the state timeouts against.
///
/// SETUP: add it to the Nemesis root. It costs nothing while switched off — Update returns on the
/// first line and OnGUI is not entered.
///
/// THE FILE. While it is open, what it shows also goes to a .txt in the project's Logs/NemesisF9
/// folder, one per opening (see <see cref="FileLog"/>), and <see cref="markKey"/> drops a numbered
/// mark in it. So a playtest can be read back, and handed over, as what the Nemesis said it was
/// doing against what the player saw.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisDebugHUD : MonoBehaviour
{
    [Header("Toggle")]
    [Tooltip("Shows and hides the overlay. Off by default: this is a tuning tool, not a feature.")]
    [SerializeField] private bool visible;

    [Tooltip("Key that toggles the overlay while playing.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;

    [Header("Layout")]
    [SerializeField] private Vector2 origin = new Vector2(12f, 12f);
    [SerializeField] private float width = 340f;

    [Tooltip("Seconds of state history shown in the strip along the bottom of the panel.")]
    [SerializeField, Min(5f)] private float historySeconds = 60f;

    [Header("File log (editor only, like F9)")]
    [Tooltip("While the overlay is open, writes what it shows to a .txt in the project's " +
             "Logs/NemesisF9 folder: a new file each time it is opened.")]
    [SerializeField] private bool logToFile = true;

    [Tooltip("Seconds between entries. Each one lists only the rows that changed since the last.")]
    [SerializeField, Min(0.05f)] private float logInterval = 0.5f;

    [Tooltip("Seconds between full copies of the panel, so any stretch of the file reads on its own.")]
    [SerializeField, Min(1f)] private float logFullEvery = 10f;

    [Tooltip("With the overlay open: drops a numbered mark in the file, with a full copy of the " +
             "panel. For the moment something looks wrong.")]
    [SerializeField] private KeyCode markKey = KeyCode.F7;

    // Same palette as NemesisGizmos, and for the same reason: amber is alert, cool blue is
    // passive, red is danger and appears exactly once. A HUD that colours states differently from
    // the Scene view makes you translate between two pictures of the same thing.
    private static readonly Color PatrolColor    = new Color(0.35f, 0.50f, 0.62f);
    private static readonly Color InvestigColor  = new Color(0.54f, 0.71f, 0.83f);
    private static readonly Color SearchColor    = new Color(0.65f, 0.55f, 0.85f);
    private static readonly Color ChaseColor     = new Color(1.00f, 0.78f, 0.31f);
    private static readonly Color TraverseColor  = new Color(0.55f, 0.75f, 0.45f);
    private static readonly Color CatchColor     = new Color(0.80f, 0.10f, 0.10f);

    private readonly struct Sample
    {
        public readonly float Time;
        public readonly NemesisStateManager.ENemesisState State;

        public Sample(float time, NemesisStateManager.ENemesisState state)
        {
            Time = time;
            State = state;
        }
    }

    private NemesisStateManager stateManager;
    private NemesisChaseProgress chaseProgress;
    private readonly List<Sample> history = new List<Sample>();

    private NemesisStateManager.ENemesisState? lastState;
    private float stateEnteredAt;

    // "How long until you feel safe", sampled every time it gives up and returns to Patrolling.
    private float lastSafeTime = -1f;
    private float minSafeTime = float.PositiveInfinity;
    private float maxSafeTime;
    private float totalSafeTime;
    private int safeSamples;

    private GUIStyle panelStyle;
    private GUIStyle textStyle;
    private Texture2D panelTexture;
    private Texture2D barTexture;

    private readonly FileLog fileLog = new FileLog();
    private readonly List<(string label, string value)> frameRows = new List<(string, string)>();
    private bool collectingRows;

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        // Editor and Development Build only, like the other debug keys: the prefab ships with visible
        // ticked, and in a release build there is no F9 to turn it back off.
        visible = false;
#endif

        // NemesisStateManager adds this itself during its own Awake when the prefab is missing it,
        // so by the time any Update runs it exists — but script order between two components on
        // one object is not guaranteed, so this is re-resolved lazily where it is read.
        chaseProgress = GetComponent<NemesisChaseProgress>();
    }

    private void OnDisable() => fileLog.Close();

    private void OnDestroy()
    {
        fileLog.Close();

        // Created with new, so they are not owned by any scene object and would leak on a domain
        // reload otherwise.
        if (panelTexture != null) Destroy(panelTexture);
        if (barTexture != null) Destroy(barTexture);
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
        if (visible && Input.GetKeyDown(markKey)) fileLog.RequestMark();
#endif
        // Closed, from F9 or the inspector: that opening's file ends here, and the next opening
        // starts a new one.
        if (!visible)
        {
            fileLog.Close();
            return;
        }

        if (stateManager == null) return;

        TrackState();
    }

    /// <summary>
    /// Records transitions and, on each return to Patrolling, how long it had been since the last
    /// detection.
    ///
    /// Sampled from <see cref="NemesisStateManager.BeliefAge"/> rather than timed here, because
    /// that is the honest measure: the clock that matters starts at the last time the Nemesis
    /// sensed you, not at the moment some state happened to be entered.
    /// </summary>
    private void TrackState()
    {
        NemesisStateManager.ENemesisState? key = stateManager.CurrentStateKey;
        if (!key.HasValue) return;

        if (lastState.HasValue && lastState.Value == key.Value) return;

        if (key.Value == NemesisStateManager.ENemesisState.Patrolling && lastState.HasValue)
        {
            float age = stateManager.BeliefAge;
            if (!float.IsPositiveInfinity(age))
            {
                lastSafeTime = age;
                minSafeTime = Mathf.Min(minSafeTime, age);
                maxSafeTime = Mathf.Max(maxSafeTime, age);
                totalSafeTime += age;
                safeSamples++;
            }
        }

        lastState = key.Value;
        stateEnteredAt = Time.time;

        history.Add(new Sample(Time.time, key.Value));

        float cutoff = Time.time - historySeconds;
        while (history.Count > 1 && history[1].Time < cutoff) history.RemoveAt(0);
    }

    private void OnGUI()
    {
        if (!visible || stateManager == null) return;

        EnsureStyles();

        const float lineHeight = 17f;
        const float stripHeight = 22f;
        float height = lineHeight * 26f + stripHeight + 32f;

        Rect panel = new Rect(origin.x, origin.y, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);

        Rect line = new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, lineHeight);

        // Collected for the file once per frame: OnGUI also runs for layout and input events, with
        // the same rows every time.
        collectingRows = logToFile && Event.current.type == EventType.Repaint;
        frameRows.Clear();

        Row(ref line, "estado", DescribeState());
        Row(ref line, "regla", DescribeRung());
        Row(ref line, "sospecha", DescribeAwareness());
        Row(ref line, "escondite", DescribeHidingSpot());
        Row(ref line, "creencia", DescribeBelief());
        Row(ref line, "  oído", DescribeHearing());
        Row(ref line, "foco", DescribeFocus());
        Row(ref line, "distancia", DescribeDistance());
        Row(ref line, "persecución", DescribeChaseProgress());
        Row(ref line, "búsqueda", DescribeSearch());
        Row(ref line, "  mapa", DescribePossibilityMap());
        Row(ref line, "cúmulo", DescribeCluster());
        Row(ref line, "agente", DescribeAgent());
        Row(ref line, "trabas", DescribeStuck());
        Row(ref line, "bajada", DescribeDrop());

        line.y += 6f;
        Row(ref line, "ritmo", DescribePacing());
        Row(ref line, "presión", DescribePressure());

        line.y += 6f;
        Row(ref line, "hábitos", DescribeHabits());
        Row(ref line, "  desbloquea", DescribeUnlocks());
        Row(ref line, "  escondites", DescribeSpotMemory());
        Row(ref line, "escalada", DescribeEscalation());

        line.y += 6f;
        Row(ref line, "seguro en", lastSafeTime >= 0f ? $"{lastSafeTime:0.0} s" : "—");
        Row(ref line, "  mín / prom / máx", DescribeSafeStats());

        if (collectingRows) fileLog.Capture(frameRows, this, logInterval, logFullEvery, markKey);
        collectingRows = false;

        line.y += 6f;
        DrawHistoryStrip(new Rect(panel.x + 10f, line.y, panel.width - 20f, stripHeight));
    }

    private void Row(ref Rect line, string label, string value)
    {
        GUI.Label(line, $"<b>{label}</b>  {value}", textStyle);
        line.y += line.height;

        if (collectingRows) frameRows.Add((label, value));
    }

    /// <summary>
    /// The state, how long it has been in it, and HOW IT IS ALLOWED TO MOVE.
    ///
    /// The movement policy is on this row because it is the thing that used to be invisible. Node
    /// movement and free roam produce very different-looking behaviour from the same state name —
    /// a search working the waypoint graph and a search sweeping a room are both "Searching" — and
    /// without the label the only way to tell them apart while playing is to guess from the path
    /// it walks. See NemesisStateManager.MovementOf.
    /// </summary>
    private string DescribeState()
    {
        NemesisStateManager.ENemesisState? key = stateManager.CurrentStateKey;
        if (!key.HasValue) return "sin arrancar (dormido)";

        string movement = stateManager.CurrentMovement == NemesisStateManager.ENemesisMovement.FreeRoam
            ? "free roam"
            : "nodos";

        return $"{key.Value}  ({Time.time - stateEnteredAt:0.0} s)  ·  <b>{movement}</b>";
    }

    /// <summary>
    /// Which rung of the priority ladder won this frame.
    ///
    /// Without it, "why is it doing that" is not a question anyone can answer while playing —
    /// the state is the ANSWER, and this is the reason. It matters most for the cases that look
    /// like bugs and are not: a Nemesis that keeps chasing a player it cannot see is rung 6
    /// holding, and a Nemesis that ignores a noise for a third of a second is the hysteresis
    /// window doing its job.
    /// </summary>
    private string DescribeRung()
    {
        NemesisDecision decision = stateManager.Decision;
        if (decision == null) return "—";

        return decision.LastRungIndex >= 0
            ? $"#{decision.LastRungIndex + 1}  {decision.LastReason}"
            : decision.LastReason;
    }

    /// <summary>
    /// The peripheral-vision meter, as a bar plus its number and the threshold it has to clear.
    ///
    /// Without this row the whole two-band cone is untunable: AwarenessBuildTime is a rate nobody
    /// can see, so "did it not notice me, or did it notice me and the threshold is too high" is
    /// not a question anyone can answer while playing - and those two have opposite fixes.
    ///
    /// A bar and not just a number because what matters while testing is the SHAPE of the ramp:
    /// how fast it fills as you step further into the cone, and how fast it drains once you duck
    /// back out. Both read at a glance and neither reads off a figure changing ten times a second.
    /// </summary>
    private string DescribeAwareness()
    {
        float awareness = stateManager.Awareness;

        SO_NemesisData data = stateManager.NemesisData;
        float threshold = data != null ? data.AwarenessTriggerThreshold : 0f;

        const int Cells = 12;
        int filled = Mathf.Clamp(Mathf.RoundToInt(awareness * Cells), 0, Cells);

        string bar = new string('#', filled) + new string('.', Cells - filled);

        FieldOfView eyes = stateManager.FieldOfView;
        if (stateManager.HasVisualTarget)
        {
            // Out of the corner of its eye, where it already believed the player was: the senses added
            // up into a sighting, skipping the suspicion (VisionZones.Corroborates).
            bool corroborated = eyes != null && eyes.LastSightCorroborated;
            return corroborated ? $"[{bar}] <b>lo ve</b> (de reojo, donde ya lo creía)" : $"[{bar}] <b>lo ve</b>";
        }

        string state = stateManager.IsSuspicious ? "  ·  <b>sospecha</b>" : "";

        // Which zone is filling it (VisionZones): the corner of its eye, or a presence behind it.
        string zone = eyes == null ? ""
                    : eyes.ContactZone == VisionZones.EZone.Peripheral ? "  ·  de reojo"
                    : eyes.ContactZone == VisionZones.EZone.Rear ? "  ·  siente algo atrás"
                    : "";

        return $"[{bar}] {awareness:0.00} / {threshold:0.00}{zone}{state}";
    }

    /// <summary>
    /// Which hiding spot the Nemesis knows or suspects the player is in, why, and what the search
    /// is doing about it.
    ///
    /// The levels of knowledge (plan §3.4) are indistinguishable from outside until the monster
    /// has its hand on the door: a Nemesis walking to a locker it SAW you enter, one walking to a
    /// locker it only glimpsed, and one that happens to be sweeping past it all produce the same
    /// walk. The reason is the tell, and it is what SeenEnteringWindow and the slat/table ranges
    /// get tuned against - "it knew" and "it guessed" have opposite fixes.
    ///
    /// The third case is level B in progress: the meter on the row above is filling THROUGH a
    /// spot, the one situation where a full meter marks a spot known instead of starting a chase.
    /// </summary>
    private string DescribeHidingSpot()
    {
        NemesisHidingAwareness awareness = stateManager.HidingAwareness;

        HidingSpot known = stateManager.KnownHidingSpot;
        if (known != null)
            return $"<b>sabe</b> {NameOf(known)}{ReasonOf(awareness)}{SpotCheckOf(known)}";

        HidingSpot suspected = stateManager.SuspectedHidingSpot;
        if (suspected != null)
            return $"sospecha {NameOf(suspected)}{ReasonOf(awareness)}{SpotCheckOf(suspected)}";

        FieldOfView view = stateManager.FieldOfView;
        HidingSpot through = view != null ? view.SensedThroughSpot : null;
        if (through != null) return $"lo distingue por {NameOf(through)}";

        return "—";
    }

    /// <summary>SpotId when the designer set one, the GameObject's name otherwise.</summary>
    private static string NameOf(HidingSpot spot) =>
        string.IsNullOrEmpty(spot.SpotId) ? spot.name : spot.SpotId;

    private static string ReasonOf(NemesisHidingAwareness awareness)
    {
        string reason = awareness != null ? awareness.Reason : null;
        return string.IsNullOrEmpty(reason) ? "" : $" ({reason})";
    }

    /// <summary>
    /// Whether the search is on its way to <paramref name="spot"/> or already standing at it.
    /// Nothing while no search is heading there: the knowledge outlives the walk, and a known
    /// spot nobody is going to is exactly the case worth noticing - it is the one the memory
    /// safety net in NemesisHidingAwareness exists for.
    /// </summary>
    private string SpotCheckOf(HidingSpot spot)
    {
        NemesisSearchingState searching = stateManager.SearchingState;
        if (searching != null && ReferenceEquals(searching.SpotTarget, spot))
            return searching.IsCheckingSpot ? "  ·  <b>revisando</b>" : "  ·  yendo";

        // A suspected spot is looked at by Investigating instead, on its own dwell.
        NemesisInvestigatingState investigating = stateManager.InvestigatingState;
        if (investigating != null && ReferenceEquals(investigating.SpotTarget, spot))
            return investigating.IsInspecting ? "  ·  <b>revisando</b>" : "  ·  yendo";

        return "";
    }

    private string DescribeBelief()
    {
        NemesisBelief belief = stateManager.Belief;
        string lead = DescribeLead(belief);

        if (belief == null || !belief.HasBelief) return "nunca lo sintió" + lead;

        string source = belief.IsAnchoredBySight ? "vista" : "oído";
        NemesisController controller = stateManager.NemesisController;
        float freshness = controller != null ? controller.BeliefFreshness() : 0f;

        return $"{source}  ·  {belief.Age:0.0} s  ·  radio {belief.Radius:0.0} m  ·  " +
               $"frescura {freshness:0.00}  ·  ancla: {DescribeAnchor(belief)}{lead}";
    }

    /// <summary>
    /// What a search would do with this belief (Plan-Busqueda-Nemesis Fase 1): walk to the point —
    /// "vista", or "ruido preciso" for a noise heard right beside it — or sweep around it without
    /// going to the point, "zona". Read off the same rule the search uses.
    /// </summary>
    private string DescribeAnchor(NemesisBelief belief)
    {
        if (!NemesisSearchingState.MayVisitEvidence(belief, stateManager.NemesisData))
            return belief.LastEvidenceFromHidingSpot ? "zona (escondite)" : "zona";

        return belief.IsAnchoredBySight ? "vista" : "ruido preciso";
    }

    /// <summary>
    /// The last noise of the player's it heard, as the ear placed it (Plan-Busqueda-Nemesis Fase 1):
    /// how long ago, how far, and how far off the ear may have put it. In the editor, also how far off
    /// it really was — the number HearingLocalizationError is tuned against. The real position is
    /// editor-only on the sensor, so a build shows the rest and never the truth.
    /// </summary>
    private string DescribeHearing()
    {
        FieldOfListening ears = stateManager.FieldOfListening;
        if (ears == null || !ears.TryGetLastPlayerNoise(out FieldOfListening.HeardNoise noise))
            return "nunca te oyó";

        string text = $"hace {Time.time - noise.HeardAt:0.0} s  ·  a {noise.Distance:0.0} m  ·  " +
                      $"error hasta ±{noise.LocalizationError:0.0} m";
#if UNITY_EDITOR
        if (ears.TryGetLastPlayerNoiseTruth(out Vector3 truth))
        {
            Vector3 off = noise.Position - truth;
            off.y = 0f;
            text += $"  ·  <b>se equivocó {off.magnitude:0.0} m</b> (editor)";
        }
#endif
        return text;
    }

    /// <summary>
    /// What it is paying attention to (plan §17.4, Fase 2B part 4): the focus, what it is worth now,
    /// how long ago it was chosen, and the last decision with the question that made it — "cambió:
    /// radio 0.36 > vos 0.28 [11]", "siguió con cadenas: vos 0.40 vs 0.65 [9]", "ignoró cadenas (0.09)
    /// [7]". The last decision fades after ten seconds so a stale one is not read as current.
    /// </summary>
    private string DescribeFocus()
    {
        NemesisChoice choice = stateManager.Choice;
        if (choice == null) return "—";

        string focus = choice.FocusKind == FocusArbiter.EKind.None
            ? "nada"
            : $"<b>{choice.DescribeFocus()}</b> {choice.FocusValue:0.00} · hace {choice.FocusAge:0} s";

        DecoyNoiseSource decoy = choice.FocusDecoy;
        int visits = choice.FruitlessVisitsTo(decoy);
        string habituation = visits > 0 ? $" · {visits} visita(s) vacía(s)" : "";

        string decision = Time.time - choice.LastDecisionAt < 10f ? $"  ·  {choice.LastDecision}" : "";
        return focus + habituation + decision;
    }

    /// <summary>The lead it carries besides the player, if a recent one exists (plan §17: a decoy or
    /// a Director pulse is a lead, not the player).</summary>
    private static string DescribeLead(NemesisBelief belief)
    {
        if (belief == null || !belief.TryGetLead(out _, out float age, out DecoyNoiseSource decoy)) return "";
        if (age > 10f) return "";

        string what = decoy != null ? decoy.name : "ruido";
        return $"  ·  pista: {what} ({age:0.0} s)";
    }

    /// <summary>
    /// Straight line and NavMesh distance side by side.
    ///
    /// The pair is the point: the gap between them is the whole reason NemesisNav exists, and it
    /// is invisible in every other view. A Nemesis one floor below reads 4 m straight and 40 m on
    /// foot, and being able to watch those two numbers diverge is what makes "measure over the
    /// NavMesh" stop being a slogan.
    /// </summary>
    private string DescribeDistance()
    {
        Transform player = stateManager.PlayerTransform;
        if (player == null) return "sin jugador";

        float straight = Vector3.Distance(transform.position, player.position);
        bool reachable = NemesisNav.TryGetPathDistance(transform.position, player.position,
                                                       out float path);

        return reachable
            ? $"recta {straight:0.0} m  ·  NavMesh {path:0.0} m"
            : $"recta {straight:0.0} m  ·  <b>sin camino</b>";
    }

    /// <summary>
    /// Whether the chase is closing the distance, and what the pursuit is doing about it when it
    /// is not.
    ///
    /// The loop round a table is the one chase failure nothing else on this panel can show: the
    /// state says Chasing, the rung says "lo está viendo", the agent is moving, the watchdog is
    /// quiet — every row reads healthy while the player runs rings round the monster. This row is
    /// the window in progress (metres gained against the metres it needs, and the seconds left)
    /// and, once it latches, how many detour waypoints the trail penalty actually had to push
    /// against. "Estancado" with 0 penalised means the counterplay had nothing to choose between —
    /// no waypoints near the obstacle — and no tuning will fix that; waypoints will.
    ///
    /// The ChaseStalled count stays on the row after the chase ends. The one the habit thresholds
    /// read is PlayerHabitTracker's (the "hábitos" row), which also survives the level.
    /// </summary>
    private string DescribeChaseProgress()
    {
        // Re-resolved lazily: the state manager adds it in its own Awake, and script order between
        // two components on one object is not guaranteed.
        if (chaseProgress == null) chaseProgress = GetComponent<NemesisChaseProgress>();
        if (chaseProgress == null) return "—";

        int stalls = chaseProgress.ChaseStalledCount;
        string count = stalls > 0 ? $"  ·  {stalls} ChaseStalled" : "";

        if (!chaseProgress.IsMeasuring)
        {
            bool chasing = stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Chasing;
            if (!chasing) return "—" + count;

            // Said out loud because it is the one "not measuring" that is on purpose: the escape
            // paces the gap by design (see NemesisChaseProgress.Tick).
            NemesisDecision decision = stateManager.Decision;
            if (decision != null && decision.ChaseFloor) return "no mide durante el escape" + count;

            return "sin medir (sin vista reciente, sin camino o frenado)" + count;
        }

        float progress = chaseProgress.WindowProgress;

        if (chaseProgress.IsChaseStagnant)
        {
            NemesisChasingState chasingState = stateManager.ChasingState;
            NemesisPursuit pursuit = chasingState != null ? chasingState.Pursuit : null;
            int penalized = pursuit != null ? pursuit.PenalizedLastReplan : 0;

            return $"<b>ESTANCADO</b>  {progress:+0.0;-0.0;0.0} m  ·  rastro: " +
                   $"{penalized} waypoints penalizados{count}";
        }

        SO_NemesisData data = stateManager.NemesisData;
        float needed = data != null ? data.ChaseMinProgress : 0f;

        return $"acortó {progress:+0.0;-0.0;0.0} / {needed:0.0} m  ·  " +
               $"quedan {chaseProgress.WindowRemaining:0.0} s{count}";
    }

    private string DescribeSearch()
    {
        if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Searching)
            return "—";

        NemesisSearchingState searching = stateManager.SearchingState;
        if (searching == null) return "—";

        // How warm it still is first (plan §18.5 B): the silence since the player's last evidence
        // against the window it tolerates, and the time in the state against the cap. This is the
        // number that says when it will give up; "se enfrió" means the ladder is about to let go.
        // Then what it is doing — looking around at a point it reached, or walking to the next —
        // and the sweep behind it (§18.5 A). The sweep numbers are the ones to check against the
        // "creencia" row above: the centre follows the belief, and the radius comes from the
        // precision of its last evidence.
        //
        // "al último punto": still walking to where the evidence came from, so the silence does not
        // count yet (it counts from the arrival — playtest 27/09).
        string cooling = !searching.IsWarm ? "<b>se enfrió</b>"
                       : searching.IsHeadingToEvidence ? $"tibia (sin contar)/{searching.QuietWindow:0.#} s"
                       : $"tibia {searching.Silence:0.0}/{searching.QuietWindow:0.#} s";
        string cap = searching.Cap > 0f
            ? $"tope {stateManager.TimeInCurrentState:0}/{searching.Cap:0} s" + (searching.IsEscalated ? " (corta, D26)" : "")
            : $"{stateManager.TimeInCurrentState:0} s, sin tope";

        string doing;
        if (searching.IsPausing) doing = "<b>mirando alrededor</b>";
        else
        {
            float toTarget = Vector3.Distance(transform.position, searching.SearchTarget);
            string where = searching.IsHeadingToEvidence ? " al último punto" : "";
            doing = $"<b>yendo</b>{where} a {toTarget:0.0} m";
        }

        if (!searching.IsSweeping) return $"{cooling}  ·  {cap}  ·  {doing}";

        NemesisFreeRoam roam = searching.FreeRoam;
        float toAnchor = Vector3.Distance(transform.position, roam.Anchor);
        string covered = searching.SearchedEverything ? "  ·  <b>revisó todo</b>"
                       : roam.IsFullySwept ? "  ·  cubierto" : "";
        string room = roam.Room != null ? $"  ·  {roam.Room}" : "";

        return $"{cooling}  ·  {cap}  ·  {doing}  ·  barrido r {roam.Radius:0.#} m, centro a " +
               $"{toAnchor:0.0} m, {roam.SweptPoints.Count} puntos{covered}{room}";
    }

    /// <summary>
    /// The possibility map (Plan-Busqueda-Nemesis Fase 2a): how much of it is on the Nemesis's floor,
    /// where the likeliest place is and how much sits around it, how spread out it is (as the area it
    /// still has to search), what went into the Hub and into hiding spots, and how many nodes it
    /// cleared by looking on the last tick. Nothing decides off it yet: this row and the gizmo are how
    /// "does it reason where you went" gets judged before the search starts using it (2b).
    /// </summary>
    private string DescribePossibilityMap()
    {
        NemesisPossibilityMap possibility = stateManager.PossibilityMap;
        if (possibility == null || !possibility.IsBuilt) return "sin armar";

        PossibilityMap map = possibility.Map;
        NemesisPossibilityGraphBuilder.Result build = possibility.Build;
        if (!map.HasValue)
            return $"sin valor  ·  {map.Graph.NodeCount} nodos, armado en {build.Milliseconds:0} ms";

        string best = "—";
        if (possibility.TryGetBest(out _, out Vector3 bestAt))
        {
            float share = possibility.ShareNear(bestAt, 4f);
            best = $"a {Vector3.Distance(transform.position, bestAt):0.0} m ({share:P0} en 4 m)";
        }

        string hub = map.SinkValue > 0.005f ? $"  ·  Hub {map.SinkValue:P0}" : "";
        float hidden = map.SumOfKind(PossibilityGraph.ENodeKind.HidingSpot);
        string spots = hidden > 0.005f ? $"  ·  escondites {hidden:P0}" : "";

        return $"en su piso {possibility.ShareOnOwnFloor:P0}  ·  mejor {best}  ·  repartido " +
               $"{possibility.SpreadArea:0} m²{hub}{spots}  ·  limpió {possibility.ClearedLastTick}";
    }

    private string DescribeCluster()
    {
        NemesisController controller = stateManager.NemesisController;
        if (controller == null || controller.CurrentCluster < 0) return "—";

        return $"#{controller.CurrentCluster}  " +
               $"{controller.ClusterTourIndex + 1}/{controller.ClusterTourBudget}";
    }

    private string DescribeAgent()
    {
        string ready = stateManager.IsAgentReady ? "listo" : "<b>apagado / fuera del NavMesh</b>";
        string watchdog = stateManager.IsStuckDetectionSuppressed ? "  ·  watchdog suprimido" : "";
        return ready + watchdog;
    }

    /// <summary>
    /// What the stuck watchdog has had to do this run.
    ///
    /// The two numbers are the point: repaths are the cheap fix working (a path went bad, it was
    /// asked for again, nobody saw anything), warps are the body having been genuinely wedged.
    /// A run that ends with warps at zero and a handful of repaths is a healthy level. Warps
    /// climbing is a NavMesh bake or a waypoint sitting somewhere it should not, and no tuning in
    /// SO_NemesisData will fix it — which is exactly the distinction that was invisible while the
    /// watchdog only ever teleported.
    /// </summary>
    private string DescribeStuck()
    {
        int repaths = stateManager.StuckRepathCount;
        int warps = stateManager.StuckWarpCount;

        if (repaths == 0 && warps == 0) return "ninguna";

        string warpText = warps > 0 ? $"<b>{warps} warp</b>" : "0 warp";
        return $"{repaths} recalculo  ·  {warpText}";
    }

    /// <summary>
    /// The drop between floors in progress (plan §15.4): which kind, how tall, which phase and for
    /// how long, and whether the grab is off. Between drops, how many are cooling down — the reason
    /// it takes the stairs down a way it used a moment ago.
    /// </summary>
    private string DescribeDrop()
    {
        NemesisElevatorUser user = stateManager.ElevatorUser;
        if (user == null) return "—";

        if (user.CurrentDrop == null)
        {
            int cooling = user.SuspendedDropCount;
            return cooling > 0 ? $"—  ·  {cooling} en enfriamiento" : "—";
        }

        string grab = user.IsDroppingOrRecovering ? "  ·  <b>no agarra</b>" : "";

        return $"<b>DROP</b> {user.CurrentDropKind} {user.CurrentDropHeight:0.0} m  ·  " +
               $"fase {DescribeDropPhase(user.CurrentDropPhase)}  ·  {user.DropPhaseTime:0.0} s{grab}";
    }

    private static string DescribeDropPhase(EDropPhase phase)
    {
        switch (phase)
        {
            case EDropPhase.Align:       return "Alinear";
            case EDropPhase.Look:        return "Anticipar";
            case EDropPhase.HopTakeoff:  return "Despegar";
            case EDropPhase.HangTurn:    return "Darse vuelta";
            case EDropPhase.HangRelease: return "Colgarse";
            case EDropPhase.Fall:        return "En el aire";
            case EDropPhase.Land:        return "Recuperarse";
            default:                     return phase.ToString();
        }
    }

    /// <summary>The Director's pacing (plan §6.4): without it, "why did it leave just now" has no answer.</summary>
    private static string DescribePacing()
    {
        if (!NemesisDirector.Exists) return "sin Director";

        NemesisTension tension = NemesisDirector.Tension;
        if (tension == null || tension.Pacing == null) return "apagado (sin SO_DirectorPacing)";
        if (!tension.IsRunning) return "esperando que se despierte";

        const int Cells = 10;
        int filled = Mathf.Clamp(Mathf.RoundToInt(tension.Tension * Cells), 0, Cells);
        string bar = new string('#', filled) + new string('.', Cells - filled);

        string state = tension.IsSuspended ? $"en pausa: {tension.SuspendReason}" : tension.State.ToString();

        string timer = tension.State == NemesisTension.EPacingState.SustainPeak ||
                       tension.State == NemesisTension.EPacingState.Relax
            ? $" {tension.StateTimeRemaining:0} s"
            : "";

        string quiet = tension.State == NemesisTension.EPacingState.BuildUp
            ? tension.IsPlayerInSafeZone
                ? "  ·  en el Hub"
                : $"  ·  silencio {tension.QuietTime:0}/{tension.Pacing.QuietTimeout:0} s"
            : "";

        return $"<b>{state}</b>{timer}  ·  [{bar}] {tension.Tension:0.00}{quiet}";
    }

    /// <summary>
    /// Who is pulling the patrol, and how long a search holds out (plan §18.5 C6).
    ///
    /// With no Director pressure the patrol is not unsteered: NemesisController's zone roll leans on
    /// the player's REAL position (see <see cref="DescribeStalking"/>), which is the most constant
    /// stalking in the game and used to show here as a bare "—". The search persistence the Director
    /// lends (lever 5) is appended whenever it is not 1, with the reason — the pacing state, or the
    /// rising sensitivity — and a pending "vuelve a pasar" shows its countdown.
    /// </summary>
    private string DescribePressure()
    {
        float persistence = NemesisDirector.SearchPersistence;
        string persistenceText = Mathf.Approximately(persistence, 1f)
            ? ""
            : $"  ·  persistencia ×{persistence:0.##} ({NemesisDirector.SearchPersistenceReason})";

        string zone = NemesisDirector.ActiveZoneId;
        if (zone == null)
        {
            string head = NemesisDirector.Exists ? "sin presión" : "sin Director";

            float revisitIn = NemesisDirector.RevisitTimeRemaining;
            string revisit = revisitIn >= 0f ? $"  ·  vuelta en {revisitIn:0} s" : "";

            return $"{head}  ·  acecho: {DescribeStalking()}{revisit}{persistenceText}";
        }

        string step = NemesisDirector.RisingStep > 0 ? $" x{NemesisDirector.RisingStep}" : "";

        return $"<b>{zone}</b> {NemesisDirector.ActiveIntensity:0.00}  ·  " +
               $"{NemesisDirector.ActiveSourceLabel}{step}  ·  quedan {NemesisDirector.ActiveTimeRemaining:0} s" +
               persistenceText;
    }

    /// <summary>
    /// Whether the cluster roll is leaning on the player's real position right now. Mirrors the
    /// fallback of NemesisController.TryGetZoneAnchor (private, and it asks the Director first, which
    /// the caller already has): SO_NemesisData.ZoneBiasUsesRealPlayer on, a player registered, that
    /// player not in the Hub (C5), not hidden, and no hunt in progress or in its grace (D40). Plus the
    /// one condition around it: the anchor only feeds the cluster patrol, so with
    /// ClusterPatrolEnabled off nothing reads it.
    /// </summary>
    private string DescribeStalking()
    {
        SO_NemesisData data = stateManager.NemesisData;
        if (data == null || !data.ZoneBiasUsesRealPlayer) return "no";
        if (!data.ClusterPatrolEnabled) return "no (sin cúmulos)";

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return "no (sin jugador)";
        if (NemesisSafeZones.Contains(player.position)) return "no (jugador en el Hub)";

        PlayerStateManager playerState = PlayerRegistry.Current;
        if (playerState != null && playerState.IsHidden) return "no (escondido, D40)";

        NemesisController controller = stateManager.NemesisController;
        if (controller != null && controller.IsInHuntOrGrace) return "no (caza o gracia, D40)";

        return "<b>jugador</b>";
    }

    private static readonly ECounterplay[] Counterplays =
        (ECounterplay[])System.Enum.GetValues(typeof(ECounterplay));

    private readonly List<HabitLedger.SpotReading> spotReadings = new List<HabitLedger.SpotReading>();

    /// <summary>What PlayerHabitTracker has counted (plan Fase 3), drain applied: the numbers the
    /// thresholds of SO_CounterplayRules get calibrated from.</summary>
    private static string DescribeHabits()
    {
        if (!PlayerHabitTracker.Exists) return "sin tracker (va en la escena Data)";

        PlayerHabitTracker habits = PlayerHabitTracker.Instance;

        return $"esc {habits.GetExploitCount(EExploitKind.EscapedWhileHidden):0.#}  ·  " +
               $"repite {habits.GetExploitCount(EExploitKind.SameSpotReused):0.#}  ·  " +
               $"estanca {habits.GetExploitCount(EExploitKind.ChaseStalled):0.#}  ·  " +
               $"Hub {habits.GetExploitCount(EExploitKind.SafeZoneEscape):0.#}";
    }

    /// <summary>What those counts WOULD unlock, and at what chance. Nothing acts on it yet: in
    /// Fase 3 this is the answer to "how soon would it have started ambushing me".</summary>
    private static string DescribeUnlocks()
    {
        if (!PlayerHabitTracker.Exists) return "—";

        PlayerHabitTracker habits = PlayerHabitTracker.Instance;
        string text = "";

        foreach (ECounterplay counterplay in Counterplays)
        {
            if (!habits.IsUnlocked(counterplay)) continue;

            if (text.Length > 0) text += "  ·  ";
            text += $"{ShortName(counterplay)} {habits.CounterplayChance(counterplay):0%}";
        }

        string last = habits.LastRegistration != null
            ? $"hace {Time.time - habits.LastRegistrationAt:0} s"
            : "";

        if (text.Length == 0) return last.Length > 0 ? $"—  ·  {last}" : "—";
        return last.Length > 0 ? $"{text}  ·  {last}" : text;
    }

    /// <summary>Short enough that three of them and the time fit on one row of the panel.</summary>
    private static string ShortName(ECounterplay counterplay)
    {
        switch (counterplay)
        {
            case ECounterplay.CheckHidingSpots: return "revisar";
            case ECounterplay.PrioritizeSuspiciousSpots: return "priorizar";
            case ECounterplay.BurnHidingSpot: return "romper";
            case ECounterplay.ExitAmbush: return "emboscada";
            case ECounterplay.ChaseFlank: return "flanqueo";
            case ECounterplay.ZoneDefense: return "defensa";
            default: return counterplay.ToString();
        }
    }

    /// <summary>The spot memory (plan Fase 2D): the most used spots and their meter; whether getting
    /// out of the current one would leave an escape to confirm ("cazado"); and, once out, whether
    /// one is still waiting out its window ("escape pendiente").</summary>
    private string DescribeSpotMemory()
    {
        if (!PlayerHabitTracker.Exists) return "—";

        PlayerHabitTracker habits = PlayerHabitTracker.Instance;
        SO_CounterplayRules rules = habits.ActiveRules;

        int survived = habits.StaySearchesSurvived;
        string text = habits.StaySpotKey != null
            ? $"adentro{(habits.IsStayHunted ? ", cazado" : "")}{(survived > 0 ? $", {survived} búsq." : "")}"
            : habits.HasPendingEscape ? "escape pendiente" : "";

        // One spot fewer while inside one, so the row still fits on a single line.
        int count = habits.CollectSpotReadings(spotReadings);
        int shown = Mathf.Min(habits.StaySpotKey != null ? 1 : 2, count);
        for (int i = 0; i < shown; i++)
        {
            HabitLedger.SpotReading spot = spotReadings[i];

            // Same "0 = never" reading of a threshold as HabitLedger.
            string flag = rules.SpotBurnThreshold > 0f && spot.Meter >= rules.SpotBurnThreshold ? " (rompe)"
                        : rules.SpotPriorityThreshold > 0f && spot.Meter >= rules.SpotPriorityThreshold ? " (primero)"
                        : "";

            if (text.Length > 0) text += "  ·  ";
            text += $"{spot.SpotId} {spot.Meter:0.#}{flag}";
        }

        return text.Length > 0 ? text : "—";
    }

    /// <summary>The escalation by completed puzzles (plan Fase 7): which tier, from how many puzzles
    /// (or F10), and what it multiplies. Speed is never on this row because it never escalates.
    /// </summary>
    private static string DescribeEscalation()
    {
        if (!NemesisEscalation.Exists) return "sin escalada (va en la escena Data)";

        NemesisEscalation escalation = NemesisEscalation.Instance;
        EscalationTier tier = escalation.CurrentTier;

        string source = escalation.DebugTierOverride >= 0 ? "F10" : $"{escalation.CompletedPuzzles} puzzles";
        if (tier == null) return $"base  ·  {source}";

        return $"nivel {escalation.CurrentTierIndex} ({source})  ·  vista x{tier.SightMultiplier:0.##}  ·  " +
               $"oído x{tier.HearingMultiplier:0.##}  ·  búsq. x{tier.SearchPersistenceMultiplier:0.##}";
    }

    private string DescribeSafeStats()
    {
        if (safeSamples == 0) return "—";

        return $"{minSafeTime:0.0} / {totalSafeTime / safeSamples:0.0} / {maxSafeTime:0.0} s " +
               $"({safeSamples})";
    }

    /// <summary>
    /// The last <see cref="historySeconds"/> of state as a colour strip.
    ///
    /// A list of transitions with timestamps says the same thing and nobody reads it. The strip
    /// shows the RHYTHM — whether the encounter was one long chase or six short ones, whether the
    /// searches are all clipping to their timeout, whether patrol ever gets a look in — and that
    /// is the shape being tuned.
    /// </summary>
    private void DrawHistoryStrip(Rect rect)
    {
        if (history.Count == 0) return;

        float now = Time.time;
        float start = now - historySeconds;

        for (int i = 0; i < history.Count; i++)
        {
            float from = Mathf.Max(history[i].Time, start);
            float to = i + 1 < history.Count ? history[i + 1].Time : now;
            if (to <= start) continue;

            float x0 = rect.x + rect.width * ((from - start) / historySeconds);
            float x1 = rect.x + rect.width * ((to - start) / historySeconds);

            GUI.color = ColorOf(history[i].State);
            GUI.DrawTexture(new Rect(x0, rect.y, Mathf.Max(1f, x1 - x0), rect.height), barTexture);
        }

        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x, rect.yMax, rect.width, 14f),
                  $"<size=9>últimos {historySeconds:0} s</size>", textStyle);
    }

    private static Color ColorOf(NemesisStateManager.ENemesisState state) => state switch
    {
        NemesisStateManager.ENemesisState.Patrolling    => PatrolColor,
        NemesisStateManager.ENemesisState.Investigating => InvestigColor,
        NemesisStateManager.ENemesisState.Searching     => SearchColor,
        NemesisStateManager.ENemesisState.Chasing       => ChaseColor,
        NemesisStateManager.ENemesisState.Traversing    => TraverseColor,
        NemesisStateManager.ENemesisState.Catch         => CatchColor,
        _                                               => Color.grey,
    };

    /// <summary>Built lazily and not in Awake: GUI styles can only be touched from OnGUI, and
    /// GUI.skin is not ready before the first one.</summary>
    private void EnsureStyles()
    {
        if (textStyle != null) return;

        panelTexture = SolidTexture(new Color(0.04f, 0.05f, 0.07f, 0.86f));
        barTexture = SolidTexture(Color.white);

        panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture } };

        textStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 11,
            normal = { textColor = new Color(0.89f, 0.91f, 0.94f) },
        };
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// What the overlay shows, written to a .txt for as long as it is open: one file per opening,
    /// in the project's Logs/NemesisF9 folder (ignored by git, next to NemesisTraceRecorder's CSVs).
    ///
    /// Each entry carries the game time, the same clock as the first column of the trace CSV so
    /// the two files line up, and the wall clock, which is what a playtest note remembers. Only the
    /// rows that changed go in. A full copy of the panel goes in on opening, every few seconds, at
    /// every mark and on closing, so any stretch of the file reads on its own.
    ///
    /// Only observes: it writes the strings the panel already built and asks the Nemesis nothing.
    /// </summary>
    private sealed class FileLog
    {
        private const int LabelWidth = 20;
        private static readonly Regex RichText = new Regex("<[^>]*>");

        private StreamWriter writer;
        private string filePath;
        private bool failed;

        private readonly List<(string label, string value)> last = new List<(string, string)>();
        private readonly Dictionary<string, string> written = new Dictionary<string, string>();

        private float nextEntryAt;
        private float nextFullAt;
        private bool markRequested;
        private int marks;

        public void RequestMark() => markRequested = true;

        public void Capture(List<(string label, string value)> rows, MonoBehaviour owner, float interval,
                            float fullEvery, KeyCode markKey)
        {
            if (failed) return;

            bool opening = writer == null;
            if (opening && !TryOpen(owner, fullEvery, markKey)) return;

            last.Clear();
            foreach ((string label, string value) in rows) last.Add((label, RichText.Replace(value, "")));

            float now = Time.time;

            if (markRequested)
            {
                markRequested = false;
                marks++;
                WriteEntry(now, $"★ MARCA {marks}", onlyChanged: false);
                Debug.Log($"[{nameof(NemesisDebugHUD)}] Marca {marks} en {filePath}", owner);

                nextEntryAt = now + interval;
                nextFullAt = now + fullEvery;
                return;
            }

            if (!opening && now < nextEntryAt) return;
            nextEntryAt = now + interval;

            bool full = opening || now >= nextFullAt;
            if (full) nextFullAt = now + fullEvery;

            WriteEntry(now, full ? "foto completa" : null, onlyChanged: !full);
        }

        public void Close()
        {
            if (writer == null) return;

            WriteEntry(Time.time, "foto completa · F9 cerrado", onlyChanged: false);
            writer.Dispose();
            writer = null;

            last.Clear();
            written.Clear();
            markRequested = false;
            marks = 0;
        }

        private void WriteEntry(float now, string title, bool onlyChanged)
        {
            bool headed = false;

            foreach ((string label, string value) in last)
            {
                if (onlyChanged && written.TryGetValue(label, out string before) && before == value) continue;

                if (!headed)
                {
                    string time = now.ToString("0.00", CultureInfo.InvariantCulture);
                    string clock = System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

                    writer.WriteLine();
                    writer.WriteLine(title != null ? $"== {time} s · {clock} · {title} ==" : $"== {time} s · {clock} ==");
                    headed = true;
                }

                writer.WriteLine(label.PadRight(LabelWidth) + value);
                written[label] = value;
            }

            // Flushed per entry, a couple a second at most: the file has to be readable while the
            // game is still running, and survive the editor going down.
            if (headed) writer.Flush();
        }

        private bool TryOpen(MonoBehaviour owner, float fullEvery, KeyCode markKey)
        {
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "NemesisF9");
                Directory.CreateDirectory(folder);

                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                filePath = Path.Combine(folder, $"f9_{stamp}.txt");
                for (int i = 2; File.Exists(filePath); i++) filePath = Path.Combine(folder, $"f9_{stamp}_{i}.txt");

                writer = new StreamWriter(filePath, false, new UTF8Encoding(false));

                string opened = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                writer.WriteLine($"F9 del Nemesis '{owner.name}' · escena {owner.gameObject.scene.name} · " +
                                 $"abierto el {opened}, a los {Time.time.ToString("0.0", CultureInfo.InvariantCulture)} s de juego.");
                writer.WriteLine("Cada entrada: segundo de juego (el mismo reloj que la primera columna del CSV de " +
                                 "Logs/NemesisTrace) y hora. Van sólo las filas que cambiaron; la foto completa va " +
                                 $"al abrir, cada {fullEvery:0} s, en cada marca ({markKey}) y al cerrar.");

                Debug.Log($"[{nameof(NemesisDebugHUD)}] F9 se guarda en {filePath}", owner);
                return true;
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                failed = true;
                writer = null;
                Debug.LogWarning($"[{nameof(NemesisDebugHUD)}] Could not open the F9 log ({e.Message}). " +
                                 "Logging is off for this session.", owner);
                return false;
            }
        }
    }
}
