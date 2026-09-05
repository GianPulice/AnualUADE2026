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

    /// <summary>
    /// Drawn always, not only when selected: a selected-only gizmo is invisible while the game is
    /// running, which is precisely when someone wants to see where the pressure is.
    /// </summary>
    private void OnDrawGizmos()
    {
        float intensity = NemesisDirector.IntensityOf(zoneId);
        bool live = intensity > 0f;

        Gizmos.color = live
            ? Color.Lerp(new Color(1f, 0.7f, 0.1f, 0.9f), new Color(1f, 0.15f, 0.1f, 0.9f), intensity)
            : new Color(0.4f, 0.45f, 0.5f, 0.35f);

        Gizmos.DrawWireSphere(Center, radius);

#if UNITY_EDITOR
        string label = live ? $"{zoneId}  ·  presión {intensity:0.00}" : zoneId;
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.Label(Center + Vector3.up * 1.5f, label);
#endif
    }
}
