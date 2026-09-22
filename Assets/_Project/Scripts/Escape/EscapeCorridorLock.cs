using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Locks a list of doors one after another, in rapid sequence, with the lock sound on each (Paso 2B:
/// "el resto de las puertas del pasillo se bloquean"). One job. The doors are sealed through
/// <see cref="DoorInteractable.SetSequenceLocked"/>, which is separate from the key / puzzle lock
/// of their SO_DoorData, so this never touches what a door needs to open in the rest of the game.
///
/// A door that is open when it locks swings shut first: a sealed door the player can still walk
/// through the doorway of is not locked at all.
///
/// Do NOT put the safe-zone door in the list — it is the one that stays usable. It is sealed later,
/// on its own, as the player comes out of it (<see cref="LockDoor"/>). The hub's other exits need
/// not be listed either: the director seals every door around the hub itself.
/// </summary>
public class EscapeCorridorLock : MonoBehaviour
{
    [Tooltip("Las puertas del pasillo que se traban, EN EL ORDEN en que se traban. La del centro NO. " +
             "Las otras salidas del hub no hace falta ponerlas: el director traba solo toda puerta " +
             "alrededor del hub. Una puerta abierta se cierra al trabarse.")]
    [SerializeField] private DoorInteractable[] doors = new DoorInteractable[0];

    // Sealed one by one from outside the list (the safe door, once the player is out of it), and
    // given back with the rest.
    private readonly List<DoorInteractable> extra = new List<DoorInteractable>();

    private Coroutine routine;

    /// <summary>The doors are (being) locked.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Locks every door in order, spaced by the config's interval.</summary>
    public void LockAll(SO_EscapeSequenceConfig config)
    {
        if (IsLocked) return;
        IsLocked = true;

        routine = StartCoroutine(LockRoutine(config));
    }

    /// <summary>Locks every door at once, silently. What a skip needs. Open ones still swing shut.</summary>
    public void LockAllNow()
    {
        IsLocked = true;
        if (routine != null) StopCoroutine(routine);
        routine = null;

        for (int i = 0; i < doors.Length; i++) Seal(doors[i]);
    }

    /// <summary>Seals one more door, shutting it first if it is open, with the lock sound once it is
    /// home (unless <paramref name="quiet"/>). For the safe door, which closes behind the player as
    /// they come out, and for the hub's other exits, which the director finds on its own.</summary>
    public void LockDoor(DoorInteractable door, SO_EscapeSequenceConfig config, bool quiet = false)
    {
        if (door == null) return;
        if (!extra.Contains(door) && System.Array.IndexOf(doors, door) < 0) extra.Add(door);

        if (quiet) Seal(door);
        else StartCoroutine(SealAndClack(door, config));
    }

    /// <summary>Whether this door is in the list (so a caller sealing doors of its own skips it).</summary>
    public bool Lists(DoorInteractable door) => door != null && System.Array.IndexOf(doors, door) >= 0;

    /// <summary>Opens the doors' lock again, the list's and the extra ones.</summary>
    public void UnlockAll()
    {
        IsLocked = false;
        if (routine != null) StopCoroutine(routine);
        routine = null;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null) doors[i].SetSequenceLocked(false);
        }

        foreach (DoorInteractable door in extra)
        {
            if (door != null) door.SetSequenceLocked(false);
        }
        extra.Clear();
    }

    private IEnumerator LockRoutine(SO_EscapeSequenceConfig config)
    {
        for (int i = 0; i < doors.Length; i++)
        {
            DoorInteractable door = doors[i];
            if (door == null) continue;

            Seal(door);
            PlayLockSound(door, config);

            float interval = config != null ? config.DoorLockInterval : 0.12f;
            if (interval > 0f) yield return new WaitForSeconds(interval);
        }

        routine = null;
    }

    private void Seal(DoorInteractable door)
    {
        if (door == null) return;

        door.SetSequenceLocked(true);
        if (door.IsOpen || door.IsAnimating) StartCoroutine(ShutWhenIdle(door));
    }

    // A door mid-swing cannot be told to close (DoorInteractable ignores it): wait for the swing.
    private static IEnumerator ShutWhenIdle(DoorInteractable door)
    {
        while (door != null && door.IsAnimating) yield return null;
        if (door != null && door.IsOpen) door.CloseDoor();
    }

    private IEnumerator SealAndClack(DoorInteractable door, SO_EscapeSequenceConfig config)
    {
        Seal(door);

        // One frame for the close to start, then the swing out.
        yield return null;
        while (door != null && door.IsAnimating) yield return null;

        PlayLockSound(door, config);
    }

    private static void PlayLockSound(DoorInteractable door, SO_EscapeSequenceConfig config)
    {
        string sound = config != null ? config.DoorLockSoundId : string.Empty;
        if (door != null && !string.IsNullOrEmpty(sound) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(sound, door.transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null) Gizmos.DrawWireCube(doors[i].transform.position + Vector3.up, new Vector3(1f, 2f, 0.3f));
        }
    }
}
