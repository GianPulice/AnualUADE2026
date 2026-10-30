using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Switch of the SP2 (boxes) room. Without power (SP1 not completed) it still takes the interaction
/// highlight, makes no sound and says it needs power, and its lights (<see cref="objects"/>) start
/// switched off. Once SP1 is completed it toggles them: on activates every object in the array, off
/// deactivates them again, with a click and a lever animation each time, plus the sound of the
/// lights themselves (<see cref="turnOnSoundId"/> / <see cref="turnOffSoundId"/>).
///
/// TURNING ON IS A SEQUENCE, TURNING OFF IS NOT. The lights come on one by one, each at the moment of
/// its click in the turn-on sound (<see cref="turnOnTimes"/>), nearest to this switch first; off is
/// every light at once. The switch is out of reach while a run lasts, and the second button of the
/// room waits for it too: they share the lights, so they share the run.
///
/// <see cref="LevelLightsOn"/> answers whether the level's lights are on, for things that only make
/// sense with light (the <see cref="LookDownTrigger"/> spots).
/// </summary>
[RequireComponent(typeof(Collider))]
public class PoweredLightSwitch : BaseRangeInteractable
{
    [Tooltip("The lights. Deactivated at start while SP1 is not completed; the switch toggles them.")]
    [SerializeField] private GameObject[] objects = new GameObject[0];

    [Tooltip("SP1 completed: the switch has power. Set automatically when the puzzle below completes.")]
    [SerializeField] private bool sp1Completed;

    [PuzzleId]
    [SerializeField] private string sp1PuzzleId = "sp1_panel_electrico";

    [Tooltip("ON: if this button's GameObject is inactive (activeSelf false) it does nothing — no " +
             "toggle, no look change. For a second button that shares the same objects.")]
    [SerializeField] private bool ignoreIfInactive = true;

    [Header("Text")]
    [SerializeField] private string turnOnPrompt = "Turn on";
    [SerializeField] private string turnOffPrompt = "Turn off";
    [SerializeField] private string noPowerInfo = "You need to turn on the energy first";

    [Header("Audio")]
    [SoundId] [SerializeField] private string clickSoundId = "sfx_elevator_button_01";

    [Tooltip("The lights coming on, played together with the click. Empty = only the click.")]
    [SoundId] [SerializeField] private string turnOnSoundId = "sfx_warehouse_lights_on";

    [Tooltip("The lights going off, played together with the click. Empty = only the click.")]
    [SoundId] [SerializeField] private string turnOffSoundId = "sfx_big_switch_lights_off";

    [Header("Turning on")]
    [Tooltip("Seconds after the press at which each light comes on, in the order they are lit: the " +
             "clicks of the turn-on sound. Measured on sfx_warehouse_lights_on, which has seven: " +
             "0.69, 1.11, 1.80, 1.88, 2.48, 3.18 and 3.25 s. A light with no entry comes on " +
             "Extra Light Spacing after the previous one. Empty = all at once.")]
    [SerializeField] private float[] turnOnTimes = { 0.692f, 1.108f, 1.804f, 1.876f, 2.476f, 3.176f, 3.248f };

    [Tooltip("Seconds between lights that have no entry in Turn On Times.")]
    [SerializeField, Min(0f)] private float extraLightSpacing = 0.15f;

    [Tooltip("Light the lamps nearest to this switch first, so the light spreads out from the lever. " +
             "Off: the order of the Objects list.")]
    [SerializeField] private bool nearestFirst = true;

    [Header("Turning off")]
    [Tooltip("Seconds between the press and every light going out at once. 0 = in the press, which is " +
             "what was asked for. The big switch sound has half a second of lead before its clunk: " +
             "0.5 here puts the dark on the clunk.")]
    [SerializeField, Min(0f)] private float turnOffDelay = 0f;

    [Header("Switch animation")]
    [Tooltip("Animator driving the lever mesh. Left empty it is taken from this object's own " +
             "children, so the prefab needs no wiring at all.")]
    [SerializeField] private Animator switchAnimator;

    [Tooltip("The name of the trigger in the animator when pressed.")]
    [SerializeField] private string pressTriggerName = "Pressed";

    private int pressTriggerHash;

    // On = any of the lights is active (the first one to come on during the sequence is enough). Read
    // from the objects themselves, not stored: two buttons sharing the same array would otherwise
    // each keep their own idea of it and undo each other.
    private bool IsOn
    {
        get
        {
            foreach (GameObject go in objects)
                if (go != null && go.activeSelf) return true;
            return false;
        }
    }

    // The lights a run is changing right now, keyed by the first of them: the second button of a room
    // holds the same array, so it finds the same key and waits for the same run.
    private static readonly HashSet<GameObject> running = new HashSet<GameObject>();

    private GameObject LightsKey
    {
        get
        {
            foreach (GameObject go in objects)
                if (go != null) return go;
            return null;
        }
    }

    private bool IsRunning
    {
        get
        {
            GameObject key = LightsKey;
            return key != null && running.Contains(key);
        }
    }

    private bool HasLights
    {
        get
        {
            foreach (GameObject go in objects)
                if (go != null) return true;
            return false;
        }
    }

    private bool Ignored => ignoreIfInactive && !gameObject.activeSelf;

    // ── Level lights ────────────────────────────────────────────────────────

