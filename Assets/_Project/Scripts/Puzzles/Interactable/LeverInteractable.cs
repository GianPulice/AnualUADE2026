using System.Collections;
using UnityEngine;

/// <summary>
/// One lever of the boiler room panel (Central Puzzle 2 — SP1). [E] pulls it: it goes down, plays
/// its own knock and springs back up, so the same lever can be pulled twice in a row. Its number
/// is its place among the panel's levers, given by <see cref="LeverPanelController"/>.
///
/// The moving part is the child called "Handle", rotated on its local X; without one the lever
/// still works, it just does not move.
/// </summary>
public class LeverInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [Tooltip("Degrees the handle turns on its local X when pulled.")]
    [SerializeField] private float pullAngle = 70f;

    private LeverPanelController panel;
    private int leverNumber;
    private Transform handle;
    private Quaternion handleRest;
    private Coroutine pullRoutine;

    public int LeverNumber => leverNumber;

    protected override void Awake()
    {
        base.Awake();
        handle = transform.Find("Handle");
        if (handle != null) handleRest = handle.localRotation;
    }

    /// <summary>Called by the panel: which panel this lever reports to and which lever it is.</summary>
    public void Bind(LeverPanelController owner, int number)
    {
        panel = owner;
        leverNumber = number;
    }

    private SO_LeverSequencePuzzleData Data => panel != null ? panel.PuzzleData : null;

    public override string GetPromptText() =>
        Data != null ? string.Format(Data.LeverPrompt, leverNumber) : "Unconfigured lever";

    public override bool IsRepeatable() => true;

    public override bool IsFinished() => panel != null && panel.IsCompleted;

    protected override bool CanInteractInCloseRange() =>
        panel != null && Data != null && pullRoutine == null && !panel.IsCompleted;

    protected override void OnInteract()
    {
        if (AudioManager.Exists)
        {
            AudioClip knock = Data.GetKnockClip(leverNumber);
            if (knock != null)
                AudioManager.Instance.PlaySFX(knock, transform.position, Data.Volume, 1f, 1f, Data.MaxHearingDistance);
        }

        pullRoutine = StartCoroutine(Pull(Data.LeverPullSeconds));
        panel.RegisterPull(leverNumber);
    }

    private void OnDisable()
    {
        pullRoutine = null;
        if (handle != null) handle.localRotation = handleRest;
    }

    private IEnumerator Pull(float seconds)
    {
        Quaternion down = handleRest * Quaternion.Euler(pullAngle, 0f, 0f);
        float half = seconds * 0.5f;

        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            if (handle != null) handle.localRotation = Quaternion.Slerp(handleRest, down, t / half);
            yield return null;
        }
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            if (handle != null) handle.localRotation = Quaternion.Slerp(down, handleRest, t / half);
            yield return null;
        }

        if (handle != null) handle.localRotation = handleRest;
        pullRoutine = null;
    }
}
