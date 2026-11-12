using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One space of a test scene the F10 console can move the session to: the player is put on
/// <see cref="playerStart"/>, the capture respawn moves there with them, and the Nemesis is handed
/// this space's routes and spawn points and woken again at the safest of them. Its one job.
///
/// Built for NemesisTestbed, which holds two spaces side by side and not joined by NavMesh: the
/// original testbed and a copy of Zona 2 (<c>Tools/Nemesis/Build Zona 2 Lab (NemesisTestbed)</c>). The
/// console lists every enabled area (<see cref="Active"/>) as a button, so a scene with one area or
/// none simply shows less.
///
/// Same move as Zona 2's own wake (<see cref="NemesisZoneWake"/>): <see cref="NemesisController.ReplaceArea"/>,
/// then <see cref="NemesisStateManager.Sleep"/> and <see cref="NemesisStateManager.Activate"/>, so the
/// Nemesis appears far, out of the player's view and behind geometry, never on top of them. The
/// Director's pressure is released: a zone of the space being left would keep pulling at nothing.
///
/// What it does NOT do: solve or reset puzzles, touch the escalation, or carry the habits ledger
/// over (both stay as they were, and the console has their own buttons).
///
/// Refuses, with a warning, while the player is hidden or being captured, and while the Nemesis is
/// catching or crossing a lift or a drop: those own the bodies and a teleport under them leaves a
/// state nothing undoes.
/// </summary>
public class NemesisTestArea : MonoBehaviour
{
    private static readonly List<NemesisTestArea> active = new List<NemesisTestArea>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    /// <summary>The enabled areas, in the order they were enabled (load order).</summary>
    public static IReadOnlyList<NemesisTestArea> Active => active;

    [Tooltip("Name on the F10 console's button.")]
    [SerializeField] private string displayName = "Area";

    [Tooltip("Where the player appears, facing this transform's Z axis (yaw only).")]
    [SerializeField] private Transform playerStart;

    [Tooltip("Where a capture sends the player while this area is the one in use. Activated by the " +
             "move itself, so give it no puzzle ids and no trigger. Empty = the respawn is left as it was.")]
    [SerializeField] private Checkpoint respawn;

    [Tooltip("The routes the Nemesis patrols here. They replace its own on entering.")]
    [SerializeField] private List<NemesisRoute> routes = new List<NemesisRoute>();

    [Tooltip("Where the Nemesis may appear here, and where a capture puts it back.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    public string DisplayName => displayName;

    /// <summary>The area the session was last moved to, or null before the first move.</summary>
    public static NemesisTestArea Current { get; private set; }

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    private void OnDisable()
    {
        active.Remove(this);
        if (Current == this) Current = null;
    }

    /// <summary>Moves the session here: player, respawn and Nemesis.</summary>
    /// <returns>false when it refused (logged).</returns>
    public bool Enter()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            Debug.LogWarning($"[{nameof(NemesisTestArea)}] '{displayName}': no player registered.", this);
            return false;
        }

        if (player.CurrentHidingSpot != null || player.IsRecoveringFromCapture || player.IsDisabled)
        {
            Debug.LogWarning($"[{nameof(NemesisTestArea)}] '{displayName}': the player is hidden, " +
                             "being captured or frozen. Try again once they have control.", this);
            return false;
        }

        NemesisStateManager nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis != null && (nemesis.IsCrossingLink ||
                                nemesis.CurrentStateKey == NemesisStateManager.ENemesisState.Catch))
        {
            Debug.LogWarning($"[{nameof(NemesisTestArea)}] '{displayName}': the Nemesis is catching or " +
                             "crossing a link. Try again in a moment.", this);
            return false;
        }

        Transform start = playerStart != null ? playerStart : transform;
        float yaw = start.eulerAngles.y;
        player.TeleportTo(start.position, Quaternion.Euler(0f, yaw, 0f));
        Zone2EntryTeleport.PlaceCameraBehind(yaw);

        if (respawn != null && CheckpointManager.Exists) CheckpointManager.Instance.ActivateCheckpoint(respawn);

        NemesisDirector.ReleasePressure();

        if (nemesis != null)
        {
            if (nemesis.NemesisController != null) nemesis.NemesisController.ReplaceArea(routes, spawnPoints);

            // Asleep first: Activate does nothing to a Nemesis that is already awake, and this is
            // what takes it off the other space and onto one of this one's spawn points.
            nemesis.Sleep();
            nemesis.Activate();
        }

        Current = this;
        Debug.Log($"[{nameof(NemesisTestArea)}] Session moved to '{displayName}' " +
                  $"({routes.Count} routes, {spawnPoints.Count} spawn points).", this);
        return true;
    }

    private void OnDrawGizmos()
    {
        Transform start = playerStart != null ? playerStart : transform;

        Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(start.position + Vector3.up * 0.9f, 0.35f);
        Gizmos.DrawRay(start.position + Vector3.up * 0.9f, start.forward * 1.2f);
    }
}
