using UnityEngine;

/// <summary>
/// One hiding stay at a time, and the escape it may turn into (plan R1, D28). This is the part of
/// PlayerHabitTracker that decides WHETHER something was an escape, kept pure so the cases that are
/// easy to get wrong can be tested without a scene.
///
/// AN ESCAPE IS NOT DECIDED AT THE DOOR. Getting out while the Nemesis is pulling the player out, or
/// being seen leaving and caught in the chase that follows, is being caught, not getting away. So a
/// hunted stay leaves a PENDING escape. It is only confirmed once the player has been out for
/// <see cref="ConfirmSeconds"/> with no capture and no chase running. A capture cancels it, and so
/// does the player going away with the level.
///
/// ONE SEARCH PER HUNT. A search can end because the Nemesis heard an exhale, investigated it and
/// came back to search again without ever returning to patrol. That is one hunt outlasted, not two,
/// so <see cref="NoteSearchOutlasted"/> counts once per hunt id; the caller decides where a hunt
/// starts and ends.
/// </summary>
public class HidingStayBook
{
    /// <summary>What a confirmed escape hands back.</summary>
    public readonly struct Escape
    {
        public readonly string SpotKey;
        public readonly int SearchesOutlasted;

        public Escape(string spotKey, int searchesOutlasted)
        {
            SpotKey = spotKey;
            SearchesOutlasted = searchesOutlasted;
        }
    }

    /// <summary>
    /// Seconds out of the spot before an escape stands: the climb out, the 0.8 s pull-out and a short
    /// dash into arm's reach all fit inside it. A capture in some later, separate encounter is past
    /// it and does not reach back to cancel an escape that already happened.
    /// </summary>
    public const float DefaultConfirmSeconds = 5f;

    private readonly float confirmSeconds;

    private string stayKey;
    private bool hunted;
    private int searchesOutlasted;
    private int lastCountedHunt;
    private bool hasCountedHunt;

    private bool hasPending;
    private Escape pending;
    private float pendingSince;

    public HidingStayBook(float confirmSeconds = DefaultConfirmSeconds) =>
        this.confirmSeconds = Mathf.Max(0f, confirmSeconds);

    public float ConfirmSeconds => confirmSeconds;

    /// <summary>The spot of the stay in progress, or null.</summary>
    public string StayKey => stayKey;

    /// <summary>Whether the Nemesis hunted nearby during the stay in progress.</summary>
    public bool IsHunted => stayKey != null && hunted;

    /// <summary>Hunts outlasted during the stay in progress.</summary>
    public int SearchesOutlasted => stayKey != null ? searchesOutlasted : 0;

    /// <summary>Whether a finished stay is waiting to be confirmed as an escape.</summary>
    public bool HasPendingEscape => hasPending;

    /// <summary>
    /// Starts a stay in <paramref name="spotKey"/>. A pending escape from an earlier stay comes back
    /// first in <paramref name="confirmed"/>: getting into another spot unharmed confirms it.
    /// </summary>
    public bool Enter(string spotKey, out Escape confirmed)
    {
        bool had = TakePending(out confirmed);

        stayKey = string.IsNullOrEmpty(spotKey) ? null : spotKey;
        ResetStay();

        return had;
    }

    /// <summary>The Nemesis hunted near the spot during the stay.</summary>
    public void MarkHunted()
    {
        if (stayKey != null) hunted = true;
    }

    /// <summary>
    /// A search ended near the spot without finding the player, during hunt
    /// <paramref name="huntId"/>. Counts once per hunt, and counts as hunting nearby either way.
    /// True when it counted.
    /// </summary>
    public bool NoteSearchOutlasted(int huntId)
    {
        if (stayKey == null) return false;

        hunted = true;
        if (hasCountedHunt && huntId == lastCountedHunt) return false;

        hasCountedHunt = true;
        lastCountedHunt = huntId;
        searchesOutlasted++;
        return true;
    }

    /// <summary>
    /// The stay is over. <paramref name="chosen"/> means the player walked out: not a capture, not a
    /// cinematic taking them, not the level going away. A chosen way out of a hunted stay leaves a
    /// pending escape; any other way out leaves nothing.
    /// </summary>
    public void Exit(bool chosen, float now)
    {
        if (stayKey == null) return;

        if (chosen && hunted)
        {
            pending = new Escape(stayKey, searchesOutlasted);
            pendingSince = now;
            hasPending = true;
        }

        stayKey = null;
        ResetStay();
    }

    /// <summary>The player was caught, or went away: no pending escape survives that. True if one
    /// was cancelled.</summary>
    public bool CancelPending()
    {
        bool had = hasPending;
        hasPending = false;
        return had;
    }

    /// <summary>
    /// Hands back the pending escape once the player has been out for <see cref="ConfirmSeconds"/>
    /// and no chase is running. A chase that started as they came out holds it until the chase ends,
    /// however long that takes: if it ends in a capture, the capture cancels the escape first.
    /// </summary>
    public bool TryConfirm(float now, bool chaseRunning, out Escape escape)
    {
        escape = default;
        if (!hasPending || chaseRunning || now - pendingSince < confirmSeconds) return false;

        return TakePending(out escape);
    }

    /// <summary>Forgets the stay and any pending escape: New Game.</summary>
    public void Clear()
    {
        stayKey = null;
        ResetStay();
        hasPending = false;
    }

    private void ResetStay()
    {
        hunted = false;
        searchesOutlasted = 0;
        hasCountedHunt = false;
    }

    private bool TakePending(out Escape escape)
    {
        escape = hasPending ? pending : default;

        bool had = hasPending;
        hasPending = false;
        return had;
    }
}
