using UnityEngine;

/// <summary>
/// Trigger Zone mode of a <see cref="LightRevealWall"/> (its asset's Reveal Mode): the area in front
/// of the wall where the player's light counts as reaching it — while the player is inside, the
/// wall's arrows show. A trigger collider on this object; its size is how close the player has to
/// get. The wall is found in the parents, so the zone lives as a child of the wall in the prefab.
/// In Module Light mode (the default) the wall ignores it.
///
/// Light degradation (the asset's list of exploded modules) shrinks a BoxCollider's depth towards
/// the wall, so a degraded light means getting closer here too.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LightRevealZone : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [Tooltip("Wall this zone lights. Empty = the LightRevealWall in the parents.")]
    [SerializeField] private LightRevealWall wall;

    private bool playerInside;
    private BoxCollider box;
    private Vector3 baseSize;
    private Vector3 baseCenter;
    private float appliedMultiplier = 1f;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (wall == null) wall = GetComponentInParent<LightRevealWall>();
        if (wall == null)
            Debug.LogError($"[{nameof(LightRevealZone)}] '{name}' has no LightRevealWall to light.", this);

        box = GetComponent<BoxCollider>();
        if (box != null)
        {
            baseSize = box.size;
            baseCenter = box.center;
        }
    }

    private void FixedUpdate()
    {
        if (box == null || wall == null || wall.PuzzleData == null) return;

        float multiplier = Mathf.Clamp01(wall.PuzzleData.LightDegradationMultiplier());
        if (Mathf.Approximately(multiplier, appliedMultiplier)) return;
        appliedMultiplier = multiplier;

        // Keep the face against the wall where it is and pull the far face in.
        float towardsWall = Mathf.Sign(transform.InverseTransformPoint(wall.transform.position).z - baseCenter.z);
        float lost = baseSize.z * (1f - multiplier);
        box.size = new Vector3(baseSize.x, baseSize.y, baseSize.z * multiplier);
        box.center = baseCenter + Vector3.forward * (towardsWall * lost * 0.5f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (playerInside || wall == null || !other.CompareTag(PlayerTag)) return;
        playerInside = true;
        wall.SetLit(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!playerInside || wall == null || !other.CompareTag(PlayerTag)) return;
        playerInside = false;
        wall.SetLit(false);
    }

    private void OnDisable()
    {
        if (!playerInside || wall == null) return;
        playerInside = false;
        wall.SetLit(false);
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.78f, 0.3f, 0.2f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(1f, 0.78f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
