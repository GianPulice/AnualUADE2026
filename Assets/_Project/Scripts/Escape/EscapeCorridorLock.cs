using System.Collections;
using UnityEngine;

/// <summary>
/// Locks a list of doors one after another, in rapid sequence, with the lock sound on each (Paso 2B:
/// "el resto de las puertas del pasillo se bloquean"). One job. The doors are sealed through
/// <see cref="DoorInteractable.SetSequenceLocked"/>, which is separate from the key / puzzle lock
/// of their SO_DoorData, so this never touches what a door needs to open in the rest of the game.
///
/// Do NOT put the safe-zone door in the list — it is the one that stays usable.
/// </summary>
public class EscapeCorridorLock : MonoBehaviour
{
    [Tooltip("Las puertas del pasillo que se traban, EN EL ORDEN en que se traban.")]
    [SerializeField] private DoorInteractable[] doors = new DoorInteractable[0];

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

    /// <summary>Locks every door at once, silently. What a skip needs.</summary>
    public void LockAllNow()
    {
        IsLocked = true;
        if (routine != null) StopCoroutine(routine);
        routine = null;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null) doors[i].SetSequenceLocked(true);
        }
    }

    /// <summary>Opens the doors' lock again.</summary>
    public void UnlockAll()
    {
        IsLocked = false;
        if (routine != null) StopCoroutine(routine);
        routine = null;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null) doors[i].SetSequenceLocked(false);
        }
    }

    private IEnumerator LockRoutine(SO_EscapeSequenceConfig config)
    {
        for (int i = 0; i < doors.Length; i++)
        {
            DoorInteractable door = doors[i];
            if (door == null) continue;

            door.SetSequenceLocked(true);

            string sound = config != null ? config.DoorLockSoundId : string.Empty;
            if (!string.IsNullOrEmpty(sound) && AudioManager.Exists)
                AudioManager.Instance.PlaySFX(sound, door.transform.position);

            float interval = config != null ? config.DoorLockInterval : 0.12f;
            if (interval > 0f) yield return new WaitForSeconds(interval);
        }

        routine = null;
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
