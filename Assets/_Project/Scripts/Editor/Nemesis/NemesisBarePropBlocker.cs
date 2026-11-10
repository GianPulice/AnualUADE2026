using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gives a body to the dressing props that have none: every prop under the level's loose-props roots
/// that has renderers and NO collider, and stands in the Nemesis's way (its volume is over walkable
/// NavMesh, between its shins and its head), gets a collider and a carving NavMeshObstacle — the same
/// pair the props placed by hand carry. Without them the Nemesis walks straight through a washing
/// machine, and so does the player.
///
/// Then it moves any patrol waypoint that ended up inside one of those props to the nearest free
/// floor, so blocking a prop never strands a route.
///
/// LEFT ALONE, and listed in the report:
///   cloth        (Cortina, Clothesline): something to walk through, not a wall.
///   floor litter (top under <see cref="MinSolidHeight"/> over the floor): stepped over.
///   overhead     (bottom above the Nemesis's head): nothing to bump into.
///
/// Undoable (one Undo step), re-runnable (a prop that already has a collider is skipped), and it does
/// NOT save the scene. Carving shows in Play; nothing needs baking.
/// </summary>
public static class NemesisBarePropBlocker
{
    private const string MenuPath = "Tools/Nemesis/Block Bare Props (Zona 2)";
    private const string LogTag = "[NemesisBareProps]";

    private static readonly string[] ScopeRoots = { "---- PROPS SUELTOS LV2 ----" };
    private static readonly string[] ClothNames = { "Cortina", "Clothesline" };

    /// <summary>The Nemesis's shins: lower than this over the floor is litter it steps over.</summary>
    private const float MinSolidHeight = 0.5f;

    /// <summary>The Nemesis walking (NemesisHeadroom.bodyHeight).</summary>
    private const float BodyHeight = 2.3f;

    /// <summary>The lowest it can get by hunching (NemesisHeadroom: 45° is ~0.4 m). A prop whose
    /// underside is lower than this blocks the floor under it; higher, it ducks.</summary>
    private const float DuckUnderHeight = 1.9f;

    private const float WaypointMargin = 0.5f;

    [MenuItem(MenuPath, true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Run()
    {
        var log = new StringBuilder();
        var blocked = new List<Bounds>();
        int skippedCloth = 0, skippedLitter = 0, skippedOverhead = 0, duckable = 0;

        Undo.SetCurrentGroupName("Block bare props");
        int group = Undo.GetCurrentGroup();

        foreach (Transform prop in BareProps())
        {
            Renderer[] renderers = prop.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) continue;

            Bounds world = renderers[0].bounds;
            foreach (Renderer r in renderers) world.Encapsulate(r.bounds);

            if (!TryFindFloorUnder(world, out float floorY)) continue;

            float bottom = world.min.y - floorY;
            float top = world.max.y - floorY;

            if (ClothNames.Any(n => prop.name.Contains(n))) { skippedCloth++; continue; }
            if (top < MinSolidHeight) { skippedLitter++; continue; }
            if (bottom >= BodyHeight) { skippedOverhead++; continue; }

            Bounds local = LocalBounds(prop, renderers);

            BoxCollider box = Undo.AddComponent<BoxCollider>(prop.gameObject);
            box.center = local.center;
            box.size = local.size;

            // Low enough to duck under: the collider is what NemesisHeadroom feels; the floor stays.
            if (bottom >= DuckUnderHeight)
            {
                duckable++;
                log.AppendLine($"  duck   {prop.name}: underside {bottom:0.00} m over the floor (collider only)");
                continue;
            }

            NavMeshObstacle obstacle = Undo.AddComponent<NavMeshObstacle>(prop.gameObject);
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = local.center;
            obstacle.size = local.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            blocked.Add(world);
            log.AppendLine($"  block  {prop.name} at ({world.center.x:0.0}, {world.center.y:0.0}, {world.center.z:0.0}) " +
                           $"{world.size.x:0.0}×{world.size.y:0.0}×{world.size.z:0.0} m, {bottom:0.00}–{top:0.00} m over the floor");
        }

        int moved = MoveStrandedWaypoints(blocked, log);

        Undo.CollapseUndoOperations(group);
        if (blocked.Count + duckable + moved > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"{LogTag} {blocked.Count} prop(s) blocked, {duckable} left to duck under, {moved} waypoint(s) moved. " +
                  $"Left alone: {skippedCloth} cloth, {skippedLitter} floor litter, {skippedOverhead} overhead. " +
                  $"Not saved; Ctrl+Z undoes all of it.\n{log}");
    }

    /// <summary>Props with renderers and no collider anywhere in them: the children of the folders
    /// (PROPS, STRUCTURE, PIPES, LIGHTS) of the scope roots.</summary>
    private static IEnumerable<Transform> BareProps()
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!ScopeRoots.Contains(root.name)) continue;

