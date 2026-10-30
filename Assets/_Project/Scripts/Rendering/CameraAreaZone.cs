using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger volume that names the area on the player's camera: while the player is inside, the
/// camera's label, top left, reads <see cref="label"/> instead of SO_PlayerCameraFeed's default,
/// and it retypes itself on the change (<see cref="PlayerCameraFeed"/>).
///
/// Same shape as <see cref="AmbienceZone"/> and LightZone. Setup:
///   1. Empty GameObject in the gameplay scene, over the area.
///   2. A BoxCollider (or any Collider). Is Trigger is forced on when the component is added.
///   3. This component, with the text to show.
/// In the Scene view a zone only shows while it or its parent is selected: keep them under one
/// parent (in Zona1, "----- ZONE CHANGES -----") and select it to see them all.
///
/// Nesting: the innermost zone wins — enter A, then B inside it, and the label reads B; leave B and
/// it reads A again; leave A and it goes back to the default. Leaving A first (volumes that only
/// partly overlap) changes nothing on screen while the player is still in B.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CameraAreaZone : MonoBehaviour
{
    [Tooltip("The area's name, shown top left on the player's camera while they are in it (just the " +
             "name: HUB, CORRIDOR). Letters, digits and " +
             ": - / . # > % _ ! + [ ] = | (accents are dropped, anything else shows as a space). " +
             "32 at most.")]
    [SerializeField] private string label = "AREA";

    [Header("Detection")]
    [Tooltip("Tag of the GameObject that fires the trigger. Default: Player.")]
    [SerializeField] private string playerTag = "Player";

    // The zones the player is in, in the order they were entered: the last one is the innermost.
    private static readonly List<CameraAreaZone> Occupied = new List<CameraAreaZone>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Occupied.Clear();

    /// <summary>The label of the innermost zone the player is in; null outside every zone. An empty
    /// one means the default label.</summary>
    public static string CurrentLabel => Occupied.Count > 0 ? Occupied[Occupied.Count - 1].label : null;

    private bool isPlayerInside;

    private void Reset()
    {
        // Is Trigger as soon as the component is added, so it cannot be forgotten.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnDisable()
    {
        // Disabled (or unloaded) with the player inside: never leave its label on screen.
        if (isPlayerInside) Occupied.Remove(this);
        isPlayerInside = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (isPlayerInside) return;   // Several colliders on the player.

        isPlayerInside = true;
        Occupied.Remove(this);
        Occupied.Add(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!isPlayerInside) return;

        isPlayerInside = false;
        Occupied.Remove(this);
    }

#if UNITY_EDITOR
    // The area in the Scene view, in the camera overlay's pale blue, only while this zone or its
    // parent (the folder the zones are kept in) is selected: a dozen boxes over the whole level all
    // the time hide it, and catch the clicks meant for what is inside them.
    //
    // The zone selected: drawn from OnDrawGizmosSelected, which cannot be clicked, so a click inside
    // the box still reaches the room. Its parent selected: from OnDrawGizmos, which can, so a click
    // on a box picks that zone out of the folder.
    private void OnDrawGizmos()
    {
        if (IsSelected(transform.parent) && !IsSelected(transform)) DrawArea();
    }

    private void OnDrawGizmosSelected()
    {
        // Unity can call this for a selected parent as well: that case is OnDrawGizmos's.
        if (IsSelected(transform)) DrawArea();
    }

    private static bool IsSelected(Transform target) =>
        target != null && UnityEditor.Selection.Contains(target.gameObject);

    private void DrawArea()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Color fill = new Color(0.55f, 0.75f, 0.95f, 0.1f);
        Color wire = new Color(0.55f, 0.75f, 0.95f, 0.6f);

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = fill;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else
        {
            Bounds bounds = col.bounds;
            Gizmos.color = fill;
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
#endif
}
