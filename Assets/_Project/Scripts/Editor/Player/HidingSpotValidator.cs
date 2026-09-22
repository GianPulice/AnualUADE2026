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
/// there, and cannot open it. Nothing errors in any of those cases — the monster just looks broken.
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
