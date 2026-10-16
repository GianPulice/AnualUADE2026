using UnityEngine;

/// <summary>
/// Keeps the renderers on this GameObject and below it out of the <see cref="ItemProximityHighlight"/>
/// above them.
///
/// For parts that drive their own emission. The highlight pins <c>_EmissionColor</c> on every slot
/// it drives with a property block captured in its Awake, so a part that changes its glow later
/// (a reward box's lock LEDs going from red to cyan, <see cref="RewardBoxRig"/>) would be put back to
/// the captured colour the next time the highlight lerps. Excluded, the part keeps whatever its own
/// script writes, and the rest of the object still lights up when looked at.
/// </summary>
[DisallowMultipleComponent]
public class ExcludeFromHighlight : MonoBehaviour
{
}
