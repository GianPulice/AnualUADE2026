using System.Collections;
using UnityEngine;

/// <summary>
/// One lever of the boiler room panel (Central Puzzle 2 — SP1). [E] pulls it: it goes down, plays
/// its own knock and springs back up, so the same lever can be pulled twice in a row — once its
/// knock has finished: like the boiler, it shows "Listen..." and ignores [E] meanwhile. Its number
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
    private Coroutine knockRoutine;
    private PuzzleKnockVoices voices;

    public int LeverNumber => leverNumber;

    /// <summary>Its knock is still sounding: like the boiler, it cannot be used again until it ends.</summary>
    private bool IsKnocking => knockRoutine != null;

    private bool HasPoses => poseRest != null && posePulled != null;

    protected override void Awake()
    {
        base.Awake();
        voices = new PuzzleKnockVoices(transform);
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

    /// <summary>
    /// The boiler's "Listen..." while its knock sounds (there is nothing to press), kept through the
    /// lever's way back up so the prompt does not blink off between the knock ending and the lever
    /// being usable again.
    /// </summary>
    public override string GetInfoText() =>
        (IsKnocking || pullRoutine != null) && Data != null ? Data.BoilerPlayingInfo : string.Empty;

    public override bool IsRepeatable() => true;

    public override bool IsFinished() => panel != null && panel.IsCompleted;

    protected override bool CanInteractInCloseRange() =>
        panel != null && Data != null && pullRoutine == null && !IsKnocking && !panel.IsCompleted;

    protected override void OnInteract()
    {
        // On sources of its own, not the pooled PlaySFX: see PuzzleKnockVoices — a knock heard when
        // the game is paused comes back from where it was on unpausing instead of being lost.
        AudioClip knock = Data.GetKnockClip(leverNumber);
        if (knock != null)
        {
            voices.Play(knock, Data.VolumeAt(transform.position), Data.MaxHearingDistance);
            knockRoutine = StartCoroutine(WaitForKnock(knock.length));
        }

        pullRoutine = StartCoroutine(Pull(Data.LeverPullSeconds));
        panel.RegisterPull(leverNumber);

        // "Listen..." right away, not only after looking away and back (same as the boiler).
        InteractionEvents.RequestPromptRefresh();
    }

    // Scaled time, like the boiler's sequence: the pause menu holds the wait along with the knock.
    private IEnumerator WaitForKnock(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        knockRoutine = null;
        InteractionEvents.RequestPromptRefresh();   // back to "Pull lever N"
    }

    private void OnDisable()
    {
        // A coroutine dies with the object; the flags must not outlive it, nor the knock.
        knockRoutine = null;
        voices?.StopAll();
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
            while (IsKnocking) yield return null;   // stays down while its knock sounds
            ShowPulled(false);
        }
        else
        {
            // Eased both ways with a short hold at the bottom: a lever has weight, it does not
            // snap. Down and up take DownShare of the time each, the rest is the hold — at least,
            // since it stays down until its knock has finished and only then springs back up.
            Quaternion down = handleRest * Quaternion.Euler(pullAngle, 0f, 0f);
            float travel = seconds * DownShare;
            float hold = Mathf.Max(0f, seconds - 2f * travel);

            yield return Swing(handleRest, down, travel);
            if (hold > 0f) yield return new WaitForSeconds(hold);
            while (IsKnocking) yield return null;   // scaled-time wait: the pause holds it too
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
