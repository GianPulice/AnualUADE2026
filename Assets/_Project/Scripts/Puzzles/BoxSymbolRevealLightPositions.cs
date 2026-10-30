using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the SP2 UV reveal lamp shines: one lamp over each of the puzzle's boxes, and only one of
/// them lit at a time. <see cref="RevealLampTablet"/> moves the light from one to the next.
///
/// The prefab is built with one lamp (<see cref="lamp"/>: the Light, its <see cref="BoxSymbolRevealLight"/>,
/// the fog pieces) and the ceiling fixture. When the level loads this, once:
///   1. draws one box from the run's seed (<see cref="RunSeed.RandomFor"/>, keyed on the puzzle id)
///      and moves the whole lamp, fixture included, straight above where that box starts. That box
///      is the one lit first, so the first lamp is still a different one every run;
///   2. copies the lamp, without the fixture, over every other box. Through the fog nobody sees the
///      ceiling at that height, so the copies are only the light and what goes with it. They start off.
///
/// ONLY X AND Z MOVE. Every lamp keeps the height the prefab has in the scene, so the hand-fitted
/// fog pool (the preset, the bypass and the trigger, all local to the lamp) fits under each of them
/// without re-fitting. The lamps are fixtures: they do not follow a box once the player pushes it.
///
/// The draw is not stored, and neither is where the tablet left the light: a scene reload, a
/// checkpoint or a capture puts the light back over the drawn box. The boxes are sorted by id before
/// the draw, so the pick depends on the seed and the puzzle, not on the order the scene lists them in
/// (and the same seed draws the same box the single lamp of earlier versions hung over).
///
/// Power is not this component's job. The <see cref="PoweredLightSwitch"/> levers turn the whole
/// prefab on and off; inside it, this keeps every lamp but the lit one deactivated.
///
/// Done in Awake and not Start because the switch that powers the lamps deactivates them on its own
/// Start: a lamp that is off by then would never reach a Start of its own.
/// </summary>
[DisallowMultipleComponent]
public class BoxSymbolRevealLightPositions : MonoBehaviour
{
    // Added to the puzzle id to make this draw's key. The container puzzle deals its baskets with the
    // bare id, so a key of its own keeps the lamp's box unrelated to which basket is which.
    private const string SeedKeySuffix = ".reveal_lamp";

    [Tooltip("The puzzle whose boxes get a lamp: the push boxes (BallPuzzleItem) linked to this id. " +
             "It is also the key of the draw. Empty = every push box in the scene.")]
    [PuzzleId]
    [SerializeField] private string puzzleId = "sp2_contenedores";

    [Tooltip("The lamp that is copied over each box: the Light with its BoxSymbolRevealLight and the " +
             "fog pieces, without the fixture model. It becomes the lamp of the drawn box.")]
    [SerializeField] private GameObject lamp;

    private readonly List<GameObject> lamps = new List<GameObject>();
    private bool built;
    private int selected;
    private bool lit;

    /// <summary>How many places the light can be in: one per box.</summary>
    public int Count
    {
        get
        {
            EnsureBuilt();
            return lamps.Count;
        }
    }

    /// <summary>The place the light is in, or is on its way to while <see cref="IsLit"/> is false.</summary>
    public int Selected
    {
        get
        {
            EnsureBuilt();
            return selected;
        }
    }

    /// <summary>The selected lamp is on (it still needs the levers' power to actually shine).</summary>
    public bool IsLit
    {
        get
        {
            EnsureBuilt();
            return lit;
        }
    }

    /// <summary>The levers have the lamps switched on.</summary>
    public bool IsPowered => gameObject.activeInHierarchy;

    /// <summary>World position of the lamp at <paramref name="index"/>, where its beam comes from.</summary>
    public Vector3 PositionOf(int index)
    {
        EnsureBuilt();
        return lamps[index].transform.position;
    }

    /// <summary>Switches the lit lamp off. <see cref="Selected"/> keeps pointing at it.</summary>
    public void TurnOff()
    {
        EnsureBuilt();
        lit = false;
        ApplyLit();
    }

    /// <summary>Lights the lamp at <paramref name="index"/> and only that one.</summary>
    public void TurnOn(int index)
    {
        EnsureBuilt();
        if (index < 0 || index >= lamps.Count) return;

        selected = index;
        lit = true;
        ApplyLit();
    }

    private void Awake() => EnsureBuilt();

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        if (lamp == null)
        {
            Debug.LogWarning($"[{nameof(BoxSymbolRevealLightPositions)}] '{name}' has no lamp assigned. " +
                             "Nothing is lit.", this);
            return;
        }

        // Off before copying, so no copy turns on (and registers its fog pieces) on its way in.
        lamp.SetActive(false);

        List<BallPuzzleItem> boxes = FindBoxes();
        if (boxes.Count == 0)
        {
            Debug.LogWarning($"[{nameof(BoxSymbolRevealLightPositions)}] '{name}' found no box linked " +
                             $"to puzzle '{puzzleId}'. The lamp stays where the scene has it.", this);
            lamps.Add(lamp);
            TurnOn(0);
            return;
        }

        System.Random random = RunSeed.RandomFor(puzzleId + SeedKeySuffix);
        int drawn = random.Next(boxes.Count);

        // The prefab's own height, the drawn box's X and Z: the fixture hangs over the first light.
        Vector3 rootPosition = transform.position;
        Vector3 drawnBox = boxes[drawn].transform.position;
        transform.position = new Vector3(drawnBox.x, rootPosition.y, drawnBox.z);

        float lampHeight = lamp.transform.position.y;
        for (int i = 0; i < boxes.Count; i++)
        {
            GameObject boxLamp = i == drawn ? lamp : Instantiate(lamp, lamp.transform.parent);
            Vector3 boxPosition = boxes[i].transform.position;
            boxLamp.transform.position = new Vector3(boxPosition.x, lampHeight, boxPosition.z);
            if (i != drawn) boxLamp.name = $"{lamp.name} ({boxes[i].BallId})";

            lamps.Add(boxLamp);
        }

        TurnOn(drawn);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[{nameof(BoxSymbolRevealLightPositions)}] {puzzleId}: this run's UV lamp starts " +
                  $"over '{boxes[drawn].BallId}' ({boxes[drawn].name}); {boxes.Count} positions in all.");
#endif
    }

    private void ApplyLit()
    {
        for (int i = 0; i < lamps.Count; i++)
            lamps[i].SetActive(lit && i == selected);
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
