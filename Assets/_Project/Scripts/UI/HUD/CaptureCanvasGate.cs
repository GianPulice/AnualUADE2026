using UnityEngine;

/// <summary>
/// Takes this Canvas off the screen for the length of a capture: from the Nemesis's grab, through
/// the shot, the black cover and the respawn, until the player is back on their feet with control
/// (<see cref="PlayerStateManager.IsRecoveringFromCapture"/>). For HUD that only means something
/// while the player can act — the crosshair, which otherwise sits in the middle of the grab's shot
/// and, being on a canvas sorted above the HUD's, on top of the black cover too.
///
/// THE CANVAS, NOT A CANVASGROUP'S ALPHA. The crosshair's alpha already belongs to its
/// <see cref="ModalVisibilityGate"/>, which writes it on every modal push and pop. Two writers of
/// one alpha is a flicker waiting for the wrong order; a disabled Canvas draws nothing whatever
/// the alpha under it says, and the two never meet.
///
/// POLLED, NOT DRIVEN BY AN EVENT. There is an event for the grab
/// (<see cref="PlayerEvents.OnPlayerCaptured"/>) and none for control coming back, and the player
/// lives in another scene: its state is read off <see cref="PlayerRegistry"/> every frame, the
/// way SafeZoneAlert does.
///
/// SETUP: on the object that holds the Canvas (CrosshairCanvas, in the LevelUI scene).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class CaptureCanvasGate : MonoBehaviour
{
    private Canvas canvas;
    private bool hidden;

    private void Awake() => canvas = GetComponent<Canvas>();

    private void Update()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        bool captured = player != null && player.IsRecoveringFromCapture;
        if (captured == hidden) return;

        hidden = captured;
        canvas.enabled = !captured;
    }

    private void OnDisable()
    {
        // Switched off mid-capture, nothing would be left to switch the canvas back on.
        if (!hidden) return;

        hidden = false;
        if (canvas != null) canvas.enabled = true;
    }
}
