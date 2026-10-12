using System.Collections;
using UnityEngine;

/// <summary>
/// One lever of the boiler room panel (Central Puzzle 2 — SP1). [E] pulls it: it goes down, plays
/// its own knock and springs back up, so the same lever can be pulled twice in a row. Its number
/// is its place among the panel's levers, given by <see cref="LeverPanelController"/>.
///
/// How it shows the pull depends on what the model offers, so any lever art works:
///  • Two poses — children called "PoseRest" and "PosePulled": the pulled one shows while the
///    lever is down and the rest one comes back after. For models that come as one piece in two
///    states (the knife switch).
///  • A moving part — a child called "Handle", rotated on its local X by Pull Angle, eased down
///    and back up (the knife switch: its blade is cut out of the model as its own mesh, pivoted
///    on the hinge).
///  • Neither: the lever still works, it just does not move.
/// </summary>
public class LeverInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [Tooltip("Degrees the Handle child turns on its local X when pulled (moving-part levers only).")]
    [SerializeField] private float pullAngle = 70f;

    private LeverPanelController panel;
    private int leverNumber;
    private Transform handle;
    private Quaternion handleRest;
    private GameObject poseRest;
    private GameObject posePulled;
    private Coroutine pullRoutine;

    public int LeverNumber => leverNumber;

    private bool HasPoses => poseRest != null && posePulled != null;

    protected override void Awake()
    {
        base.Awake();
        handle = transform.Find("Handle");
        if (handle != null) handleRest = handle.localRotation;

        Transform rest = transform.Find("PoseRest");
        Transform pulled = transform.Find("PosePulled");
        if (rest != null && pulled != null)
        {
            poseRest = rest.gameObject;
            posePulled = pulled.gameObject;
        }
        ShowPulled(false);
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
                AudioManager.Instance.PlaySFX(knock, transform.position, Data.VolumeAt(transform.position), 1f, 1f, Data.MaxHearingDistance);
        }

        pullRoutine = StartCoroutine(Pull(Data.LeverPullSeconds));
        panel.RegisterPull(leverNumber);
    }

    private void OnDisable()
    {
        pullRoutine = null;
        if (handle != null) handle.localRotation = handleRest;
        ShowPulled(false);
    }

    private void ShowPulled(bool pulled)
    {
        if (!HasPoses) return;
        poseRest.SetActive(!pulled);
        posePulled.SetActive(pulled);
    }

    private IEnumerator Pull(float seconds)
    {
        if (HasPoses)
        {
            ShowPulled(true);
            yield return new WaitForSeconds(seconds);
            ShowPulled(false);
        }
        else
        {
            // Eased both ways with a short hold at the bottom: a lever has weight, it does not
            // snap. Down and up take DownShare of the time each, the rest is the hold.
            Quaternion down = handleRest * Quaternion.Euler(pullAngle, 0f, 0f);
            float travel = seconds * DownShare;
            float hold = Mathf.Max(0f, seconds - 2f * travel);

            yield return Swing(handleRest, down, travel);
            if (hold > 0f) yield return new WaitForSeconds(hold);
            yield return Swing(down, handleRest, travel);

            if (handle != null) handle.localRotation = handleRest;
        }

        pullRoutine = null;
        InteractionEvents.RequestPromptRefresh();   // usable again under the crosshair
    }

    // Share of the pull spent travelling down (the same again coming up); the rest is the hold.
    private const float DownShare = 0.4f;

    private IEnumerator Swing(Quaternion from, Quaternion to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (handle != null) handle.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        if (handle != null) handle.localRotation = to;
    }
}
