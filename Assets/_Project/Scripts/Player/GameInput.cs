using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's controls, read from the project-wide Input System actions
/// (Project Settings > Input System > InputSystem_Actions, map "Player"). Keyboard and gamepad
/// come through the same action, so nothing that reads from here cares which one is in use.
///
/// The camera is the exception: Cinemachine's InputAxisController reads its own Look action.
/// Debug keys (F8, F9, F10, R, Y) stay on the legacy Input Manager on purpose.
/// </summary>
public static class GameInput
{
    private static InputActionMap player;
    private static InputAction move, look, sprint, crouch, interact, inventory, holdBreath;
    private static bool warned;

    public static InputAction Move       => Resolve() ? move : null;
    public static InputAction Look       => Resolve() ? look : null;
    public static InputAction Sprint     => Resolve() ? sprint : null;
    public static InputAction Crouch     => Resolve() ? crouch : null;
    public static InputAction Interact   => Resolve() ? interact : null;
    public static InputAction Inventory  => Resolve() ? inventory : null;
    public static InputAction HoldBreath => Resolve() ? holdBreath : null;

    /// <summary>Stick or WASD, -1..1 per axis. Zero if the actions are missing.</summary>
    public static Vector2 MoveValue => Move != null ? move.ReadValue<Vector2>() : Vector2.zero;

    public static bool SprintHeld        => Sprint != null && sprint.IsPressed();
    public static bool CrouchPressed     => Crouch != null && crouch.WasPressedThisFrame();
    public static bool InteractPressed   => Interact != null && interact.WasPressedThisFrame();
    public static bool InventoryPressed  => Inventory != null && inventory.WasPressedThisFrame();

    /// <summary>
    /// Held, not pressed: holding your breath inside a hiding spot lasts exactly as long as the
    /// player keeps the key down, and LETTING GO is the event that costs them (the exhale).
    /// Read by <see cref="PlayerHiddenState"/> and nothing else.
    /// </summary>
    public static bool HoldBreathHeld    => HoldBreath != null && holdBreath.IsPressed();

    /// <summary>True while the player is moving or looking around.</summary>
    public static bool AnyMoveOrLook(float threshold = 0.1f)
    {
        if (MoveValue.sqrMagnitude > threshold * threshold) return true;
        return Look != null && look.ReadValue<Vector2>().sqrMagnitude > threshold * threshold;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        player = null;
        warned = false;
        move = look = sprint = crouch = interact = inventory = holdBreath = null;
    }

    private static bool Resolve()
    {
        if (player != null) return true;

        InputActionAsset asset = InputSystem.actions;
        if (asset == null)
        {
            if (warned) return false;
            warned = true;
            Debug.LogError("[GameInput] No project-wide Input Actions: assign InputSystem_Actions in " +
                           "Project Settings > Input System Package.");
            return false;
        }

        player = asset.FindActionMap("Player", throwIfNotFound: true);
        move      = player.FindAction("Move", throwIfNotFound: true);
        look      = player.FindAction("Look", throwIfNotFound: true);
        sprint    = player.FindAction("Sprint", throwIfNotFound: true);
        crouch    = player.FindAction("Crouch", throwIfNotFound: true);
        interact  = player.FindAction("Interact", throwIfNotFound: true);
        inventory = player.FindAction("Inventory", throwIfNotFound: true);

        // NOT throwIfNotFound, unlike everything above it. HoldBreath is the one action this
        // project added to the Unity template asset, so it is the one that can go missing on a
        // merge that takes the other side's InputSystem_Actions — and a throw in here would take
        // Move and Look down with it. Losing the ability to hold your breath is survivable;
        // losing the ability to walk is not.
        holdBreath = player.FindAction("HoldBreath", throwIfNotFound: false);
        if (holdBreath == null)
        {
            Debug.LogWarning("[GameInput] The Player map has no 'HoldBreath' action, so the " +
                             "player cannot hold their breath while hidden. Re-add it to " +
                             "InputSystem_Actions (Button; F on keyboard, left shoulder on pad).");
        }

        // Project-wide actions start enabled, but a map someone disabled would read as silence.
        if (!player.enabled) player.Enable();
        return true;
    }
}
