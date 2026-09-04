using UnityEngine;

/// <summary>
/// The invisible wall at a freight-elevator landing: solid while the cabin is somewhere else, open
/// once it has arrived.
///
/// It exists so the shaft cannot be walked into. Without it a player at the top landing can stroll
/// straight off the edge into the empty shaft, and the call panel stops being a decision — you
/// never have to wait for the cabin, you just fall to the floor below.
///
/// <b>Place it at the LANDING, not on the cabin.</b> It has to stay behind when the lift leaves;
/// parented to the cabin it would travel with the player and wall them in wherever they went.
///
/// SETUP:
///   1. A GameObject at the landing edge with a Collider on layer <b>Wall</b> — that layer is
///      already in all five "what is solid" masks, so it blocks sight as well as movement.
///   2. This component on it, or on any parent. Leave both references empty to have them resolved
///      from the parents.
///   3. No renderer needed. The scene's NavMeshSurface is set to Physics Colliders, so a collider
///      alone is enough to keep the Nemesis out of the shaft too.
/// </summary>
public class ElevatorLandingBarrier : MonoBehaviour
{
    [Header("Shaft")]
    [Tooltip("The shaft this landing belongs to. Left empty it is looked up in the parents.")]
    [SerializeField] private NemesisElevatorLink elevator;

    [Header("Barrier")]
    [Tooltip("The collider switched on and off. Left empty it is taken from this GameObject.\n\n" +
             "Only the collider is toggled, never the GameObject: disabling the object would take " +
             "this component down with it and the barrier could never come back.")]
    [SerializeField] private Collider barrier;

    [Tooltip("Invert the rule: solid while the cabin IS here, open while it is away. Off is the " +
             "normal case — you are being kept out of an empty shaft.")]
    [SerializeField] private bool invert;

    private bool isConfigured;

    /// <summary>
    /// Deliberately not a bool defaulting to false: the first Refresh has to apply the state even
    /// when it happens to match, because the collider's authored enabled flag may disagree with
    /// where the cabin actually starts.
    /// </summary>
    private bool? lastSolid;

    private void Awake()
    {
        if (elevator == null) elevator = GetComponentInParent<NemesisElevatorLink>();
        if (barrier == null) barrier = GetComponent<Collider>();

        isConfigured = elevator != null && barrier != null;

        if (!isConfigured)
        {
            Debug.LogError($"[{nameof(ElevatorLandingBarrier)}] '{name}' is missing its " +
                           $"{nameof(NemesisElevatorLink)} or its Collider, so the shaft at this " +
                           "landing is left permanently open. Assign them, or parent this under " +
                           "the elevator root.", this);
            enabled = false;
            return;
        }

        if (barrier.isTrigger)
        {
            Debug.LogWarning($"[{nameof(ElevatorLandingBarrier)}] '{name}': the barrier collider " +
                             "is a trigger, so it will never stop anyone. Turn Is Trigger off.", this);
        }
    }

    /// <summary>
    /// Applied in Start rather than Awake so the barrier reads a cabin whose own Awake has already
    /// run — NemesisElevatorLink measures the shaft and calibrates the ride distance there, and a
    /// position sampled before that can name the wrong landing.
    /// </summary>
    private void Start() => Refresh();

    /// <summary>
    /// Polled rather than driven by MovingPlatform.OnRideCompleted, because arriving is only one of
    /// the four ways the cabin's whereabouts change: it also LEAVES (by the ride button, by a call
    /// panel at the other landing, by the Nemesis claiming it, and by autoReturnToBottom). Only the
    /// arrival raises an event, so an event-driven barrier would open correctly and then never
    /// close again. The check itself is two float comparisons.
    /// </summary>
    private void Update()
    {
        if (isConfigured) Refresh();
    }

    private void Refresh()
    {
        bool cabinHere = elevator.IsCabinOnSameSideAs(transform.position);

        // Solid while the cabin is AWAY: that is the state the player has to be protected from.
        bool solid = invert ? cabinHere : !cabinHere;

        if (lastSolid == solid) return;

        lastSolid = solid;
        barrier.enabled = solid;
    }
}
