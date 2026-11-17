using System.Collections;
using UnityEngine;

/// <summary>
/// The boiler of the boiler room (Central Puzzle 2 — SP1). [E] plays the knock sequence of its
/// <see cref="SO_LeverSequencePuzzleData"/>, as many times as the player wants; the lever panel in
/// the next room is where it is solved. Nothing is stored: a player interrupted by the Nemesis
/// just comes back and listens again.
///
/// The knocks are 3D at the boiler, fade out at the asset's Max Hearing Distance and drop to its
/// Occluded Volume through a wall (one check per knock) — keep the lever panel farther than that
/// distance, or behind walls on the asset's Occluders layers.
///
/// It is used through the valve mounted on the boiler: the prefab keeps this component on the
/// object that holds both the boiler model and the valve (so the highlight lights them together),
/// with the only Interactable collider on the valve and the boiler's solid body outside it. While
/// the sequence plays, the valve wheel makes exactly one turn, from [E] to the last knock.
/// </summary>
public class BoilerInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [SerializeField] private SO_LeverSequencePuzzleData puzzleData;

    [Tooltip("The valve wheel that turns once while the sequence plays. Empty = nothing turns.")]
    [SerializeField] private Transform valveWheel;

    [Tooltip("Axis the wheel turns on, in its parent's space (the valve stem).")]
    [SerializeField] private Vector3 valveAxis = Vector3.up;

    [Header("Letter screen")]
    [Tooltip("Where the little screen goes, in this object's space: just above the valve, facing out.")]
    [SerializeField] private Vector3 screenPosition = new Vector3(-0.5017f, 1.78f, 0.5017f);

    [Tooltip("Direction the screen faces, as a yaw in degrees (0 = looks towards -Z). 135 = out of the valve's corner.")]
    [SerializeField] private float screenYaw = 135f;

    [SerializeField] private Vector2 screenSize = new Vector2(0.34f, 0.22f);

    [Tooltip("Material of the screen's body. Empty = the default one.")]
    [SerializeField] private Material screenBodyMaterial;

    private PuzzleLetterScreen screen;
    private Coroutine playRoutine;
    private Quaternion valveRest;
    private PuzzleKnockVoices voices;

    private bool IsPlaying => playRoutine != null;

    private bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    protected override void Awake()
    {
        base.Awake();
        voices = new PuzzleKnockVoices(transform);
        if (valveWheel != null) valveRest = valveWheel.localRotation;
        screen = PuzzleLetterScreen.Create(transform, screenPosition, Quaternion.Euler(0f, screenYaw, 0f),
                                           screenSize, screenBodyMaterial);
        if (puzzleData == null)
            Debug.LogError($"[{nameof(BoilerInteractable)}] No SO_LeverSequencePuzzleData on '{name}'.", this);
    }

    public override string GetPromptText() => puzzleData != null ? puzzleData.BoilerPrompt : "Unconfigured boiler";

    public override string GetInfoText() => IsPlaying && puzzleData != null ? puzzleData.BoilerPlayingInfo : string.Empty;

    public override bool IsRepeatable() => true;

    /// <summary>Solved: the sequence has nothing left to teach.</summary>
    public override bool IsFinished() => IsCompleted;

    protected override bool CanInteractInCloseRange() => puzzleData != null && !IsPlaying && !IsCompleted;

    protected override void OnInteract()
    {
        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.BoilerStartSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.BoilerStartSoundId, transform.position);

        playRoutine = StartCoroutine(PlaySequence());
        if (valveWheel != null) StartCoroutine(SpinValve(SequenceSeconds()));

        // "Listen..." right away, not only after looking away and back.
        InteractionEvents.RequestPromptRefresh();
    }

    private void OnDisable()
    {
        // A coroutine dies with the object; the flag must not outlive it.
        playRoutine = null;
        if (valveWheel != null) valveWheel.localRotation = valveRest;   // its turn died with it too
        voices?.StopAll();                                              // and so did its knocks
        if (screen != null) screen.Clear();
    }

    /// <summary>How long <see cref="PlaySequence"/> runs, lead-in included: the time the valve has for its turn.</summary>
    private float SequenceSeconds()
    {
        float seconds = puzzleData.LeadInSeconds;
        if (puzzleData.BoilerSequenceClip != null) return seconds + puzzleData.BoilerSequenceClip.length;

        foreach (SO_LeverSequencePuzzleData.Knock knock in puzzleData.Sequence)
            seconds += KnockSeconds(knock);
        return seconds;
    }

    private float KnockSeconds(SO_LeverSequencePuzzleData.Knock knock)
    {
        AudioClip clip = puzzleData.GetKnockClip(knock.lever);
        return (clip != null ? clip.length * 0.5f : 0f) + knock.pauseAfter;
    }

    // Same scaled time as the sequence, so the wheel and the knocks pause and end together.
    private IEnumerator SpinValve(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            valveWheel.localRotation = Quaternion.AngleAxis(360f * t / seconds, valveAxis) * valveRest;
            yield return null;
        }
        valveWheel.localRotation = valveRest;   // a full turn ends where it started
    }

    // Scaled time: the pause menu holds the sequence where it is instead of letting it run unheard.
    private IEnumerator PlaySequence()
    {
        yield return new WaitForSeconds(puzzleData.LeadInSeconds);

        if (puzzleData.BoilerSequenceClip != null)
        {
            Play(puzzleData.BoilerSequenceClip);
            // One clip with the whole sequence: its knocks are not known, so the screen follows the
            // authored rhythm. Keep that rhythm in step with the clip.
            float elapsed = 0f;
            foreach (SO_LeverSequencePuzzleData.Knock knock in puzzleData.Sequence)
            {
                yield return ShowKnock(knock);
                elapsed += KnockSeconds(knock);
            }
            float rest = puzzleData.BoilerSequenceClip.length - elapsed;
            if (rest > 0f) yield return new WaitForSeconds(rest);
        }
        else
        {
            foreach (SO_LeverSequencePuzzleData.Knock knock in puzzleData.Sequence)
            {
                Play(puzzleData.GetKnockClip(knock.lever));
                yield return ShowKnock(knock);
            }
        }

        screen.Clear();
        playRoutine = null;
        InteractionEvents.RequestPromptRefresh();   // back to "Activate the boiler"
    }

    // The knock's letter while it sounds, then dark for the pause (so a repeated lever shows twice).
    // Adds up to KnockSeconds, the same time the sequence waits.
    private IEnumerator ShowKnock(SO_LeverSequencePuzzleData.Knock knock)
    {
        float total = KnockSeconds(knock);
        float lit = Mathf.Min(total, Mathf.Max(0.25f, total - knock.pauseAfter));
        screen.Show(knock.lever);
        yield return new WaitForSeconds(lit);
        screen.Clear();
        if (total > lit) yield return new WaitForSeconds(total - lit);
    }

    // On sources of its own, not the pooled PlaySFX: see PuzzleKnockVoices — a knock heard when the
    // game is paused comes back from where it was on unpausing instead of being lost.
    private void Play(AudioClip clip)
    {
        if (clip == null) return;
        voices.Play(clip, puzzleData.VolumeAt(transform.position), puzzleData.MaxHearingDistance);
    }
}
