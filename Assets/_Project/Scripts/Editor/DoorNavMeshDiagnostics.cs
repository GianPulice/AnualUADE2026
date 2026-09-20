using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Answers one question, per door, from inside the editor: can the Nemesis path through it?
///
/// Written because the symptom ("it treats every door as a wall") has at least four possible
/// causes that all look identical from the outside — the doorway baked shut, no walkable floor
/// under it, a carving obstacle, or a gap narrower than the agent radius — and reading the scene
/// YAML cannot tell them apart. This measures the actual NavMesh with the actual agent settings.
///
/// Run it with the gameplay scene open, from WIRED ▸ Nemesis ▸ Diagnose doors. Read-only: it
/// changes nothing in the scene.
/// </summary>
public static class DoorNavMeshDiagnostics
{
    /// <summary>
    /// How far to each side of the leaf the probe points are placed, in metres. Several offsets,
    /// tried nearest first, because a single fixed one is not trustworthy: at 1.3 m a probe can
    /// land inside the wall of a thick doorway, or in the gap behind an open leaf, and report "no
    /// NavMesh" for a doorway that is perfectly walkable. The first offset that finds NavMesh
    /// wins, and the one that worked is printed so the reading can be checked by hand.
    /// </summary>
    private static readonly float[] ProbeOffsets = { 0.9f, 1.3f, 1.9f, 2.6f };

    /// <summary>Search radius when snapping a probe point onto the NavMesh. Anything further than
    /// this is "no NavMesh on that side" as far as the agent is concerned.</summary>
    private const float SampleRadius = 1f;

    /// <summary>Second, much wider search used only to describe a failure: it separates "this room
    /// has no NavMesh at all" from "the NavMesh is right there and the probe just missed it".</summary>
    private const float FarSearchRadius = 6f;

    [MenuItem("WIRED/Nemesis/Diagnose doors")]
    private static void Diagnose()
    {
        DoorInteractable[] doors = Object.FindObjectsByType<DoorInteractable>(FindObjectsInactive.Include);

        if (doors.Length == 0)
        {
            Debug.LogWarning("[DoorNavMeshDiagnostics] No DoorInteractable in the open scene.");
            return;
        }

        // Agent settings come from the NavMesh agent type, not from the NavMeshAgent component:
        // that is what the bake eroded the walkable surface by, so it is the number a doorway has
        // to beat.
        NavMeshBuildSettings agentSettings = NavMesh.GetSettingsByID(0);
        var report = new StringBuilder();
        report.AppendLine($"[DoorNavMeshDiagnostics] {doors.Length} doors | agent radius " +
                          $"{agentSettings.agentRadius:0.00} m, height {agentSettings.agentHeight:0.00} m");

        int blocked = 0;

        foreach (DoorInteractable door in doors)
        {
            if (!TryDescribe(door, out string line, out bool doorIsBlocked)) continue;

            if (doorIsBlocked) blocked++;
            report.AppendLine(line);
        }

        report.AppendLine($"— {blocked} of {doors.Length} doors have no usable path through them.");
        Debug.Log(report.ToString());
    }

    private static bool TryDescribe(DoorInteractable door, out string line, out bool doorIsBlocked)
    {
        line = null;
        doorIsBlocked = false;

        Transform hinge = GetHinge(door);
        if (hinge == null)
        {
            line = $"  {door.name}: NO HINGE assigned — cannot measure.";
            return true;
        }

        if (!TryGetLeafBounds(hinge, out Bounds leaf))
        {
            line = $"  {door.name}: no renderer under the hinge — cannot measure.";
            return true;
        }

        // The direction you walk THROUGH the door is the leaf's thin horizontal axis.
        Vector3 through = leaf.size.x < leaf.size.z ? Vector3.right : Vector3.forward;
        float clearWidth = Mathf.Max(leaf.size.x, leaf.size.z);

        bool aOk = TryProbeSide(leaf.center, through, out NavMeshHit aHit, out string aInfo);
        bool bOk = TryProbeSide(leaf.center, -through, out NavMeshHit bHit, out string bInfo);

        string pathState;
        if (!aOk || !bOk)
        {
            // No NavMesh beside the door at all: the problem is the bake on that side, not the
            // doorway itself.
            pathState = $"NO NAVMESH on {(aOk || bOk ? "one" : "both")} side(s) (A: {aInfo}; B: {bInfo})";
            doorIsBlocked = true;
        }
        else
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(aHit.position, bHit.position, NavMesh.AllAreas, path);

            float straight = Vector3.Distance(aHit.position, bHit.position);
            float walked = PathLength(path);

            // A complete path is not enough on its own: with the doorway shut, the agent happily
            // paths the long way round the building and reports success. A detour many times the
            // straight-line distance IS the doorway being closed.
            bool detour = path.status == NavMeshPathStatus.PathComplete && walked > straight * 3f;

            pathState = $"{path.status}, {walked:0.0} m vs {straight:0.0} m straight" +
                        (detour ? " → DETOUR, not through the doorway" : string.Empty);
            doorIsBlocked = path.status != NavMeshPathStatus.PathComplete || detour;
        }

