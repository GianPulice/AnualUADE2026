using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chooses where the search goes next, off the possibility map (Plan-Busqueda-Nemesis §3.4, Fase 2b):
/// "tira entre los lugares por valor ÷ (1 + tiempo de llegada): una tirada, no el máximo".
///
/// WHAT IT REPLACES
///
/// NemesisFreeRoam, which swept a DISC around the belief: points sampled evenly inside it, weighted by
/// "near the centre" and "sooner", clipped by the walls seen from the centre, three to eight metres
/// wide. Two things followed from the shape, and no amount of weighting fixed either:
///
///   A DISC HAS NO DIRECTION (WIR-062). In a corridor the points behind the Nemesis — the way it had
///   just come, the stretch it had been looking down the whole chase — were as good as the points
///   ahead, and a little better, being closer to its body. It searched backwards as readily as
///   forwards.
///
///   A DISC DOES NOT GO THROUGH A DOOR. Capped at eight metres and clipped by line of sight from its
///   centre, the room past the only exit was outside it by construction. However plainly the player
///   could only have gone that way, the search could not follow.
///
/// The possibility map has both for free: the value only ever moves along walkable edges, and what
/// the Nemesis looks at is emptied. So nothing is left behind it (it was looking there), and what is
/// left has already run on through the door. This class only has to go where the value is.
///
/// THE TWO HALVES OF A PICK
///
///   PURE (PossibilityMap.CollectZones): the value still on the floor, cut into a few ZONES — a place
///   to stand and the share of the value around it. Never a hiding spot, never the Hub, never where
///   it already stands.
///
///   SCENE (here): how long each zone takes to WALK to — a path query, at the search's speed — and
///   the roll, by SearchPickRules.Worth. A zone it cannot reach on foot is worth nothing, and "on
///   foot" excludes the freight elevator: a search that walks onto the lift's link hands the body to
///   NemesisElevatorUser, the ladder calls that Traversing, and the ride ends back in Searching. The
///   lift is the ladder's decision ("para llegar hay que tomar el montacargas"), never a side effect
///   of where a search happened to roll. Drops stay allowed: they are the hunting shortcut the plan
///   gives it (§15), one way, with their own cooldown.
///
/// A ROLL AND NOT AN ARGMAX, like every other selection in this system. And only among places WORTH
/// THE WALK (SearchCooling.NothingWorthTheWalk): when the best of them is under the threshold the
/// search does not wander off to the least bad one — it stays where it is, and the state reads that as
/// "revisó todo".
///
/// COST: one path query per zone that holds enough to matter, at most SearchMapCandidates of them,
/// once per pick. A pick happens when the search arrives somewhere, when the place it was heading to
/// has lost its value, or when new evidence moves it — never per frame. The state throttles the last
/// two.
///
/// WHY IT IS NOT A MonoBehaviour: same shape as <see cref="NemesisPursuit"/>, which it sits beside. A
/// plain object constructed with the state manager and owned by the state that uses it; it needs no
/// Update of its own and nothing about it belongs on a GameObject.
/// </summary>
public sealed class NemesisSearchPicker
{
    /// <summary>One of the places the last pick weighed. Kept after the pick so the gizmos and F9 can
    /// show why it went where it went.</summary>
    public readonly struct Candidate
    {
        /// <summary>The middle of the zone: where it would stand.</summary>
        public readonly Vector3 Position;

        /// <summary>The zone's share of everything the map believes.</summary>
        public readonly float Share;

        /// <summary>Seconds to walk there at the search's speed. Infinity when it cannot be walked
        /// to: no complete path, or one that needs the freight elevator. NaN when nobody asked: the
        /// zone holds too little to be worth any walk.</summary>
        public readonly float Seconds;

        /// <summary>Share ÷ (1 + Seconds): what the roll weighs it by. 0 when it cannot be walked to,
        /// or was not worth asking about.</summary>
        public readonly float Worth;

        /// <summary>What it actually rolled by: <see cref="Worth"/>, times how far it lies along the
        /// player's heading when the pick was handed one (<see cref="HeadingHint"/>). 0 when it took
        /// no part in the roll.</summary>
        public readonly float Weight;

        /// <summary>Whether it took part in the roll: reachable, and worth the walk.</summary>
        public readonly bool InRoll;

        public Candidate(Vector3 position, float share, float seconds, float worth, float weight, bool inRoll)
        {
            Position = position;
            Share = share;
            Seconds = seconds;
            Worth = worth;
            Weight = weight;
            InRoll = inRoll;
        }
    }

