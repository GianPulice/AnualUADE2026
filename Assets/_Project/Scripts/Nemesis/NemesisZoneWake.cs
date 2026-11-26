using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wakes the Nemesis in one part of the level the first time the player takes one of that part's
/// rewards (the items its reward boxes hand out), and hands it that part's routes and spawn points
/// to do it with. Its one job. HOW it shows up is not decided here: with an
/// <see cref="entrance"/> it comes out in that cinematic (Zona 2: it knocks the arrival gate down,
/// seen on a security camera); without one, or when it cannot play, at the safest spawn point.
///
/// Until 26/11/2026 it woke on the first of the part's puzzles being solved, at a spawn point out of
/// sight — and on the playday nobody noticed it had come out at all.
///
/// Built for Zona 2 (plan D25: the Nemesis belongs to Zona 2; in Zona1 it only appears in the escape).
/// The Nemesis in the scene is Zona 1's, asleep for a script (NemesisController.WakeOnlyFromScript),
/// and it reaches Zona 2 in one of two states:
///
///   after the escape   awake, parked against Zona 1's gate and still the cinematic's
///                      (<see cref="NemesisCinematicActor"/>, its FSM off): handed back, sent to
///                      sleep there, and woken here.
///   F6, no escape      still asleep: woken here.
///
/// Without the entrance it is woken the way a puzzle wakes it anywhere else: at the safest of these
/// spawn points (far over the NavMesh, out of the player's view cone, behind geometry), or the
/// moment one is — never in sight of the player. The spawn points are also where a capture puts it
/// back, entrance or not.
///
/// Its routes and spawn points REPLACE the Nemesis's own (<see cref="NemesisController.ReplaceArea"/>):
/// the NavMesh does not join the two zones (the way between them is a teleport), so Zona 1's routes
/// would only be islands it cannot reach, and Zona 1's spawn points somewhere a capture could send it.
///
/// Sits on its own object in the level, not on the Nemesis: it finds the Nemesis in the scene, so
/// the prefab needs no edit (same as NemesisCinematicActor).
/// </summary>
public class NemesisZoneWake : MonoBehaviour
{
    [Tooltip("Objetos que despiertan al Nemesis acá: el primero que el jugador agarra lo despierta; " +
             "los demás ya no hacen nada.\n\n" +
             "Zona 2: lo que dan sus tres cajas (Válvula de calderas, Placa de relés, Bomba centrífuga).")]
    [SerializeField] private List<SO_InventoryItem> wakeItems = new List<SO_InventoryItem>();

    [Tooltip("Cómo aparece: la cinemática del portón. Vacío (o si no puede jugarse) = aparece en el " +
             "spawn más seguro, fuera de vista.")]
    [SerializeField] private NemesisGateEntrance entrance;

    [Tooltip("Solo para cargar una partida: si al cargar ya está resuelto alguno de estos puzzles, " +
             "despierta en un spawn, sin cinemática. En vivo lo despiertan los objetos de arriba.\n\n" +
             "Zona 2: p2_sp1_calderas, p2_sp2_switches, p2_sp3_mechanism.")]
    [PuzzleId]
    [SerializeField] private List<string> wakePuzzleIds = new List<string>();

    [Tooltip("This part's patrol routes. They REPLACE the Nemesis's own on waking: from then on it " +
             "patrols here and nowhere else.")]
    [SerializeField] private List<NemesisRoute> routes = new List<NemesisRoute>();

