using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    private NemesisStateManager nemesisStateManager;

    private float timeOut = 0;
    private float currentTime = 0;

    /// <summary>
    /// Shortest time this state can last, in seconds.
    ///
    /// Without it, Chasing handing over a target it can see (because the path to it was partial)
    /// and this state handing it straight back (because it can see it) is a closed loop that
    /// turns over every other frame: speed, the Animator and this state's own logging all thrash,
    /// and the Nemesis stands vibrating in place instead of doing either thing. A floor is
    /// cheaper and more robust than teaching both sides about the other's exit condition.
    /// </summary>
    private const float MinimumDwellTime = 0.5f;

    private float dwellTime;

    /// <summary>Graph nodes already visited during this search. Cleared on entry and whenever a
    /// fresh noise restarts the search somewhere else.</summary>
    private readonly List<int> sweptNodes = new List<int>();

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        timeOut = nemesisStateManager.NemesisData.SearchTimeOut;
    }

    public override void EnterState()
    {
        NextState = StateKey;
        currentTime = 0;
        dwellTime = 0;
        sweptNodes.Clear();
        nemesisStateManager.NavAgent.speed = nemesisStateManager.NemesisMovement.SearchSpeed;
        nemesisStateManager.AnimController.SetBool("isRunning", true);
    }

    public override void ExitState()
    {
        nemesisStateManager.AnimController.SetBool("isRunning", false);
    }

    public override NemesisStateManager.ENemesisState GetNextState()
    {
        if (NextState != StateKey) return NextState;
        else return StateKey;
    }

    public override void OnTriggerEnter(Collider other)
    {

    }

    public override void OnTriggerExit(Collider other)
    {

    }

    public override void OnTriggerStay(Collider other)
    {

    }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        dwellTime += Time.deltaTime;

        if (nemesisStateManager.HasVisualTarget && dwellTime >= MinimumDwellTime)
        {
            NextState = NemesisStateManager.ENemesisState.Chasing;
        }
        else
        {
            if (currentTime < timeOut)
            {
                currentTime += Time.deltaTime;
                if (nemesisStateManager.HasAudioTarget)
                {
                    // A fresh noise outranks the sweep: go to it, and clear the swept set so the
                    // search restarts around the new information instead of carrying on ticking
                    // off waypoints chosen for where the player used to be.
                    nemesisStateManager.NavAgent.destination = PredictedFrom(
                        nemesisStateManager.FieldOfListening.LastKnownPosition);
                    sweptNodes.Clear();
                }

                // remainingDistance follows the actual path (stairs, detours); a straight-line
                // check here made the sweep stall on any point placed at a different height —
                // see NemesisPatrolState for the full explanation of the same fix.
                NavMeshAgent agent = nemesisStateManager.NavAgent;
                bool hasArrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
                if (hasArrived)
                {
                   agent.destination = GetNextSweepPoint();
                }
            }
            else
            {
                NextState = NemesisStateManager.ENemesisState.Patrolling;
            }
        }
    }
    /// <summary>
    /// Where to look next.
    ///
    /// Walks the patrol graph outward from where the Nemesis is standing, nearest unswept node
    /// first, staying on its own island. That replaces scattering random points within five
    /// metres, which had the Nemesis searching the room it was already in — it would circle the
    /// spot it lost you at while you walked out of the building. The waypoints already blanket
    /// the level and already know which of them are connected to which, so the sweep gets its
    /// map for free.
    ///
    /// Falls back to the random scatter when there is no usable graph: an early test scene with
    /// no routes should still get a Nemesis that mills about rather than one that freezes.
    /// </summary>
    private Vector3 GetNextSweepPoint()
    {
        NemesisController controller = nemesisStateManager.NemesisController;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;

        if (graph == null || !graph.IsBuilt) return GetRandomPointInNavMesh();

        Vector3 origin = nemesisStateManager.transform.position;

        // Which island the Nemesis is standing on, via the node closest to it. Without this the
        // sweep would happily pick a waypoint behind a sealed door and stall against it.
        int anchor = FindNearestNode(graph, origin, respectSwept: false);
        if (anchor < 0) return GetRandomPointInNavMesh();

        int island = graph.ComponentOf(anchor);
        int next = FindNearestNode(graph, origin, respectSwept: true, island: island);

        if (next < 0)
        {
            // Every reachable waypoint has been visited this search. Start the set over so a long
            // search keeps moving instead of standing still on the last one.
            sweptNodes.Clear();
            return GetRandomPointInNavMesh();
        }

        sweptNodes.Add(next);
        return graph.GetNode(next).Position;
    }

    /// <summary>
    /// Index of the graph node closest to a point, or -1.
    /// </summary>
    /// <param name="respectSwept">Skip nodes already visited during this search, and restrict to
    /// <paramref name="island"/>. False ignores both and is used to locate the Nemesis itself on
    /// the graph.</param>
    private int FindNearestNode(NemesisRouteGraph graph, Vector3 origin, bool respectSwept,
                                int island = -1)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (respectSwept)
            {
                if (sweptNodes.Contains(i)) continue;
                if (graph.ComponentOf(i) != island) continue;
            }

            NemesisRouteGraph.Node node = graph.GetNode(i);
            if (!node.IsValid) continue;

            // Straight line and not path distance on purpose: this runs over every waypoint in
            // the level each time the Nemesis arrives somewhere, and a CalculatePath per node
            // would be a stall. The island filter above already guarantees whatever it picks is
            // actually reachable, which is the part that matters.
            float distance = Vector3.SqrMagnitude(node.Position - origin);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Nudges a remembered position forward along the direction the target was last observed
    /// moving.
    ///
    /// Kept to a fraction of a second deliberately. The velocity comes from what the sensors
    /// actually saw (see <see cref="FieldOfView.LastKnownVelocity"/>), so a longer lead would
    /// extrapolate a stale observation into a confident claim about somewhere nobody has been
    /// seen — and arriving ahead of the player from a guess reads as the game cheating rather
    /// than as the monster being sharp.
    ///
    /// Snapped back onto the NavMesh: extrapolating a running player walks the point straight
    /// through the wall they were about to turn at.
    /// </summary>
    private Vector3 PredictedFrom(Vector3 remembered)
    {
        SO_NemesisData data = nemesisStateManager.NemesisData;
        float leadTime = data != null ? data.SearchLeadTime : 0f;
        if (leadTime <= 0f) return remembered;

        FieldOfView view = nemesisStateManager.FieldOfView;
        if (view == null) return remembered;

        Vector3 velocity = view.LastKnownVelocity;
        if (velocity.sqrMagnitude < 0.01f) return remembered;

        Vector3 predicted = remembered + velocity * leadTime;

        return NavMesh.SamplePosition(predicted, out NavMeshHit hit, 2f, NavMesh.AllAreas)
            ? hit.position
            : remembered;
    }

    /// <summary>
    /// A point on the NavMesh near the current destination, to keep sweeping the area.
    ///
    /// Returns the position snapped by SamplePosition and not the raw random point: the raw
    /// one usually falls off the mesh, and setting it as a destination made the agent walk to
    /// the nearest edge instead. The attempts are capped because the original do/while had no
    /// way out — with the agent outside the NavMesh it span forever and hung Unity.
    /// </summary>
    private Vector3 GetRandomPointInNavMesh()
    {
        const int maxAttempts = 30;
        const float sampleRadius = 1f;

        // Read off the SO rather than a local const. It was hardcoded at 5, which meant the one
        // number deciding how wide the fallback sweep is could not be seen in the inspector, could
        // not be drawn by the gizmos, and could not be tuned without a recompile.
        SO_NemesisData data = nemesisStateManager.NemesisData;
        float range = data != null ? data.SearchSweepRadius : 5f;

        Vector3 origin = nemesisStateManager.NavAgent.destination;

        Vector3 forward = nemesisStateManager.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Horizontal only: onUnitSphere also varied Y and threw points above and below
            // the floor. The forward bias is kept so it sweeps ahead of where it is looking.
            Vector2 circle = Random.insideUnitCircle;
            Vector3 randomDir = new Vector3(circle.x, 0f, circle.y) + forward;
            Vector3 randomPoint = origin + randomDir * range;

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Nothing valid nearby: stay put rather than heading for an unreachable point.
        return nemesisStateManager.transform.position;
    }
}
