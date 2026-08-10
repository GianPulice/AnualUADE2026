using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hearing sensor of the Nemesis.
///
/// The tuneable values (range, wall occlusion) live in <see cref="SO_NemesisData"/> so a
/// designer edits them in one asset and Tier 3.3 can scale them by handing this component a
/// runtime copy of the SO through <see cref="SetData"/>. The LayerMasks stay here: those are
/// scene wiring, not design values.
/// </summary>
public class FieldOfListening : MonoBehaviour
{
    [Tooltip("Seconds between hearing sweeps. Kept on the component: it is a performance knob, " +
             "not a design value.")]
    [SerializeField] private float listenDelay = 0.1f;

    [SerializeField] private LayerMask listenMask;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents.")]
    [SerializeField] private SO_NemesisData nemesisData;

    private List<GameObject> listenedTargets;
    private float currentTimer = 0;
    private bool hasAudioTarget = false;
    private Vector3 lastKnownPosition;

    public bool HasAudioTarget { get => hasAudioTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    private void Awake()
    {
        listenedTargets = new List<GameObject>();

        if (nemesisData != null) return;

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
            Debug.LogError($"[{nameof(FieldOfListening)}] No SO_NemesisData assigned and none " +
                           $"found in the parents — the Nemesis will not hear anything.", this);
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

    // Update is called once per frame
    void Update()
    {
        // Same guard as NemesisStateManager: this Update is its own, so without it the
        // Nemesis kept hearing (and reacting) with the game paused.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (currentTimer < listenDelay) currentTimer += Time.deltaTime;
        else
        {
            currentTimer = 0;
            ListenTargets();
        }
    }
    private void ListenTargets()
    {
        if (nemesisData == null) return;

        float listenRange = nemesisData.ListenRange;

        listenedTargets.Clear();
        Collider[] targetsInListenRadius = Physics.OverlapSphere(transform.position, listenRange, listenMask);
        for (int i = 0; i < targetsInListenRadius.Length; i++)
        {
            GameObject target = targetsInListenRadius[i].gameObject;
            if (listenedTargets.Contains(target)) continue;

            if (nemesisData.WallOcclusionEnabled && IsOccludedByWall(target.transform.position))
            {
                // A wall doesn't block sound outright, it attenuates it: only heard within
                // the reduced range. OverlapSphere already guarantees distance <= listenRange.
                float distance = Vector3.Distance(transform.position, target.transform.position);
                if (distance > listenRange * nemesisData.WallOcclusionMultiplier) continue;
            }

            listenedTargets.Add(target);
        }
        if(listenedTargets.Count > 0)
        {
            hasAudioTarget = true;
            lastKnownPosition = listenedTargets[0].transform.position;
        }
        else hasAudioTarget = false;
    }

    /// <summary>
    /// True if a wall stands between this sensor and the given point.
    /// Public so the Nemesis audio (Tier 2.6) can attenuate its loops through the same raycast
    /// instead of reimplementing it.
    /// </summary>
    public bool IsOccludedByWall(Vector3 targetPosition) =>
        IsOccludedByWall(transform.position, targetPosition);

    /// <summary>
    /// Same test from an arbitrary origin. Used by the stuck detection to ask "can the player
    /// see this waypoint?", which reuses the obstacleMask already wired on this component
    /// instead of adding a second one to the state manager.
    /// </summary>
    public bool IsOccludedByWall(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        return Physics.Raycast(origin, toTarget / distance, distance, obstacleMask);
    }
}
