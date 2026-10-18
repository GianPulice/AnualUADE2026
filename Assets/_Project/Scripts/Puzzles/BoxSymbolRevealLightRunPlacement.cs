using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hangs the SP2 UV reveal lamp over one of the puzzle's boxes, a different one every run.
///
/// The lamp is built over a fixed spot of the lower floor. When the level loads this moves it, once:
/// it draws one box of the puzzle from the run's seed (<see cref="RunSeed.RandomFor"/>, keyed on the
/// puzzle id) and puts the lamp straight above where that box starts. That box is then the one whose
/// symbol the lamp gives away when the lights come on (<see cref="BoxSymbolSignalLoss"/> fades it in).
///
/// ONLY X AND Z MOVE. The lamp keeps the height it has in the scene, where it hangs from the ceiling,
/// so its beam comes down from the same place whichever box is drawn. The beam's axis is the lamp
/// root's vertical, so the pool lands centred on the box.
///
/// Nothing stores the draw: the same seed gives the same box, and checkpoints, captures and scene
/// reloads put the lamp in the same place without help. The boxes are sorted by id before the draw,
/// so the pick depends on the seed and the puzzle, not on the order the scene happens to list them
/// in. The lamp is a fixture: it does not follow the box once the player pushes it.
///
/// Done in Awake and not Start because the switch that powers the lamp deactivates it on its own
/// Start: a lamp that is off by then would never reach a Start of its own. Nothing on the lamp keeps
/// its position (the fog beacon and the fog volume read it every frame, the reveal reads the Light
/// on each query), so moving it this early is safe.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxSymbolRevealLight))]
public class BoxSymbolRevealLightRunPlacement : MonoBehaviour
{
    // Added to the puzzle id to make this draw's key. The container puzzle deals its baskets with the
    // bare id, so a key of its own keeps the lamp's box unrelated to which basket is which.
    private const string SeedKeySuffix = ".reveal_lamp";

    [Tooltip("The puzzle whose boxes the lamp can hang over: the push boxes (BallPuzzleItem) linked " +
             "to this id. It is also the key of the draw. Empty = every push box in the scene.")]
    [PuzzleId]
    [SerializeField] private string puzzleId = "sp2_contenedores";

    private void Awake()
    {
        List<BallPuzzleItem> boxes = FindBoxes();
        if (boxes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(BoxSymbolRevealLightRunPlacement)}] '{name}' found no box linked " +
                             $"to puzzle '{puzzleId}'. The lamp stays where the scene has it.", this);
            return;
        }

        System.Random random = RunSeed.RandomFor(puzzleId + SeedKeySuffix);
        BallPuzzleItem chosen = boxes[random.Next(boxes.Count)];

        // The lamp's own height, the box's X and Z.
        Vector3 position = transform.position;
        Vector3 boxPosition = chosen.transform.position;
        transform.position = new Vector3(boxPosition.x, position.y, boxPosition.z);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[{nameof(BoxSymbolRevealLightRunPlacement)}] {puzzleId}: this run's UV lamp " +
                  $"hangs over '{chosen.BallId}' ({chosen.name}).");
#endif
    }

    // The puzzle's boxes in a fixed order: by id, and by name if two share one.
    private List<BallPuzzleItem> FindBoxes()
    {
        var boxes = new List<BallPuzzleItem>();
        foreach (BallPuzzleItem box in FindObjectsByType<BallPuzzleItem>(FindObjectsInactive.Exclude))
        {
            if (!box.IsConfigured) continue;
            if (!string.IsNullOrEmpty(puzzleId) && box.LinkedPuzzleId != puzzleId) continue;
            boxes.Add(box);
        }

        boxes.Sort((a, b) =>
        {
            int byId = string.CompareOrdinal(a.BallId, b.BallId);
            return byId != 0 ? byId : string.CompareOrdinal(a.name, b.name);
        });
        return boxes;
    }
}
