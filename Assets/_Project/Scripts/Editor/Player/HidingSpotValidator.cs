#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Checks every <see cref="HidingSpot"/> in the open scenes against what the rest of the game
/// assumes about it (plan §14.4).
///
/// <b>Almost every failure here is silent in play.</b> A duplicate id merges two spots' histories
/// in the habit tracker; an approach point off the NavMesh means the Nemesis can never walk up to
/// check the spot; one further than its grab reach from the interior pose means it walks up, stands
/// there, and cannot open it. An exit pose out of its reach from the door, inside the prop or over
/// nothing breaks the pull-out (see ValidateExitPose). Nothing errors in any of those cases — the
/// monster just looks broken.
///
/// Reports the way <see cref="NemesisSetupValidator"/> and <see cref="ItemHighlightValidator"/> do:
/// one warning with everything in it, so a whole level can be fixed in one pass.
/// </summary>
public static class HidingSpotValidator
{
    // How far off the NavMesh an approach point may sit and still count as on it. Tight on purpose:
    // the Nemesis stops where the agent can stand, and a point 0.5 m away from that is a point it
    // cannot reach.
    private const float NavMeshTolerance = 0.3f;

    // Used when no SO_NemesisData asset can be found. The shipped value.
    private const float FallbackCatchReach = 1f;

    // The player's capsule (Player.prefab: radius 0.3, height 1.86), a little slimmer so a pose
    // authored flush against a wall is not a finding, and lifted off the floor it stands on.
    private const float PlayerRadius = 0.28f;
    private const float PlayerHeight = 1.86f;
    private const float FloorSkin = 0.05f;

    // What counts as solid for "the player would appear inside it": the same layers the Nemesis's
    // senses treat as solid (NemesisSetupValidator.OcclusionLayerNames). The spot's own solid
    // collider stays on Default by design (plan §14.4), so it is in here too.
    private static readonly string[] SolidLayerNames = { "Default", "Ground", "Wall", "Props" };

    [MenuItem("Tools/Player/Validate Hiding Spots")]
    private static void Validate()
    {
        HidingSpot[] spots = Object.FindObjectsByType<HidingSpot>(FindObjectsInactive.Include);
        if (spots.Length == 0)
        {
            Debug.Log("[HidingSpotValidator] No hiding spots in the open scenes.");
            return;
        }

        float catchReach = ResolveCatchReach();
        int interactableLayer = LayerMask.NameToLayer("Interactable");
        int solid = LayerMask.GetMask(SolidLayerNames);

        // Edit-mode physics only sees where things were at the last sync, and this project runs with
        // autoSyncTransforms off: without this a pose moved a moment ago is checked where it was.
        Physics.SyncTransforms();

        StringBuilder report = new StringBuilder();
        int problems = 0;

        Dictionary<string, HidingSpot> byId = new Dictionary<string, HidingSpot>();

        foreach (HidingSpot spot in spots)
        {
            SerializedObject so = new SerializedObject(spot);
            string where = Path(spot.transform);

            // ── Identity ──
            string id = so.FindProperty("spotId").stringValue;
            if (string.IsNullOrWhiteSpace(id))
            {
                report.AppendLine($"- '{where}' has no Spot Id. The habit tracker counts uses per id.");
                problems++;
            }
            else if (byId.TryGetValue(id.Trim(), out HidingSpot other))
            {
                report.AppendLine($"- '{where}' and '{Path(other.transform)}' share the Spot Id " +
                                  $"'{id}'. Their use counts would merge into one spot.");
                problems++;
            }
            else
            {
                byId.Add(id.Trim(), spot);
            }

            if (so.FindProperty("data").objectReferenceValue == null)
            {
                report.AppendLine($"- '{where}' has no SO_HidingData. It disables itself on Awake.");
                problems++;
            }

            // ── Poses ──
            Transform interior = so.FindProperty("interiorPose").objectReferenceValue as Transform;
            Transform approach = so.FindProperty("approachPoint").objectReferenceValue as Transform;

            if (interior == null)
            {
                report.AppendLine($"- '{where}' has no Interior Pose. The player would be put at the " +
                                  "spot's pivot, which is usually inside the floor or the mesh.");
                problems++;
            }

            if (approach == null)
            {
                report.AppendLine($"- '{where}' has no Approach Point. The Nemesis needs one to walk " +
                                  "up and open the spot (phase 2), and it is the fallback exit.");
                problems++;
            }
            else
            {
                if (!NavMesh.SamplePosition(approach.position, out _, NavMeshTolerance, NavMesh.AllAreas))
                {
                    report.AppendLine($"- '{where}': its Approach Point is not on the NavMesh (within " +
                                      $"{NavMeshTolerance} m). The Nemesis can never reach it. " +
                                      "(Bake the NavMesh first if this scene has none.)");
                    problems++;
                }

                if (interior != null)
                {
                    float gap = Vector3.Distance(approach.position, interior.position);
                    if (gap > catchReach)
                    {
                        report.AppendLine($"- '{where}': Approach Point is {gap:0.00} m from the " +
                                          $"Interior Pose, beyond the Nemesis's catch reach " +
                                          $"({catchReach:0.##} m). It would open the spot and be " +
                                          "unable to grab the player inside.");
                        problems++;
                    }
                }
            }

            // ── Exit pose (plan §14.4) ──
            Transform exit = so.FindProperty("exitPose").objectReferenceValue as Transform;
            if (exit != null) problems += ValidateExitPose(report, where, spot, exit, approach, catchReach, solid);

            // ── Camera ──
            if (so.FindProperty("interiorCamera").objectReferenceValue == null)
            {
                report.AppendLine($"- '{where}' has no Interior Camera. The view stays on the " +
                                  "third-person rig, which can see over and around the spot.");
                problems++;
            }

            // ── Crosshair ──
            if (interactableLayer >= 0 && !HasColliderOnLayer(spot, interactableLayer))
            {
                report.AppendLine($"- '{where}' has no collider on the Interactable layer, so the " +
                                  "crosshair can never pick it and E does nothing.");
                problems++;
            }
        }

        if (problems == 0)
        {
            Debug.Log($"[HidingSpotValidator] All good: {spots.Length} hiding spot(s) checked.");
            return;
        }

        Debug.LogWarning($"[HidingSpotValidator] {problems} problem(s) across {spots.Length} " +
                         $"spot(s):\n\n{report}\nSee docs/Plan-IA-Stalker.md §14.4 for the setup.");
    }

