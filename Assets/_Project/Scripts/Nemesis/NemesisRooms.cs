using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which room a point is in. One question, answered from the level as it is built.
///
/// There are no authored room volumes in the project, and the search needs to tell "the room the
/// player went into" from "the corridor outside it" (the user's rule: seen entering a room, the
/// points inside that room come first). What the levels DO have is one floor collider per room,
/// named after it: Zona1's generator writes <c>&lt;ROOM&gt;_Floor_&lt;n&gt;</c>
/// (<c>OFICINA_Floor_0</c>, <c>PASILLO_PLANTA_Floor_1</c>, <c>HUB_01_Floor_8</c>), and the testbed
/// groups each room's geometry under a parent named after it (<c>Testbed/Geometry/ENTRADA</c>).
/// So the room of a point is the floor under it, and the floor's name — or its parent's — is the
/// room.
///
/// Returns false where there is no floor under the point, and for floors that do not follow either
/// convention it falls back to the floor object's own name, which still keeps two different
/// floors apart. A walkway made of several pieces (<c>Bridges_2 (5)</c>) collapses to one key per
/// prefab name, which is the right answer for a single catwalk.
/// </summary>
public static class NemesisRooms
{
    private const string FloorToken = "_Floor";
    private const float ProbeUp = 0.6f;
    private const float ProbeDown = 3f;

    private static readonly Dictionary<Collider, string> cache = new Dictionary<Collider, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>The floor layers: what a room key is read off.</summary>
    private static int FloorMask => LayerMask.GetMask("Default", "Ground");

    /// <summary>The room <paramref name="point"/> is standing in, or false when there is no floor
    /// under it.</summary>
    public static bool TryGetRoom(Vector3 point, out string room)
    {
        room = null;
        if (!Physics.Raycast(point + Vector3.up * ProbeUp, Vector3.down, out RaycastHit hit,
                             ProbeUp + ProbeDown, FloorMask, QueryTriggerInteraction.Ignore))
            return false;

        room = KeyOf(hit.collider);
        return room != null;
    }

    /// <summary>Whether two points are in the same room. Two points with no floor under either
    /// are NOT: "unknown" must never read as a match.</summary>
    public static bool SameRoom(Vector3 a, Vector3 b) =>
        TryGetRoom(a, out string ra) && TryGetRoom(b, out string rb) && ra == rb;

    private static string KeyOf(Collider floor)
    {
        if (floor == null) return null;
        if (cache.TryGetValue(floor, out string key)) return key;

        string name = floor.gameObject.name;
        int token = name.IndexOf(FloorToken, System.StringComparison.OrdinalIgnoreCase);

        if (token > 0) key = name.Substring(0, token);                    // OFICINA_Floor_0 -> OFICINA
        else if (floor.transform.parent != null) key = floor.transform.parent.name; // Testbed/Geometry/ENTRADA/Floor -> ENTRADA
        else key = StripInstanceSuffix(name);

        cache[floor] = key;
        return key;
    }

    /// <summary>"Bridges_2 (5)" -> "Bridges_2": the copies of one prefab are one piece of floor.</summary>
    private static string StripInstanceSuffix(string name)
    {
        int paren = name.LastIndexOf(" (", System.StringComparison.Ordinal);
        return paren > 0 && name.EndsWith(")") ? name.Substring(0, paren) : name;
    }
}
