using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wakes the Nemesis in one part of the level the first time any of that part's puzzles is solved,
/// and hands it that part's routes and spawn points to do it with. Its one job.
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
/// Either way it is woken the way a puzzle wakes it anywhere else: at the safest of these spawn
/// points (far over the NavMesh, out of the player's view cone, behind geometry), or the moment one
/// is — never in sight of the player.
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
    [Tooltip("Puzzles that wake the Nemesis here. The first of them to be solved does it; the rest do " +
             "nothing more.\n\n" +
             "Zona 2: its three sub-puzzles, the ones its hub asks for (p2_sp1_calderas, " +
             "p2_sp2_switches, p2_sp3_mechanism).")]
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
    private void Awake() => PuzzleCompletionAftermath.Settled += HandlePuzzleSettled;

    private void OnDestroy() => PuzzleCompletionAftermath.Settled -= HandlePuzzleSettled;

    /// <summary>
    /// Catch-up, same as NemesisStateManager and NemesisRoute: one of the puzzles may already be solved
    /// by the time this loads (a restored snapshot), and Settled only fires on the transition.
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

            Wake();
            yield break;
        }
    }

    private void HandlePuzzleSettled(string puzzleId)
    {
        if (woken || string.IsNullOrWhiteSpace(puzzleId) || !wakePuzzleIds.Contains(puzzleId)) return;

        Wake();
    }

    private void Wake()
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

        // Handed back before anything else: while the cinematic holds it, its FSM is off and nothing
        // below would move it.
        NemesisCinematicActor actor = FindAnyObjectByType<NemesisCinematicActor>();
        if (actor != null && actor.HasControl) actor.Release();

        if (nemesis.NemesisController != null) nemesis.NemesisController.ReplaceArea(routes, spawnPoints);

        // Asleep first, because Activate does nothing to a Nemesis that is already awake: this is
        // what moves it from Zona 1's gate to one of the spawn points here.
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
