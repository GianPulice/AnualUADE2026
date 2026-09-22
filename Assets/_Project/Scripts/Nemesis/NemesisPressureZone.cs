using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A named piece of the level the <see cref="NemesisDirector"/> can lean on: "make the monster
/// felt around here for a while".
///
/// A sphere with an id, and deliberately nothing more. It was tempting to reuse
/// <see cref="NemesisRoute"/> as the unit of pressure — the routes already group the level into
/// zones — but the two answer different questions. A route is a path the Nemesis walks; a pressure
/// zone is an area a designer wants haunted, and the second does not have to line up with the
/// first. The pump room can be pressured without there being a "pump room route", and a route that
/// runs through three areas should not have to be split just to name one of them.
///
/// What it does NOT do is decide anything. It has no update, no state and no opinion about the
/// Nemesis; the Director asks it where it is and how big it is, and that is the whole contract.
///
/// SETUP: an empty GameObject at the centre of the area, this component, an id, a radius. The
/// gizmo draws the radius to scale so it can be sized against the actual geometry.
/// </summary>
public class NemesisPressureZone : MonoBehaviour
{
    [Tooltip("Nombre con el que el resto del juego pide presión acá — el mismo string que se le " +
             "pasa a NemesisDirector.RequestPressure.\n\n" +
             "Conviene que describa el lugar ('sala de bombas', 'pasillo este') y no lo que pasa " +
             "ahí ('después del puzzle 2'): la zona sobrevive al evento que la usó primero.")]
    [SerializeField] private string zoneId;

    [Tooltip("Radio de la zona, en metros. Se dibuja a escala en la escena.\n\n" +
             "Generoso a propósito: esto no marca dónde tiene que pararse el Nemesis, marca qué " +
             "parte del nivel se considera 'acá'. Un radio del tamaño de una habitación hace que " +
             "la presión se sienta como una persecución dirigida; uno del tamaño de un ala del " +
             "nivel, como mala suerte — que es lo que se busca.")]
    [SerializeField, Min(1f)] private float radius = 12f;

    private static readonly List<NemesisPressureZone> active = new List<NemesisPressureZone>();

    /// <summary>
    /// Static state survives leaving Play mode when domain reload is disabled, and a stale zone
    /// here is not harmless: the Director would hand the patrol an anchor at a position from the
    /// previous session, in a scene that no longer has it.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    public string ZoneId => zoneId;
    public float Radius => radius;
    public Vector3 Center => transform.position;

    /// <summary>Every zone currently in the level. For the test console, which offers one button
    /// per zone so the Director can be exercised without wiring a puzzle to it first.</summary>
    public static IReadOnlyList<NemesisPressureZone> Active => active;

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            Debug.LogError($"[{nameof(NemesisPressureZone)}] '{name}' has no id, so nothing can " +
                           "ask for pressure here. The zone is ignored.", this);
            return;
        }

        if (!active.Contains(this)) active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

    /// <summary>
    /// The zone with this id, or null. Case-insensitive, because an id is typed by hand into a
    /// dozen different inspectors and "Sala De Bombas" not matching "sala de bombas" is a bug
    /// nobody can see by looking at either end of it.
    /// </summary>
    public static NemesisPressureZone Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        for (int i = 0; i < active.Count; i++)
        {
            NemesisPressureZone zone = active[i];
            if (zone != null && string.Equals(zone.zoneId, id, System.StringComparison.OrdinalIgnoreCase))
                return zone;
        }

        return null;
    }

    /// <summary>Whether a point is inside this zone. Flat: the zone names a place on the level,
    /// and a room directly above another is not the same place — but it is close enough
    /// vertically that a spherical test would swallow it whole.</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 offset = point - Center;
        offset.y = 0f;
        return offset.sqrMagnitude <= radius * radius;
    }

