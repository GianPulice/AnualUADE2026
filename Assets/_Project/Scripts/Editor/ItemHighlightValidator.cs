#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks that the crosshair highlight actually reaches the screen on every interactable in the
/// open scene.
///
/// <b>Every failure this looks for is silent.</b> `ItemProximityHighlight` drives its values
/// through a <see cref="MaterialPropertyBlock"/>, and writing a property the shader does not
/// declare is a no-op — no error, no warning, no visual difference. So an interactable whose parts
/// use a shader with no highlight property looks correctly set up in the inspector, runs its lerp
/// every time you look at it, and does nothing at all. Same for a highlight with no profile, or a
/// profile whose near and far values are equal.
///
/// This is the counterpart to <see cref="NemesisSetupValidator"/> and reports the same way: one
/// warning with everything in it, so the whole scene can be fixed in one pass.
/// </summary>
public static class ItemHighlightValidator
{
    [MenuItem("Tools/Items/Validate Interactable Highlights")]
    private static void Validate()
    {
        StringBuilder report = new StringBuilder();
        int problems = 0;

        problems += ReportInteractablesWithoutHighlight(report);
        problems += ReportHighlightsThatCannotShow(report);

        if (problems == 0)
        {
            Debug.Log("[ItemHighlightValidator] All good: every interactable has a highlight, and " +
                      "every part of every highlighted interactable can show it.");
            return;
        }

        Debug.LogWarning($"[ItemHighlightValidator] {problems} problem(s):\n\n{report}\n" +
                         "Interactables are meant to answer the crosshair by lifting their tint " +
                         "and emission — see docs/Materials-System.md §5.");
    }

    /// <summary>
    /// Interactables the player can look at but that never respond.
    ///
    /// Searched down the hierarchy, not just on the object itself: the highlight normally sits on
    /// the root, but on the freight elevator's ride button it lives on the Visual child.
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

    /// <summary>Highlights that run but cannot produce a visible change, in whole or in part.</summary>
    private static int ReportHighlightsThatCannotShow(StringBuilder report)
    {
        int problems = 0;

        foreach (ItemProximityHighlight highlight in FindAll<ItemProximityHighlight>())
        {
            string where = Path(highlight.transform);
            SO_HighlightProfile profile = highlight.Profile;

            if (profile == null)
            {
                report.AppendLine($"- '{where}' has no SO_HighlightProfile, so it never lights up.");
                problems++;
            }
            else if (Mathf.Approximately(profile.FarTint, profile.NearTint) &&
                     Mathf.Approximately(profile.FarEmission, profile.NearEmission))
            {
                // The lerp runs and lands where it started.
                report.AppendLine($"- '{where}': profile '{profile.name}' has Near and Far identical, " +
                                  "so looking at it changes nothing.");
                problems++;
            }

            List<Renderer> renderers = ItemProximityHighlight.GatherRenderers(highlight.transform);
            if (renderers.Count == 0)
            {
                report.AppendLine($"- '{where}' has no Renderer under it, so it has nothing to light.");
                problems++;
                continue;
            }

            problems += ReportSlots(report, where, renderers);
        }

        return problems;
    }

    /// <summary>
    /// The important one. Checked against sharedMaterials so the inspector is not made to
    /// instantiate a material per renderer just to be validated — which would also break the SRP
    /// Batcher the property block exists to preserve.
    /// </summary>
    private static int ReportSlots(StringBuilder report, string where, List<Renderer> renderers)
    {
        int lit = 0;
        var keywordOff = new List<string>();
        var unsupported = new List<string>();

        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;

                switch (ItemProximityHighlight.GetSupport(material))
                {
                    case ItemProximityHighlight.SlotSupport.HighlightShader:
                    case ItemProximityHighlight.SlotSupport.EmissionOnly:
                        lit++;
                        break;
                    case ItemProximityHighlight.SlotSupport.EmissionKeywordOff:
                        AddOnce(keywordOff, material.name);
                        break;
                    default:
                        AddOnce(unsupported, $"{material.name} ({material.shader.name})");
                        break;
                }
            }
        }

        if (keywordOff.Count == 0 && unsupported.Count == 0) return 0;

        report.AppendLine(lit == 0
            ? $"- '{where}': NONE of its parts can show the highlight."
            : $"- '{where}': {lit} material slot(s) light up, but some parts stay dark.");
        if (keywordOff.Count > 0)
            report.AppendLine("    Emission switched off (URP compiles it out; Tools > Interactables > " +
                              "Set Up Highlights turns it on): " + string.Join(", ", keywordOff));
        if (unsupported.Count > 0)
            report.AppendLine("    Shader has no highlight property at all: " + string.Join(", ", unsupported));

        return 1;
    }

    private static void AddOnce(List<string> list, string entry)
    {
        if (!list.Contains(entry)) list.Add(entry);
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
