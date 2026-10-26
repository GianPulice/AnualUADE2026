using System.Collections;
using UnityEngine;

/// <summary>
/// One up/down switch of the switch room (Central Puzzle 2 — SP2). [E] flips it. Its position is
/// owned by <see cref="SwitchPanelController"/> (stored in PuzzleStateManager); this only shows it.
///
/// The moving part is the child called "Handle", turned on its local X from the pose it was
/// modelled in: Up Angle when up, Down Angle when down, Middle Angle while it has never been
/// touched (see <see cref="SwitchPanelController.Middle"/>). Eased the same way as the boiler
/// levers, except a switch stays where it lands until the next [E].
/// Without a Handle the switch still works, it just does not move.
/// </summary>
public class SwitchInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [Tooltip("Degrees the Handle turns on its local X, from its modelled pose, while the switch is up.")]
    [SerializeField] private float upAngle = 0f;

    [Tooltip("Degrees the Handle turns on its local X, from its modelled pose, while the switch is down.")]
    [SerializeField] private float downAngle = 38.87f;

    [Tooltip("Degrees the Handle turns on its local X, from its modelled pose, before the switch is " +
             "first flipped (neither up nor down).")]
    [SerializeField] private float middleAngle = 19.4f;

    private SwitchPanelController panel;
    private int index;
    private Transform handle;
    private Quaternion handleRest;
    private Coroutine flipRoutine;

    protected override void Awake()
    {
        base.Awake();
        handle = transform.Find("Handle");
        if (handle != null) handleRest = handle.localRotation;
    }

    /// <summary>Called by the panel: which panel this switch belongs to and its place in the row.</summary>
    public void Bind(SwitchPanelController owner, int switchIndex)
    {
        panel = owner;
        index = switchIndex;
    }

    public override string GetPromptText() =>
        panel != null && panel.PuzzleData != null ? panel.PuzzleData.SwitchPrompt : "Unconfigured switch";

    public override bool IsRepeatable() => true;

    public override bool IsFinished() => panel != null && panel.IsCompleted;

    protected override bool CanInteractInCloseRange() =>
        panel != null && panel.PuzzleData != null && flipRoutine == null && !panel.IsCompleted;

    protected override void OnInteract()
    {
        float from = TargetAngle(panel.Position(index));
        panel.Flip(index);
        flipRoutine = StartCoroutine(Animate(from, TargetAngle(panel.Position(index)), panel.PuzzleData.FlipSeconds));
    }

    /// <summary>Shows the stored position at once (start, checkpoint).</summary>
    public void SnapToState()
    {
        if (handle == null || panel == null) return;
        if (flipRoutine != null) { StopCoroutine(flipRoutine); flipRoutine = null; }
        handle.localRotation = TargetRotation(panel.Position(index));
    }

    private void OnDisable() => flipRoutine = null;

    private void Update()
    {
        // Follows a checkpoint restore, which rewrites the stored position under it.
        if (flipRoutine == null && handle != null && panel != null &&
            Quaternion.Angle(handle.localRotation, TargetRotation(panel.Position(index))) > 0.5f)
            SnapToState();
    }

    private float TargetAngle(int position) =>
        position == SwitchPanelController.Up ? upAngle
        : position == SwitchPanelController.Down ? downAngle
        : middleAngle;

    private Quaternion TargetRotation(int position) => HandleRotation(TargetAngle(position));

    private Quaternion HandleRotation(float angle) => handleRest * Quaternion.Euler(angle, 0f, 0f);

    // Eases the angle, not the rotation: up and down can be 180° apart, where a slerp could swing
    // the handle any way round (through the board included).
    private IEnumerator Animate(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            // Eased like the boiler levers' swing: a lever has weight, it does not snap.
            if (handle != null) handle.localRotation = HandleRotation(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        if (handle != null) handle.localRotation = HandleRotation(to);
        flipRoutine = null;
        InteractionEvents.RequestPromptRefresh();   // usable again under the crosshair
    }
}