    /// <summary>
    /// The way the player was last seen going, for the one pick that should care: the first of a
    /// search a chase has just handed over to (SearchPickRules.HeadingWeight). The default value is
    /// "no heading", and the pick is the plain roll by worth.
    /// </summary>
    public readonly struct HeadingHint
    {
        public readonly bool Known;
        public readonly Vector3 LastSeen;
        public readonly Vector3 Direction;

        public HeadingHint(Vector3 lastSeen, Vector3 direction)
        {
            Known = true;
            LastSeen = lastSeen;
            Direction = direction;
        }
    }

    /// <summary>
    /// How far past the agent's stopping distance a place has to be to count as somewhere to GO.
    /// Inside it the agent "arrives" without taking a step or turning, so the look-around that
    /// follows faces wherever it already faced — and whatever it missed, it misses again.
    /// </summary>
    private const float MinTravelMargin = 0.5f;

    // Fallbacks for a missing SO, which ValidateReferences already reports as an error. They exist
    // so a broken prefab still searches something sensible.
    private const float FallbackZoneRadius = 2.5f;
    private const int FallbackCandidates = 8;
    private const float FallbackWorthThreshold = 0.015f;
    private const float FallbackSearchSpeed = 2.75f;
    private const float FallbackHeadingBoost = 4f;

    private readonly NemesisStateManager stateManager;

    // Reused across picks: this runs every second or so while searching, and the path queries below
    // are the cost worth paying — three lists a pick are not.
    private readonly List<PossibilityMap.Zone> zones = new List<PossibilityMap.Zone>();
    private readonly List<Candidate> candidates = new List<Candidate>();
    private readonly List<float> weights = new List<float>();

    public NemesisSearchPicker(NemesisStateManager manager)
    {
        stateManager = manager;
    }

    // ── The last pick ────────────────────────────────────────────────────────

    /// <summary>The places the last pick weighed, most valuable first. Empty when the map held
    /// nothing on the floor.</summary>
    public IReadOnlyList<Candidate> Candidates => candidates;

    /// <summary>Which of <see cref="Candidates"/> the roll chose, or -1.</summary>
    public int ChosenIndex { get; private set; } = -1;

    /// <summary>The best worth among the candidates of the last pick: what "revisó todo" is judged
    /// on. 0 with no candidate it can walk to.</summary>
    public float BestWorth { get; private set; }

    /// <summary>The worth of everything that took part in the last roll: a candidate's chance was
    /// its own worth over this.</summary>
    public float RollTotal { get; private set; }

    /// <summary>The share of the value in the Hub's sink at the last pick.</summary>
    public float SinkShare { get; private set; }

    /// <summary>
    /// The last pick found the map holding value and no place worth walking to
    /// (SearchCooling.NothingWorthTheWalk): too spread out, out of reach, inside hiding spots, or
    /// gone into the Hub. The answer of the last <see cref="TryPick"/>, not a fresh query — it is the
    /// same set of path queries as the pick, and there is no reason to pay for them twice.
    /// </summary>
    public bool NothingWorthTheWalk { get; private set; }

    // ── What it reads ────────────────────────────────────────────────────────

    private SO_NemesisData Data => stateManager.NemesisData;

    private PossibilityMap Map
    {
        get
        {
            NemesisPossibilityMap possibility = stateManager.PossibilityMap;
            return possibility != null && possibility.IsBuilt ? possibility.Map : null;
        }
    }

