using UnityEngine;

/// <summary>
/// The area in front of a <see cref="LightRevealWall"/> where the player's module light reaches
/// it: while the player is inside, the wall's arrows show. A trigger collider on this object; its
/// size is how close the player has to get. The wall is found in the parents, so the zone lives
/// as a child of the wall in the prefab.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LightRevealZone : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [Tooltip("Wall this zone lights. Empty = the LightRevealWall in the parents.")]
    [SerializeField] private LightRevealWall wall;

    private bool playerInside;

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
