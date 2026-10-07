using System.Collections;
using UnityEngine;

/// <summary>
/// The boiler of the boiler room (Central Puzzle 2 — SP1). [E] plays the knock sequence of its
/// <see cref="SO_LeverSequencePuzzleData"/>, as many times as the player wants; the lever panel in
/// the next room is where it is solved. Nothing is stored: a player interrupted by the Nemesis
/// just comes back and listens again.
///
/// The knocks are 3D at the boiler and fade out at the asset's Max Hearing Distance — keep the
/// lever panel farther than that.
/// </summary>
public class BoilerInteractable : BaseRangeInteractable, IPuzzleInteractable
{
    [SerializeField] private SO_LeverSequencePuzzleData puzzleData;

    private Coroutine playRoutine;

    private bool IsPlaying => playRoutine != null;

    private bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    protected override void Awake()
    {
        base.Awake();
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
    }

    private void OnDisable()
    {
        // A coroutine dies with the object; the flag must not outlive it.
        playRoutine = null;
    }

    // Scaled time: the pause menu holds the sequence where it is instead of letting it run unheard.
    private IEnumerator PlaySequence()
    {
        yield return new WaitForSeconds(puzzleData.LeadInSeconds);

        if (puzzleData.BoilerSequenceClip != null)
        {
            Play(puzzleData.BoilerSequenceClip);
            yield return new WaitForSeconds(puzzleData.BoilerSequenceClip.length);
        }
        else
        {
            foreach (SO_LeverSequencePuzzleData.Knock knock in puzzleData.Sequence)
            {
                AudioClip clip = puzzleData.GetKnockClip(knock.lever);
                Play(clip);
                yield return new WaitForSeconds((clip != null ? clip.length * 0.5f : 0f) + knock.pauseAfter);
            }
        }

        playRoutine = null;
    }

    private void Play(AudioClip clip)
    {
        if (clip == null || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(clip, transform.position, puzzleData.Volume, 1f,
                                      1f, puzzleData.MaxHearingDistance);
    }
}
