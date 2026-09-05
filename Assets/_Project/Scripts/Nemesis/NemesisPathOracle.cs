using UnityEngine;

/// <summary>
/// Answers "can I get to that point, is it on another floor, and is the freight elevator on the
/// way?" — at a rate the frame budget can afford.
///
/// It is a component of its own rather than four more members on NemesisStateManager, which is
/// already carrying more than its name promises. The FSM asks it a question; it owns the cost and
/// the staleness of the answer.
///
/// WHY THE THROTTLE IS NOT ONLY A PERFORMANCE KNOB
///
/// Each miss costs a <see cref="UnityEngine.AI.NavMesh.CalculatePath"/>, a synchronous A* across
/// the level, so asking per frame is expensive. But the reason the interval must not be tuned
/// towards zero is stability, not cost: the Nemesis used to oscillate between Chasing and
/// Searching every other frame while standing directly under the player, because each state
/// re-derived the verdict and the two read the same borderline path differently. Holding one
/// answer for a while is what makes the decision stick.
///
/// SETUP: goes on the Nemesis root, next to NemesisStateManager, which finds it itself.
/// </summary>
public class NemesisPathOracle : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager on this object or its " +
             "parents. Supplies RouteVerdictInterval and FloorHeightThreshold.")]
    [SerializeField] private SO_NemesisData nemesisData;

    /// <summary>
    /// Used only when no SO_NemesisData can be found, so a missing asset degrades to a sane rate
    /// instead of one CalculatePath per frame.
    ///
    /// A const and not a serialised field: it is the value that stands in FOR the asset, so
    /// exposing it in the inspector would offer a second place to tune the same number — which is
    /// the scattering this whole component set was consolidated to get rid of.
    /// </summary>
    private const float FallbackInterval = 0.4f;

    /// <summary>
    /// When the cache is allowed to miss again, on the <see cref="Time.time"/> clock.
    ///
    /// A deadline rather than a countdown ticked in Update, for two reasons: there is no Update to
    /// get the order of wrong against the FSM, and Time.time is scaled — so a paused game freezes
    /// the cache, which is right, since nothing it measures can move while paused.
    /// </summary>
    private float nextQueryTime;

    private NemesisNav.NavRoute cachedRoute;
    private bool cachedRouteValid;

    private void Awake()
    {
        if (nemesisData != null) return;

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
        {
            Debug.LogWarning($"[{nameof(NemesisPathOracle)}] No {nameof(SO_NemesisData)} assigned " +
                             $"and none found in the parents — falling back to a " +
                             $"{FallbackInterval}s interval.", this);
        }
    }

    /// <summary>Seconds an answer is allowed to be reused for.</summary>
    /// <summary>
    /// Repoints this at another tuning asset. Mirrors <c>FieldOfView.SetData</c> and
    /// <c>FieldOfListening.SetData</c>, and exists for the same one caller: the Director swaps in
    /// a boosted runtime copy for the length of a pressure request, and every holder of the
    /// reference has to follow or half the Nemesis runs on the old numbers.
    /// </summary>
    public void SetData(SO_NemesisData data) => nemesisData = data;

    private float Interval => nemesisData != null
        ? Mathf.Max(0.05f, nemesisData.RouteVerdictInterval)
        : FallbackInterval;

    /// <summary>Height difference past which a target counts as being on another floor.</summary>
    public float FloorHeightThreshold => nemesisData != null ? nemesisData.FloorHeightThreshold : 2.5f;

    /// <summary>
    /// The route from here to a point, recomputed at most once per <see cref="Interval"/>.
    ///
    /// Deliberately keyed on time alone and not on how far the target has moved: a running player
    /// moves every frame, so a movement-keyed cache would miss every frame and throttle nothing.
    /// What this answers — reachable, which floor, lift on the way — does not change in 0.4
    /// seconds because the player took two steps.
    /// </summary>
    /// <returns>false when the query could not run at all (an end off the NavMesh). A partial
    /// path returns true with <see cref="NemesisNav.NavRoute.IsComplete"/> false, which is a
    /// different and useful answer.</returns>
    public bool TryGetRoute(Vector3 target, out NemesisNav.NavRoute route)
    {
        if (Time.time >= nextQueryTime)
        {
            nextQueryTime = Time.time + Interval;
            cachedRouteValid = NemesisNav.TryGetRoute(transform.position, target, out cachedRoute);
        }

        route = cachedRoute;
        return cachedRouteValid;
    }

    /// <summary>
    /// Whether the target sits far enough above or below to count as another floor, and getting
    /// there means the lift.
    ///
    /// The two conditions are separate on purpose. A big height difference alone is a mezzanine
    /// or a crate the agent walks up to. A link alone is a shortcut on the same floor. Together
    /// they are the case this whole system exists for.
    /// </summary>
    public bool IsAcrossFloors(in NemesisNav.NavRoute route) =>
        route.CrossesLink && Mathf.Abs(route.VerticalDelta) >= FloorHeightThreshold;

    /// <summary>
    /// Drops the cached answer so the next <see cref="TryGetRoute"/> recomputes.
    ///
    /// For the moment a state is entered on the strength of a verdict: acting on a reading taken
    /// up to an interval ago and half a level away is worse than paying for one extra query.
    /// </summary>
    public void Invalidate() => nextQueryTime = 0f;
}
