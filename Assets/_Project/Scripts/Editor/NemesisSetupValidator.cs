using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine.AI;

/// <summary>
/// Checks the Nemesis's navigation and senses setup in the open scene.
///
/// It exists for a concrete reason: the worst Nemesis bugs were not in the code but in
/// misconfigured layer masks, and a misconfigured mask does not fail — it quietly does less, until
/// somebody plays and watches the monster walk through a wall. This turns them into a console
/// message.
///
/// What it checks:
///   - That the NavMeshSurface includes every layer the bake has to account for. With one missing,
///     the NavMesh is baked underneath that geometry and the Nemesis walks straight through it.
///   - That the sensors' obstacleMask includes everything that separates spaces. With those
///     missing, the Nemesis sees and hears through geometry — and chases and grabs you from the
///     other side.
///   - That the camera's Deoccluder and the interaction raycast agree with the sensors about what
///     counts as solid. They are the same question asked by three systems, and they were answering
///     it differently: the camera clipped through floors and the player interacted through walls.
///   - That waypoints are tagged and land on the NavMesh.
///   - That NemesisDoorUser has a usable door mask.
///
/// It checks nothing that depends on Play mode: for what only shows up at runtime (NavMesh
/// islands, elevator links) the warnings come from NemesisRouteGraph and NemesisElevatorLink.
/// </summary>
public static class NemesisSetupValidator
{
    /// <summary>
    /// Layers the NavMesh bake has to account for.
    ///
    /// Deliberately NOT the same list as <see cref="OcclusionLayerNames"/>, and Default is
    /// deliberately absent: this project keeps ceilings on Default, and a ceiling that goes into
    /// the bake comes out as a perfectly walkable roof. Props get their own layer precisely so
    /// they can be baked (as Not Walkable) without dragging the ceilings in with them.
    /// </summary>
    private static readonly string[] NavLayerNames = { "Ground", "Wall", "Props" };

    /// <summary>
    /// Layers that count as solid for line of sight — for the Nemesis's senses, for the camera's
    /// Deoccluder, and for the interaction raycast's blocking mask.
    ///
    /// Default IS here, unlike in the bake: a ceiling should not be walkable, but it absolutely
    /// should stop a raycast.
    /// </summary>
    private static readonly string[] OcclusionLayerNames = { "Default", "Ground", "Wall", "Props" };

    /// <summary>
    /// Scene roots whose whole subtree belongs on a given layer, for
    /// <see cref="MigratePropLayers"/>.
    ///
    /// By name and not by reference because these are scene objects and this is a static editor
    /// class; by root and not per object because the blockout has ~200 of them and the grouping
    /// already exists in the Hierarchy. Missing roots are skipped silently — not every scene has
    /// every group.
    /// </summary>
    private static readonly (string Root, string Layer)[] LayerMigrations =
    {
        ("---- PROPS SUELTOS ----", "Props"),
        ("WIRED_ZONA_02_PROPS",     "Props"),
        ("VISUAL_MASS",             "Props"),

        // Stairs were on Default, which is outside the bake mask — so the Nemesis could not use
        // them at all and the camera clipped through them.
        ("STAIRS",                  "Ground"),
    };

    [MenuItem("Tools/Nemesis/Validate Navigation Setup")]
    private static void Validate()
    {
        StringBuilder report = new StringBuilder();
        int problems = 0;

        problems += ValidateSurfaces(report);
        problems += ValidateModifierVolumes(report);
        problems += ValidateSensors(report);
        problems += ValidateCameraAndInteraction(report);
        problems += ValidateWaypoints(report);
        problems += ValidateDoorUsers(report);

        if (problems == 0)
        {
            Debug.Log("[NemesisSetupValidator] All good: NavMeshSurface, modifier volumes, sensors, " +
                      "camera, interaction, waypoints and doors are set up correctly.");
            return;
        }

        Debug.LogWarning($"[NemesisSetupValidator] {problems} problem(s):\n\n{report}\n" +
                         "Tools > Nemesis > Repair Layer Masks fixes the masks, and Tools > " +
                         "Nemesis > Migrate Prop Layers moves the scene objects. The NavMesh has " +
                         "to be rebaked afterwards.");
    }