    /// <summary>There is a possibility map to pick from at all: built, over a level that has NavMesh
    /// for the Nemesis.</summary>
    public bool HasMap
    {
        get
        {
            PossibilityMap map = Map;
            return map != null && map.Graph.NodeCount > 0;
        }
    }

    /// <summary>The map believes something. False with no belief (the facade clears it), and once
    /// everything it could believe has been looked at.</summary>
    public bool HasValue
    {
        get
        {
            PossibilityMap map = Map;
            return map != null && map.HasValue;
        }
    }

    /// <summary>
    /// The belief sequence the map last folded in (NemesisPossibilityMap.EvidenceSequence): moves
    /// when new evidence about the player has redrawn the map. What the state watches to know the
    /// place it is heading to has to be judged again — the map's, not the belief's own, which runs up
    /// to a tick ahead and would have it judged against a map that had not changed yet.
    /// </summary>
    public int EvidenceSequence
    {
        get
        {
            NemesisPossibilityMap possibility = stateManager.PossibilityMap;
            return possibility != null ? possibility.EvidenceSequence : int.MinValue;
        }
    }

    /// <summary>The radius of a place, in metres (SO_NemesisData.SearchMapZoneRadius).</summary>
    public float ZoneRadius => Data != null ? Data.SearchMapZoneRadius : FallbackZoneRadius;

    /// <summary>Below this worth a place is not worth the walk (SearchMapWorthThreshold).</summary>
    public float WorthThreshold => Data != null ? Data.SearchMapWorthThreshold : FallbackWorthThreshold;

    /// <summary>How much more a place dead ahead of the player's heading weighs in the pick a chase
    /// hands over with (SearchMapChaseHeadingBoost).</summary>
    public float HeadingBoost => Data != null ? Data.SearchMapChaseHeadingBoost : FallbackHeadingBoost;

    /// <summary>Whether the last pick leant towards where the player was heading. For F9.</summary>
    public bool LeantOnHeading { get; private set; }

    /// <summary>How far away a place has to be to count as somewhere to go. See
    /// <see cref="MinTravelMargin"/>.</summary>
    public float MinTravel => stateManager.DefaultStoppingDistance + MinTravelMargin;

    private int MaxCandidates => Data != null ? Mathf.Max(1, Data.SearchMapCandidates) : FallbackCandidates;

    private float SearchSpeed
    {
        get
        {
            SO_NemesisMovement movement = stateManager.NemesisMovement;
            return Mathf.Max(0.5f, movement != null ? movement.SearchSpeed : FallbackSearchSpeed);
        }
    }

    /// <summary>
    /// The value a search can still look at around a point: what a zone centred there would gather.
    /// What the state measures the place it is heading to against, when it was picked and now.
    /// </summary>
    public float ShareAt(Vector3 point)
    {
        PossibilityMap map = Map;
        if (map == null || !map.HasValue) return 0f;

        return map.SearchableSumNear(point, ZoneRadius, NemesisPossibilityMap.FloorBand);
    }

    /// <summary>Whether the search may send the agent to a point: a complete path, and one that does
    /// not go through the freight elevator. One path query.</summary>
    public bool IsReachableOnFoot(Vector3 point) =>
        !float.IsPositiveInfinity(SecondsOnFoot(stateManager.transform.position, point, SearchSpeed));

