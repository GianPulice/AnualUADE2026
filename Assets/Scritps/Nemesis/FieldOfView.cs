using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class FieldOfView : MonoBehaviour
{
    [SerializeField] private float viewRange;
    [Range(0, 360)]
    [SerializeField] private float viewAngle;
    [SerializeField] private Transform viewTransform;
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstacleMask;
    private List<GameObject> visibleTargets;

    private float TemporalViewDelay = 0;
    private List<Vector3> gizmoPoint;

    private void Start()
    {
        visibleTargets = new List<GameObject>();
        gizmoPoint = new List<Vector3>();
    }
    private void Update()
    {
        if (TemporalViewDelay < 0.5f)
        {
            TemporalViewDelay += Time.deltaTime;
        }
        else
        {
            gizmoPoint.Clear();
            FindVisibleTargets();
            Debug.Log(visibleTargets.Count);
            TemporalViewDelay = 0;
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
                //gizmoPoint.Add(targetPoint);
                //Debug.Log("Target Point de j = " + j + " : " + targetPoint);
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
    }
    public Vector3 DirFromAngle(float AnglesInDegrees, bool AngleIsGlobal)
    {
        if (!AngleIsGlobal)
        {
            AnglesInDegrees += transform.eulerAngles.y;
        }
        return new Vector3(Mathf.Sin(AnglesInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(AnglesInDegrees * Mathf.Deg2Rad));
    }
    /*private void OnDrawGizmos()
    {
        foreach (Vector3 t in gizmoPoint) 
        {
            Gizmos.DrawLine(viewTransform.position, t);
        }
    }*/
}
