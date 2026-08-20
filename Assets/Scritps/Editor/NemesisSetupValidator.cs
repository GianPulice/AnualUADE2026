using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine.AI;

/// <summary>
/// Checks the Nemesis's navigation and senses setup in the open scene.
///
/// It exists for a concrete reason: the three worst Nemesis bugs were not in the code but in
/// misconfigured layer masks, and a misconfigured mask does not fail — it quietly does less, until
/// somebody plays and watches the monster walk through a wall. This turns them into a console
/// message.
///
/// What it checks:
///   - That the NavMeshSurface includes both the floor AND the wall layers. With Wall missing, the
///     NavMesh is baked underneath the walls and the Nemesis walks straight through them.
///   - That the sensors' obstacleMask includes floor and wall. With those missing, the Nemesis
///     sees and hears through geometry — and chases and grabs you from the other side.
///   - That waypoints are tagged and land on the NavMesh.
///   - That NemesisDoorUser has a usable door mask.
///
/// It checks nothing that depends on Play mode: for what only shows up at runtime (NavMesh
/// islands, elevator links) the warnings come from NemesisRouteGraph and NemesisElevatorLink.
/// </summary>
public static class NemesisSetupValidator
{
    /// <summary>Layers that must be in the mask for the bake and the senses to account for the
    /// geometry that separates spaces.</summary>
    private static readonly string[] BlockingLayerNames = { "Ground", "Wall" };

    [MenuItem("Tools/Nemesis/Validate Navigation Setup")]
    private static void Validate()
    {
        StringBuilder report = new StringBuilder();
        int problems = 0;

        problems += ValidateSurfaces(report);
        problems += ValidateSensors(report);
        problems += ValidateWaypoints(report);
        problems += ValidateDoorUsers(report);

        if (problems == 0)
        {
            Debug.Log("[NemesisSetupValidator] All good: NavMeshSurface, sensors, waypoints and " +
                      "doors are set up correctly.");
            return;
        }

        Debug.LogWarning($"[NemesisSetupValidator] {problems} problem(s):\n\n{report}\n" +
                         "Tools > Nemesis > Repair Layer Masks fixes the masks. The NavMesh has to " +
                         "be rebaked afterwards.");
    }

    [MenuItem("Tools/Nemesis/Repair Layer Masks")]
    private static void RepairMasks()
    {
        int blocking = BuildBlockingMask();
        if (blocking == 0)
        {
            Debug.LogError("[NemesisSetupValidator] There are no Ground/Wall layers in this " +
                           "project. Create them in Project Settings > Tags and Layers before " +
                           "repairing anything.");
            return;
        }

        int fixedCount = 0;

        foreach (NavMeshSurface surface in FindAll<NavMeshSurface>())
        {
            if ((surface.layerMask.value & blocking) == blocking) continue;

            Undo.RecordObject(surface, "Repair NavMeshSurface mask");
            surface.layerMask = surface.layerMask.value | blocking;
            EditorUtility.SetDirty(surface);
            fixedCount++;
        }

        // The sensors live on a prefab, so the prefab is repaired rather than each instance:
        // otherwise every Nemesis in the scene ends up with an override and the next one spawned
        // is born broken again.
        fixedCount += RepairSensorMask<FieldOfView>(blocking);
        fixedCount += RepairSensorMask<FieldOfListening>(blocking);

        Debug.Log($"[NemesisSetupValidator] {fixedCount} mask(s) repaired. IMPORTANT: rebake the " +
                  "NavMesh (Navigation window > Bake) — the mask defines what geometry goes into " +
                  "the bake, but it does not rebake on its own.");
    }

    // ── Checks ──────────────────────────────────────────────────────────────

    private static int ValidateSurfaces(StringBuilder report)
    {
        int blocking = BuildBlockingMask();
        int problems = 0;

        NavMeshSurface[] surfaces = FindAll<NavMeshSurface>();
        if (surfaces.Length == 0)
        {
            report.AppendLine("- There is no NavMeshSurface in the scene. Without one the Nemesis " +
                              "cannot navigate at all.");
            return 1;
        }

        foreach (NavMeshSurface surface in surfaces)
        {
            int missing = blocking & ~surface.layerMask.value;
            if (missing == 0) continue;

            report.AppendLine($"- NavMeshSurface '{surface.name}': its Include Layers does NOT " +
                              $"include {DescribeLayers(missing)}. That geometry does not go into " +
                              "the bake, so the NavMesh is generated underneath it and the Nemesis " +
                              "walks straight through. This is the cause of 'Nemesis walks through " +
                              "walls'.");
            problems++;
        }

        return problems;
    }

    private static int ValidateSensors(StringBuilder report)
    {
        int blocking = BuildBlockingMask();
        int problems = 0;

        foreach (FieldOfView view in FindAll<FieldOfView>())
        {
            problems += CheckSensorMask(report, view, GetMask(view, "obstacleMask"), blocking,
                                        "it sees the player through walls and floors, chases them " +
                                        "and grabs them from the other side");
        }

        foreach (FieldOfListening listening in FindAll<FieldOfListening>())
        {
            problems += CheckSensorMask(report, listening, GetMask(listening, "obstacleMask"), blocking,
                                        "it hears the player through walls and floors with no " +
                                        "attenuation");
        }

        return problems;
    }

