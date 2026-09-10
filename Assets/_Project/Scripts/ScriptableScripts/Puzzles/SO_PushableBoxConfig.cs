using UnityEngine;

/// <summary>
/// Shared config for the push boxes (<see cref="PushableBox"/>). One asset drives every box
/// that references it, so the three level boxes can share the same feel without dragging the
/// same numbers on each prefab.
///
/// The reach on <c>SO_InteractionManager.InteractionDistance</c> is deliberately generous (about
/// 2.5 m) so that doors, valves and pickups are comfortable to aim at across a room. Boxes are
/// different: at that range the auto-slide onto the side anchor reads as a teleport. This asset
/// caps the grab distance for boxes only, without touching the global interaction reach — the
/// crosshair still highlights the box, but pressing E is refused until the player is close
/// enough. The info text below is what the prompt shows while the player is out of range.
/// </summary>
[CreateAssetMenu(fileName = "SO_PushableBoxConfig", menuName = "Scriptable Objects/SO_PushableBoxConfig")]
public class SO_PushableBoxConfig : ScriptableObject
{
    [Tooltip("Maximum distance in metres, measured in the XZ plane between the player and the " +
             "closest side anchor, at which the box will accept a grab. Beyond this the crosshair " +
             "still finds the box and the prompt is shown, but pressing E is refused and the " +
             "info text is displayed instead. Kept smaller than SO_InteractionManager's global " +
             "reach so grabbing does not visibly teleport the player onto the anchor.")]
    [SerializeField, Min(0f)] private float maxGrabDistance = 1.4f;

    [Tooltip("Prompt shown (as info text, not as an action prompt) when the box is in range of " +
             "the crosshair but out of grab range. Leave empty to show nothing at all in that " +
             "case — the box will just look uninteractable until the player walks closer.")]
    [SerializeField] private string outOfRangePrompt = "Get closer to push";

    public float MaxGrabDistance => maxGrabDistance;
    public string OutOfRangePrompt => outOfRangePrompt;
}