            foreach (Transform folder in root.transform)
            {
                foreach (Transform prop in folder)
                {
                    if (!prop.gameObject.activeInHierarchy) continue;
                    if (prop.GetComponentInChildren<Collider>(true) != null) continue;
                    yield return prop;
                }
            }
        }
    }

    /// <summary>Walkable floor inside the prop's footprint that its volume stands over, between the
    /// floor and the Nemesis's head.</summary>
    private static bool TryFindFloorUnder(Bounds world, out float floorY)
    {
        floorY = 0f;
        float reach = world.extents.y + BodyHeight;

        for (float x = world.min.x; x <= world.max.x + 0.001f; x += 0.25f)
        {
            for (float z = world.min.z; z <= world.max.z + 0.001f; z += 0.25f)
            {
                var probe = new Vector3(x, world.center.y, z);
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, reach, NavMesh.AllAreas)) continue;

                Vector3 flat = hit.position - probe;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.15f * 0.15f) continue;
                if (world.max.y < hit.position.y + 0.15f || world.min.y > hit.position.y + BodyHeight) continue;

                floorY = hit.position.y;
                return true;
            }
        }

        return false;
    }

    /// <summary>The renderers' bounds in the prop's own space, so the box follows a rotated prop.</summary>
    private static Bounds LocalBounds(Transform prop, Renderer[] renderers)
    {
        bool any = false;
        var bounds = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            Bounds mesh = renderer.localBounds;
            Matrix4x4 toProp = prop.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = toProp.MultiplyPoint3x4(corner);

                if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                else bounds.Encapsulate(point);
            }
        }

        return bounds;
    }

    private static int MoveStrandedWaypoints(List<Bounds> blocked, StringBuilder log)
    {
        if (blocked.Count == 0) return 0;

        int moved = 0;
        foreach (NemesisRoute route in Object.FindObjectsByType<NemesisRoute>(FindObjectsInactive.Include))
        {
            foreach (Transform waypoint in route.transform)
            {
                if (!waypoint.CompareTag("NemesisWaypoint") || !IsInside(waypoint.position, blocked)) continue;

                if (!TryFindFreeSpot(waypoint.position, blocked, out Vector3 spot))
                {
                    log.AppendLine($"  WAYPOINT {route.name}/{waypoint.name} is inside a blocked prop and no free floor " +
                                   "was found within 4 m: move it by hand.");
                    continue;
                }

                Undo.RecordObject(waypoint, "Move stranded waypoint");
                log.AppendLine($"  moved  {route.name}/{waypoint.name} out of a prop: {waypoint.position:0.0} -> {spot:0.0}");
                waypoint.position = spot;
                moved++;
            }
        }

        return moved;
    }

    private static bool IsInside(Vector3 point, List<Bounds> blocked)
    {
        foreach (Bounds b in blocked)
        {
            if (point.x > b.min.x - WaypointMargin && point.x < b.max.x + WaypointMargin &&
                point.z > b.min.z - WaypointMargin && point.z < b.max.z + WaypointMargin &&
                point.y > b.min.y - BodyHeight && point.y < b.max.y) return true;
        }

        return false;
    }

    /// <summary>The nearest floor to the waypoint that is clear of every blocked prop and of the
    /// NavMesh's own edges, on its floor.</summary>
    private static bool TryFindFreeSpot(Vector3 from, List<Bounds> blocked, out Vector3 spot)
    {
        spot = from;

        for (float radius = 0.25f; radius <= 4f; radius += 0.25f)
        {
            int steps = Mathf.Max(8, Mathf.CeilToInt(radius * 12f));
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                Vector3 candidate = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.3f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - from.y) > 0.5f || IsInside(hit.position, blocked)) continue;
                if (NavMesh.FindClosestEdge(hit.position, out NavMeshHit edge, NavMesh.AllAreas) && edge.distance < WaypointMargin) continue;

                spot = hit.position;
                return true;
            }
        }

        return false;
    }
}
