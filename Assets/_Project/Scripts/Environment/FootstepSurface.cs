using UnityEngine;

/// <summary>
/// Marks a piece of geometry as a footstep surface. Drop it on a floor — or on the root of a
/// prop the player can walk on — and everything that walks over it will step on the right
/// material.
///
/// This is the authoritative half of <see cref="SO_FootstepBank"/>'s resolution: the emitter looks
/// for this component first, with <c>GetComponentInParent</c>, so marking a catwalk root covers
/// every plank mesh under it.
///
/// It exists because this project has nothing else to read. There are no surface tags (the tag
/// list holds exactly one entry, NemesisWaypoint) and exactly one PhysicMaterial in the whole
/// project, so the usual "read the PhysicMaterial name" trick has nothing to read. A component is
/// also the only option that survives a mesh re-import, which a tag on the child does not.
///
/// Cost: none at runtime. It has no Update, holds one enum, and is only ever read from inside a
/// footstep, which happens roughly once a second per walker.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Audio/Footstep Surface")]
public class FootstepSurface : MonoBehaviour
{
    [Tooltip("What this geometry is made of. The walker's SO_FootstepBank decides which clips " +
             "that maps to, so the same marker sounds different under the player and under the " +
             "Nemesis.")]
    [SerializeField] private SO_FootstepBank.ESurface surface = SO_FootstepBank.ESurface.Concrete;

    public SO_FootstepBank.ESurface Surface => surface;

#if UNITY_EDITOR
    /// <summary>Colour-codes the marker in the Scene view so a mis-tagged floor is visible.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = surface switch
        {
            SO_FootstepBank.ESurface.Metal    => new Color(0.70f, 0.75f, 0.85f, 0.35f),
            SO_FootstepBank.ESurface.Wood     => new Color(0.75f, 0.55f, 0.30f, 0.35f),
            SO_FootstepBank.ESurface.Gravel   => new Color(0.60f, 0.58f, 0.50f, 0.35f),
            SO_FootstepBank.ESurface.Water    => new Color(0.30f, 0.60f, 0.90f, 0.35f),
            SO_FootstepBank.ESurface.Oil      => new Color(0.25f, 0.20f, 0.28f, 0.45f),
            SO_FootstepBank.ESurface.Dirt     => new Color(0.50f, 0.40f, 0.28f, 0.35f),
            _                                 => new Color(0.65f, 0.65f, 0.65f, 0.30f),
        };

        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Bounds b = col.bounds;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawCube(b.center, b.size);
    }
#endif
}
