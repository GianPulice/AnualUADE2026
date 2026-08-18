using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spot in the level where an ambient one-shot is allowed to come from: a pipe run, a ventilation
/// grate, a stairwell, a loose sheet of metal.
///
/// WHY THESE EXIST
/// Purely random placement around the player produces sounds from wherever the maths lands, which in
/// a blockout is frequently outside the building or inside a wall. The placement resolver filters
/// those out, but a filtered random point is still just empty space — a chain rattling in a corridor
/// with no chain in it. An anchor makes the sound diegetic: it comes from the thing that would
/// actually make it. That is the single biggest credibility win available here, and it costs an LD a
/// few minutes per area.
///
/// Anchors are optional. With none placed, the resolver falls back to validated random for
/// everything and nothing breaks — which is the state the blockout is in today.
///
/// Setup:
///   1. Empty GameObject (or a child of the prop itself) at the spot the sound should come from.
///   2. Add this component.
///   3. Set acceptedTags to the kinds of sound this spot can produce. Leave it as None to accept
///      anything — a bare anchor is a valid "generic noise can come from here" marker.
///
/// IMPORTANT — if you give an anchor a collider, put it on Ignore Raycast or make it a trigger.
/// The resolver rejects candidate points that are inside geometry, and it would otherwise reject
/// its own anchors.
/// </summary>
public class AmbienceEmitter : MonoBehaviour
{
    private static readonly List<AmbienceEmitter> registered = new List<AmbienceEmitter>();

    /// <summary>
    /// Static state must be reset explicitly: domain reload is disabled in this project, so without
    /// this the list accumulates destroyed emitters across Play sessions. Same guard as
    /// NemesisEvents.ResetStatics.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => registered.Clear();

    /// <summary>Every enabled anchor in the loaded scenes. Read by AmbiencePlacementResolver.</summary>
    public static IReadOnlyList<AmbienceEmitter> Registered => registered;

    [Header("What can play from here")]
    [Tooltip("The kinds of sound this spot can produce. None accepts everything.\n\n" +
             "An event is allowed here when it shares at least one tag with this anchor, so a " +
             "grate marked Metal|Structure will take both a metallic rattle and a structural creak.")]
    [SerializeField]
    private SO_AmbienceEventBank.EEventTag acceptedTags = SO_AmbienceEventBank.EEventTag.None;

    [Header("Pacing")]
    [Tooltip("Relative chance of this anchor being picked over the others in range. Raise it for a " +
             "spot you want to be characteristic of an area.")]
    [SerializeField, Min(0.01f)] private float weight = 1f;

    [Tooltip("Seconds before this anchor may be used again. Stops the same pipe from being the " +
             "source of three sounds in a row, which reads as scripted rather than incidental.")]
    [SerializeField, Min(0f)] private float cooldown = 20f;

    [Header("Editor")]
    [Tooltip("Gizmo radius. Purely visual — it has no effect on where the sound plays.")]
    [SerializeField, Min(0.05f)] private float gizmoRadius = 0.4f;

    private float availableAtTime;

    // ── Public API ───────────────────────────────────────────────────────────

    public float Weight => weight;

    /// <summary>False while this anchor is inside its cooldown.</summary>
    public bool IsReady => Time.time >= availableAtTime;

    /// <summary>Starts the cooldown. Called by the resolver after this anchor is chosen.</summary>
    public void MarkUsed() => availableAtTime = Time.time + cooldown;

    /// <summary>
    /// True if an event carrying <paramref name="eventTags"/> may play from here. An anchor with no
    /// accepted tags takes anything, so an LD can drop a marker without thinking about taxonomy.
    /// </summary>
    public bool Accepts(SO_AmbienceEventBank.EEventTag eventTags)
    {
        if (acceptedTags == SO_AmbienceEventBank.EEventTag.None) return true;
        return (acceptedTags & eventTags) != 0;
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (!registered.Contains(this)) registered.Add(this);
    }

    private void OnDisable() => registered.Remove(this);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Dim when not selected so a level full of anchors stays readable.
        Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);
        Gizmos.DrawSphere(transform.position, gizmoRadius);
    }
#endif
}
