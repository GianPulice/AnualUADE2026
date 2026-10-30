using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The tablet that moves the SP2 UV light. Each press switches the lit lamp off, with the sound of
/// an old fluorescent tube dying where that lamp hangs, and lights the next box's lamp once
/// <see cref="travelSeconds"/> have passed: the time the screen takes to show the light moving
/// across its map (<see cref="RevealLampTabletScreen"/>). The order is the boxes' id order, round
/// and round.
///
/// It only works while the levers have the lamps on (<see cref="BoxSymbolRevealLightPositions.IsPowered"/>);
/// otherwise it says so in the prompt's grey info style. It is out of reach while a move lasts.
///
/// Which lamps exist and which one is lit is <see cref="BoxSymbolRevealLightPositions"/>'s job; this
/// only asks it to change, and nothing stores where the light was left.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RevealLampTablet : BaseRangeInteractable
{
    [Tooltip("The UV lamps this tablet moves (the root of Reflector Symbol Reveal).")]
    [SerializeField] private BoxSymbolRevealLightPositions lamps;

    [Tooltip("The screen that shows the move. Left empty it is taken from this object's children.")]
    [SerializeField] private RevealLampTabletScreen screen;

    [Header("Text")]
    [SerializeField] private string movePrompt = "Move the UV light";
    [SerializeField] private string noPowerInfo = "The UV light is off";

    [Header("Audio")]
    [Tooltip("The tablet's own beep, played on the tablet. Empty = none.")]
    [SoundId] [SerializeField] private string pressSoundId = "sfx_interaction_panel_control_01";

    [Tooltip("The lamp going out, played where the lamp that goes out hangs.")]
    [SoundId] [SerializeField] private string lampOffSoundId = "sfx_uv_lamp_off";

    [Header("Timing")]
    [Tooltip("Seconds between the old lamp going out and the new one coming on. The screen's light " +
             "travels between the two for exactly this long.")]
    [SerializeField, Min(0f)] private float travelSeconds = 1.2f;

    private bool moving;

    protected override void Awake()
    {
        base.Awake();
        if (screen == null) screen = GetComponentInChildren<RevealLampTabletScreen>(true);

        if (lamps == null)
        {
            Debug.LogWarning($"[{nameof(RevealLampTablet)}] '{name}' has no lamps assigned. It will " +
                             "never move the UV light.", this);
        }
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetPromptText() => movePrompt;

    // Shown by the prompt in its grey info style while the tablet cannot be used. Nothing while a
    // move lasts: the prompt just comes back when it ends.
    public override string GetInfoText() => HasPower || moving ? string.Empty : noPowerInfo;

    protected override bool CanInteractInCloseRange() => HasPower && !moving && lamps.Count > 1;

    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        if (!CanInteractInCloseRange()) return;

        int from = lamps.Selected;
        int to = (from + 1) % lamps.Count;

        PlaySound(pressSoundId, transform.position);
        MoveAsync(from, to, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private bool HasPower => lamps != null && lamps.IsPowered;

    // ── The move ────────────────────────────────────────────────────────────
    //
    // UniTask, per the project's async convention. Scaled time on purpose: the pause freezes the
    // sound, the screen and the light that follows them. Marked busy before the first await, so the
    // prompt refresh right after the press already finds the tablet busy.

    private async UniTaskVoid MoveAsync(int from, int to, CancellationToken token)
    {
        moving = true;
        InteractionEvents.RequestPromptRefresh();

        try
        {
            PlaySound(lampOffSoundId, lamps.PositionOf(from));
            lamps.TurnOff();
            if (screen != null) screen.ShowMove(from, to, travelSeconds);

            float elapsed = 0f;
            while (elapsed < travelSeconds)
            {
                await UniTask.NextFrame(token, cancelImmediately: true);
                elapsed += Time.deltaTime;
            }
        }
        finally
        {
            // Also on cancel: whatever happens, the room is never left without its UV light.
            if (lamps != null) lamps.TurnOn(to);
            moving = false;
            InteractionEvents.RequestPromptRefresh();
        }
    }

    private static void PlaySound(string id, Vector3 position)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(id, position);
    }
}
