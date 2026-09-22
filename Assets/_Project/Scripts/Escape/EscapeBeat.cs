/// <summary>
/// What an <see cref="EscapeBeatMarker"/> makes happen when the Timeline playhead crosses it. One
/// action each — the marker only names it, and <see cref="EscapeSequenceDirector"/> does it using
/// the scene objects wired on its Stage.
///
/// Append-only: a Timeline asset stores the numeric value, so reordering or inserting in the
/// middle would silently retarget every marker already placed.
///
/// Only three beats are live: the opening is now the socket, the alarm and the lock-down. The rest
/// belonged to the old opening — the Nemesis leaving its door and the player's automatic run out of
/// the safe room — which the corridor reveal replaced; they are kept only so the numbers of the
/// others do not move, and a marker set to one of them does nothing but warn.
/// </summary>
public enum EscapeBeat
{
    // ── Opening cinematic ───────────────────────────────────────────────────

    /// <summary>The alarm starts: alarm loop + tension layer, the socket light goes red, and the
    /// corridor lamps start failing.</summary>
    AlarmStart = 0,

    /// <summary>Retired: the Nemesis no longer leaves its door in the opening.</summary>
    NemesisOpenDoor = 1,

    /// <summary>Retired: the Nemesis no longer leaves its door in the opening.</summary>
    NemesisWalkOut = 2,

    /// <summary>Retired: the Nemesis no longer leaves its door in the opening.</summary>
    NemesisLookLeft = 3,

    /// <summary>Retired: the Nemesis no longer leaves its door in the opening.</summary>
    NemesisLookRight = 4,

    /// <summary>Retired: the Nemesis no longer leaves its door in the opening.</summary>
    NemesisRunAway = 5,

    /// <summary>Retired: the player walks out on their own now.</summary>
    PlacePlayer = 6,

    /// <summary>The centre door (the safe-zone door) opens: the only way out.</summary>
    PlayerOpensSafeDoor = 7,

    /// <summary>Every other door of the corridor locks, one after another (rapid sequence).</summary>
    LockCorridorDoors = 8,

    /// <summary>Retired: the Nemesis appears in the corridor reveal, not in the opening.</summary>
    NemesisApproach = 9,

    /// <summary>Retired: the player walks out on their own now.</summary>
    PlayerRunToSpot = 10,

    /// <summary>Retired: control comes back after the corridor reveal.</summary>
    PlayerCameraPan = 11,
}
