using UnityEngine;

/// <summary>What comes after the escape's last shot. Stored as a number in the asset: only append.</summary>
public enum EscapeEndingOutcome
{
    /// <summary>The Win screen, titled as below: the end of the slice.</summary>
    ToBeContinued = 0,

    /// <summary>The run goes on: <see cref="Zone2EntryTeleport"/> takes the player to Zona 2.</summary>
    ContinueToZone2 = 1,
}

/// <summary>
/// What the run does once the escape's final cinematic is over. Read by
/// <see cref="EscapeSequenceDirector"/> when the last shot ends, and by <see cref="WinView"/> for the
/// title of the screen.
/// </summary>
[CreateAssetMenu(fileName = "SO_EscapeEndingConfig", menuName = "Scriptable Objects/Escape/Escape Ending Config")]
public class SO_EscapeEndingConfig : ScriptableObject
{
    [Tooltip("To Be Continued = the final cinematic ends on the title screen below. " +
             "Continue To Zone 2 = the player is brought to Zona 2 with no screen in between " +
             "(needs an enabled Zone2EntryTeleport in the scene).")]
    [SerializeField] private EscapeEndingOutcome outcome = EscapeEndingOutcome.ToBeContinued;

    [Tooltip("Large title of the screen shown when the final cinematic ends.")]
    [SerializeField] private string title = "TO BE CONTINUED";

    public bool ContinuesPastEnding => outcome == EscapeEndingOutcome.ContinueToZone2;
    public string Title => title;
}
