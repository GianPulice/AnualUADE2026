using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// TEMP one-shot tool — delete after use (this file and its .meta).
//
// Moves every lamp a PoweredLightSwitch turns on from "Light Base" to "Light Base Switch" (the
// variant with the FogBeacon and the clear-only pool), then fits each lamp to what is around it:
//   - the pool (FogLightBypass sphere) is centred on whatever is right under the lamp, the catwalk,
//     so the fog clears where the spot actually lands;
//   - the beacon sits just under the lamp model next to it (CeilingLamp_Baked), where the tubes are,
//     so the model does not hide it.
// The per-instance Range override (4.89) is reverted so the variant's value applies.
//
// One undo step. It only marks the scene dirty: look at it, then save. Running it again re-fits the
// lamps that are already on the variant (after moving one, for example).
public static class TempLightBaseSwitchVariant
{
    private const string BasePath    = "Assets/_Project/Prefabs/Light/Light Base.prefab";
    private const string VariantPath = "Assets/_Project/Prefabs/Light/Light Base Switch.prefab";
    private const string FixturePath = "Assets/_Project/Prefabs/Environment/CeilingLamp_Baked.fbx";

    private const float FloorSearch   = 12f;   // how far down to look for the catwalk
    private const float FixtureSearch = 0.8f;  // horizontal radius to find the lamp model
    private const float BelowFixture  = 0.03f; // beacon this far under the model's lowest point

    [MenuItem("Tools/Temp/Light Base to Switch Variant")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[LightBaseSwitch] Exit Play Mode first: changes made in Play Mode are lost.");
            return;
        }

        GameObject baseRoot    = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        GameObject variantRoot = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (baseRoot == null || variantRoot == null || variantRoot.GetComponent<FogBeacon>() == null)
        {
            Debug.LogError($"[LightBaseSwitch] Missing '{BasePath}' or '{VariantPath}', or the variant " +
                           "has no FogBeacon. Nothing changed.");
            return;
        }

        PoweredLightSwitch[] switches = Object.FindObjectsByType<PoweredLightSwitch>(FindObjectsInactive.Include);
        var toReplace = new List<GameObject>();
        var lamps = new List<GameObject>();
        foreach (PoweredLightSwitch sw in switches)
        {
            foreach (GameObject go in SwitchObjects(sw))
            {
                if (go == null || lamps.Contains(go) || !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (source == baseRoot) toReplace.Add(go);
                else if (source != variantRoot) continue; // not a Light Base: leave it alone

                lamps.Add(go);
            }
        }

        if (lamps.Count == 0)
        {
            Debug.LogWarning("[LightBaseSwitch] No PoweredLightSwitch in the open scenes points at a " +
                             "Light Base. Nothing changed.");
            return;
        }

        Undo.SetCurrentGroupName("Light Base to Switch Variant");
        int undoGroup = Undo.GetCurrentGroup();
        int missingBefore = CountMissing(switches);

        if (toReplace.Count > 0)
        {
            var settings = new PrefabReplacingSettings
            {
                logInfo = false,
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                changeRootNameToAssetName = false,
            };
            PrefabUtility.ReplacePrefabAssetOfPrefabInstances(toReplace.ToArray(), variantRoot, settings,
                                                              InteractionMode.UserAction);
        }

        Physics.SyncTransforms();
        var report = new StringBuilder($"[LightBaseSwitch] {toReplace.Count} moved to the variant, " +
                                       $"{lamps.Count} fitted:\n");
        foreach (GameObject lamp in lamps)
        {
            // Replacing is meant to keep the instance roots. If one came back as a new object, the
            // lamps already on the variant are picked up again by a second run.
            if (lamp == null)
            {
                report.AppendLine("  !! A lamp was recreated by the replace: run the tool again to fit it.");
                continue;
            }
            Fit(lamp, report);
            EditorSceneManager.MarkSceneDirty(lamp.scene);
        }

        // The switches should still point at the same lamps. If they do not, a switch would toggle
        // nothing: say so loudly.
        int lost = CountMissing(switches) - missingBefore;
        if (lost > 0)
            report.AppendLine($"  !! The switches lost {lost} lamp reference(s). Undo (Ctrl+Z) and check.");

        Undo.CollapseUndoOperations(undoGroup);

        report.AppendLine("Restart the editor once so the 16-beacon cap reaches the shader: a global " +
                          "array keeps the length of its first upload.");
        Debug.Log(report.ToString());
    }

    private static void Fit(GameObject lamp, StringBuilder report)
    {
        Transform t = lamp.transform;
        var line = new StringBuilder($"  {lamp.name}:");

        // The variant's range, not the old per-instance 4.89 that ended right at the catwalk.
        if (lamp.TryGetComponent(out Light light))
        {
            SerializedProperty range = new SerializedObject(light).FindProperty("m_Range");
            if (range != null && range.prefabOverride)
                PrefabUtility.RevertPropertyOverride(range, InteractionMode.UserAction);
            line.Append($" range {light.range:0.##} m");
        }

        if (lamp.TryGetComponent(out FogLightBypass bypass))
        {
            if (Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, FloorSearch,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Undo.RecordObject(bypass, "Fit Light Base pool");
                bypass.centerOffset = t.InverseTransformPoint(hit.point);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bypass);
                line.Append($" | pool {hit.distance:0.00} m down, on '{hit.collider.name}'");
            }
            else
            {
                line.Append(" | pool: nothing below, kept the variant's offset");
            }
        }

        if (lamp.TryGetComponent(out FogBeacon beacon))
        {
            Renderer fixture = FindFixture(t.position);
            if (fixture != null)
            {
                Bounds b = fixture.bounds;
                Vector3 bulb = new Vector3(b.center.x, b.min.y - BelowFixture, b.center.z);
                Undo.RecordObject(beacon, "Fit Light Base beacon");
                beacon.centreOffset = t.InverseTransformPoint(bulb);
                PrefabUtility.RecordPrefabInstancePropertyModifications(beacon);
                line.Append($" | beacon under '{fixture.name}', {Vector3.Distance(t.position, bulb):0.00} m " +
                            "from the light");
            }
            else
            {
                line.Append(" | beacon: no CeilingLamp_Baked nearby, kept the variant's offset");
            }
        }

        report.AppendLine(line.ToString());
    }

    /// <summary>The CeilingLamp_Baked model hanging at this light, or null.</summary>
    private static Renderer FindFixture(Vector3 lampPosition)
    {
        Renderer best = null;
        float bestDistance = float.MaxValue;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject) != FixturePath) continue;

            Bounds b = r.bounds;
            var flat = new Vector2(b.center.x - lampPosition.x, b.center.z - lampPosition.z);
            if (flat.magnitude > FixtureSearch || Mathf.Abs(b.center.y - lampPosition.y) > 1.5f) continue;

            float distance = Vector3.Distance(b.center, lampPosition);
            if (distance < bestDistance)
            {
                best = r;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>The switch's private 'objects' array, read the way the inspector does.</summary>
    private static IEnumerable<GameObject> SwitchObjects(PoweredLightSwitch sw)
    {
        SerializedProperty objects = new SerializedObject(sw).FindProperty("objects");
        if (objects == null) yield break;
        for (int i = 0; i < objects.arraySize; i++)
            yield return objects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
    }

    private static int CountMissing(PoweredLightSwitch[] switches)
    {
        int missing = 0;
        foreach (PoweredLightSwitch sw in switches)
            foreach (GameObject go in SwitchObjects(sw))
                if (go == null) missing++;
        return missing;
    }
}
