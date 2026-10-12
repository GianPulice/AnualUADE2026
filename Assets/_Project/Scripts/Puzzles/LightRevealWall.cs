using UnityEngine;

/// <summary>
/// The arrow wall of the switch room (Central Puzzle 2 — SP2). Painted arrows that are invisible
/// until the player's module light reaches them. How they are revealed is the asset's Reveal Mode:
///  • Module Light (default): each arrow on its own, while it is within the module light's reach
///    (<see cref="PlayerModuleLight"/> × the asset's Light Reach Fraction × the degradation of
///    exploded modules) and nothing blocks the line between the light and it. Arrows near the
///    player appear first.
///  • Trigger Zone: all of them while the player stands in a <see cref="LightRevealZone"/>.
///
/// The arrows are this object's children painted with the reveal shader, in hierarchy order: arrow
/// 0 matches switch 0. Each one is pointed up or down from <see cref="SO_SwitchPuzzleData"/>
/// (rolled 180° on its local Z for down), so the answer only ever lives in the asset. Arrows past
/// the switch count are hidden.
///
/// They use the "WIRED/Puzzles/Light Reveal" shader, whose _LightReveal (0 = invisible,
/// 1 = fully visible) is faded here per arrow through a MaterialPropertyBlock.
/// </summary>
public class LightRevealWall : MonoBehaviour
{
    private static readonly int LightRevealId = Shader.PropertyToID("_LightReveal");
    private const string RevealShaderName = "WIRED/Puzzles/Light Reveal";

    // The line of sight aims a little in front of the paint, so the wall it is painted on never
    // counts as what blocks it.
    private const float SightStandOff = 0.15f;

    [SerializeField] private SO_SwitchPuzzleData puzzleData;

    private Renderer[] arrows = new Renderer[0];
    private float[] reveal = new float[0];
    private MaterialPropertyBlock block;
    private int litBy;

    public SO_SwitchPuzzleData PuzzleData => puzzleData;

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (puzzleData == null)
        {
            Debug.LogError($"[{nameof(LightRevealWall)}] No SO_SwitchPuzzleData on '{name}'.", this);
            return;
        }

        CollectArrows();
        for (int i = 0; i < arrows.Length; i++) Apply(i);
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
        reveal = new float[arrows.Length];

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

    /// <summary>Trigger Zone mode: a reveal zone tells the wall the player is (or no longer is) in it.</summary>
    public void SetLit(bool lit)
    {
        litBy = Mathf.Max(0, litBy + (lit ? 1 : -1));
    }

    private void Update()
    {
        if (puzzleData == null || arrows.Length == 0) return;

        bool byLight = puzzleData.Reveal == SO_SwitchPuzzleData.RevealMode.ModuleLight;
        Vector3 lightPosition = Vector3.zero;
        float reach = 0f;
        bool hasLight = byLight && TryGetLight(out lightPosition, out reach);
        float step = Time.deltaTime / puzzleData.RevealFadeSeconds;

        for (int i = 0; i < arrows.Length; i++)
        {
            Renderer arrow = arrows[i];
            if (arrow == null || !arrow.gameObject.activeInHierarchy) continue;

            float target;
            if (byLight) target = hasLight && IsLit(arrow.transform, lightPosition, reach) ? 1f : 0f;
            else target = litBy > 0 ? 1f : 0f;

            if (Mathf.Approximately(reveal[i], target)) continue;
            reveal[i] = Mathf.MoveTowards(reveal[i], target, step);
            Apply(i);
        }
    }

    private bool TryGetLight(out Vector3 position, out float reach)
    {
        reach = 0f;
        if (!PlayerModuleLight.TryGet(puzzleData.FallbackLightRange, out position, out float range)) return false;

        position += Vector3.up * puzzleData.LightHeight;
        reach = range * puzzleData.LightReachFraction * puzzleData.LightDegradationMultiplier();
        return reach > 0.01f;
    }

    private bool IsLit(Transform arrow, Vector3 lightPosition, float reach)
    {
        // The quad shows its face along -forward: aim at a point just off the paint, on that side.
        Vector3 target = arrow.position - arrow.forward * SightStandOff;
        if ((target - lightPosition).sqrMagnitude > reach * reach) return false;

        // Light from behind the wall does not reach the paint.
        if (Vector3.Dot(lightPosition - arrow.position, -arrow.forward) <= 0f) return false;

        return !Physics.Linecast(lightPosition, target, puzzleData.LightBlockers, QueryTriggerInteraction.Ignore);
    }

    private void Apply(int index)
    {
        Renderer arrow = arrows[index];
        if (arrow == null) return;
        arrow.GetPropertyBlock(block);
        block.SetFloat(LightRevealId, reveal[index]);
        arrow.SetPropertyBlock(block);
    }

    private void OnDrawGizmosSelected()
    {
        if (puzzleData == null) return;

        // The reach the arrows need with the fallback light and no degradation, from each arrow.
        // In play, the real reach follows the room's fog preset.
        float reach = puzzleData.FallbackLightRange * puzzleData.LightReachFraction;
        Gizmos.color = new Color(1f, 0.78f, 0.3f, 0.6f);
        foreach (Transform child in transform)
        {
            Renderer r = child.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null ||
                r.sharedMaterial.shader.name != RevealShaderName) continue;
            Gizmos.DrawWireSphere(child.position, reach);
        }
    }
}