    [MenuItem("Tools/Nemesis/Repair Layer Masks")]
    private static void RepairMasks()
    {
        int nav = BuildMask(NavLayerNames);
        int occlusion = BuildMask(OcclusionLayerNames);

        if (nav == 0 || occlusion == 0)
        {
            Debug.LogError("[NemesisSetupValidator] Some of the Ground/Wall/Props layers do not " +
                           "exist in this project. Create them in Project Settings > Tags and " +
                           "Layers before repairing anything.");
            return;
        }

        int fixedCount = 0;

        foreach (NavMeshSurface surface in FindAll<NavMeshSurface>())
        {
            if ((surface.layerMask.value & nav) == nav) continue;

            Undo.RecordObject(surface, "Repair NavMeshSurface mask");
            surface.layerMask = surface.layerMask.value | nav;
            EditorUtility.SetDirty(surface);
            fixedCount++;
        }

        // The sensors live on a prefab, so the prefab is repaired rather than each instance:
        // otherwise every Nemesis in the scene ends up with an override and the next one spawned
        // is born broken again.
        fixedCount += RepairSerializedMask<FieldOfView>("obstacleMask", occlusion);
        fixedCount += RepairSerializedMask<FieldOfListening>("obstacleMask", occlusion);

        // Same mask, same problem, two other systems. The camera clipped through floors because
        // its CollideAgainst was missing Ground; the interaction raycast reached through walls
        // because its blockingLayers was Default alone.
        fixedCount += RepairSerializedMask<CinemachineDeoccluder>("CollideAgainst", occlusion);
        fixedCount += RepairDecolliderMask(occlusion);
        fixedCount += RepairInteractionBlockingMask(occlusion);

        // The player's wall-slide cast asks the same question again. It ships empty on the
        // existing prefab (a field added after the fact deserialises to Nothing), and empty means
        // no deflection at all — the player sticks to every crate, which is the bug it was added
        // to fix. Filling it in here is what stops that from being a silent regression.
        fixedCount += RepairSerializedMask<PlayerStateManager>("obstacleMask", occlusion);

        Debug.Log($"[NemesisSetupValidator] {fixedCount} mask(s) repaired. IMPORTANT: rebake the " +
                  "NavMesh (Navigation window > Bake) — the mask defines what geometry goes into " +
                  "the bake, but it does not rebake on its own.");
    }

    /// <summary>
    /// Moves whole Hierarchy subtrees onto the layer they belong to, per
    /// <see cref="LayerMigrations"/>.
    ///
    /// Exists because the alternative is ~200 objects by hand, which is how they ended up on
    /// Default in the first place. Idempotent: an object already on the right layer is skipped, so
    /// running it twice is free and running it after adding new props only touches the new ones.
    /// </summary>
    [MenuItem("Tools/Nemesis/Migrate Prop Layers")]
    private static void MigratePropLayers()
    {
        StringBuilder report = new StringBuilder();
        int totalMoved = 0;

        foreach ((string rootName, string layerName) in LayerMigrations)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                report.AppendLine($"- Layer '{layerName}' does not exist — '{rootName}' skipped. " +
                                  "Create it in Project Settings > Tags and Layers.");
                continue;
            }

            GameObject root = FindSceneObjectByName(rootName);
            if (root == null) continue;   // Not every scene has every group.

            Undo.RegisterFullObjectHierarchyUndo(root, "Migrate prop layers");

            int moved = ApplyLayerRecursively(root.transform, layer);
            totalMoved += moved;

