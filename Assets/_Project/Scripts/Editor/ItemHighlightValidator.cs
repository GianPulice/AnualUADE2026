#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks that the proximity highlight actually reaches the screen on every interactable in the
/// open scene.
///
/// <b>Every failure this looks for is silent.</b> `ItemProximityHighlight` drives its two values
/// through a <see cref="MaterialPropertyBlock"/>, and writing a property the shader does not
/// declare is a no-op — no error, no warning, no visual difference. So an interactable with a plain
/// URP/Lit material looks correctly set up in the inspector, runs its lerp every time you look at
/// it, and does nothing at all. Same for a component pointed at a Renderer with no visible mesh, or
/// one whose near and far values are equal.
///
/// This is the counterpart to <see cref="NemesisSetupValidator"/> and reports the same way: one
/// warning with everything in it, so the whole scene can be fixed in one pass.
/// </summary>
public static class ItemHighlightValidator
{
    private static readonly int TintId = Shader.PropertyToID("_TintIntensity");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionIntensity");

    [MenuItem("Tools/Items/Validate Interactable Highlights")]
    private static void Validate()
    {
        StringBuilder report = new StringBuilder();
        int problems = 0;

        problems += ReportInteractablesWithoutHighlight(report);
        problems += ReportHighlightsThatCannotShow(report);

        if (problems == 0)
        {
            Debug.Log("[ItemHighlightValidator] All good: every interactable has a proximity " +
                      "highlight, and every highlight can actually reach its material.");
            return;
        }

        Debug.LogWarning($"[ItemHighlightValidator] {problems} problem(s):\n\n{report}\n" +
                         "Interactables are meant to answer the crosshair by lifting their tint " +
                         "and emission — see docs/Materials-System.md §5.");
    }

    /// <summary>
    /// Interactables the player can look at but that never respond.
    ///
    /// Searched down the hierarchy, not just on the object itself: the highlight belongs on
    /// whatever carries the Renderer, and on a prefab whose mesh is a child that is not the same
    /// GameObject as the IInteractable.
    /// </summary>
    private static int ReportInteractablesWithoutHighlight(StringBuilder report)
    {
        int problems = 0;

        foreach (MonoBehaviour behaviour in FindAll<MonoBehaviour>())
        {
            if (!(behaviour is IInteractable)) continue;

            // One entry per GameObject: several IInteractable components on the same object would
            // otherwise report the same missing highlight several times.
            if (behaviour.GetComponentInChildren<ItemProximityHighlight>(true) != null) continue;
            if (!IsFirstInteractableOn(behaviour)) continue;

            report.AppendLine(
                $"- '{Path(behaviour.transform)}' is an interactable ({behaviour.GetType().Name}) " +
                "with no ItemProximityHighlight anywhere under it, so it gives the player no " +
                "feedback when the crosshair finds it.");
            problems++;
        }

        return problems;
    }

    /// <summary>Highlights that run but cannot produce a visible change.</summary>
    private static int ReportHighlightsThatCannotShow(StringBuilder report)
    {
        int problems = 0;

        foreach (ItemProximityHighlight highlight in FindAll<ItemProximityHighlight>())
        {
            SerializedObject serialized = new SerializedObject(highlight);

            Renderer renderer = serialized.FindProperty("targetRenderer").objectReferenceValue as Renderer
                                ?? highlight.GetComponent<Renderer>();

            if (renderer == null)
            {
                report.AppendLine(
                    $"- '{Path(highlight.transform)}' has an ItemProximityHighlight but no Renderer " +
                    "on the same GameObject and nothing assigned to Target Renderer, so it has " +
                    "nothing to tint.");
                problems++;
                continue;
            }

            problems += ReportMissingShaderProperties(report, highlight, renderer);
            problems += ReportFlatValues(report, highlight, serialized);
        }

        return problems;
    }

    /// <summary>
    /// The important one. A material whose shader has no <c>_TintIntensity</c> /
    /// <c>_EmissionIntensity</c> swallows every write the component makes.
    ///
    /// Checked against sharedMaterials rather than materials so the inspector is not made to
    /// instantiate a material per renderer just to be validated — which would also break the SRP
    /// Batcher the property block exists to preserve.
    /// </summary>
    private static int ReportMissingShaderProperties(StringBuilder report,
                                                     ItemProximityHighlight highlight,
                                                     Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials == null || materials.Length == 0)
        {
            report.AppendLine($"- '{Path(highlight.transform)}': its Renderer has no material.");
            return 1;
        }

        foreach (Material material in materials)
        {
            if (material == null) continue;
            if (material.HasProperty(TintId) && material.HasProperty(EmissionId)) return 0;
        }

        report.AppendLine(
            $"- '{Path(highlight.transform)}' uses material '{Describe(materials)}', whose shader " +
            "declares no _TintIntensity / _EmissionIntensity. The MaterialPropertyBlock writes " +
            "into nothing and the highlight is invisible — with no error, which is why this is " +
            "easy to ship. Use ItemPSX_Outline (Materials/Items/) or a material based on it.");

        return 1;
    }

    /// <summary>Near and far set to the same number: the lerp runs and lands where it started.</summary>
    private static int ReportFlatValues(StringBuilder report,
                                        ItemProximityHighlight highlight,
                                        SerializedObject serialized)
    {
        float farTint = serialized.FindProperty("farTint").floatValue;
        float nearTint = serialized.FindProperty("nearTint").floatValue;
        float farEmission = serialized.FindProperty("farEmission").floatValue;
        float nearEmission = serialized.FindProperty("nearEmission").floatValue;

        if (!Mathf.Approximately(farTint, nearTint)) return 0;
        if (!Mathf.Approximately(farEmission, nearEmission)) return 0;

        report.AppendLine(
            $"- '{Path(highlight.transform)}' has Near and Far identical (tint {nearTint:0.##}, " +
            $"emission {nearEmission:0.##}), so looking at it changes nothing. Puzzle props are " +
            "allowed a tint of 0 on both, but the emission has to differ or there is no feedback.");

        return 1;
    }

    /// <summary>
    /// Whether this is the first IInteractable on its GameObject, in component order. Used to
    /// report a missing highlight once per object rather than once per interactable component.
    /// </summary>
    private static bool IsFirstInteractableOn(MonoBehaviour behaviour)
    {
        foreach (MonoBehaviour other in behaviour.GetComponents<MonoBehaviour>())
        {
            if (other is IInteractable) return ReferenceEquals(other, behaviour);
        }

        return true;
    }

    private static string Describe(IReadOnlyList<Material> materials)
    {
        for (int i = 0; i < materials.Count; i++)
        {
            if (materials[i] != null) return materials[i].name;
        }

        return "(none)";
    }

    /// <summary>Full hierarchy path, so the object is findable from the console line alone.</summary>
    private static string Path(Transform transform)
    {
        string path = transform.name;

        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
        {
            path = $"{parent.name}/{path}";
        }

        return path;
    }

    private static T[] FindAll<T>() where T : Object =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include);
}
#endif
