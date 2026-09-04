using UnityEngine;

/// <summary>
/// Trigger volume that applies an <see cref="SO_AmbienceProfile"/> while the player is inside.
/// On exit the controller falls back to the previous profile, or to its default if there was no
/// nesting.
///
/// Structurally a copy of LightZone, which is the proven shape for this in the project. One
/// deliberate difference: fields are plain camelCase here, matching the audio systems
/// (AudioManager, NemesisAudio, SO_SoundData) rather than LightZone's _camelCase, which is confined
/// to the UI and a couple of Rendering files.
///
/// Setup:
///   1. Empty GameObject in the gameplay scene, placed over the area.
///   2. Add a BoxCollider (or any Collider). Is Trigger is forced by Reset when the component is
///      added, so an LD cannot forget it.
///   3. Add this component.
///   4. Assign the profile for this area.
///
/// Nesting: if zone B sits inside zone A, the player enters A, then B. While in B the controller
/// applies B; leaving B returns to A; leaving A returns to the default. Leaving A first — which
/// happens when the volumes only partly overlap — removes A from the stack without changing what is
/// audible, because B is still the innermost zone.
///
/// The controller is looked up on the first trigger. If the gameplay scene has no
/// AmbienceController, a warning is logged and the zone does nothing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AmbienceZone : MonoBehaviour
{
    [Header("Profile applied on enter")]
    [Tooltip("The ambience for this area. Six profiles comfortably cover a whole floor — reuse " +
             "one profile across every corridor rather than authoring one per room.")]
    [SerializeField] private SO_AmbienceProfile profile;

    [Header("Detection")]
    [Tooltip("Tag of the GameObject that fires the trigger. Default: Player.")]
    [SerializeField] private string playerTag = "Player";

    private AmbienceController controller;
    private bool isPlayerInside;

    private void Reset()
    {
        // Force the collider to be a trigger as soon as the component is added, so LDs don't forget.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        AcquireController();
    }

    private void OnDisable()
    {
        // If the player was inside and the zone gets disabled, pop manually so the profile is not
        // left applied with nothing to remove it.
        if (isPlayerInside && controller != null)
        {
            controller.PopProfile(profile);
            isPlayerInside = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (isPlayerInside) return; // guard against duplicate triggers from multiple colliders

        if (controller == null) AcquireController();
        if (controller == null || profile == null) return;

        controller.PushProfile(profile);
        isPlayerInside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!isPlayerInside) return;

        if (controller != null && profile != null)
            controller.PopProfile(profile);

        isPlayerInside = false;
    }

    private void AcquireController()
    {
        if (controller != null) return;

        // FindAnyObjectByType and not FindFirstObjectByType: the latter is deprecated because it
        // orders by instance ID, and that ordering is worth nothing here — a scene only ever has
        // one ambience controller, so "any" is "the one".
        controller = FindAnyObjectByType<AmbienceController>();

        if (controller == null)
        {
            Debug.LogWarning($"[{nameof(AmbienceZone)}] No {nameof(AmbienceController)} was found " +
                             $"in the scene. Zone '{name}' will do nothing.", this);
        }
    }

#if UNITY_EDITOR
    // Trigger visualization in the Scene view, tinted from the profile's gizmo colour so overlapping
    // areas can be told apart at a glance.
    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Color baseColor = profile != null ? profile.GizmoColor : new Color(1f, 1f, 0f, 1f);
        Color fill = new Color(baseColor.r, baseColor.g, baseColor.b, 0.12f);
        Color wire = new Color(baseColor.r, baseColor.g, baseColor.b, 0.6f);

        Gizmos.color = fill;

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = wire;
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
        else
        {
            // Mesh and capsule colliders: fall back to the world-space bounds. Approximate, but it
            // still shows an LD roughly where the zone reaches.
            Bounds bounds = col.bounds;
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
#endif
}