#if UNITY_EDITOR
    private static readonly Color IdleColor = new Color(0.45f, 0.62f, 0.75f, 0.8f);
    private static readonly Color LiveLowColor = new Color(1f, 0.7f, 0.1f, 0.95f);
    private static readonly Color LiveHighColor = new Color(1f, 0.15f, 0.1f, 0.95f);
    private static readonly Color WarningColor = new Color(1f, 0.3f, 0.85f, 0.95f);

    private static readonly List<Transform> WaypointCache = new List<Transform>();
    private static double lastWaypointScan = double.NegativeInfinity;

    private readonly List<Transform> coveredScratch = new List<Transform>();
    private readonly List<float> levelScratch = new List<float>();

    /// <summary>
    /// Drawn always, not only when selected: a selected-only gizmo is invisible while the game is
    /// running, which is precisely when someone wants to see where the pressure is.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!NemesisGizmos.DrawingEnabled) return;
        DrawZone(selected: false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!NemesisGizmos.DrawingEnabled) return;
        DrawZone(selected: true);
    }

    /// <summary>
    /// The zone as what it is: a vertical cylinder. One disc per floor its waypoints stand on, so the
    /// floors it reaches are visible; the label says what it covers and what is wrong with it.
    /// </summary>
    private void DrawZone(bool selected)
    {
        CollectCoveredWaypoints(coveredScratch);
        CollectLevels(coveredScratch, levelScratch);

        float intensity = NemesisDirector.IntensityOf(zoneId);
        bool live = intensity > 0f;
        bool atHub = !NemesisSafeZones.IsCentreClear(Center);
        bool empty = coveredScratch.Count == 0;

        Color color = atHub || empty ? WarningColor
                    : live ? Color.Lerp(LiveLowColor, LiveHighColor, intensity)
                    : IdleColor;

        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, selected || live ? 0.14f : 0.05f);
        UnityEditor.Handles.DrawSolidDisc(Center, Vector3.up, radius);

        UnityEditor.Handles.color = color;

        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < levelScratch.Count; i++)
        {
            float y = levelScratch[i];
            UnityEditor.Handles.DrawWireDisc(new Vector3(Center.x, y, Center.z), Vector3.up, radius,
                                             selected ? 2.5f : 1.5f);
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
        }

        if (maxY > minY)
        {
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 rim = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                UnityEditor.Handles.DrawDottedLine(new Vector3(Center.x + rim.x, minY, Center.z + rim.z),
                                                   new Vector3(Center.x + rim.x, maxY, Center.z + rim.z), 3f);
            }
        }

        if (selected) DrawCoverageLines(color);

        UnityEditor.Handles.Label(new Vector3(Center.x, maxY, Center.z) + Vector3.up * 1.5f,
                                  BuildLabel(intensity, atHub, empty));
    }

    private string BuildLabel(float intensity, bool atHub, bool empty)
    {
        string label = $"{(string.IsNullOrWhiteSpace(zoneId) ? "(sin id)" : zoneId)}  ·  r {radius:0.#} m  ·  " +
                       $"{coveredScratch.Count} waypoints";

        if (intensity > 0f)
            label += $"  ·  presión {intensity:0.00} ({NemesisDirector.ActiveSourceLabel})";

        if (atHub)
            label += $"\n⚠ centro a {NemesisSafeZones.FlatDistance(Center):0.0} m del Hub " +
                     $"(mínimo {NemesisSafeZones.Clearance:0}): el Director la rechaza";

        if (empty) label += "\n⚠ no toca ningún waypoint: la palanca de rutas no hace nada";

        return label;
    }

    private void DrawCoverageLines(Color color)
    {
        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 0.6f);

        for (int i = 0; i < coveredScratch.Count; i++)
        {
            Vector3 waypoint = coveredScratch[i].position;
            Vector3 hub = new Vector3(Center.x, waypoint.y, Center.z);

            UnityEditor.Handles.DrawLine(hub, waypoint);
            Gizmos.color = color;
            Gizmos.DrawSphere(waypoint, 0.25f);
        }
    }

    private void CollectCoveredWaypoints(List<Transform> covered)
    {
        RefreshWaypointCache();
        covered.Clear();

        for (int i = 0; i < WaypointCache.Count; i++)
        {
            Transform waypoint = WaypointCache[i];
            if (waypoint != null && Contains(waypoint.position)) covered.Add(waypoint);
        }
    }

    /// <summary>Floors this zone reaches: its own height plus every distinct waypoint height, to half a metre.</summary>
    private void CollectLevels(List<Transform> covered, List<float> levels)
    {
        levels.Clear();
        levels.Add(Center.y);

        for (int i = 0; i < covered.Count; i++)
        {
            float y = Mathf.Round(covered[i].position.y * 2f) * 0.5f;

            bool known = false;
            for (int l = 0; l < levels.Count && !known; l++) known = Mathf.Abs(levels[l] - y) < 1f;

            if (!known) levels.Add(y);
        }
    }

    /// <summary>Every tagged waypoint in the open scene, rescanned at most once a second.</summary>
    private static void RefreshWaypointCache()
    {
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - lastWaypointScan < 1.0) return;
        lastWaypointScan = now;

        WaypointCache.Clear();

        foreach (NemesisRoute route in FindObjectsByType<NemesisRoute>(FindObjectsInactive.Exclude))
        {
            for (int i = 0; i < route.transform.childCount; i++)
            {
                Transform child = route.transform.GetChild(i);
                if (child.CompareTag(NemesisRoute.WaypointTag)) WaypointCache.Add(child);
            }
        }
    }
#endif
}