    [Tooltip("Where it may appear, and where a capture puts it back. They replace the Nemesis's own " +
             "too. Same rules as NemesisController's: apart from each other, behind cover, and away " +
             "from where the player stands when the puzzles above complete. Empty, it never appears.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    private bool woken;

    // Static event: subscribed in Awake and released in OnDestroy, per docs/CLAUDE.md.
    private void Awake() => InventoryEvents.OnItemAdded += HandleItemAdded;

    private void OnDestroy() => InventoryEvents.OnItemAdded -= HandleItemAdded;

    /// <summary>
    /// Catch-up, same as NemesisStateManager and NemesisRoute: one of the puzzles may already be solved
    /// by the time this loads (a restored snapshot), and the pickup that would have woken it happened
    /// in another session. Wakes it at a spawn point: the entrance is for the moment it happens.
    ///
    /// A frame late on purpose: the Nemesis's own Start puts it to sleep, and waking it first would
    /// have that undo the wake.
    /// </summary>
    private IEnumerator Start()
    {
        yield return null;

        if (!PuzzleStateManager.Exists) yield break;

        for (int i = 0; i < wakePuzzleIds.Count; i++)
        {
            if (!PuzzleStateManager.Instance.IsPuzzleCompleted(wakePuzzleIds[i])) continue;

            Wake(withEntrance: false);
            yield break;
        }
    }

    private void HandleItemAdded(SO_InventoryItem item)
    {
        if (woken || item == null || !wakeItems.Contains(item)) return;

        Wake(withEntrance: true);
    }

    private void Wake(bool withEntrance)
    {
        if (woken) return;

        NemesisStateManager nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis == null)
        {
            Debug.LogWarning($"[{nameof(NemesisZoneWake)}] '{name}': no Nemesis in the scene to wake.", this);
            return;
        }

        woken = true;

        if (spawnPoints.Count == 0)
            Debug.LogWarning($"[{nameof(NemesisZoneWake)}] '{name}' has no spawn points: the Nemesis " +
                             "goes to sleep and never appears.", this);

        // The area first, whichever way it comes out: it patrols these routes once it is free.
        if (nemesis.NemesisController != null) nemesis.NemesisController.ReplaceArea(routes, spawnPoints);

        // The entrance shows it coming out; once the shot is over it is moved to a spawn point like
        // any other wake. The arrival vestibule's NavMesh does not reach the zone's floors, so left
        // where the shot leaves it, it would patrol nothing. The player is far away by then and
        // cannot see the vestibule: the move is never seen.
        if (withEntrance && entrance != null)
        {
            entrance.Finished += HandleEntranceFinished;
            if (entrance.TryPlay())
            {
                Debug.Log($"[{nameof(NemesisZoneWake)}] '{name}': the Nemesis comes out through " +
                          $"'{entrance.name}' ({routes.Count} routes, {spawnPoints.Count} spawn points).", this);
                return;
            }
            entrance.Finished -= HandleEntranceFinished;
        }

        WakeAtSpawnPoint(nemesis);
    }

    private void HandleEntranceFinished()
    {
        entrance.Finished -= HandleEntranceFinished;

        NemesisStateManager nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis != null) WakeAtSpawnPoint(nemesis);
    }

    private void WakeAtSpawnPoint(NemesisStateManager nemesis)
    {
        // Handed back before anything else: while a cinematic holds it, its FSM is off and nothing
        // below would move it.
        foreach (NemesisCinematicActor actor in FindObjectsByType<NemesisCinematicActor>())
            if (actor.HasControl) actor.Release();

        // Asleep first, because Activate does nothing to a Nemesis that is already awake: this is
        // what moves it from wherever it stands (Zona 1's gate, the arrival vestibule) to one of the
        // spawn points here.
        nemesis.Sleep();
        nemesis.Activate();

        Debug.Log($"[{nameof(NemesisZoneWake)}] '{name}': the Nemesis wakes here " +
                  $"({routes.Count} routes, {spawnPoints.Count} spawn points).", this);
    }

    /// <summary>The spawn points, so they can be placed: they are plain transforms with nothing of
    /// their own to draw.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.9f);

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Transform point = spawnPoints[i];
            if (point == null) continue;

            Gizmos.DrawWireSphere(point.position + Vector3.up, 0.4f);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 2f);
        }
    }
}
