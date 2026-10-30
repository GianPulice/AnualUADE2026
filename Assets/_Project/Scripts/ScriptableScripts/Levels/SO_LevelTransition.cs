using UnityEngine;

/// <summary>What starts a <see cref="LevelTransition"/>.</summary>
public enum LevelTransitionTrigger
{
    /// <summary>The player walks into the transition's trigger collider.</summary>
    PlayerEntersTrigger,

    /// <summary>The Required Puzzle Id is completed (it must not be empty).</summary>
    PuzzleCompleted,

    /// <summary>Only when something calls <see cref="LevelTransition.RequestTransition"/>: the end
    /// of a cinematic (Timeline Signal), an animation event, a UnityEvent.</summary>
    Manual
}

/// <summary>
/// Where a level goes when it is over, and when that is. One asset per exit: the level designer
/// drops the LevelTransition prefab in the scene and gives it this asset.
///
/// The next level is a scene group of the 'Scene List' asset (ScriptableObjects/Screen and Scenes),
/// loaded by the ScreenManager like any other screen — loading screen and input lock included. The
/// persistent managers (modules, puzzles) live in the Data scene, so their state goes with the
/// player; the inventory is emptied on the way unless <see cref="ClearInventory"/> is off.
/// </summary>
[CreateAssetMenu(fileName = "SO_LevelTransition", menuName = "Scriptable Objects/Levels/Level Transition")]
public class SO_LevelTransition : ScriptableObject
{
    [Tooltip("Label of the scene group to load, exactly as written in 'Scene List' " +
             "(ScriptableObjects/Screen and Scenes). E.g. 'TestAlesioPuzzle2'.")]
    [SerializeField] private string nextGroupLabel;

    [Tooltip("What starts the transition. Player Enters Trigger = the prefab's trigger box. " +
             "Puzzle Completed = as soon as Required Puzzle Id is completed. Manual = only when " +
             "a cinematic / animation / UnityEvent calls RequestTransition().")]
    [SerializeField] private LevelTransitionTrigger trigger = LevelTransitionTrigger.PlayerEntersTrigger;

    [Tooltip("Puzzle that must be completed before the level can be left. Empty = no condition " +
             "(not allowed with Puzzle Completed).")]
    [SerializeField, PuzzleId] private string requiredPuzzleId;

    [Tooltip("Seconds between the trigger and the scene change, for a sound or a camera move to " +
             "finish. Gameplay time: it waits while the game is paused.")]
    [SerializeField, Min(0f)] private float delaySeconds;

    [Tooltip("Empty the inventory before the next level loads. Nothing found in one level travels " +
             "to the next.")]
    [SerializeField] private bool clearInventory = true;

    public string NextGroupLabel => nextGroupLabel;
    public LevelTransitionTrigger Trigger => trigger;
    public string RequiredPuzzleId => requiredPuzzleId;
    public float DelaySeconds => delaySeconds;
    public bool ClearInventory => clearInventory;
}
