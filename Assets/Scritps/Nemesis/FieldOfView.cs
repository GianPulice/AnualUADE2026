using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class FieldOfView : MonoBehaviour
{
    [SerializeField] private float viewRange;
    [Range(0, 360)]
    [SerializeField] private float viewAngle;
    [SerializeField] private float viewDelay = 0.1f;
    [SerializeField] private Transform viewTransform;
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstacleMask;

    private List<GameObject> visibleTargets;
    private float currentTimer = 0;
    private bool hasVisualTarget = false;
    private Vector3 lastKnownPosition;

    public bool HasVisualTarget { get => hasVisualTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    private void Start()
    {
        visibleTargets = new List<GameObject>();
    }
    private void Update()
    {
        if (currentTimer < viewDelay) currentTimer += Time.deltaTime;
        else 
        {
            currentTimer = 0;
            FindVisibleTargets();
        }
    }

    public void FindVisibleTargets()
    {
        visibleTargets.Clear();
        Collider[] targetsInViewRadius = Physics.OverlapSphere(viewTransform.position, viewRange, targetMask);
        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            for (int j = -1; j < 2; j++)
            {
                Vector3 targetPoint = targetsInViewRadius[i].bounds.center + new Vector3(0f, j * targetsInViewRadius[i].bounds.extents.y * 0.9f, 0f);
                float distToTarget = Vector3.Distance(viewTransform.position, targetPoint);
                Vector3 dirToTarget = (targetPoint - viewTransform.position).normalized;
                if (Vector3.Angle(viewTransform.forward, dirToTarget) < viewAngle / 2)
                {

                    if (!Physics.Raycast(viewTransform.position, dirToTarget, distToTarget, obstacleMask))
                    {
                        if (!visibleTargets.Contains(targetsInViewRadius[i].gameObject)) visibleTargets.Add(targetsInViewRadius[i].gameObject);
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
}
