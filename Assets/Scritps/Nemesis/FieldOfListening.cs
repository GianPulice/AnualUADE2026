using System.Collections.Generic;
using UnityEngine;

public class FieldOfListenig : MonoBehaviour
{
    [SerializeField] private float listenRange = 0;
    [SerializeField] private float listenDelay = 0.1f;
    [SerializeField] private LayerMask listenMask;

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
            if (!listenedTargets.Contains(targetsInListenRadius[i].gameObject)) listenedTargets.Add(targetsInListenRadius[i].gameObject);
        }
        if(listenedTargets.Count > 0) 
        {
            hasAudioTarget = true;
            lastKnownPosition = listenedTargets[0].transform.position;
        }
        else hasAudioTarget = false;
    }
}