        line = $"  {door.name}: {pathState} | clear width {clearWidth:0.00} m | " +
               $"{DescribeNavSetup(door)} | {DescribeBlockers(door, leaf)}";
        return true;
    }

    /// <summary>
    /// Walks out from the doorway on one side looking for NavMesh, and describes what it found.
    ///
    /// On failure the description says how far the NEAREST NavMesh is, which is the difference
    /// that matters: half a metre away means the probe missed a surface that is there, while
    /// "none within 6 m" means that room never got baked.
    /// </summary>
    private static bool TryProbeSide(Vector3 doorCenter, Vector3 direction, out NavMeshHit hit,
                                     out string info)
    {
        hit = default;
        Vector3 last = doorCenter;

        foreach (float offset in ProbeOffsets)
        {
            last = SnapToFloor(doorCenter + direction * offset);
            if (!NavMesh.SamplePosition(last, out hit, SampleRadius, NavMesh.AllAreas)) continue;

            info = $"ok at {offset:0.0} m";
            return true;
        }

        info = NavMesh.SamplePosition(last, out NavMeshHit far, FarSearchRadius, NavMesh.AllAreas)
            ? $"none — nearest NavMesh {Vector3.Distance(last, far.position):0.0} m away, probe at {last:F1}"
            : $"none within {FarSearchRadius:0} m, probe at {last:F1}";

        return false;
    }

    /// <summary>What the door contributes to navigation: the modifier that keeps it out of the
    /// bake, and any obstacle that carves it out at runtime.</summary>
    private static string DescribeNavSetup(DoorInteractable door)
    {
        var modifier = door.GetComponentInChildren<NavMeshModifier>(includeInactive: true);
        string modifierState = modifier == null
            ? "NO NavMeshModifier (door is baked INTO the NavMesh)"
            : $"modifier ignoreFromBuild={modifier.ignoreFromBuild}, applyToChildren={modifier.applyToChildren}";

        var obstacle = door.GetComponentInChildren<NavMeshObstacle>(includeInactive: true);
        string obstacleState = obstacle == null
            ? "no obstacle (added at runtime)"
            : $"obstacle carving={obstacle.carving}";

        return $"{modifierState}, {obstacleState}";
    }

    /// <summary>Everything solid sitting in the doorway that is NOT this door — the colliders that
    /// would still be baked in after the door itself is excluded.</summary>
    private static string DescribeBlockers(DoorInteractable door, Bounds leaf)
    {
        Collider[] hits = Physics.OverlapBox(leaf.center, leaf.extents, Quaternion.identity,
                                             ~0, QueryTriggerInteraction.Ignore);
        var names = new StringBuilder();
        int count = 0;

        foreach (Collider hit in hits)
        {
            if (hit.GetComponentInParent<DoorInteractable>() == door) continue;

            count++;
            if (count > 4) continue;

            if (names.Length > 0) names.Append(", ");
            names.Append($"{hit.name} [{LayerMask.LayerToName(hit.gameObject.layer)}]");
        }

        if (count == 0) return "doorway clear of other colliders";

        return $"{count} other collider(s) in the doorway: {names}" + (count > 4 ? ", …" : string.Empty);
    }

    /// <summary>The door's hinge, read straight off the private field so the diagnostic does not
    /// force a public accessor to exist purely for debugging.</summary>
    private static Transform GetHinge(DoorInteractable door)
    {
        FieldInfo field = typeof(DoorInteractable).GetField(
            "hinge", BindingFlags.NonPublic | BindingFlags.Instance);

        return field?.GetValue(door) as Transform;
    }

    private static bool TryGetLeafBounds(Transform hinge, out Bounds bounds)
    {
        bounds = default;

        Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers == null || renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        return true;
    }

    /// <summary>Drops a probe point onto whatever floor is under it, so the NavMesh sample is not
    /// taken at the height of the leaf's centre.</summary>
    private static Vector3 SnapToFloor(Vector3 point)
    {
        return Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit hit, 6f,
                               ~0, QueryTriggerInteraction.Ignore)
            ? hit.point
            : point;
    }

    private static float PathLength(NavMeshPath path)
    {
        float total = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            total += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return total;
    }
}
