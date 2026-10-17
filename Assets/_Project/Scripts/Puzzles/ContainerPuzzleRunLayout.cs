using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Repaints the baskets of the container puzzle so each one shows the symbol of the box that belongs
/// in it this run. The baskets never move; only which is which changes, as dealt by
/// <see cref="SO_ContainerPuzzleData"/>'s Shuffle Each Run.
///
/// Every basket is built showing the symbol of the box the puzzle's written table puts in it
/// (Canasto_A shows A's). This only moves those symbols between the baskets: it reads them all
/// first, then gives each basket the one of the box the run's table sends to it. With the shuffle
/// off the run's table is the written one and nothing changes.
///
/// Sits next to the <see cref="ContainerPuzzleController"/> and takes its puzzle from it, so the
/// prefab needs no wiring. Done once per level load: the run's table never changes mid-run.
/// </summary>
[RequireComponent(typeof(ContainerPuzzleController))]
public class ContainerPuzzleRunLayout : MonoBehaviour
{
    [Tooltip("Name of the child of each basket (the BasketTrigger's parent) that carries its symbol.")]
    [SerializeField] private string symbolChildName = "Plane";

    private void Start()
    {
        SO_ContainerPuzzleData data = GetComponent<ContainerPuzzleController>().PuzzleData;
        if (data == null || data.Requirements == null || data.AuthoredRequirements == null) return;

        // Every basket's symbol renderer and the materials it was built with, by basket id.
        var symbolRenderers = new Dictionary<string, Renderer>();
        var builtSymbols = new Dictionary<string, Material[]>();
        foreach (BasketTrigger basket in FindObjectsByType<BasketTrigger>(FindObjectsInactive.Include))
        {
            if (basket.LinkedPuzzleId != data.PuzzleId) continue;

            Transform root = basket.transform.parent != null ? basket.transform.parent : basket.transform;
            Transform symbol = root.Find(symbolChildName);
            Renderer symbolRenderer = symbol != null ? symbol.GetComponent<Renderer>() : null;
            if (symbolRenderer == null)
            {
                Debug.LogWarning($"[{nameof(ContainerPuzzleRunLayout)}] Basket '{basket.BasketId}' has no " +
                                 $"'{symbolChildName}' child with a Renderer: its symbol cannot follow the run.", basket);
                continue;
            }

            symbolRenderers[basket.BasketId] = symbolRenderer;
            builtSymbols[basket.BasketId] = symbolRenderer.sharedMaterials;
        }

        // The symbol of each box: the one on the basket the written table puts it in.
        var boxSymbols = new Dictionary<string, Material[]>();
        foreach (SO_ContainerPuzzleData.ContainerRequirement written in data.AuthoredRequirements)
            if (builtSymbols.TryGetValue(written.requiredSlotId, out Material[] materials))
                boxSymbols[written.containerId] = materials;

        foreach (SO_ContainerPuzzleData.ContainerRequirement run in data.Requirements)
            if (symbolRenderers.TryGetValue(run.requiredSlotId, out Renderer target) &&
                boxSymbols.TryGetValue(run.containerId, out Material[] materials))
                target.sharedMaterials = materials;
    }
}
