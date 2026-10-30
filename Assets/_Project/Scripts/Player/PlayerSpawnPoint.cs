using UnityEngine;

/// <summary>
/// A place the player can be moved to from the player's inspector (PlayerStateManager → "Move the
/// player to"), in edit mode, so Play starts from it. The player faces this transform's blue arrow
/// (yaw only).
///
/// An editor convenience: nothing reads it at runtime. Put it on any empty GameObject to add a button.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSpawnPoint : MonoBehaviour
{
}
