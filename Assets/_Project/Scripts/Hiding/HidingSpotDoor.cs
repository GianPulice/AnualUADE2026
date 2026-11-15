using UnityEngine;

/// <summary>
/// Swings a hiding spot's door open and shut inside the climb window, and flings it open when
/// the Nemesis pulls the player out.
///
/// Getting in: it opens during the climb and shuts just after it, once the camera has cut inside.
/// Getting out: open and shut inside the climb-out.
///
/// PURELY VISUAL. It listens to <see cref="HidingSpot.ClimbStarted"/> and fits the swing into the
/// duration the spot already uses (<see cref="SO_HidingData.EnterDuration"/> /
/// <see cref="SO_HidingData.ExitDuration"/>): the player is just as visible, and just as
/// grabbable, as without it. To make the door slower, lengthen those durations in the data asset —
/// and know that this also lengthens the window the Nemesis's "I saw you climb in" rule reads.
///
/// Scaled time, like the climb itself: it freezes with the pause menu.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Hiding/Hiding Spot Door")]
public class HidingSpotDoor : MonoBehaviour
{
    [SerializeField] private HidingSpot spot;

    [Tooltip("The door, pivoted on its hinge. It turns around its own up axis.")]
    [SerializeField] private Transform door;

    [Tooltip("How far it opens, in degrees. The sign picks the side it swings to: it has to swing " +
             "OUT of the locker.")]
    [SerializeField] private float openAngle = 100f;

    [Header("Getting in")]
    [Tooltip("Share of the climb-in spent opening; it stays open until the climb ends. The camera " +
             "cuts inside at that moment (CB_HidingSpotBlends), so the player sees the door open and " +
             "then the inside of the locker, never the door closing on them.")]
    [SerializeField, Range(0.05f, 1f)] private float enterOpenShare = 0.6f;

    [Tooltip("Seconds it takes to shut AFTER the climb-in, with the player already inside.")]
    [SerializeField, Min(0.01f)] private float enterCloseSeconds = 0.35f;

    [Header("Getting out (fractions of the climb-out)")]
    [Tooltip("Share of the climb-out spent opening.")]
    [SerializeField, Range(0.05f, 0.9f)] private float exitOpenShare = 0.35f;

    [Tooltip("Share of the climb-out spent closing. It finishes shut on the exact frame the climb ends.")]
    [SerializeField, Range(0.05f, 0.9f)] private float exitCloseShare = 0.35f;

    [Header("Pulled out by the Nemesis")]
    [SerializeField, Min(0.01f)] private float yankOpenSeconds = 0.12f;
    [SerializeField, Min(0f)] private float yankHoldSeconds = 1.6f;
    [SerializeField, Min(0.01f)] private float yankCloseSeconds = 0.8f;

    [Header("Sound")]
    [SoundId]
    [SerializeField] private string openSoundId = "sfx_armario_puerta_open";
    [SoundId]
    [SerializeField] private string closeSoundId = "sfx_interaction_cerrar_puerta";

    [Header("While the player is inside")]
    [Tooltip("Hide the door from the camera while the player is inside. The interior camera sits a " +
             "hand from the door and the HUD's locker overlay draws the door and its slits; the real " +
             "mesh in between would cover the view through them. It still casts its shadow, and it " +
             "is back the moment the player starts to get out, before it swings open.")]
    [SerializeField] private bool hideFromCameraWhileInside = true;

    private Renderer[] doorRenderers = new Renderer[0];
    private UnityEngine.Rendering.ShadowCastingMode[] cachedShadowModes = new UnityEngine.Rendering.ShadowCastingMode[0];
    private bool hiddenFromCamera;

    private Quaternion closedRotation;

    // The swing in flight: from the angle it had when it started, to open, held, back to 0.
    private float startAngle, angle;
    private float tOpen, tHold, tClose, elapsed;
    private bool playing, closeSoundPlayed;

