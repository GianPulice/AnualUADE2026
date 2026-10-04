using UnityEngine;

/// <summary>
/// How many <c>ChaseStalled</c> one chase is worth to the habit ledger: ONE, however long it
/// stalls (bug report 30/09).
///
/// WHY ONE PER CHASE AND NOT ONE PER WINDOW. NemesisChaseProgress judges a window every few
/// seconds, and a chase that stays stalled fails window after window. Raised each time, one chase
/// of 50 s added 8 stalls and a 9-minute session ended with 17, and the habit ledger took every one
/// for a separate time the player had pulled the trick: "estanca" took the flank and zone-defence
/// counterplays from 35% to 85% in five minutes, the habit decay (5 minutes of hold, 0.1 a minute)
/// could never keep up, and the thresholds of SO_CounterplayRules (flank at 1, zone defence at 2)
/// were being measured in windows when the plan wrote them in occasions.
///
/// <see cref="HabitLedger"/> counts +1 per <c>RegisterExploit</c> and unlocks by that count, so a
/// habit is an OCCURRENCE: it cannot tell "one long loop" from "the same loop eight times", and has
/// no business trying. The cut belongs upstream, where the chase is known: this class.
///
/// WHY THE FIRST STALLED WINDOW AND NOT "WHEN THE PLAYER REALLY GETS AWAY". The other candidate rule
/// was to count a stall only if the chase ended with the Nemesis losing the player. Rejected: the
/// window already says the player outran it for a whole window while in plain sight, which is the
/// exploit; waiting for the outcome would not count the loop the flank counterplay breaks (the
/// player is caught: a loop all the same, and the very thing the counterplay exists for), it would
/// make "estanca" invisible in F9 until the chase was over, and it would turn the retest "one long
/// chase adds 1" into "adds 0 unless you also escape". Counting the first stalled window keeps the
/// number readable live and bounded: one per chase.
///
/// WHAT "A CHASE" IS. The Nemesis being in Chasing, with a regroup tolerance: it flickers between
/// Chasing and Searching when the sight breaks for a second (the chase-flicker bug), and every
/// re-entry would start a "new chase" that counts again. So a chase only ends once the Nemesis has
/// been out of Chasing for more than <c>regroupSeconds</c>, or on a capture (<see cref="EndChase"/>).
/// Two loops separated by a real search are two chases and count twice.
///
/// PURE: the clock is passed in, so EditMode tests reach it.
/// </summary>
public class ChaseStallCounter
{
    private bool counted;
    private float lastChasingAt = float.NegativeInfinity;

    /// <summary>Whether this chase has already been counted. For the HUD.</summary>
    public bool CountedThisChase => counted;

    /// <summary>Windows that expired without progress in this chase, counted or not. For the HUD:
    /// "8 windows, 1 counted" is what makes the new rule readable. Level geometry is read off it
    /// too: a column that reports one a window for a minute beats the chase outright.</summary>
    public int WindowsThisChase { get; private set; }

    /// <summary>
    /// Call every tick the Nemesis is chasing, before anything is judged. Starts a new chase when
    /// the last tick that saw it chasing was more than <paramref name="regroupSeconds"/> ago.
    /// </summary>
    public void NoteChasing(float now, float regroupSeconds)
    {
        if (now - lastChasingAt > Mathf.Max(0f, regroupSeconds)) EndChase();

        lastChasingAt = now;
    }

    /// <summary>
    /// A window expired without progress. True when this is the one that counts: the first of the
    /// chase. False for every later one, which still shows in <see cref="WindowsThisChase"/>.
    /// </summary>
    public bool RegisterStalledWindow()
    {
        WindowsThisChase++;

        if (counted) return false;

        counted = true;
        return true;
    }

    /// <summary>The chase is over for good (a capture), without waiting out the regroup time. The
    /// next stalled window counts again.</summary>
    public void EndChase()
    {
        counted = false;
        WindowsThisChase = 0;
    }
}
