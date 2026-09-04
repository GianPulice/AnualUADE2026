using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the Nemesis exists and whether it exists yet: dormancy before its activation puzzle, the
/// agent tuning applied on waking, and every teleport it ever takes.
///
/// Extracted from NemesisStateManager. What binds these together is that none of them are
/// decisions — they are the physical facts of the Nemesis's presence. The FSM decides to
/// reposition after a capture; this is what actually moves it, checks the landing took, and keeps
/// it off the player's lap.
///
/// Starting the FSM itself deliberately stays in NemesisStateManager: entering the first state
/// touches the protected State dictionary of the shared FSM base, and reaching into that from
/// outside would make this component a second owner of the machine.
///
/// SETUP: goes on the Nemesis root. NemesisStateManager finds it, adds it if missing, and calls
/// into it.
/// </summary>
public class NemesisLifecycle : MonoBehaviour
{
    // Tuning lives in SO_NemesisData, reached through the state manager, so a designer edits one
    // asset instead of hunting for values scattered across the components. Nothing is serialised
    // on this component at all — it has no scene wiring of its own.
    private const float FallbackRepositionMinPlayerDistance = 15f;

    private NemesisStateManager stateManager;
    private List<Renderer> hiddenWhileDormant;

    /// <summary>Waypoints closer than this to the player are not eligible when repositioning after
    /// a capture, so the Nemesis does not warp on top of the player it just respawned.</summary>
    private float RepositionMinPlayerDistance
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? data.RepositionMinPlayerDistance
                                : FallbackRepositionMinPlayerDistance;
        }
    }

    /// <summary>Called by NemesisStateManager during its Awake, so this is wired before any use.
    /// </summary>
    public void Initialize(NemesisStateManager manager) => stateManager = manager;

    // ── Dormancy ────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns off everything that would make a dormant Nemesis visible or reactive.
    ///
    /// The GameObject itself deliberately stays active: a deactivated one gets no OnEnable, so
    /// NemesisStateManager could not listen for its own activation puzzle and would never wake up.
    /// </summary>
    public void SetDormant(bool dormant)
    {
        if (stateManager.NavAgent != null) stateManager.NavAgent.enabled = !dormant;
        if (stateManager.FieldOfView != null) stateManager.FieldOfView.enabled = !dormant;
        if (stateManager.FieldOfListening != null) stateManager.FieldOfListening.enabled = !dormant;

        // Lights are not Renderers, so HideRenderers below does not touch them — a dormant Nemesis
        // would sit in the dark with its eyes still glowing, which is the one thing that would
        // give away a monster that has not spawned yet.
        NemesisEyes eyes = GetComponent<NemesisEyes>();
        if (eyes != null) eyes.SetLightsEnabled(!dormant);

        if (dormant) HideRenderers();
        else         RestoreRenderers();
    }

    /// <summary>
    /// Switches off only the renderers that were actually on, and remembers exactly those.
    /// Blanket-enabling every renderer on wake-up would light up anything the prefab left
    /// disabled on purpose (spare meshes, effect emitters, debug visuals).
    /// </summary>
    private void HideRenderers()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        List<Renderer> hidden = new List<Renderer>(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !all[i].enabled) continue;

            all[i].enabled = false;
            hidden.Add(all[i]);
        }

        hiddenWhileDormant = hidden;
    }

    private void RestoreRenderers()
    {
        if (hiddenWhileDormant == null) return;

        for (int i = 0; i < hiddenWhileDormant.Count; i++)
        {
            if (hiddenWhileDormant[i] != null) hiddenWhileDormant[i].enabled = true;
        }

        hiddenWhileDormant = null;
    }

    // ── Agent tuning ────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the agent tuning from <see cref="SO_NemesisMovement"/> onto the NavMeshAgent.
    ///
    /// It exists because until recently nothing did. The asset has exposed AngularSpeed,
    /// Acceleration and StoppingDistance since it was written and had zero readers in the whole
    /// project — the states only ever set NavAgent.speed — so the agent silently ran on whatever
    /// the prefab was serialised with, and editing the asset a designer is told is the tuning
    /// file did nothing at all.
    ///
    /// Speed is deliberately NOT set here: it is per-state (patrol / investigate / chase / search)
    /// and each state's EnterState owns it. Everything here is constant for the run.
    ///
    /// autoBraking off: it exists to stop precisely on a destination, which is what you want for a
    /// waypoint and exactly what you do not want for a pursuit — braking into every path corner is
    /// what makes a chase read as hesitant.
    /// </summary>
    public void ApplyMovementTuning()
    {
        UnityEngine.AI.NavMeshAgent agent = stateManager.NavAgent;
        SO_NemesisMovement movement = stateManager.NemesisMovement;

        if (agent == null || movement == null) return;

        agent.angularSpeed = movement.AngularSpeed;
        agent.acceleration = movement.Acceleration;
        agent.stoppingDistance = movement.StoppingDistance;
        agent.autoBraking = false;

        // Published so every NemesisNav query measures over the same NavMesh the agent is allowed
        // to walk on. This is what makes an off-limits area (a safe room painted with a custom
        // NavMesh area, with that bit cleared on the agent) mean the same thing to the route
        // oracle, the patrol graph, the hearing sensor and the HUD as it does to the agent —
        // instead of only stopping the body while every measurement still reads straight through.
        NemesisNav.AreaMask = agent.areaMask;
    }

    // ── Repositioning ───────────────────────────────────────────────────────

    /// <summary>
    /// Warps the Nemesis away after a capture: to a random spawn point when the controller has any
    /// configured, and to a random unlocked waypoint otherwise. Without this it would resume
    /// patrolling from the spot where it caught you, which is right where you reappear when the
    /// checkpoint is close.
    ///
    /// Spawn points first because that is exactly what they are: hand-placed "the Nemesis comes
    /// back from here" markers, chosen to sit away from where the player is sent. Waypoints stay
    /// as the fallback so a scene that never filled the spawn list in keeps working instead of
    /// leaving the Nemesis parked on top of the checkpoint.
    ///
    /// Random and not <see cref="NemesisController.ChooseSpawnPoint"/>: that one deliberately
    /// picks the farthest hidden point, which is right for the first arrival but would send the
    /// Nemesis to the same corner after every single capture.
    /// </summary>
    public void RepositionAfterCapture()
    {
        // The encounter is over. Forgetting where the player was is as much a part of that as
        // moving away from it: the checkpoint has physically relocated them, so the remembered
        // position now points at the one spot they provably are not.
        //
        // Without this the Nemesis walks away and comes straight back. The belief is fresh — it
        // had its hands on the player a second ago — so BeliefFreshness sits near 1 and the patrol
        // bias pulls hard towards the capture point, undoing the warp below. And on the way there
        // the decision ladder reads that same fresh belief and lands it in Searching instead of
        // back on patrol, which is the behaviour this fixes.
        if (stateManager.FieldOfView != null) stateManager.FieldOfView.ForgetLastKnownPosition();
        if (stateManager.FieldOfListening != null) stateManager.FieldOfListening.ForgetLastKnownPosition();

        NemesisController controller = stateManager.NemesisController;

        IReadOnlyList<Transform> spawns = controller != null ? controller.SpawnPoints : null;
        if (TryWarpToRandom(spawns)) return;

        IReadOnlyList<Transform> waypoints = controller != null
            ? controller.AllUnlockedWaypoints
            : null;

        if (TryWarpToRandom(waypoints)) return;

        Debug.LogWarning($"[{nameof(NemesisLifecycle)}] Nowhere to reposition to after a capture: " +
                         "no spawn points configured on the NemesisController and no unlocked " +
                         "waypoints either. The Nemesis stays where it caught the player.", this);
    }

    /// <summary>
    /// Splits <paramref name="points"/> into the ones far enough from the respawned player and the
    /// ones that are not, and warps to a random entry of the first group — falling through to the
    /// second only if none of them worked. Standing on top of the player is bad, staying at the
    /// capture point is worse.
    /// </summary>
    private bool TryWarpToRandom(IReadOnlyList<Transform> points)
    {
        if (points == null || points.Count == 0) return false;

        Transform player = stateManager.PlayerTransform;
        float minDistance = RepositionMinPlayerDistance;

        List<Transform> far = new List<Transform>(points.Count);
        List<Transform> near = new List<Transform>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            Transform point = points[i];
            if (point == null) continue;

            bool tooClose = player != null &&
                            Vector3.Distance(point.position, player.position) < minDistance;

            if (tooClose) near.Add(point);
            else          far.Add(point);
        }

        return TryWarpToAnyOf(far) || TryWarpToAnyOf(near);
    }

    /// <summary>
    /// Tries every entry, starting from a random one and wrapping, instead of picking one and
    /// hoping. NavMeshAgent.Warp fails silently — it returns false and leaves the agent exactly
    /// where it was — whenever the target does not land on the NavMesh, so a single marker nudged
    /// off the mesh used to be enough to leave the Nemesis standing at the capture point, which is
    /// the whole thing this is here to avoid.
    /// </summary>
    private bool TryWarpToAnyOf(List<Transform> points)
    {
        if (points.Count == 0) return false;

        int start = Random.Range(0, points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Transform target = points[(start + i) % points.Count];
            if (stateManager.WarpTo(target.position)) return true;
        }

        return false;
    }
}
