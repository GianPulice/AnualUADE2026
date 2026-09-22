using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One on-screen help: "USE [WASD] TO MOVE". Authored on an <see cref="InputHintTrigger"/>.
///
/// Normally it names an Input System action (<see cref="inputAction"/>): the key or button between
/// brackets is read from that action's bindings for the device the player is using — keyboard or
/// gamepad — and doing the action closes the hint early. <see cref="keys"/> / <see cref="gamepadKeys"/>
/// only override the label when the binding's own name reads badly (a composite like WASD).
/// </summary>
[Serializable]
public class InputHint
{
    [Tooltip("Identifies the hint. A hint shows once per run: another trigger with the same id stays " +
             "quiet. Empty = input action (or keys) + action text.")]
    public string id = string.Empty;

    [Tooltip("The Input System action it teaches, as Map/Action (Player/Sprint). The label between " +
             "brackets comes from its binding for the device in use, and performing it closes the " +
             "hint early. Empty = only the labels below.")]
    [InputActionPath] public string inputAction = string.Empty;

    [Tooltip("Keyboard label, shown between brackets. E.g. WASD. Empty = read from the action's " +
             "Keyboard&Mouse binding.")]
    public string keys = string.Empty;

    [Tooltip("Gamepad label. E.g. L STICK. Empty = read from the action's Gamepad binding (falls " +
             "back to the keyboard label if it has none).")]
    public string gamepadKeys = string.Empty;

    [Tooltip("What it does. E.g. move, interact, open the inventory.")]
    public string action = "move";

    [Tooltip("Seconds on screen before it fades.")]
    [Min(0.5f)] public float seconds = 5f;

    [Tooltip("Legacy keys that also count as having done it. Not needed when Input Action is set.")]
    public KeyCode[] dismissKeys = Array.Empty<KeyCode>();

    [Tooltip("Moving (the Horizontal / Vertical axes) counts as having done it.")]
    public bool dismissOnMove;

    public string Id
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(id)) return id;
            string what = string.IsNullOrWhiteSpace(inputAction) ? keys : inputAction;
            return $"{what}|{action}";
        }
    }

    /// <summary>The action named by <see cref="inputAction"/> in the project-wide actions, or null.</summary>
    public InputAction Action => InputHintEvents.FindAction(inputAction);

    /// <summary>True if there is something to put between the brackets.</summary>
    public bool HasLabel =>
        !string.IsNullOrWhiteSpace(keys) || !string.IsNullOrWhiteSpace(gamepadKeys) || Action != null;

    /// <summary>What goes between the brackets for keyboard or gamepad.</summary>
    public string LabelFor(bool gamepad)
    {
        string authored = gamepad ? gamepadKeys : keys;
        if (!string.IsNullOrWhiteSpace(authored)) return authored;

        string bound = InputHintEvents.BindingLabel(Action, gamepad ? GamepadGroup : KeyboardGroup);
        if (!string.IsNullOrEmpty(bound)) return bound;

        // A gamepad with no binding for this: better the keyboard key than an empty bracket.
        return gamepad ? LabelFor(false) : gamepadKeys;
    }

    private const string KeyboardGroup = "Keyboard&Mouse";
    private const string GamepadGroup = "Gamepad";
}

/// <summary>
/// Static bus for input hints, and the once-per-run bookkeeping. <see cref="InputHintView"/> listens;
/// anything can raise one — an <see cref="InputHintTrigger"/> or code.
/// </summary>
public static class InputHintEvents
{
    private static readonly HashSet<string> shown = new HashSet<string>();

    public static event Action<InputHint> OnHint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnHint = null;
        shown.Clear();
    }

    // After GameSession clears its event in SubsystemRegistration (same load type = no order), or
    // the hook is wiped and the hints never show again on a second run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void HookSessionReset()
    {
        GameSession.OnNewSessionStarting -= ClearShown;
        GameSession.OnNewSessionStarting += ClearShown;
    }

    // A New Game or a Retry is a new run: every help shows again. A checkpoint respawn is not.
    private static void ClearShown() => shown.Clear();

    public static bool HasShown(string id) => shown.Contains(id);

    /// <summary>Shows the hint unless one with its id already showed this run.</summary>
    /// <returns>True if it was sent to the screen.</returns>
    public static bool Show(InputHint hint)
    {
        if (hint == null || !hint.HasLabel) return false;

        // With nothing listening (no HUD loaded) the hint is not spent: it can still show later.
        if (OnHint == null || shown.Contains(hint.Id)) return false;

        shown.Add(hint.Id);
        OnHint.Invoke(hint);
        return true;
    }

    // ── Input System ─────────────────────────────────────────────────────────────────────

    /// <summary>"Player/Sprint" in the project-wide actions (Project Settings > Input System).</summary>
    public static InputAction FindAction(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        InputActionAsset asset = InputSystem.actions;
        return asset != null ? asset.FindAction(path, throwIfNotFound: false) : null;
    }

    /// <summary>
    /// True when the last thing the player touched was a gamepad rather than the keyboard or mouse.
    /// Devices only send state when something changes, so the most recent update is the one in use.
    /// </summary>
    public static bool UsingGamepad
    {
        get
        {
            Gamepad pad = Gamepad.current;
            if (pad == null) return false;

            double keyboard = Keyboard.current != null ? Keyboard.current.lastUpdateTime : 0d;
            double mouse = Mouse.current != null ? Mouse.current.lastUpdateTime : 0d;
            return pad.lastUpdateTime > Math.Max(keyboard, mouse);
        }
    }

    /// <summary>
    /// The first binding of <paramref name="action"/> in a control scheme's group. Only the first
    /// one: "E | Enter" would read worse than just "E".
    ///
    /// Keyboard keys are named from the layout ("Left Shift", "Tab"), always in English: the
    /// connected keyboard's own names come from the OS layout, so on a Spanish Windows they read
    /// "Mayús", "Tabulador". Gamepad buttons are named from the connected pad ("Y" on Xbox,
    /// "Triangle" on PlayStation), which is not localized.
    /// </summary>
    public static string BindingLabel(InputAction action, string group)
    {
        bool fromLayout = group != "Gamepad";

        if (action == null) return null;

        InputBinding mask = InputBinding.MaskByGroup(group);
        var bindings = action.bindings;

        for (int i = 0; i < bindings.Count; i++)
        {
            InputBinding binding = bindings[i];
            if (binding.isPartOfComposite) continue;

            bool inGroup;
            if (binding.isComposite)
            {
                // The group lives on the parts, not on the composite itself.
                inGroup = false;
                for (int j = i + 1; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                    if (mask.Matches(bindings[j])) { inGroup = true; break; }
            }
            else
            {
                inGroup = mask.Matches(binding);
            }

            if (!inGroup) continue;

            if (!fromLayout)
                return action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);

            if (!binding.isComposite) return LayoutName(binding);

            // A composite (WASD): its parts in the group, in order, once per part name ("up" is
            // bound to both W and the up arrow; the first one wins).
            var parts = new List<string>();
            var seen = new HashSet<string>();
            for (int j = i + 1; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                if (mask.Matches(bindings[j]) && seen.Add(bindings[j].name))
                    parts.Add(LayoutName(bindings[j]));
            return string.Join("/", parts);
        }

        return null;
    }

    /// <summary>"&lt;Keyboard&gt;/leftShift" → "Left Shift", from the layout, not the device.</summary>
    private static string LayoutName(InputBinding binding) =>
        InputControlPath.ToHumanReadableString(binding.effectivePath,
                                               InputControlPath.HumanReadableStringOptions.OmitDevice);
}
