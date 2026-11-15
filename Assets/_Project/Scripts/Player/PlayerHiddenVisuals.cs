using UnityEngine;

/// <summary>
/// Switches the player's visible parts off while they are inside a hiding spot, and back on the
/// moment they leave — before the climb-out starts, so the door opens on a player who is there.
///
/// Why: the modules the player carries hang off the rig's bones, each with its own LED light.
/// Inside a locker those lights are the only light source, and they painted the inside of the door
/// orange. With the model off the inside of the spot is dark, as it should be.
///
/// ONLY THE VISIBLE PARTS, NEVER THE PLAYER ROOT. PlayerStateManager runs the hidden state from
/// the root: the breathing the Nemesis hears, holding breath with F and its meter. Switching the
/// root off would stop all of it and make hiding total silence. Put the model root here
/// (HS_CHARA_RIG: body, Animator and the modules with their lights), not the player.
///
/// Off on <see cref="HidingEvents.OnEntered"/>, which comes once the camera has cut inside; back
/// on <see cref="HidingEvents.OnExited"/>, which every way out raises — getting out (right before
/// the climb-out), a capture, a respawn, a cinematic, the spot going away.
///
/// The Animator restarts from its entry state when the model comes back. Nothing in the hidden
/// state writes to it, and the state the player leaves into sets its own parameters.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Player/Player Hidden Visuals")]
public class PlayerHiddenVisuals : MonoBehaviour
{
    [Tooltip("Switched off while the player is inside a hiding spot. The model root, with the " +
             "modules and their lights under its bones. Never the player root itself.")]
    [SerializeField] private GameObject[] hideWhileHidden = new GameObject[0];

    // What each one was before we switched it off, so one a designer left off stays off.
    private bool[] wasActive = new bool[0];
    private bool hidden;

    // Awake/OnDestroy for the static events, as the project does everywhere.
    private void Awake()
    {
        HidingEvents.OnEntered += HandleEntered;
        HidingEvents.OnExited += HandleExited;
    }

    private void OnDestroy()
    {
        HidingEvents.OnEntered -= HandleEntered;
        HidingEvents.OnExited -= HandleExited;
    }

    private void HandleEntered(HidingSpot spot)
    {
        if (hidden) return;
        hidden = true;

        if (wasActive.Length != hideWhileHidden.Length) wasActive = new bool[hideWhileHidden.Length];
        for (int i = 0; i < hideWhileHidden.Length; i++)
        {
            GameObject target = hideWhileHidden[i];
            if (target == null) continue;

            wasActive[i] = target.activeSelf;
            target.SetActive(false);
        }
    }

    private void HandleExited(HidingSpot spot)
    {
        if (!hidden) return;
        hidden = false;

        for (int i = 0; i < hideWhileHidden.Length && i < wasActive.Length; i++)
        {
            GameObject target = hideWhileHidden[i];
            if (target != null && wasActive[i]) target.SetActive(true);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        for (int i = 0; i < hideWhileHidden.Length; i++)
        {
            if (hideWhileHidden[i] != null && hideWhileHidden[i] == gameObject)
            {
                Debug.LogWarning($"[{nameof(PlayerHiddenVisuals)}] The player root is in Hide While " +
                                 "Hidden. That stops the breathing and the hold-breath logic; put " +
                                 "the model root there instead.", this);
            }
        }
    }
#endif
}
