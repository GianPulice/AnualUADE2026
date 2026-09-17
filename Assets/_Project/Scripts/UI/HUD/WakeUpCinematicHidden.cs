using UnityEngine;

/// <summary>
/// Hides a HUD element for the whole wake-up cinematic and fades it in the moment the player gets
/// control back (<see cref="WakeUpCinematicEvents.IsCameraLocked"/> clears). Made for the crosshair:
/// its canvas sorts at 1000, above the cinematic's black, so without this it floated over the
/// "closed eyes".
///
/// Uses its own CanvasGroup, separate from any <see cref="ModalVisibilityGate"/> on a parent:
/// nested groups multiply, so the two never fight over the same alpha.
///
/// SETUP: on the Crosshair in LevelUI (Tools ▸ Architect ▸ Setup Wake-Up Cinematic).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class WakeUpCinematicHidden : MonoBehaviour
{
    [Tooltip("Seconds to fade in once the cinematic ends. 0 = pop in.")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.35f;

    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Hidden until the first Update decides: the cinematic locks the camera in its own Start,
        // and a frame of crosshair over the black is exactly what this component is here to avoid.
        group.alpha = 0f;
    }

    private void Update()
    {
        if (WakeUpCinematicEvents.IsCameraLocked)
        {
            group.alpha = 0f;
            return;
        }

        if (group.alpha >= 1f) return;

        // Unscaled: control comes back on the voice line's unscaled clock.
        group.alpha = fadeInDuration > 0f
            ? Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / fadeInDuration)
            : 1f;
    }
}
