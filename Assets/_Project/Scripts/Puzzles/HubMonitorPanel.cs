using UnityEngine;

/// <summary>
/// The monitoring panel of the Ventilation Hub (Central Puzzle 2, spec §4): one indicator per
/// sub-puzzle of its <see cref="SO_StabilizationPuzzleData"/>, red while pending and green once
/// solved, readable from anywhere in the room without interacting. While the stabilization checks
/// run the indicators blink; once the hub puzzle is completed they all show the stabilized colour.
///
/// The indicators are this object's children with a Light, in hierarchy order (indicator 0 =
/// RequiredPuzzleIds[0]); a Renderer on the same child gets the colour too, as emission. State is
/// read live from PuzzleStateManager every frame, so a checkpoint rollback is followed for free.
/// </summary>
public class HubMonitorPanel : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private SO_StabilizationPuzzleData puzzleData;

    [Tooltip("The panel the checks are started from. Empty = the one in the parents or children " +
             "of this object; only used to blink while its checks run.")]
    [SerializeField] private StabilizationPanelInteractable stabilizationPanel;

    private Light[] lights = new Light[0];
    private Renderer[] renderers = new Renderer[0];
    private Color[] shown = new Color[0];
    private bool[] wasResolved = new bool[0];
    private MaterialPropertyBlock block;
    private bool firstFrame = true;

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (puzzleData == null)
        {
            Debug.LogError($"[{nameof(HubMonitorPanel)}] No SO_StabilizationPuzzleData on '{name}'.", this);
            return;
        }

        if (stabilizationPanel == null) stabilizationPanel = GetComponentInChildren<StabilizationPanelInteractable>();
        if (stabilizationPanel == null) stabilizationPanel = GetComponentInParent<StabilizationPanelInteractable>();

        var found = new System.Collections.Generic.List<Light>();
        foreach (Transform child in transform)
        {
            Light l = child.GetComponent<Light>();
            if (l != null) found.Add(l);
        }
        lights = found.ToArray();

        int count = puzzleData.RequiredPuzzleIds.Length;
        if (lights.Length < count)
            Debug.LogWarning($"[{nameof(HubMonitorPanel)}] '{name}' has {lights.Length} indicators for " +
                             $"{count} sub-puzzles.", this);

        renderers = new Renderer[lights.Length];
        shown = new Color[lights.Length];
        wasResolved = new bool[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            renderers[i] = lights[i].GetComponent<Renderer>();
            lights[i].gameObject.SetActive(i < count);
        }
    }

    private void Update()
    {
        if (puzzleData == null || lights.Length == 0) return;

        string[] ids = puzzleData.RequiredPuzzleIds;
        bool stabilized = IsCompleted(puzzleData.PuzzleId);
        bool running = stabilizationPanel != null && stabilizationPanel.IsRunning;
        bool blinkOff = running && Mathf.Repeat(Time.unscaledTime * puzzleData.RunningBlinkRate, 1f) > 0.5f;
        float step = firstFrame ? 1f : Time.deltaTime / puzzleData.IndicatorFadeSeconds;

        for (int i = 0; i < lights.Length && i < ids.Length; i++)
        {
            bool resolved = IsCompleted(ids[i]);
            Color target = stabilized ? puzzleData.StabilizedColor
                         : resolved ? puzzleData.ResolvedColor
                         : puzzleData.PendingColor;

            // A soft confirmation the moment an indicator turns green (not on load).
            if (resolved && !wasResolved[i] && !firstFrame && AudioManager.Exists &&
                !string.IsNullOrEmpty(puzzleData.IndicatorResolvedSoundId))
                AudioManager.Instance.PlaySFX(puzzleData.IndicatorResolvedSoundId, lights[i].transform.position);
            wasResolved[i] = resolved;

            shown[i] = firstFrame ? target : Color.Lerp(shown[i], target, Mathf.Clamp01(step * 4f));
            Apply(i, blinkOff ? Color.black : shown[i]);
        }

        firstFrame = false;
    }

    private void Apply(int index, Color color)
    {
        lights[index].color = color;
        lights[index].enabled = color.maxColorComponent > 0.01f;

        Renderer r = renderers[index];
        if (r == null) return;
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        block.SetColor(EmissionColorId, color * 2f);
        r.SetPropertyBlock(block);
    }

    private static bool IsCompleted(string id) =>
        !string.IsNullOrWhiteSpace(id) && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(id);
}
