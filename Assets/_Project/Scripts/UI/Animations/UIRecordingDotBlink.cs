using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blinks a Graphic on and off like the recording dot of the player's camera feed
/// (<see cref="PlayerCameraFeed"/>): lit for the first half of each beat, then off, with no fade. Used
/// by the "LAST PLAYED" mark of the save slot cards, so it reads as the same camera recording.
///
/// Runs on unscaled time, so it keeps blinking in a menu that pauses the game. It drives the
/// CanvasRenderer's alpha instead of enabling the Graphic, so a blink never rebuilds the canvas.
/// </summary>
[RequireComponent(typeof(Graphic))]
[AddComponentMenu("WIRED/UI Animations/UI Recording Dot Blink")]
public class UIRecordingDotBlink : MonoBehaviour
{
    [Tooltip("Blinks per second. 1 = the camera feed's recording dot (SO_PlayerCameraFeedConfig, Rec Blink Hz).")]
    [SerializeField, Min(0.1f)] private float blinksPerSecond = 1f;

    private Graphic graphic;

    private void Awake() => graphic = GetComponent<Graphic>();

    // Applied on enable too: the dot is lit or dark from the very first frame, never a stale state.
    private void OnEnable() => Apply();

    private void Update() => Apply();

    // Left lit: anything else that shows this Graphic later must not find it switched off.
    private void OnDisable()
    {
        if (graphic != null) graphic.canvasRenderer.SetAlpha(1f);
    }

    private void Apply()
    {
        if (graphic == null) return;

        bool lit = Mathf.Repeat(Time.unscaledTime * blinksPerSecond, 1f) < 0.5f;
        graphic.canvasRenderer.SetAlpha(lit ? 1f : 0f);
    }
}