    private void Awake()
    {
        if (spot == null) spot = GetComponent<HidingSpot>();
        if (door != null)
        {
            closedRotation = door.localRotation;
            doorRenderers = door.GetComponentsInChildren<Renderer>(true);
            cachedShadowModes = new UnityEngine.Rendering.ShadowCastingMode[doorRenderers.Length];
        }
    }

    private void OnEnable()
    {
        HidingEvents.OnEntered += HandleEntered;
        HidingEvents.OnExited += HandleExited;

        if (spot == null) return;
        spot.ClimbStarted += HandleClimbStarted;
        spot.PulledOpen += HandlePulledOpen;
    }

    private void OnDisable()
    {
        HidingEvents.OnEntered -= HandleEntered;
        HidingEvents.OnExited -= HandleExited;
        ShowToCamera();

        if (spot != null)
        {
            spot.ClimbStarted -= HandleClimbStarted;
            spot.PulledOpen -= HandlePulledOpen;
        }

        // Never left hanging open by a disable mid-swing.
        playing = false;
        angle = 0f;
        Apply();
    }

    private void HandleClimbStarted(bool entering, float duration)
    {
        if (entering)
        {
            float open = duration * enterOpenShare;
            Play(open, duration - open, enterCloseSeconds);
            return;
        }

        float openOut = duration * exitOpenShare;
        float closeOut = duration * exitCloseShare;
        Play(openOut, Mathf.Max(0f, duration - openOut - closeOut), closeOut);
    }

    private void HandlePulledOpen() => Play(yankOpenSeconds, yankHoldSeconds, yankCloseSeconds);

    // Entered comes on the cut inside; Exited on every way out, before the climb-out starts — so
    // the door is visible again before it swings open, and after a capture or a respawn too.
    private void HandleEntered(HidingSpot entered)
    {
        if (!hideFromCameraWhileInside || entered != spot || hiddenFromCamera) return;
        hiddenFromCamera = true;

        for (int i = 0; i < doorRenderers.Length; i++)
        {
            if (doorRenderers[i] == null) continue;
            cachedShadowModes[i] = doorRenderers[i].shadowCastingMode;
            doorRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    private void HandleExited(HidingSpot exited)
    {
        if (exited == spot) ShowToCamera();
    }

    private void ShowToCamera()
    {
        if (!hiddenFromCamera) return;
        hiddenFromCamera = false;

        for (int i = 0; i < doorRenderers.Length; i++)
            if (doorRenderers[i] != null) doorRenderers[i].shadowCastingMode = cachedShadowModes[i];
    }

    private void Play(float open, float hold, float close)
    {
        if (door == null) return;

        startAngle = angle;
        tOpen = Mathf.Max(0.0001f, open);
        tHold = hold;
        tClose = Mathf.Max(0.0001f, close);
        elapsed = 0f;
        playing = true;
        closeSoundPlayed = false;

        PlaySound(openSoundId);
    }

    private void Update()
    {
        if (!playing) return;

        elapsed += Time.deltaTime;

        if (elapsed < tOpen)
        {
            // Ease out: it is yanked, then slows against the hinge.
            float k = elapsed / tOpen;
            angle = Mathf.Lerp(startAngle, openAngle, 1f - (1f - k) * (1f - k));
        }
        else if (elapsed < tOpen + tHold)
        {
            angle = openAngle;
        }
        else if (elapsed < tOpen + tHold + tClose)
        {
            // Ease in: pulled shut, fastest at the end, where the slam is.
            float k = (elapsed - tOpen - tHold) / tClose;
            angle = Mathf.Lerp(openAngle, 0f, k * k);
        }
        else
        {
            angle = 0f;
            playing = false;
        }

        if (!closeSoundPlayed && !playing)
        {
            closeSoundPlayed = true;
            PlaySound(closeSoundId);
        }

        Apply();
    }

    private void Apply()
    {
        if (door != null) door.localRotation = closedRotation * Quaternion.AngleAxis(angle, Vector3.up);
    }

    private void PlaySound(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists || door == null) return;
        AudioManager.Instance.PlaySFX(id, door.position);
    }
}
