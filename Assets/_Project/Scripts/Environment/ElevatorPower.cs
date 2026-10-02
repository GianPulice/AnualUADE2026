using UnityEngine;

/// <summary>
/// Whether a freight elevator has power. Without it the call panels and the ride button refuse and
/// say why; with it they work as before (WIR-063: the lift used to answer a call with the power off).
///
/// Same rule as <see cref="PoweredLightSwitch"/>: one bool, set by itself when the puzzle that turns
/// the power on completes (and caught up in Start for a level loaded after it). Tick it by hand in
/// the inspector to test without solving anything.
///
/// ONE PER SHAFT, on the root next to <see cref="NemesisElevatorLink"/>. The panels find it through
/// their elevator and the ride button through its parents, so the shaft has one source of truth for
/// "is the power on" rather than one bool per button that could disagree.
///
/// Only the player's controls read it. The Nemesis calls the cabin through
/// <see cref="MovingPlatform.RequestRide"/> directly and is not gated here.
///
/// SETUP: lives on the MontacargasRoot prefab with Power Puzzle Id empty, which means always
/// powered — the right default for a test scene. A level where the lift needs power sets the id on
/// its own instance (Zona1: sp1_panel_electrico, the same puzzle the light switches wait for).
/// </summary>
[DisallowMultipleComponent]
public class ElevatorPower : MonoBehaviour
{
    [Tooltip("The forklift has power. Set automatically when the puzzle below completes; tick it " +
             "by hand to test without solving it. Ignored while Power Puzzle Id is empty.")]
    [SerializeField] private bool hasPower;

    [Tooltip("Puzzle that turns the power on. Empty: this forklift always has power.")]
    [PuzzleId]
    [SerializeField] private string powerPuzzleId = string.Empty;

    [Tooltip("Shown in grey by the call panels and the ride button while there is no power.")]
    [SerializeField] private string noPowerInfo = "You need electricity to use the forklift.";

    /// <summary>The power came from the puzzle, not from a hand-ticked bool. Only that kind is
    /// taken away again by a rollback (<see cref="HandleRespawned"/>).</summary>
    private bool poweredByPuzzle;

    /// <summary>Whether the player's controls may move the cabin.</summary>
    public bool HasPower => string.IsNullOrWhiteSpace(powerPuzzleId) || hasPower;

    /// <summary>Why the controls refuse while <see cref="HasPower"/> is false.</summary>
    public string NoPowerInfo => noPowerInfo;

    private void Awake()
    {
        // Static events: subscribed in Awake and released in OnDestroy (docs/CLAUDE.md).
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        CheckpointManager.OnRespawned += HandleRespawned;
    }

    private void Start()
    {
        // Catch-up: the event only fires on the transition, so a level loaded after the puzzle was
        // solved would otherwise never hear about it.
        if (!IsPuzzleCompleted()) return;

        hasPower = true;
        poweredByPuzzle = true;
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        CheckpointManager.OnRespawned -= HandleRespawned;
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(powerPuzzleId) || puzzleId != powerPuzzleId) return;

        poweredByPuzzle = true;
        SetPower(true);
    }

    /// <summary>
    /// A respawn rolls the puzzles back to the checkpoint's snapshot. If that snapshot predates the
    /// power, the lift goes dark again with them instead of staying on for a puzzle that is no
    /// longer solved. A bool ticked by hand is left alone: it is a test, not the puzzle.
    /// </summary>
    private void HandleRespawned(Checkpoint checkpoint)
    {
        if (!poweredByPuzzle || IsPuzzleCompleted()) return;

        poweredByPuzzle = false;
        SetPower(false);
    }

    private void SetPower(bool on)
    {
        if (hasPower == on) return;

        hasPower = on;
        InteractionEvents.RequestPromptRefresh();
    }

    private bool IsPuzzleCompleted() =>
        !string.IsNullOrWhiteSpace(powerPuzzleId) && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(powerPuzzleId);
}
