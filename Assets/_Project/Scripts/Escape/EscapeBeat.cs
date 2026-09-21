/// <summary>
/// What an <see cref="EscapeBeatMarker"/> makes happen when the Timeline playhead crosses it. One
/// action each — the marker only names it, and <see cref="EscapeSequenceDirector"/> does it using
/// the scene objects wired on its Stage.
///
/// Append-only: a Timeline asset stores the numeric value, so reordering or inserting in the
/// middle would silently retarget every marker already placed.
/// </summary>
public enum EscapeBeat
{
    // ── Opening cinematic (Pasos 1 to 3) ────────────────────────────────────

    /// <summary>The alarm starts: alarm loop + tension layer, and the socket lights go red.</summary>
    AlarmStart = 0,

    /// <summary>The side door in front of the freight lift opens.</summary>
    NemesisOpenDoor = 1,

    /// <summary>The Nemesis steps out of the side door, walking, to the doorway marker.</summary>
    NemesisWalkOut = 2,

    /// <summary>The Nemesis turns to face the "look left" marker.</summary>
    NemesisLookLeft = 3,

    /// <summary>The Nemesis turns to face the "look right" marker.</summary>
    NemesisLookRight = 4,

    /// <summary>The Nemesis starts running to the exit marker, out of frame.</summary>
    NemesisRunAway = 5,

    /// <summary>The player is placed at the run's start marker (put this while the shot is
    /// elsewhere: the player must not be seen popping in).</summary>
    PlacePlayer = 6,

    /// <summary>The safe-zone door opens.</summary>
    PlayerOpensSafeDoor = 7,

    /// <summary>Every other door of the corridor locks, one after another (rapid sequence).</summary>
    LockCorridorDoors = 8,

    /// <summary>The Nemesis appears down the corridor and runs towards the player; it is still
    /// running when control comes back.</summary>
    NemesisApproach = 9,

    /// <summary>The player sprints from the start marker to the spot where control comes back.</summary>
    PlayerRunToSpot = 10,

    /// <summary>The last shot: the player's camera orbits from the right round to the nape, until
    /// the Timeline ends. Nothing else should be the live camera from here on (no shot clip).</summary>
    PlayerCameraPan = 11,
}
