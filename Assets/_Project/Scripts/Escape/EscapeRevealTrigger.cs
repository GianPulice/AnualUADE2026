using UnityEngine;

/// <summary>
/// The patch of corridor, a few steps out of the centre door, where the Nemesis shows itself
/// (<see cref="EscapeSequenceDirector"/>'s reveal). A volume and nothing else: the director asks
/// whether the player is inside it while it waits for them, rather than listening for them to walk
/// in, so a player already standing in it when the lock-down ends counts too — a trigger event only
/// fires on the way in.
///
/// SETUP: a Box Collider (Is Trigger, set on its own when the component is added) spanning the
/// corridor just past the door, so the reveal arms as soon as the player is out. The one in
/// Zona1 is under EscapeSequence/Stage.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EscapeRevealTrigger : MonoBehaviour
{
    private Collider area;

    private void Awake() => area = GetComponent<Collider>();

    private void Reset()
    {
        // A solid collider would stop the player in the doorway instead.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    /// <summary>Whether the point (the player's feet) is inside the volume.</summary>
    public bool Contains(Vector3 point)
    {
        if (area == null) area = GetComponent<Collider>();
        if (area == null || !area.enabled) return false;

        // ClosestPoint hands a point inside the collider back unchanged.
        return (area.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 0.72f, 0.38f, 0.2f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(1f, 0.72f, 0.38f, 0.9f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