            if (moved > 0) report.AppendLine($"- '{rootName}' -> {layerName}: {moved} object(s).");
        }

        if (totalMoved == 0 && report.Length == 0)
        {
            Debug.Log("[NemesisSetupValidator] Nothing to migrate: every configured root is " +
                      "already on its layer (or is not in this scene).");
            return;
        }

        Debug.Log($"[NemesisSetupValidator] {totalMoved} object(s) moved:\n\n{report}\n" +
                  "Now run Tools > Nemesis > Repair Layer Masks and rebake the NavMesh.");
    }

    /// <summary>Assigns a layer to a transform and everything under it. Returns how many objects
    /// actually changed, so the report can stay quiet when there was nothing to do.</summary>
    private static int ApplyLayerRecursively(Transform root, int layer)
    {
        int moved = 0;

        if (root.gameObject.layer != layer)
        {
            root.gameObject.layer = layer;
            EditorUtility.SetDirty(root.gameObject);
            moved++;
        }

        for (int i = 0; i < root.childCount; i++)
            moved += ApplyLayerRecursively(root.GetChild(i), layer);

        return moved;
    }

    private static GameObject FindSceneObjectByName(string name)
    {
        foreach (GameObject go in FindAll<GameObject>())
        {
            // Scene objects only: FindObjectsByType does not return assets, but a prefab open in
            // Prefab Mode would show up here and must not be rewritten by a scene migration.
            if (go.name == name && go.scene.IsValid()) return go;
        }
        return null;
    }

    // ── Checks ──────────────────────────────────────────────────────────────

    private static int ValidateSurfaces(StringBuilder report)
    {
        int nav = BuildMask(NavLayerNames);
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
            int missing = nav & ~surface.layerMask.value;
            if (missing != 0)
            {
                report.AppendLine($"- NavMeshSurface '{surface.name}': its Include Layers does NOT " +
                                  $"include {DescribeLayers(missing)}. That geometry does not go " +
                                  "into the bake, so the NavMesh is generated underneath it and " +
                                  "the Nemesis walks straight through. This is the cause of " +
                                  "'Nemesis walks through props / stairs / walls'.");
                problems++;
            }

            problems += ReportCollidersOutsideMask(report, surface);
        }

        return problems;
    }

    /// <summary>
    /// Reports solid geometry the bake cannot see.
    ///
    /// This is the check that would have caught the props bug on the first run instead of on the
    /// first playtest: every mask in the project was internally consistent, and the surface still
    /// ignored 188 objects because they sat on a layer nobody had thought to add. A mask is right
    /// or wrong only relative to what is actually in the scene, so that is what gets measured.
    ///
    /// Triggers are skipped (they are zones, not geometry) and so is anything on Default, which
    /// this project uses for ceilings and is excluded from the bake on purpose — see
    /// <see cref="NavLayerNames"/>.
    /// </summary>
    private static int ReportCollidersOutsideMask(StringBuilder report, NavMeshSurface surface)
    {
        // Default: ceilings live here by convention, not a finding.
        // Interactable: excluded from every occlusion/blocking mask on purpose across the whole
        // project (see OcclusionLayerNames) — it is the layer the interaction SphereCast targets,
        // and nothing else is meant to notice it. A pickup or a wall-mounted socket does not need
        // to carve the NavMesh any more than it needs to block a sightline, so flagging it here
        // would be the same false positive repeated for a second system.
        int ignored = BuildMask(new[] { "Default", "Interactable" });
        int mask = surface.layerMask.value | ignored;

        List<string> examples = new List<string>();
        int count = 0;

        foreach (Collider collider in FindAll<Collider>())
        {
            if (collider.isTrigger || !collider.enabled) continue;
            if (!collider.gameObject.scene.IsValid()) continue;
            if ((mask & (1 << collider.gameObject.layer)) != 0) continue;

            count++;
            if (examples.Count < 5)
                examples.Add($"'{collider.name}' ({DescribeLayers(1 << collider.gameObject.layer)})");
        }

        if (count == 0) return 0;

        report.AppendLine($"- NavMeshSurface '{surface.name}': {count} enabled, non-trigger " +
                          $"collider(s) sit on layers it does not bake — e.g. " +
                          $"{string.Join(", ", examples)}. The Nemesis will walk through all of " +
                          "them. Move them to Props/Ground (Tools > Nemesis > Migrate Prop " +
                          "Layers) or add their layer to Include Layers.");
        return 1;
    }

    /// <summary>
    /// Reports NavMeshModifierVolumes that the bake silently throws away, and volumes whose area
    /// does not do what its name suggests.
    ///
    /// <b>This is the check that would have caught the safe-zone bug.</b> All three volumes in the
    /// level were authored correctly — right size, right area, AffectedAgents on Everything — and
    /// all three sat on layer Default, which the surface's Include Layers deliberately excludes so
    /// that ceilings do not bake as walkable roofs. <see cref="NavMeshSurface"/> filters MODIFIER
    /// VOLUMES through that same mask, not just geometry, so every one of them was dropped without
    /// a word. The Hub read as a safe zone in the inspector and the Nemesis walked straight in.
    ///
    /// Unity reports nothing here by design: a volume that contributes nothing is not an error, it
    /// is a volume the surface was not asked to collect. That is exactly why it has to be checked
    /// against the surfaces actually in the scene rather than in isolation.
    /// </summary>
    private static int ValidateModifierVolumes(StringBuilder report)
    {
        NavMeshModifierVolume[] volumes = FindAll<NavMeshModifierVolume>();
        if (volumes.Length == 0) return 0;

        NavMeshSurface[] surfaces = FindAll<NavMeshSurface>();

        // No surface at all is already reported by ValidateSurfaces, and saying it twice from here
        // would just bury the one message that matters.
        if (surfaces.Length == 0) return 0;

        int problems = 0;

        foreach (NavMeshModifierVolume volume in volumes)
        {
            problems += ReportVolumeReachesNoSurface(report, volume, surfaces);
            problems += ReportVolumeAreaIsNotBlocking(report, volume);
        }

        return problems;
    }

    /// <summary>
    /// A volume only reaches the bake through a surface that collects BOTH its layer and its agent
    /// type. Failing either is the same silent no-op, so they are reported together with the reason
    /// spelled out — "it is on the wrong layer" and "it is aimed at another agent type" need
    /// different fixes.
    /// </summary>
    private static int ReportVolumeReachesNoSurface(StringBuilder report,
                                                    NavMeshModifierVolume volume,
                                                    NavMeshSurface[] surfaces)
    {
        int layerBit = 1 << volume.gameObject.layer;

        bool layerAccepted = false;
        bool agentAccepted = false;

        foreach (NavMeshSurface surface in surfaces)
        {
            bool layerOk = (surface.layerMask.value & layerBit) != 0;
            bool agentOk = volume.AffectsAgentType(surface.agentTypeID);

            if (layerOk) layerAccepted = true;
            if (agentOk) agentAccepted = true;

            // Both, on the SAME surface — a volume accepted by one surface's layers and another's
            // agent type is still collected by neither.
            if (layerOk && agentOk) return 0;
        }

        string cause = !layerAccepted
            ? $"it sits on layer '{LayerMask.LayerToName(volume.gameObject.layer)}', which no " +
              "NavMeshSurface has in its Include Layers"
            : !agentAccepted
                ? "its Affected Agents does not include any agent type baked in this scene"
                : "no single surface accepts both its layer and its agent type";

        report.AppendLine(
            $"- NavMeshModifierVolume '{volume.gameObject.name}' does nothing: {cause}. " +
            "The surface filters modifier volumes through the same Include Layers as geometry, so " +
            "the volume is dropped from the bake in silence — the area it declares is never " +
            "applied. Move it onto a layer the surface collects (Props is the usual choice: a " +
            "volume has no renderer or collider, so nothing else about that layer touches it), " +
            "then rebake.");

        return 1;
    }

    /// <summary>
    /// A volume set to a high-cost area rather than Not Walkable does not block anything, and its
    /// name usually claims otherwise.
    ///
    /// Cost only makes a route more expensive. Where it is the only route, or where the detour
    /// costs more than the penalty, the agent walks through regardless — so a "safe zone" built on
    /// cost is not safe, it is merely unpopular. Reported rather than fixed: a high-cost area is
    /// perfectly legitimate for steering the Nemesis away from somewhere it is still allowed to go.
    /// </summary>
    private static int ReportVolumeAreaIsNotBlocking(StringBuilder report,
                                                     NavMeshModifierVolume volume)
    {
        const int notWalkableArea = 1;

        if (volume.area == notWalkableArea) return 0;

        float cost = NavMesh.GetAreaCost(volume.area);
        if (cost < HighAreaCostThreshold) return 0;

        report.AppendLine(
            $"- NavMeshModifierVolume '{volume.gameObject.name}' uses area " +
            $"'{AreaName(volume.area)}' at cost {cost:0.#}, which does NOT block anything. A cost " +
            "only makes the route expensive: with no alternative, or with a detour dearer than the " +
            "penalty, the agent walks through anyway. If this is meant to be a place the Nemesis " +
            "can never enter, set the area to 'Not Walkable'. If it is meant to be somewhere it " +
            "merely avoids, ignore this.");

        return 1;
    }

    /// <summary>Cost above which an area reads as an attempt to block rather than to steer.
    /// Walkable is 1 and Jump is 3; anything an order of magnitude past that was authored by
    /// someone who wanted a wall.</summary>
    private const float HighAreaCostThreshold = 20f;

    /// <summary>The area's name from the Navigation settings, falling back to its index so the
    /// message still identifies it when the area was never named.</summary>
    private static string AreaName(int area)
    {
        string[] names = NavMesh.GetAreaNames();
        foreach (string name in names)
        {
            if (NavMesh.GetAreaFromName(name) == area) return name;
        }

        return $"#{area}";
    }

    private static int ValidateSensors(StringBuilder report)
    {
        int occlusion = BuildMask(OcclusionLayerNames);
        int problems = 0;

        foreach (FieldOfView view in FindAll<FieldOfView>())
        {
            problems += CheckMask(report, $"{nameof(FieldOfView)} on '{view.name}'", "obstacleMask",
                                  GetMask(view, "obstacleMask"), occlusion,
                                  "it sees the player through walls, floors and props, chases " +
                                  "them and grabs them from the other side");
        }

        foreach (FieldOfListening listening in FindAll<FieldOfListening>())
        {
            problems += CheckMask(report, $"{nameof(FieldOfListening)} on '{listening.name}'",
                                  "obstacleMask", GetMask(listening, "obstacleMask"), occlusion,
                                  "it hears the player through walls and floors with no " +
                                  "attenuation");
        }

        return problems;
    }

    /// <summary>
    /// The camera and the interaction raycast ask the same question as the sensors — "what is
    /// solid?" — and each of the three used to answer it with its own mask. That is why the camera
    /// clipped through floors while the Nemesis did not see through them, and why the player could
    /// interact through a wall the Nemesis could not see through.
    /// </summary>
    private static int ValidateCameraAndInteraction(StringBuilder report)
    {
        int occlusion = BuildMask(OcclusionLayerNames);
        int problems = 0;

        foreach (CinemachineDeoccluder deoccluder in FindAll<CinemachineDeoccluder>())
        {
            problems += CheckMask(report, $"{nameof(CinemachineDeoccluder)} on '{deoccluder.name}'",
                                  "CollideAgainst", deoccluder.CollideAgainst.value, occlusion,
                                  "the camera goes through that geometry in tight spaces and you " +
                                  "see the level from inside a wall");
        }

        foreach (SO_InteractionManager config in FindAllAssets<SO_InteractionManager>())
        {
            problems += CheckMask(report, $"{nameof(SO_InteractionManager)} '{config.name}'",
                                  "blockingLayers", config.BlockingLayers.value, occlusion,
                                  "the interaction SphereCast reaches through that geometry and " +
                                  "the player interacts (and lights item highlights) through walls");
        }

        foreach (PlayerStateManager player in FindAll<PlayerStateManager>())
        {
            problems += CheckMask(report, $"{nameof(PlayerStateManager)} on '{player.name}'",
                                  "obstacleMask", GetMask(player, "obstacleMask"), occlusion,
                                  "the player does not slide along that geometry — it sticks to " +
                                  "it, which is 'you get stuck walking into every prop'");
        }

        return problems;
    }

    private static int CheckMask(StringBuilder report, string owner, string fieldName,
                                 int mask, int required, string consequence)
    {
        int missing = required & ~mask;
        if (missing == 0) return 0;

        report.AppendLine($"- {owner}: its {fieldName} does NOT include " +
                          $"{DescribeLayers(missing)}. Consequence: {consequence}.");
        return 1;
    }

    private static int ValidateWaypoints(StringBuilder report)
    {
        int problems = 0;

        // CalculateTriangulation and not a huge-radius SamplePosition: a large sample walks the
        // whole mesh and can freeze the editor for seconds. This only asks whether anything is baked.
        bool navMeshExists = NavMesh.CalculateTriangulation().vertices.Length > 0;

        ReportGeneratedSweepPoints(report);

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

    /// <summary>
    /// Says how many sweep points the Nemesis generates around each waypoint, and what that means
    /// for the counts reported below.
    ///
    /// NOT A PROBLEM, and it returns no count for that reason — it is a note, because the number
    /// of markers in the scene stops being the number of places the Nemesis stops. Without it, a
    /// designer who marks a room with one waypoint and then watches the Nemesis walk five points
    /// around it has no way to find out where the other four came from: they are runtime positions
    /// on the NavMesh, not GameObjects, so they appear in no hierarchy and no search.
    ///
    /// It reads the SO off the Nemesis in the scene rather than a hardcoded default, so what it
    /// reports is what will actually run.
    /// </summary>
    private static void ReportGeneratedSweepPoints(StringBuilder report)
    {
        foreach (NemesisStateManager manager in FindAll<NemesisStateManager>())
        {
            SO_NemesisData data = manager.NemesisData;
            if (data == null) continue;

            if (data.WaypointSatellites <= 0)
            {
                report.AppendLine("- Note: generated sweep points are off (Waypoint Satellites = " +
                                  "0), so a cúmulo's sweep is exactly the waypoints you placed. A " +
                                  "room marked with a single waypoint gets a single stop.");
                return;
            }

            report.AppendLine($"- Note: the Nemesis generates up to {data.WaypointSatellites} extra " +
                              $"sweep points within {data.WaypointSatelliteRadius:0.#} m of each " +
                              "waypoint, on the NavMesh, at runtime. They are positions and not " +
                              "GameObjects, so they will not appear in the hierarchy or in the " +
                              "counts below — but they DO get walked, so one marker means a small " +
                              "area swept rather than a single stop. They never affect a cúmulo's " +
                              "centre or its weight.");
            return;
        }
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

    private static int BuildMask(string[] layerNames)
    {
        int mask = 0;
        foreach (string layerName in layerNames)
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

    /// <summary>
    /// ORs a mask into a serialised LayerMask field on every instance of a component.
    ///
    /// Through SerializedObject and not the public property because most of these fields are
    /// private, and a mask is scene wiring rather than API — the same reasoning as
    /// <see cref="GetMask"/>. It also means one method covers the sensors and the Deoccluder
    /// despite them having nothing else in common.
    /// </summary>
    private static int RepairSerializedMask<T>(string fieldName, int required) where T : Component
    {
        int fixedCount = 0;

        foreach (T component in FindAll<T>())
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null) continue;

            int merged = property.intValue | required;
            if (merged == property.intValue) continue;

            property.intValue = merged;
            serialized.ApplyModifiedProperties();
            fixedCount++;
        }

        return fixedCount;
    }

    /// <summary>
    /// The Decollider's mask is nested (<c>Decollision.ObstacleLayers</c>), so it cannot go through
    /// <see cref="RepairSerializedMask{T}"/>, which takes a top-level field name.
    ///
    /// It is also the one component here that gets ENABLED rather than merely repaired: it ships
    /// switched off, and it is what recovers a camera that is already inside geometry — the case
    /// the Deoccluder cannot help with, because by then there is no clear shot left to preserve.
    /// </summary>
    private static int RepairDecolliderMask(int required)
    {
        int fixedCount = 0;

        foreach (CinemachineDecollider decollider in FindAll<CinemachineDecollider>())
        {
            SerializedObject serialized = new SerializedObject(decollider);
            SerializedProperty property =
                serialized.FindProperty("Decollision.ObstacleLayers");

            bool changed = false;

            if (property != null && (property.intValue | required) != property.intValue)
            {
                property.intValue |= required;
                changed = true;
            }

            SerializedProperty enabled = serialized.FindProperty("m_Enabled");
            if (enabled != null && !enabled.boolValue)
            {
                enabled.boolValue = true;
                changed = true;
            }

            if (!changed) continue;

            serialized.ApplyModifiedProperties();
            fixedCount++;
        }

        return fixedCount;
    }

    /// <summary>
    /// Repairs the interaction raycast's blocking mask on the SO asset rather than on a scene
    /// object: <see cref="InteractionManager"/> reads it from there, so fixing an instance would
    /// fix nothing.
    /// </summary>
    private static int RepairInteractionBlockingMask(int required)
    {
        int fixedCount = 0;

        foreach (SO_InteractionManager config in FindAllAssets<SO_InteractionManager>())
        {
            SerializedObject serialized = new SerializedObject(config);
            SerializedProperty property = serialized.FindProperty("blockingLayers");
            if (property == null) continue;

            int merged = property.intValue | required;
            if (merged == property.intValue) continue;

            property.intValue = merged;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            fixedCount++;
        }

        return fixedCount;
    }

    // Sin FindObjectsSortMode: el overload que lo recibe quedó obsoleto en Unity 6.4.
    // El que toma sólo FindObjectsInactive ya no ordena, que es justo lo que se pedía acá.
    private static T[] FindAll<T>() where T : Object =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include);

    /// <summary>
    /// Every asset of a type in the project. Separate from <see cref="FindAll{T}"/> because a
    /// ScriptableObject that is not referenced by anything in the open scene is invisible to
    /// FindObjectsByType — and an unreferenced-but-wrong config is exactly what this should catch.
    /// </summary>
    private static IEnumerable<T> FindAllAssets<T>() where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) yield return asset;
        }
    }
}
#endif