    /// <summary>
    /// The Exit Pose is where the player is put back down: leaving on their own, and pulled out by
    /// the Nemesis (plan §3.5, D16). Three ways it goes wrong, none of which errors in play.
    ///
    /// - <b>Out of reach from the door.</b> D16 rests on "bailing out while it opens the spot leaves
    ///   you at the Exit Pose, inches from the Nemesis, and it grabs you anyway". The Nemesis stands
    ///   at the Approach Point, up to the spot-check stopping distance off it, and grabs within its
    ///   catch reach. An Exit Pose further than that turns climbing out mid pull-out into a free
    ///   escape (NemesisCatchState sends it back to Chasing).
    /// - <b>Inside something.</b> The player's capsule would start overlapping the spot's own shell
    ///   or the wall behind it, and the physics throws it out, which is exactly case 19's "despedido
    ///   por la física".
    /// - <b>Over nothing.</b> No floor under it: the player drops out of the spot.
    ///
    /// Only an authored Exit Pose is checked. Without one the Approach Point is the exit, and that
    /// one is checked above.
    /// </summary>
    private static int ValidateExitPose(StringBuilder report, string where, HidingSpot spot, Transform exit,
                                        Transform approach, float catchReach, int solid)
    {
        int problems = 0;
        Vector3 pose = exit.position;

        if (approach != null)
        {
            Vector3 toDoor = approach.position - pose;
            toDoor.y = 0f;

            float reach = Mathf.Max(0f, catchReach - NemesisStateManager.SpotCheckStoppingDistance);
            if (toDoor.magnitude > reach)
            {
                report.AppendLine($"- '{where}': Exit Pose is {toDoor.magnitude:0.00} m from the Approach " +
                                  $"Point, beyond the {reach:0.##} m the Nemesis standing at the door can grab " +
                                  $"(catch reach {catchReach:0.##} minus its {NemesisStateManager.SpotCheckStoppingDistance} m " +
                                  "stopping distance). Climbing out while it opens the spot would be a free " +
                                  "escape (plan D16). Move the Exit Pose next to the Approach Point.");
                problems++;
            }
        }

        Vector3 bottom = pose + Vector3.up * (PlayerRadius + FloorSkin);
        Vector3 top = pose + Vector3.up * (PlayerHeight - PlayerRadius);
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, PlayerRadius, solid, QueryTriggerInteraction.Ignore);

        if (overlaps.Length > 0)
        {
            Collider first = overlaps[0];
            string own = first.transform.IsChildOf(spot.transform) ? " (the spot's own collider)" : "";
            report.AppendLine($"- '{where}': the player put down at its Exit Pose would start inside " +
                              $"'{first.name}'{own}. Physics would throw them out of it (plan §13, case 19). " +
                              "Move the Exit Pose out into the open.");
            problems++;
        }

        if (!Physics.Raycast(pose + Vector3.up * 0.5f, Vector3.down, 1.5f, solid, QueryTriggerInteraction.Ignore))
        {
            report.AppendLine($"- '{where}': there is no floor under its Exit Pose (nothing solid within 1 m " +
                              "below). The player would drop when leaving the spot.");
            problems++;
        }

        return problems;
    }

    private static bool HasColliderOnLayer(HidingSpot spot, int layer)
    {
        foreach (Collider c in spot.GetComponentsInChildren<Collider>(true))
            if (c.gameObject.layer == layer) return true;
        return false;
    }

    /// <summary>
    /// The real catch reach, read off the project's SO_NemesisData, so this check follows the
    /// tuning instead of a number copied here. Falls back to the shipped 1 m.
    /// </summary>
    private static float ResolveCatchReach()
    {
        string[] guids = AssetDatabase.FindAssets("t:SO_NemesisData");
        if (guids.Length == 0) return FallbackCatchReach;

        SO_NemesisData data = AssetDatabase.LoadAssetAtPath<SO_NemesisData>(
            AssetDatabase.GUIDToAssetPath(guids[0]));
        return data != null && data.CatchMaxReach > 0f ? data.CatchMaxReach : FallbackCatchReach;
    }

    private static string Path(Transform transform)
    {
        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = $"{parent.name}/{path}";
        return path;
    }
}
#endif