    // ── The pick ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rolls the next place to look at among the zones the map still holds value in.
    /// </summary>
    /// <returns>false when there is nowhere to go: the map holds nothing (<see cref="HasValue"/>
    /// false — the caller's last-resort scatter), or what it holds is not worth walking to
    /// (<see cref="NothingWorthTheWalk"/> — the caller stays put and looks around).</returns>
    /// <param name="heading">Which way the player was last seen going, when this is the pick a chase
    /// hands over with; left out, the plain roll by worth.</param>
    public bool TryPick(out Candidate chosen, in HeadingHint heading = default)
    {
        chosen = default;
        Clear();

        float boost = HeadingBoost;
        LeantOnHeading = heading.Known && boost > 1f;

        // The map ticks four times a second, and the belief may be up to a tick ahead of it. A pick
        // off a map that has not heard the last footstep yet is a walk the wrong way and a turn
        // round half a second later, so the pick waits for nothing: it has the map catch up first.
        NemesisPossibilityMap possibility = stateManager.PossibilityMap;
        if (possibility != null) possibility.CatchUp();

        PossibilityMap map = Map;
        if (map == null || !map.HasValue) return false;

        Vector3 origin = stateManager.transform.position;
        float speed = SearchSpeed;
        float threshold = WorthThreshold;

        SinkShare = map.SinkValue;

        map.CollectZones(ZoneRadius, NemesisPossibilityMap.FloorBand, MaxCandidates, origin, MinTravel, zones);

        for (int i = 0; i < zones.Count; i++)
        {
            PossibilityMap.Zone zone = zones[i];

            // The reachability the disc used to path-test for too, and for the same reason: a
            // destination the agent cannot walk to is how it ends up pressed against the geometry in
            // between with remainingDistance at zero.
            //
            // Not asked for a zone that holds less than the threshold: its worth is its share ÷ (1 +
            // seconds), never more than the share, so no walk however short makes it worth it. The
            // tail of the spread is most of the zones once the value has thinned out, and each one
            // skipped is a path query saved.
            float seconds = zone.Share >= threshold ? SecondsOnFoot(origin, zone.Position, speed) : float.NaN;
            float worth = SearchPickRules.Worth(zone.Share, seconds);
            bool inRoll = worth > 0f && worth >= threshold;

            // The threshold is asked of the worth, never of the weight: leaning the roll towards the
            // player's heading must not make a place that is not worth the walk worth it.
            float weight = 0f;
            if (inRoll)
            {
                weight = LeantOnHeading
                    ? worth * SearchPickRules.HeadingWeight(
                          SearchPickRules.HeadingAlignment(heading.LastSeen, heading.Direction, zone.Position), boost)
                    : worth;
            }

            candidates.Add(new Candidate(zone.Position, zone.Share, seconds, worth, weight, inRoll));
            weights.Add(weight);

            if (worth > BestWorth) BestWorth = worth;
            RollTotal += weight;
        }

        NothingWorthTheWalk = SearchCooling.NothingWorthTheWalk(BestWorth, threshold, SinkShare);
        if (NothingWorthTheWalk || RollTotal <= 0f) return false;

        // Roulette answers uniformly when every weight is zero ("it still has to go somewhere"); here
        // that is the case already turned away above, and the index is checked all the same.
        int index = RouletteSelection.Roulette(weights);
        if (index < 0 || weights[index] <= 0f) return false;

        ChosenIndex = index;
        chosen = candidates[index];
        return true;
    }

    /// <summary>Forgets the last pick. Called on entering and leaving the state, so the gizmos never
    /// draw the candidates of a search that is over.</summary>
    public void Clear()
    {
        zones.Clear();
        candidates.Clear();
        weights.Clear();
        ChosenIndex = -1;
        BestWorth = 0f;
        RollTotal = 0f;
        SinkShare = 0f;
        NothingWorthTheWalk = false;
        LeantOnHeading = false;
    }

    /// <summary>
    /// Seconds to walk from one point to another at a speed, or infinity when the search may not
    /// send the agent there: no complete path (another island, the far side of a drop it cannot climb
    /// back from), or a path through the freight elevator (see the class comment).
    /// </summary>
    private static float SecondsOnFoot(Vector3 from, Vector3 to, float speed)
    {
        if (!NemesisNav.TryGetRoute(from, to, out NemesisNav.NavRoute route)) return float.PositiveInfinity;
        if (!route.IsComplete || route.CrossedElevator != null) return float.PositiveInfinity;

        return route.PathDistance / speed;
    }
}
