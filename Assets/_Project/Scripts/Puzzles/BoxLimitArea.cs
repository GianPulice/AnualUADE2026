using UnityEngine;

/// <summary>
/// The floor area push boxes may not be pushed out of — typically the whole room of a box puzzle,
/// stopping short of its doors. Drop the LimitColliderForBoxes prefab into the scene and size its
/// BoxCollider to cover the room; nothing else to wire.
///
/// A box belongs to the area whose volume holds its centre when the player grabs it. While pushed,
/// a move that would carry any of its corners further out of that area is blocked like a wall, so
/// the box and the player stop together (see <see cref="PlayerBoxInteractingState"/>). A box in no
/// area is not limited at all, exactly as before this existed. The collider need not sit on the
/// floor, only wrap the boxes vertically; past that, only its floor plan limits them.
///
/// The collider is only a shape to size in the Scene view. It is switched off on Awake, so in play
/// it never collides, never fires a trigger and never stops a raycast — the player, the Nemesis and
/// everything else go through the doors as usual. Ungrabbed boxes weigh 1000 and the push is the
/// only thing that moves them, so blocking the push is enough to keep them in.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class BoxLimitArea : MonoBehaviour
{
    private static readonly Color GizmoColor = new Color(1f, 0.55f, 0f, 1f);

    private BoxCollider area;

    private BoxCollider Area
    {
        get
        {
            if (area == null) area = GetComponent<BoxCollider>();
            return area;
        }
    }

    private void Reset()
    {
        BoxCollider c = GetComponent<BoxCollider>();
        if (c != null) c.isTrigger = true;
    }

    private void Awake()
    {
        if (Area != null) Area.enabled = false;
    }

    /// <summary>
    /// The area whose volume holds the point, or null. With nested areas the smallest one wins, so
    /// a small area inside a larger one still applies.
    ///
    /// Height counts here, and only here: it is what keeps an area on one floor from claiming the
    /// boxes on the floor below it. Once a box belongs to an area, only the floor plan limits it.
    /// </summary>
    public static BoxLimitArea FindContaining(Vector3 worldPoint)
    {
        BoxLimitArea best = null;
        float bestSize = float.PositiveInfinity;

        foreach (BoxLimitArea candidate in FindObjectsByType<BoxLimitArea>(FindObjectsInactive.Exclude))
        {
            if (!candidate.isActiveAndEnabled || candidate.Area == null) continue;
            if (!candidate.Encloses(worldPoint)) continue;

            float size = candidate.FloorSize();
            if (size < bestSize)
            {
                bestSize = size;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// How far the point is outside the area on the floor plan, 0 when inside. Measured in the
    /// area's own axes, so a rotated area works too. Only meant for comparing two points against
    /// the same area.
    /// </summary>
    public float DistanceOutside(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint) - Area.center;
        Vector3 scale = transform.lossyScale;
        Vector3 half = Area.size * 0.5f;

        float dx = Mathf.Max(0f, Mathf.Abs(local.x) - Mathf.Abs(half.x)) * Mathf.Abs(scale.x);
        float dz = Mathf.Max(0f, Mathf.Abs(local.z) - Mathf.Abs(half.z)) * Mathf.Abs(scale.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private bool Encloses(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint) - Area.center;
        Vector3 half = Area.size * 0.5f;
        return Mathf.Abs(local.x) <= Mathf.Abs(half.x) &&
               Mathf.Abs(local.y) <= Mathf.Abs(half.y) &&
               Mathf.Abs(local.z) <= Mathf.Abs(half.z);
    }

    private float FloorSize()
    {
        Vector3 scale = transform.lossyScale;
        return Mathf.Abs(Area.size.x * scale.x) * Mathf.Abs(Area.size.z * scale.z);
    }

    // Always drawn, not only when selected: the collider is off in play and the level has to show
    // where the boxes are held.
    private void OnDrawGizmos()
    {
        BoxCollider c = Area;
        if (c == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = GizmoColor;
        Gizmos.DrawWireCube(c.center, c.size);
        Gizmos.color = new Color(GizmoColor.r, GizmoColor.g, GizmoColor.b, 0.08f);
        Gizmos.DrawCube(c.center, c.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
