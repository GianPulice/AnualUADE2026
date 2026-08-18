using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vision sensor of the Nemesis.
///
/// The tuneable values (range, cone angle) live in <see cref="SO_NemesisData"/> so a designer
/// edits them in one asset and Tier 3.3 can scale them by handing this component a runtime copy
/// of the SO through <see cref="SetData"/>. The LayerMasks stay here: those are scene wiring,
/// not design values.
/// </summary>
public class FieldOfView : MonoBehaviour
{
    [Tooltip("Seconds between vision sweeps. Kept on the component: it is a performance knob, " +
             "not a design value.")]
    [SerializeField] private float viewDelay = 0.1f;

    [Tooltip("Inside this distance the cone is ignored, but the occlusion raycast still applies. " +
             "Models 'it is right next to me'. Not the same as the hard proximity detection in " +
             "SO_NemesisData, which ignores the raycast too.")]
    [SerializeField] private float minDistance = 1f;

    [SerializeField] private Transform viewTransform;
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents.")]
    [SerializeField] private SO_NemesisData nemesisData;

    private List<GameObject> visibleTargets;
    private GameObject lastKnownTarget;
    private float currentTimer = 0;
    private bool hasVisualTarget = false;
    private Vector3 lastKnownPosition;

    public bool HasVisualTarget { get => hasVisualTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    private void Awake()
    {
        visibleTargets = new List<GameObject>();

        // Every sweep below reads viewTransform, so an unassigned one is a NullReferenceException
        // per sweep. This component's own transform is a reasonable stand-in — hence a fallback
        // and not a hard failure — but it is worth a warning: the marker is normally placed at eye
        // height, and dropping to the object's pivot silently narrows what the cone can see.
        if (viewTransform == null)
        {
            viewTransform = transform;
            Debug.LogWarning($"[{nameof(FieldOfView)}] No {nameof(viewTransform)} assigned — " +
                             $"falling back to this object's own transform. Vision will be cast " +
                             $"from the pivot instead of from eye height.", this);
        }

        if (nemesisData != null) return;

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
            Debug.LogError($"[{nameof(FieldOfView)}] No SO_NemesisData assigned and none found " +
                           $"in the parents — the Nemesis will not see anything.", this);
    }

    /// <summary>
    /// Swaps the data asset at runtime. Tier 3.3 uses it to push a scaled copy of the SO without
    /// touching the original asset.
    /// </summary>
    public void SetData(SO_NemesisData data)
    {
        if (data == null) return;
        nemesisData = data;
    }

    private void Update()
    {
        // Same guard as NemesisStateManager: this Update is its own, so without it the
        // Nemesis kept seeing (and reacting) with the game paused.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // Extreme proximity is checked every frame and before everything else, deliberately:
        // it does not wait for the viewDelay cadence and it is the only thing that defeats
        // Hidden. Skipping the normal sweep when it hits also stops FindVisibleTargets from
        // immediately clearing the flag it just set.
        if (CheckExtremeProximity()) return;

        if (currentTimer < viewDelay) currentTimer += Time.deltaTime;
        else
        {
            currentTimer = 0;
            FindVisibleTargets();
        }
    }

    /// <summary>
    /// Hard detection: inside <c>proximityDetectionRange</c> the Nemesis notices the player no
    /// matter what — no cone, no occlusion raycast, and hiding does not save you. Forcing
    /// <see cref="HasVisualTarget"/> is enough to route the FSM into Chasing, since every state
    /// already transitions on that flag.
    /// </summary>
    /// <returns>true if the player was detected by proximity this frame.</returns>
    private bool CheckExtremeProximity()
    {
        if (nemesisData == null) return false;

        float range = nemesisData.ProximityDetectionRange;
        if (range <= 0f) return false;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return false;

        Vector3 playerPosition = player.transform.position;
        if (Vector3.Distance(viewTransform.position, playerPosition) > range) return false;

        hasVisualTarget = true;
        lastKnownTarget = player.gameObject;
        lastKnownPosition = playerPosition;
        return true;
    }

    public void FindVisibleTargets()
    {
        if (nemesisData == null) return;

        PlayerStateManager player = PlayerRegistry.Current;

        // Hidden means inside a locker or under a table: normal vision cannot reach the player
        // at all. Extreme proximity, already checked in Update before this runs, is the only
        // way out of Hidden — so getting here with IsHidden means it did not trigger.
        if (player != null && player.IsHidden)
        {
            hasVisualTarget = false;
            return;
        }

        float viewRange = nemesisData.ViewRange;
        float viewAngle = nemesisData.ViewAngle;

        // Crouching shortens the range it can be spotted at rather than breaking line of sight:
        // a lower silhouette is harder to pick out, not invisible.
        if (player != null && player.IsCrouch) viewRange *= nemesisData.CrouchVisionMultiplier;

        visibleTargets.Clear();
        Collider[] targetsInViewRadius = Physics.OverlapSphere(viewTransform.position, viewRange, targetMask);
        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            for (int j = -1; j < 2; j++)
            {
                Vector3 targetPoint = targetsInViewRadius[i].bounds.center + new Vector3(0f, j * targetsInViewRadius[i].bounds.extents.y * 0.9f, 0f);
                float distToTarget = Vector3.Distance(viewTransform.position, targetPoint);
                Vector3 dirToTarget = (targetPoint - viewTransform.position).normalized;
                if (Vector3.Angle(viewTransform.forward, dirToTarget) < viewAngle / 2 || distToTarget <= minDistance)
                {
                    if (!Physics.Raycast(viewTransform.position, dirToTarget, distToTarget, obstacleMask))
                    {
                        if (!visibleTargets.Contains(targetsInViewRadius[i].gameObject))
                        {
                            visibleTargets.Add(targetsInViewRadius[i].gameObject);
                            lastKnownTarget = targetsInViewRadius[i].gameObject;
                        }
                    }
                }
            }
        }
        if (visibleTargets.Count > 0)
        {
            hasVisualTarget = true;
            lastKnownPosition = visibleTargets[0].transform.position;
        }
        else hasVisualTarget = false;
    }
    /// <summary>
    /// Last target seen, or null if nobody has been seen yet / the object was destroyed.
    /// Returns null instead of throwing: the catch state calls this and cannot assume
    /// there is always a target.
    /// </summary>
    public PlayerStateManager GetCurrentTarget()
    {
        if (lastKnownTarget == null) return null;

        // InParent and not GetComponent: lastKnownTarget is whichever collider FindVisibleTargets
        // happened to land on, and the entire player hierarchy sits on the Player layer — the rig
        // mesh and the AudioEmitingRange trigger match targetMask exactly as much as the root
        // capsule does. Reading the component off the collider's own GameObject returned null
        // whenever the sweep picked one of those, which dropped Catch into its "nobody to
        // capture" fallback at random. CheckExtremeProximity never hit this because it stores the
        // player root directly, which is why it only failed some of the time.
        return lastKnownTarget.GetComponentInParent<PlayerStateManager>();
    }
}
