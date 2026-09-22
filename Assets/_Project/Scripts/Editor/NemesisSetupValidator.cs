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

    [MenuItem("Tools/Nemesis/Validate Navigation Setup")]
    private static void Validate()
    {
        StringBuilder report = new StringBuilder();
        int problems = 0;

        problems += ValidateSurfaces(report);
        problems += ValidateModifierVolumes(report);
        problems += ValidateModifiers(report);
        problems += ValidateNoiseLayer(report);
        problems += ValidateSensors(report);
        problems += ValidateCameraAndInteraction(report);
        problems += ValidateWaypoints(report);
        problems += ValidateDoorUsers(report);
        problems += ValidateDirector(report);

        if (problems == 0)
        {
            // The notes (sweep points, zone coverage) are still worth reading when nothing is wrong.
            Debug.Log("[NemesisSetupValidator] All good: NavMeshSurface, modifiers and modifier " +
                      "volumes, the noise layer, sensors, camera, interaction, waypoints, doors and " +
                      "the Director are set up correctly." + (report.Length > 0 ? $"\n\n{report}" : ""));
            return;
        }

        Debug.LogWarning($"[NemesisSetupValidator] {problems} problem(s):\n\n{report}\n" +
                         "Fix the masks and layers listed above, then rebake the NavMesh.");
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
        // Player: a body, not geometry. Baking the player's capsule into the NavMesh would carve a
        // hole wherever the scene happens to have them standing.
        int ignored = BuildMask(new[] { "Default", "Interactable", "Player" });
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
                          "them. Move them to Props/Ground or add their layer to Include Layers.");
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
    /// A per-object NavMeshModifier is filtered exactly like a volume: NavMeshSurface.CollectSources
    /// SKIPS every modifier whose own GameObject is not on one of its Include Layers — and with it
    /// everything it was meant to do to its children.
    ///
    /// This is the one that closed every doorway in Zona1 (WIR-028). The Door prefab carried its
    /// "ignore from build" modifier on the root, and the root sits on Interactable for the
    /// crosshair. Interactable is not baked, so the modifier was dropped, and the leaf (on Default)
    /// was baked as a wall. It went unnoticed for as long as Default itself was not baked either.
    /// </summary>
    private static int ValidateModifiers(StringBuilder report)
    {
        NavMeshSurface[] surfaces = FindAll<NavMeshSurface>();
        if (surfaces.Length == 0) return 0;

        int problems = 0;
        foreach (NavMeshModifier modifier in FindAll<NavMeshModifier>())
        {
            int layerBit = 1 << modifier.gameObject.layer;
            bool reached = false;
            foreach (NavMeshSurface surface in surfaces)
            {
                if ((surface.layerMask.value & layerBit) != 0 && modifier.AffectsAgentType(surface.agentTypeID))
                {
                    reached = true;
                    break;
                }
            }
            if (reached) continue;

            report.AppendLine(
                $"- NavMeshModifier on '{modifier.gameObject.name}' does nothing: it sits on layer " +
                $"'{LayerMask.LayerToName(modifier.gameObject.layer)}', which no NavMeshSurface bakes, and " +
                "the surface drops such modifiers in silence — including what they were meant to do " +
                "to their children. Move the modifier onto a child that IS on a baked layer (the door " +
                "prefab keeps it on its Hinge), then rebake.");
            problems++;
        }

        return problems;
    }

    /// <summary>
    /// Anything SOLID on a layer the Nemesis listens to. FieldOfListening ignores those now, but
    /// the object is still misplaced: that layer is not baked into the NavMesh and not in the vision
    /// obstacle mask, so it is invisible to both. Zona1's Stair_Divider sat on DetectableAudio and
    /// was a permanent noise source in the stairwell, a wall the monster could see through, and a
    /// gap in the NavMesh (WIR-018 / WIR-020 / WIR-028).
    /// </summary>
    private static int ValidateNoiseLayer(StringBuilder report)
    {
        int listen = 0;
        foreach (FieldOfListening ears in FindAll<FieldOfListening>()) listen |= ears.ListenMask.value;
        if (listen == 0) return 0;

        int problems = 0;
        foreach (Collider c in FindAll<Collider>())
        {
            if (c.isTrigger || ((1 << c.gameObject.layer) & listen) == 0) continue;
            report.AppendLine(
                $"- '{c.gameObject.name}' is a SOLID {c.GetType().Name} on '{LayerMask.LayerToName(c.gameObject.layer)}', " +
                "a layer the Nemesis listens to. Noise sources are triggers; level geometry belongs on " +
                "Wall/Props/Default, or it is left out of the NavMesh and out of the monster's sight.");
            problems++;
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

    /// <summary>Plan §14.5: the Director, its zones and its triggers. Coverage is reported as a note.</summary>
    private static int ValidateDirector(StringBuilder report)
    {
        NemesisPressureZone[] zones = FindAll<NemesisPressureZone>();
        NemesisDirector[] directors = FindAll<NemesisDirector>();

        if (zones.Length == 0 && directors.Length == 0) return 0;

        int problems = 0;

        NemesisDirector director = directors.Length > 0 ? directors[0] : null;
        SerializedProperty triggers = director != null
            ? new SerializedObject(director).FindProperty("puzzleTriggers")
            : null;
        int triggerCount = triggers != null ? triggers.arraySize : 0;

        if (director == null || !director.isActiveAndEnabled)
        {
            if (zones.Length > 0 || triggerCount > 0)
            {
                report.AppendLine($"- The Director is {(director == null ? "missing" : "switched off")} but the " +
                                  $"scene has {zones.Length} pressure zone(s) and {triggerCount} trigger(s). " +
                                  "None of them will ever act.");
                problems++;
            }
        }

        problems += ValidateSafeZoneMarkers(report, director != null);
        problems += ValidateZones(report, zones);

        if (triggers != null) problems += ValidateTriggers(report, triggers, zones);

        if (director != null && new SerializedObject(director).FindProperty("pacing").objectReferenceValue == null)
        {
            report.AppendLine("- Note: the Director has no SO_DirectorPacing, so pacing (tension, Relax " +
                              "retreat, rising sensitivity) is off.");
        }

        ReportCoverage(report, zones);
        return problems;
    }

    /// <summary>The C5 guard only knows the Hub through SafeZoneMarker: none means it is off, in silence.</summary>
    private static int ValidateSafeZoneMarkers(StringBuilder report, bool hasDirector)
    {
        const int notWalkableArea = 1;
        int problems = 0;

        SafeZoneMarker[] markers = FindAll<SafeZoneMarker>();

        if (markers.Length == 0 && hasDirector)
        {
            report.AppendLine("- No SafeZoneMarker in the scene: the Director cannot tell where the Hub is, so " +
                              "nothing keeps its levers away from the Hub's door (C5). Add one next to the Hub's " +
                              "Not Walkable NavMeshModifierVolume.");
            problems++;
        }

        foreach (SafeZoneMarker marker in markers)
        {
            NavMeshModifierVolume volume = marker.GetComponent<NavMeshModifierVolume>();
            if (volume != null && volume.area == notWalkableArea) continue;

            report.AppendLine($"- SafeZoneMarker on '{marker.name}' is not on a Not Walkable NavMeshModifierVolume: " +
                              "the Nemesis can walk in there, so it is not a refuge and is ignored.");
            problems++;
        }

        return problems;
    }

    private static int ValidateZones(StringBuilder report, NemesisPressureZone[] zones)
    {
        int problems = 0;
        HashSet<string> seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (NemesisPressureZone zone in zones)
        {
            if (string.IsNullOrWhiteSpace(zone.ZoneId))
            {
                report.AppendLine($"- Pressure zone '{zone.name}' has no Zone Id: it never registers.");
                problems++;
                continue;
            }

            if (!seen.Add(zone.ZoneId))
            {
                report.AppendLine($"- Two pressure zones are called '{zone.ZoneId}'. Find() returns whichever " +
                                  "registered first, so one of them can never be pressured.");
                problems++;
            }

            if (!NemesisSafeZones.IsCentreClear(zone.Center))
            {
                report.AppendLine($"- Pressure zone '{zone.ZoneId}': its centre is " +
                                  $"{NemesisSafeZones.FlatDistance(zone.Center):0.0} m from the Hub (minimum " +
                                  $"{NemesisSafeZones.Clearance:0} m). Its anchor would park the Nemesis at the " +
                                  "Hub's door (cheese C5), so the Director refuses it. Move the centre away.");
                problems++;
            }

            if (CountWaypointsIn(zone) == 0)
            {
                report.AppendLine($"- Pressure zone '{zone.ZoneId}' contains no route waypoint: the route " +
                                  "weight lever does nothing there. Move it or grow its radius.");
                problems++;
            }
        }

        return problems;
    }

    private static int ValidateTriggers(StringBuilder report, SerializedProperty triggers,
                                        NemesisPressureZone[] zones)
    {
        int problems = 0;

        string wakePuzzle = null;
        foreach (NemesisController controller in FindAll<NemesisController>())
        {
            if (!string.IsNullOrWhiteSpace(controller.ActivatedByPuzzleId)) wakePuzzle = controller.ActivatedByPuzzleId;
        }

        for (int i = 0; i < triggers.arraySize; i++)
        {
            SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
            string puzzleId = trigger.FindPropertyRelative("puzzleId").stringValue;
            string zoneId = trigger.FindPropertyRelative("zoneId").stringValue;
            bool entrance = trigger.FindPropertyRelative("stageEntrance").boolValue;
            string name = string.IsNullOrWhiteSpace(puzzleId) ? $"#{i}" : $"'{puzzleId}'";

            if (string.IsNullOrWhiteSpace(puzzleId))
            {
                report.AppendLine($"- Director trigger {name} has no puzzle id: it never fires.");
                problems++;
            }

            if (string.IsNullOrWhiteSpace(zoneId) && !entrance)
            {
                report.AppendLine($"- Director trigger {name} has no zone and no entrance: it does nothing.");
                problems++;
            }

            if (!string.IsNullOrWhiteSpace(zoneId) && !HasZone(zones, zoneId))
            {
                report.AppendLine($"- Director trigger {name} asks for zone '{zoneId}', which is not in the scene.");
                problems++;
            }

            if (entrance && wakePuzzle != null &&
                string.Equals(puzzleId, wakePuzzle, System.StringComparison.OrdinalIgnoreCase))
            {
                report.AppendLine($"- Director trigger {name} stages an entrance on the puzzle that WAKES the " +
                                  "Nemesis: it is still dormant then, so the entrance is skipped (plan §14.2).");
                problems++;
            }
        }

        return problems;
    }

    private static bool HasZone(NemesisPressureZone[] zones, string zoneId)
    {
        foreach (NemesisPressureZone zone in zones)
        {
            if (string.Equals(zone.ZoneId, zoneId, System.StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static int CountWaypointsIn(NemesisPressureZone zone)
    {
        int count = 0;
        foreach (Transform waypoint in AllWaypoints())
        {
            if (zone.Contains(waypoint.position)) count++;
        }

        return count;
    }

    private static void ReportCoverage(StringBuilder report, NemesisPressureZone[] zones)
    {
        List<string> uncovered = new List<string>();
        int total = 0;

        foreach (Transform waypoint in AllWaypoints())
        {
            total++;

            bool inAny = false;
            foreach (NemesisPressureZone zone in zones)
            {
                if (zone.Contains(waypoint.position))
                {
                    inAny = true;
                    break;
                }
            }

            if (!inAny) uncovered.Add($"{waypoint.parent.name}/{waypoint.name}");
        }

        if (total == 0) return;

        string list = uncovered.Count == 0
            ? ""
            : $" Outside every zone: {string.Join(", ", uncovered.GetRange(0, Mathf.Min(8, uncovered.Count)))}" +
              (uncovered.Count > 8 ? $" (+{uncovered.Count - 8})" : "") + ".";

        report.AppendLine($"- Note: pressure zones cover {total - uncovered.Count}/{total} waypoints.{list} " +
                          "Select the Director to see them in the Scene view.");
    }

    private static IEnumerable<Transform> AllWaypoints()
    {
        foreach (NemesisRoute route in FindAll<NemesisRoute>())
        {
            for (int i = 0; i < route.transform.childCount; i++)
            {
                Transform child = route.transform.GetChild(i);
                if (child.CompareTag(NemesisRoute.WaypointTag)) yield return child;
            }
        }
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
