using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Marks this Not Walkable volume as a player refuge (the Hub) for <see cref="NemesisSafeZones"/>. Informs only: the
/// NavMesh does the blocking. Needed because Not Walkable volumes also fill solid props (Bridges_support_2's
/// "NavMesh Blocker"), and those are not places the player can shelter in.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshModifierVolume))]
public class SafeZoneMarker : MonoBehaviour
{
}