    private static int CheckSensorMask(StringBuilder report, Object sensor, int mask, int blocking,
                                       string consequence)
    {
        int missing = blocking & ~mask;
        if (missing == 0) return 0;

        report.AppendLine($"- {sensor.GetType().Name} on '{sensor.name}': its obstacleMask does " +
                          $"NOT include {DescribeLayers(missing)}. Consequence: {consequence}.");
        return 1;
    }

    private static int ValidateWaypoints(StringBuilder report)
    {
        int problems = 0;

        // CalculateTriangulation and not a huge-radius SamplePosition: a large sample walks the
        // whole mesh and can freeze the editor for seconds. This only asks whether anything is baked.
        bool navMeshExists = NavMesh.CalculateTriangulation().vertices.Length > 0;

        foreach (NemesisRoute route in FindAll<NemesisRoute>())
        {
            int tagged = 0;

            for (int i = 0; i < route.transform.childCount; i++)
            {
                Transform child = route.transform.GetChild(i);
                if (!child.CompareTag(NemesisRoute.WaypointTag)) continue;

                tagged++;

                if (!navMeshExists) continue;
                if (NemesisNav.IsOnNavMesh(child.position)) continue;

                report.AppendLine($"- Waypoint '{child.name}' (route '{route.name}') does not land " +
                                  "on the NavMesh. The Nemesis will never be able to use it.");
                problems++;
            }

            if (tagged > 0) continue;

            report.AppendLine($"- Route '{route.name}' has no child tagged " +
                              $"'{NemesisRoute.WaypointTag}'. It will never be picked.");
            problems++;
        }

        if (!navMeshExists)
        {
            report.AppendLine("- No baked NavMesh found, so it was not possible to check whether " +
                              "the waypoints land on it. Bake it and validate again.");
            problems++;
        }

        return problems;
    }

    private static int ValidateDoorUsers(StringBuilder report)
    {
        int problems = 0;

        int doorColliderLayers = CollectDoorColliderLayers(report, ref problems);

        foreach (NemesisDoorUser user in FindAll<NemesisDoorUser>())
        {
            int mask = GetMask(user, "doorMask");

            if (mask == 0)
            {
                report.AppendLine($"- NemesisDoorUser on '{user.name}': doorMask is set to Nothing, " +
                                  "so it will never find a door.");
                problems++;
                continue;
            }

            if (doorColliderLayers == 0) continue;   // Already reported below, per door.

            // The check that catches the silent failure: a mask can be perfectly valid and still
            // match no ENABLED door collider. That was the real cause of "the Nemesis walks past
            // doors" — the mask was Interactable, which is where the DoorInteractable's own
            // collider sits, but that collider is disabled and the real geometry is in Default.
            if ((mask & doorColliderLayers) != 0) continue;

            report.AppendLine($"- NemesisDoorUser on '{user.name}': doorMask ({DescribeLayers(mask)}) " +
                              "matches NO enabled collider on any door in the scene. The doors' " +
                              $"enabled colliders are on {DescribeLayers(doorColliderLayers)}. The " +
                              "sweep will always come back empty and the Nemesis will walk straight " +
                              "through closed doors.");
            problems++;
        }

        return problems;
    }

    /// <summary>
    /// Union of the layers of every ENABLED collider on or under a DoorInteractable.
    ///
    /// Enabled is the whole point: a disabled collider is invisible to Physics queries, so a mask
    /// pointing only at its layer is indistinguishable from a mask pointing at nothing — except
    /// that it looks correct in the inspector.
    /// </summary>
    private static int CollectDoorColliderLayers(StringBuilder report, ref int problems)
    {
        int layers = 0;

        foreach (DoorInteractable door in FindAll<DoorInteractable>())
        {
            int doorLayers = 0;

            foreach (Collider collider in door.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled) continue;
                doorLayers |= 1 << collider.gameObject.layer;
            }

            if (doorLayers == 0)
            {
                report.AppendLine($"- Door '{door.name}' has no enabled collider anywhere in its " +
                                  "hierarchy. Neither the player nor the Nemesis can detect it.");
                problems++;
                continue;
            }

            layers |= doorLayers;
        }

        return layers;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static int BuildBlockingMask()
    {
        int mask = 0;
        foreach (string layerName in BlockingLayerNames)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask |= 1 << layer;
        }
        return mask;
    }

    private static string DescribeLayers(int mask)
    {
        List<string> names = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) == 0) continue;

            string name = LayerMask.LayerToName(layer);
            names.Add(string.IsNullOrEmpty(name) ? $"layer {layer}" : name);
        }
        return names.Count > 0 ? string.Join(", ", names) : "(none)";
    }

    /// <summary>
    /// Reads a serialised LayerMask by name. Through SerializedObject rather than reflection
    /// because the fields are private and this is no reason to open them up: a mask is scene
    /// wiring, not API.
    /// </summary>
    private static int GetMask(Object target, string fieldName)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        return property != null ? property.intValue : 0;
    }

    private static int RepairSensorMask<T>(int blocking) where T : Component
    {
        int fixedCount = 0;

        foreach (T sensor in FindAll<T>())
        {
            SerializedObject serialized = new SerializedObject(sensor);
            SerializedProperty property = serialized.FindProperty("obstacleMask");
            if (property == null) continue;

            int merged = property.intValue | blocking;
            if (merged == property.intValue) continue;

            property.intValue = merged;
            serialized.ApplyModifiedProperties();
            fixedCount++;
        }

        return fixedCount;
    }

    private static T[] FindAll<T>() where T : Object =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
}
#endif