    // Every switch that exists in the loaded scenes. Two buttons can share one array of lights (the
    // second button of the same room), so the answer is read off the lights and not off any one button.
    private static readonly List<PoweredLightSwitch> switches = new List<PoweredLightSwitch>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        switches.Clear();
        running.Clear();
    }

    /// <summary>
    /// The level's lights are on: the lights of any switch are active. A level without a switch (or
    /// whose switches have no lights assigned) has nothing that turns the light off, so it answers
    /// true. Read when asked, not cached, so it is right whoever last moved the lights.
    /// </summary>
    public static bool LevelLightsOn
    {
        get
        {
            bool any = false;
            foreach (PoweredLightSwitch s in switches)
            {
                if (s == null || !s.HasLights) continue;
                any = true;
                if (s.IsOn) return true;
            }
            return !any;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        switches.Add(this);
        ResolveSwitchAnimation();
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void Start()
    {
        if (PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(sp1PuzzleId))
            sp1Completed = true;

        // No power yet: the room starts dark. Left as authored when SP1 is already done.
        if (!sp1Completed) SetLights(false);
    }

    private void OnDestroy()
    {
        switches.Remove(this);
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (puzzleId != sp1PuzzleId || Ignored) return;
        sp1Completed = true;
        InteractionEvents.RequestPromptRefresh();
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetPromptText() => IsOn ? turnOffPrompt : turnOnPrompt;

    // Shown by the prompt in its grey info style while the switch cannot be used.
    public override string GetInfoText() => sp1Completed ? string.Empty : noPowerInfo;

    // Out of reach while the lights are changing: the prompt goes and comes back when the run ends.
    protected override bool CanInteractInCloseRange() => sp1Completed && !Ignored && !IsRunning;

    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        if (!sp1Completed || Ignored || IsRunning) return;

        bool turningOn = !IsOn;
        PlaySwitchPress();

        PlaySound(clickSoundId);
        PlaySound(turningOn ? turnOnSoundId : turnOffSoundId);

        // On is a sequence, and off is too if it has a delay; off with none is every light in the press.
        if (turningOn || turnOffDelay > 0f) ChangeLightsAsync(turningOn, this.GetCancellationTokenOnDestroy()).Forget();
        else SetLights(false);

        // Also what makes a LookDownTrigger the player is standing in appear (or go) with the light.
        InteractionEvents.RequestPromptRefresh();
    }

    // No OnInteractAttemptBlocked override: without power, pressing it is silent.

    private void PlaySound(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(id, transform.position);
    }

    private void SetLights(bool on)
    {
        foreach (GameObject go in objects)
            if (go != null) go.SetActive(on);
    }

    // ── Sequence ────────────────────────────────────────────────────────────
    //
    // UniTask, per the project's async convention. Scaled time on purpose: the pause freezes the
    // sound (it does not ignore the listener pause) and so it has to freeze the lights that follow it.
    // The run is marked before its first await, so the prompt refresh that follows in OnInteract
    // already finds the switch busy.

    private async UniTaskVoid ChangeLightsAsync(bool turningOn, CancellationToken token)
    {
        GameObject key = LightsKey;
        if (key != null) running.Add(key);

        try
        {
            if (turningOn)
            {
                await LightOneByOneAsync(token);
            }
            else
            {
                await WaitAsync(turnOffDelay, token);
                SetLights(false);
            }
        }
        finally
        {
            if (key != null) running.Remove(key);
            InteractionEvents.RequestPromptRefresh();
        }
    }

    private async UniTask LightOneByOneAsync(CancellationToken token)
    {
        List<GameObject> order = LightsInOrder();

        float elapsed = 0f;
        for (int i = 0; i < order.Count; i++)
        {
            float at = TimeOfLight(i);
            while (elapsed < at)
            {
                await UniTask.NextFrame(token, cancelImmediately: true);
                elapsed += Time.deltaTime;
            }

            if (order[i] != null) order[i].SetActive(true);
        }
    }

    private static async UniTask WaitAsync(float seconds, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            await UniTask.NextFrame(token, cancelImmediately: true);
            elapsed += Time.deltaTime;
        }
    }

    private List<GameObject> LightsInOrder()
    {
        var order = new List<GameObject>(objects.Length);
        foreach (GameObject go in objects)
            if (go != null) order.Add(go);

        if (nearestFirst)
        {
            Vector3 from = transform.position;
            order.Sort((a, b) => (a.transform.position - from).sqrMagnitude
                                  .CompareTo((b.transform.position - from).sqrMagnitude));
        }
        return order;
    }

    /// <summary>Seconds after the press at which the light at <paramref name="index"/> comes on.</summary>
    private float TimeOfLight(int index)
    {
        if (turnOnTimes == null || turnOnTimes.Length == 0) return 0f;
        if (index < turnOnTimes.Length) return Mathf.Max(0f, turnOnTimes[index]);

        return turnOnTimes[turnOnTimes.Length - 1] + (index - turnOnTimes.Length + 1) * extraLightSpacing;
    }

    // ── Lever animation ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the Animator and resolves the trigger hash once. See
    /// <see cref="ElevatorCallPanel.ResolveSwitchAnimation"/> for why this is resolved up front
    /// rather than looked up on every press.
    /// </summary>
    private void ResolveSwitchAnimation()
    {
        if (switchAnimator == null) switchAnimator = GetComponentInChildren<Animator>(true);

        // Silent when there is none. A switch is allowed to be a flat unanimated box.
        if (switchAnimator == null) return;

        pressTriggerHash = Animator.StringToHash(pressTriggerName);
    }

    /// <summary>Throws the lever, top to bottom and back to top. A no-op with no animator.</summary>
    private void PlaySwitchPress()
    {
        if (switchAnimator == null) return;

        switchAnimator.SetTrigger(pressTriggerHash);
    }
}
