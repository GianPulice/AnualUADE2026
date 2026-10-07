using UnityEngine;

/// <summary>
/// The arrow wall of the switch room (Central Puzzle 2 — SP2). Painted arrows that are invisible
/// until the player's module light reaches the wall — in practice, until the player stands in the
/// <see cref="LightRevealZone"/> in front of it, the spec's recommended reading of "the light
/// reaches it".
///
/// The arrows are this object's children painted with the reveal shader, in hierarchy order: arrow 0 matches
/// switch 0. Each one is pointed up or down from <see cref="SO_SwitchPuzzleData"/> (rolled 180°
/// on its local Z for down), so the answer only ever lives in the asset. Arrows past the switch
/// count are hidden.
///
/// They use the "WIRED/Puzzles/Light Reveal" shader, whose _LightReveal (0 = invisible,
/// 1 = fully visible) is faded here through a MaterialPropertyBlock.
/// </summary>
public class LightRevealWall : MonoBehaviour
{
    private static readonly int LightRevealId = Shader.PropertyToID("_LightReveal");
    private const string RevealShaderName = "WIRED/Puzzles/Light Reveal";

    [SerializeField] private SO_SwitchPuzzleData puzzleData;

    private Renderer[] arrows = new Renderer[0];
    private MaterialPropertyBlock block;
    private float reveal;
    private int litBy;

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (puzzleData == null)
        {
            Debug.LogError($"[{nameof(LightRevealWall)}] No SO_SwitchPuzzleData on '{name}'.", this);
            return;
        }

        CollectArrows();
        ApplyReveal();
    }

    private void CollectArrows()
    {
        // Only the children painted with the reveal shader are arrows: the wall itself, a frame or
        // any other prop parented here keeps its own look.
        var found = new System.Collections.Generic.List<Renderer>();
        foreach (Transform child in transform)
        {
            Renderer r = child.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null &&
                r.sharedMaterial.shader.name == RevealShaderName)
                found.Add(r);
        }
        arrows = found.ToArray();

        for (int i = 0; i < arrows.Length; i++)
        {
            bool used = i < puzzleData.SwitchCount;
            arrows[i].gameObject.SetActive(used);
            if (!used) continue;

            Vector3 euler = arrows[i].transform.localEulerAngles;
            euler.z = puzzleData.IsCorrectUp(i) ? 0f : 180f;
            arrows[i].transform.localEulerAngles = euler;
        }
    }

    /// <summary>A reveal zone tells the wall the player's light is (or is no longer) on it.</summary>
    public void SetLit(bool lit)
    {
        litBy = Mathf.Max(0, litBy + (lit ? 1 : -1));
    }

    private void Update()
    {
        if (puzzleData == null) return;

        float target = litBy > 0 ? 1f : 0f;
        if (Mathf.Approximately(reveal, target)) return;

        reveal = Mathf.MoveTowards(reveal, target, Time.deltaTime / puzzleData.RevealFadeSeconds);
        ApplyReveal();
    }

    private void ApplyReveal()
    {
        foreach (Renderer arrow in arrows)
        {
            if (arrow == null) continue;
            arrow.GetPropertyBlock(block);
            block.SetFloat(LightRevealId, reveal);
            arrow.SetPropertyBlock(block);
        }
    }
}
