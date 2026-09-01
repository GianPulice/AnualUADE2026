using System.Collections.Generic;
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
    private NemesisTelemetry telemetry;
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

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();

        // NemesisStateManager adds this itself during its own Awake when the prefab is missing it,
        // so by the time any Update runs it exists — but script order between two components on
        // one object is not guaranteed, so this is re-resolved lazily where it is read.
        telemetry = GetComponent<NemesisTelemetry>();
    }

    private void OnDestroy()
    {
        // Created with new, so they are not owned by any scene object and would leak on a domain
        // reload otherwise.
        if (panelTexture != null) Destroy(panelTexture);
        if (barTexture != null) Destroy(barTexture);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
        if (!visible || stateManager == null) return;

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
        float height = lineHeight * 13f + stripHeight + 26f;

        Rect panel = new Rect(origin.x, origin.y, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);

        Rect line = new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, lineHeight);

        Row(ref line, "estado", DescribeState());
        Row(ref line, "regla", DescribeRung());
        Row(ref line, "sospecha", DescribeAwareness());
        Row(ref line, "creencia", DescribeBelief());
        Row(ref line, "distancia", DescribeDistance());
        Row(ref line, "búsqueda", DescribeSearch());
        Row(ref line, "cúmulo", DescribeCluster());
        Row(ref line, "agente", DescribeAgent());

        line.y += 6f;
        Row(ref line, "seguro en", lastSafeTime >= 0f ? $"{lastSafeTime:0.0} s" : "—");
        Row(ref line, "  mín / prom / máx", DescribeSafeStats());

        line.y += 6f;
        DrawHistoryStrip(new Rect(panel.x + 10f, line.y, panel.width - 20f, stripHeight));
    }

    private void Row(ref Rect line, string label, string value)
    {
        GUI.Label(line, $"<b>{label}</b>  {value}", textStyle);
        line.y += line.height;
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

        if (stateManager.HasVisualTarget) return $"[{bar}] <b>lo ve</b>";

        string state = stateManager.IsSuspicious ? "  ·  <b>sospecha</b>" : "";

        return $"[{bar}] {awareness:0.00} / {threshold:0.00}{state}";
    }

    private string DescribeBelief()
    {
        if (!stateManager.TryGetBelief(out _)) return "nunca lo sintió";

        float age = stateManager.BeliefAge;
        NemesisController controller = stateManager.NemesisController;
        float freshness = controller != null ? controller.BeliefFreshness() : 0f;

        return $"{age:0.0} s  ·  frescura {freshness:0.00}";
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

    private string DescribeSearch()
    {
        if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Searching)
            return "—";

        if (telemetry == null) telemetry = GetComponent<NemesisTelemetry>();
        if (telemetry == null) return "—";

        NemesisSearchingState searching = stateManager.SearchingState;

        // The pause outranks everything else on this row: while it is standing still looking
        // around, "where is it heading" is not the question - it has already got there.
        if (searching != null && searching.IsPausing) return "<b>mirando alrededor</b>";

        // A committed room sweep outranks the intercept line below because the two are mutually
        // exclusive by construction (see NemesisSearchingState.TryCommitRoomSweep) and this is the
        // one the designer asked for: it is the row that answers "did it actually go in after me".
        if (searching != null && searching.IsSweepingRoom)
        {
            NemesisFreeRoam roam = searching.FreeRoam;
            float toAnchor = Vector3.Distance(transform.position, roam.Anchor);

            return $"<b>barriendo habitación</b>  r {roam.Radius:0.#} m  ·  " +
                   $"centro a {toAnchor:0.0} m  ·  {roam.SweptPoints.Count} puntos";
        }

        Vector3? intercept = telemetry.SearchInterceptPoint;
        if (intercept.HasValue)
        {
            float interceptDistance = Vector3.Distance(transform.position, intercept.Value);
            return $"<b>interceptando</b> a {interceptDistance:0.0} m";
        }

        // No cut-off to be had, so it is working the weighted roll. Reporting the target and how
        // far it is turns "barrido" from a label into something that can be checked against the
        // last known position: if the two keep diverging, the LKP bias is too low.
        if (searching == null) return "barrido (sin intercepción)";

        float distance = Vector3.Distance(transform.position, searching.SearchTarget);
        return $"buscando a {distance:0.0} m (sin intercepción)";
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
}
