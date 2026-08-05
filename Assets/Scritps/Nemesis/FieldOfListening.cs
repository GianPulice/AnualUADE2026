using System.Collections.Generic;
using UnityEngine;

public class FieldOfListenig : MonoBehaviour
{
    [SerializeField] private float listenRange = 0;
    [SerializeField] private float listenDelay = 0.1f;
    [SerializeField] private LayerMask listenMask;

    [Header("Wall occlusion")]
    [SerializeField] private bool wallOcclusionEnabled = true;
    [SerializeField] private LayerMask obstacleMask;
    [Tooltip("Effective range through a wall = listenRange * this. Spec default: 0.6.")]
    [SerializeField, Range(0f, 1f)] private float wallOcclusionMultiplier = 0.6f;

    private List<GameObject> listenedTargets;
    private float currentTimer = 0;
    private bool hasAudioTarget = false;
    private Vector3 lastKnownPosition;

    public bool HasAudioTarget { get => hasAudioTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        listenedTargets = new List<GameObject>();
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
        listenedTargets.Clear();
        Collider[] targetsInListenRadius = Physics.OverlapSphere(transform.position, listenRange, listenMask);
        for (int i = 0; i < targetsInListenRadius.Length; i++)
        {
            GameObject target = targetsInListenRadius[i].gameObject;
            if (listenedTargets.Contains(target)) continue;

            if (wallOcclusionEnabled && IsOccludedByWall(target.transform.position))
            {
                // A wall doesn't block sound outright, it attenuates it: only heard within
                // the reduced range. OverlapSphere already guarantees distance <= listenRange.
                float distance = Vector3.Distance(transform.position, target.transform.position);
                if (distance > listenRange * wallOcclusionMultiplier) continue;
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

    private bool IsOccludedByWall(Vector3 targetPosition)
    {
        Vector3 origin = transform.position;
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        return Physics.Raycast(origin, toTarget / distance, distance, obstacleMask);
    }
}
