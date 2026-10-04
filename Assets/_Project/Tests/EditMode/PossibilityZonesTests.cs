using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Where the search may go (Plan-Busqueda-Nemesis §3.4, Fase 2b and 2c): the zones
/// <see cref="PossibilityMap.CollectZones"/> cuts out of what the map still holds, on graphs built by
/// hand. The plan's cases as rules — a corridor with one exit (62), a T (63), a noise heard as an
/// area (the short search of D26), the Hub (67) — plus what is never a candidate: a hiding spot, the
/// Hub's door, the spot under its own feet.
///
/// What is tested is what the roll is MADE of (which places, with what share and what worth), never
/// the roll itself: that is one Random.value, and a test of it is a test of the seed.
/// </summary>
public class PossibilityZonesTests
{
    private const float Spacing = 2f;
    private const float Band = 1.5f;
    private const float Tick = 0.25f;
    private const float Speed = 4.5f;
    private const float ViewNodes = 3.5f;     // 7 m of clear range, in corridor nodes
    private const float ZoneRadius = 2.5f;    // SO_NemesisData.SearchMapZoneRadius as shipped
    private const float MinTravel = 1.5f;     // stopping distance 1 m + the picker's margin
    private const float SearchSpeed = 2.75f;  // SO_NemesisMovement.SearchSpeed as shipped
    private const int Max = 8;                // SO_NemesisData.SearchMapCandidates as shipped

    private static readonly Vector3 FarAway = new Vector3(500f, 0f, 500f);

    private static PossibilityGraph Corridor(int count)
    {
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < count; i++) graph.AddNode(new Vector3(i * Spacing, 0f, 0f));
        for (int i = 0; i + 1 < count; i++) graph.AddEdge(i, i + 1, Spacing);
        graph.Freeze();
        return graph;
    }

    /// <summary>An open room: <paramref name="side"/> × <paramref name="side"/> nodes linked to the
    /// eight around, the way the builder links a floor.</summary>
    private static PossibilityGraph Room(int side)
    {
        var graph = new PossibilityGraph(Spacing);
        for (int x = 0; x < side; x++)
            for (int z = 0; z < side; z++)
                graph.AddNode(new Vector3(x * Spacing, 0f, z * Spacing));

        for (int x = 0; x < side; x++)
        {
            for (int z = 0; z < side; z++)
            {
                int a = x * side + z;
                if (x + 1 < side) graph.AddEdge(a, (x + 1) * side + z, Spacing);
                if (z + 1 < side) graph.AddEdge(a, x * side + z + 1, Spacing);
                if (x + 1 < side && z + 1 < side) graph.AddEdge(a, (x + 1) * side + z + 1, Spacing * 1.4142f);
                if (x + 1 < side && z > 0) graph.AddEdge(a, (x + 1) * side + z - 1, Spacing * 1.4142f);
            }
        }

        graph.Freeze();
        return graph;
    }

    private static Vector3 At(float corridorNode) => new Vector3(corridorNode * Spacing, 0f, 0f);

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static float Sum(List<PossibilityMap.Zone> zones)
    {
        float sum = 0f;
        foreach (PossibilityMap.Zone zone in zones) sum += zone.Share;
        return sum;
    }

    // ── Case 62: one exit, never backwards ───────────────────────────────────

    [Test]
    public void Corridor_OnceItHasLookedDownIt_NoCandidateIsBehindIt()
    {
        var map = new PossibilityMap(Corridor(30));
        Assert.IsTrue(map.SeedPoint(At(6), Band, Vector3.right * 4f, 0f));

        var zones = new List<PossibilityMap.Zone>();

        // It runs to where it lost them at 3 m/s looking ahead, and then stands there looking down
        // the corridor: the same ticks as PossibilityMapTests, with the pick asked on every one.
        float nemesis = 0f;
        for (int t = 1; t <= 40; t++)
        {
            float now = t * Tick;
            nemesis = Mathf.Min(6f, 1.5f * now);

            map.Spread(Tick, Speed, now, 1f, 3f);
            for (int i = 0; i < map.Graph.NodeCount; i++)
            {
                float rel = i - nemesis;
                if (rel >= 0f && rel <= ViewNodes) map.ClearSeen(i);
            }
            map.Drain();
            map.Normalize();

            map.CollectZones(ZoneRadius, Band, Max, At(nemesis), MinTravel, zones);

            Assert.IsNotEmpty(zones, $"nowhere to go at t={now:0.00}");
            foreach (PossibilityMap.Zone zone in zones)
            {
                Assert.Greater(zone.Position.x, At(nemesis).x,
                               $"a candidate behind the Nemesis at t={now:0.00}");
            }
        }

        // Standing at the lost spot: every place left is past what it can see, towards the exit.
        foreach (PossibilityMap.Zone zone in zones)
            Assert.Greater(zone.Position.x, At(nemesis + ViewNodes - 1f).x);
    }

    // ── Case 63: a T — both arms, the one along the heading weighs more ──────

    [Test]
    public void TJunction_BothArmsAreCandidates_AndTheOneAlongTheHeadingIsWorthMore()
    {
        // Stem along +z up to the junction (0, 0, 8); arms along -x (left) and +x (right).
        var graph = new PossibilityGraph(Spacing);
        int junction = -1;
        for (int i = 0; i <= 4; i++)
        {
            int n = graph.AddNode(new Vector3(0f, 0f, i * Spacing));
            if (i > 0) graph.AddEdge(n - 1, n, Spacing);
            if (i == 4) junction = n;
        }

        int previousLeft = junction;
        int previousRight = junction;
        for (int i = 1; i <= 6; i++)
        {
            int l = graph.AddNode(new Vector3(-i * Spacing, 0f, 8f));
            graph.AddEdge(previousLeft, l, Spacing);
            previousLeft = l;

            int r = graph.AddNode(new Vector3(i * Spacing, 0f, 8f));
            graph.AddEdge(previousRight, r, Spacing);
            previousRight = r;
        }
        graph.Freeze();

        // Lost just short of the junction, going left. The Nemesis is down the stem looking +z: the
        // stem is all it sees.
        var map = new PossibilityMap(graph);
        map.SeedPoint(new Vector3(0f, 0f, 6f), Band, new Vector3(-3f, 0f, 1f), 0f);
        for (int t = 1; t <= 8; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 1f, 3f);
            for (int s = 0; s < 4; s++) map.ClearSeen(s);
            map.Normalize();
        }

        var zones = new List<PossibilityMap.Zone>();
        Vector3 nemesis = Vector3.zero;
        map.CollectZones(ZoneRadius, Band, Max, nemesis, MinTravel, zones);

        float leftShare = 0f, rightShare = 0f, leftWorth = 0f, rightWorth = 0f;
        foreach (PossibilityMap.Zone zone in zones)
        {
            Assert.GreaterOrEqual(zone.Position.z, 7.9f, "a candidate back down the stem it came up");

            // Up the stem and along the arm: the two arms are the same walk, mirrored.
            float seconds = (8f + Mathf.Abs(zone.Position.x)) / SearchSpeed;
            float worth = SearchPickRules.Worth(zone.Share, seconds);

            if (zone.Position.x < -1f)
            {
                leftShare += zone.Share;
                leftWorth = Mathf.Max(leftWorth, worth);
            }
            else if (zone.Position.x > 1f)
            {
                rightShare += zone.Share;
                rightWorth = Mathf.Max(rightWorth, worth);
            }
        }

        Assert.Greater(rightWorth, 0f, "the arm it was not heading for still has to be a candidate");
        Assert.Greater(leftShare, rightShare, "the way it saw them going should hold more");
        Assert.Greater(leftWorth, rightWorth, "and be worth more to the roll");
    }

    // ── What is never a candidate ────────────────────────────────────────────

    [Test]
    public void HidingSpotsAndTheHubsDoor_AreNeverACandidate_NorCountedInOne()
    {
        // A corridor ending at the Hub's door (node 5), with a locker off node 2.
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < 6; i++) graph.AddNode(At(i));
        for (int i = 0; i + 1 < 6; i++) graph.AddEdge(i, i + 1, Spacing);
        graph.MarkDrain(5);
        int locker = graph.AddNode(At(2) + Vector3.forward, PossibilityGraph.ENodeKind.HidingSpot);
        graph.AddEdge(2, locker, 1f);
        graph.Freeze();

        // A noise over the whole of it puts value everywhere, the locker and the doorway included
        // (the doorway only drains on the next tick).
        var map = new PossibilityMap(graph);
        map.SeedArea(At(3), 20f, Band);
        Assert.Greater(map.Value(locker), 0f);
        Assert.Greater(map.Value(5), 0f);

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, FarAway, MinTravel, zones);

        Assert.IsNotEmpty(zones);
        float searchable = 0f;
        for (int i = 0; i < 5; i++) searchable += map.Value(i);

        foreach (PossibilityMap.Zone zone in zones)
        {
            Assert.AreNotEqual(locker, zone.Node, "a hiding spot is not somewhere to walk and look");
            Assert.AreNotEqual(5, zone.Node, "the Hub's door is never a place to search (C5)");
        }

        // What the locker and the doorway hold is in nobody's share.
        Assert.AreEqual(searchable, Sum(zones), 1e-4f);
        Assert.Less(Sum(zones), 1f - map.Value(locker) - map.Value(5) + 1e-4f);
    }

    [Test]
    public void EverythingLeftIsInALocker_ThereIsNowhereToWalk()
    {
        var graph = new PossibilityGraph(Spacing);
        int floor = graph.AddNode(At(0));
        int locker = graph.AddNode(At(0) + Vector3.forward, PossibilityGraph.ENodeKind.HidingSpot);
        graph.AddEdge(floor, locker, 1f);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SeedArea(At(0), 2f, Band);
        map.ClearSeen(floor);
        map.Normalize();

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, FarAway, MinTravel, zones);

        // It still believes something (D38: "if they are nowhere, they are in a locker") — and that
        // is for whoever decides to open one, not somewhere for the search to walk to.
        Assert.IsTrue(map.HasValue);
        Assert.IsEmpty(zones);
        Assert.AreEqual(0f, map.SearchableSumNear(At(0), 10f, Band), 1e-6f);
    }

    [Test]
    public void EvidenceFromInsideTheHub_LeavesNowhereToGo()
    {
        var map = new PossibilityMap(Corridor(6));
        map.SeedSink();

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, FarAway, MinTravel, zones);

        Assert.IsEmpty(zones);
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(0f, 0.015f, map.SinkValue));
    }

    [Test]
    public void WhereItAlreadyStands_IsNotACandidate_ButWhatIsThereStillCounts()
    {
        // All of the value on the spot it is standing on and the nodes beside it.
        var map = new PossibilityMap(Corridor(9));
        map.SeedPoint(At(4), Band, Vector3.zero, 0f);

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, At(4), MinTravel, zones);

        Assert.IsNotEmpty(zones, "the value under its feet has to be looked at from somewhere");
        foreach (PossibilityMap.Zone zone in zones)
            Assert.GreaterOrEqual(FlatDistance(zone.Position, At(4)), MinTravel);

        // A step away is where it goes to look at it from: nothing was dropped.
        Assert.AreEqual(1f, Sum(zones), 1e-4f);

        // From anywhere else, the spot itself is the place.
        map.CollectZones(ZoneRadius, Band, Max, At(0), MinTravel, zones);
        Assert.AreEqual(4, zones[0].Node);
    }

    // ── How the zones are cut ────────────────────────────────────────────────

    [Test]
    public void Zones_NeverShareANode_AndComeMostValuableFirst()
    {
        var map = new PossibilityMap(Room(11));
        map.SeedArea(new Vector3(10f, 0f, 10f), 5f, Band);

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, 64, FarAway, MinTravel, zones);

        // Every node counted once: together they hold exactly what is on the floor.
        Assert.AreEqual(1f, Sum(zones), 1e-3f);

        for (int i = 0; i + 1 < zones.Count; i++)
            Assert.LessOrEqual(zones[i + 1].Share, zones[i].Share * 1.01f, $"zone {i + 1} out of order");

        // And the cap is a cap.
        map.CollectZones(ZoneRadius, Band, 3, FarAway, MinTravel, zones);
        Assert.AreEqual(3, zones.Count);
    }

    [Test]
    public void LoneNodeOfValue_ThePlaceIsTheNodeItself()
    {
        // Every neighbour within the radius "gathers" all of it too; the place to stand is the node.
        var map = new PossibilityMap(Corridor(9));
        map.SeedPoint(At(4), Band, Vector3.zero, 0f);
        map.ClearSeen(3);
        map.ClearSeen(5);
        map.Normalize();

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, At(0), MinTravel, zones);

        Assert.AreEqual(1, zones.Count);
        Assert.AreEqual(4, zones[0].Node);
        Assert.AreEqual(1f, zones[0].Share, 1e-4f);
    }

    [Test]
    public void TwoPatchesEitherSideOfASpotItCleared_ThePlaceIsTheMiddle()
    {
        var map = new PossibilityMap(Corridor(9));
        map.SeedArea(At(4), 2f, Band);
        map.ClearSeen(4);
        map.ClearSeen(2);
        map.ClearSeen(6);
        map.Normalize();

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, At(0), MinTravel, zones);

        // Nodes 3 and 5 hold it; node 4, empty, is where both can be looked at from.
        Assert.AreEqual(1, zones.Count);
        Assert.AreEqual(4, zones[0].Node);
        Assert.AreEqual(1f, zones[0].Share, 1e-4f);
    }

    [Test]
    public void BelievingNothing_ThereAreNoZones()
    {
        var map = new PossibilityMap(Corridor(5));
        var zones = new List<PossibilityMap.Zone> { new PossibilityMap.Zone(0, Vector3.zero, 1f) };

        map.CollectZones(ZoneRadius, Band, Max, FarAway, MinTravel, zones);

        Assert.IsEmpty(zones, "the list is cleared even when there is nothing to put in it");
    }

    // ── 2c: a noise is an area, and the search goes to places IN it ──────────

    [Test]
    public void NoiseArea_CandidatesAreSpreadAcrossIt_AndNoneOfThemIsThePoint()
    {
        // A footstep heard across a room: "over there", four metres wide.
        Vector3 heard = new Vector3(10f, 0f, 10f);
        const float radius = 4f;

        var map = new PossibilityMap(Room(11));
        map.SeedArea(heard, radius, Band);

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, 64, FarAway, MinTravel, zones);

        // It stays in the area: nothing outside what the noise seeded, and all of it accounted for.
        Assert.AreEqual(1f, Sum(zones), 1e-3f);
        foreach (PossibilityMap.Zone zone in zones)
            Assert.LessOrEqual(FlatDistance(zone.Position, heard), radius + Spacing + 0.01f);

        // It is an AREA: several places, on every side of where the ear put the noise.
        Assert.GreaterOrEqual(zones.Count, 5);
        bool west = false, east = false, south = false, north = false;
        foreach (PossibilityMap.Zone zone in zones)
        {
            west |= zone.Position.x < heard.x - 1f;
            east |= zone.Position.x > heard.x + 1f;
            south |= zone.Position.z < heard.z - 1f;
            north |= zone.Position.z > heard.z + 1f;
        }
        Assert.IsTrue(west && east && south && north, "candidates on one side of the noise only");

        // And the point is not preferred: no place — the one on the last footstep least of all —
        // holds more than a fraction of it. A roll over these is a search of the area, not a walk
        // to the locker door (WIR-057).
        foreach (PossibilityMap.Zone zone in zones)
            Assert.Less(zone.Share, 0.3f);
    }

    [Test]
    public void NoiseArea_AfterInvestigatingThePoint_EveryCandidateIsSomewhereElseInIt()
    {
        // D26: Investigating walked to the noise and found nothing; the short search starts standing
        // on the point. Where it goes next is the rest of the area.
        Vector3 heard = new Vector3(10f, 0f, 10f);
        const float radius = 4f;

        var map = new PossibilityMap(Room(11));
        map.SeedArea(heard, radius, Band);

        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, 64, heard, MinTravel, zones);

        Assert.GreaterOrEqual(zones.Count, 5);
        foreach (PossibilityMap.Zone zone in zones)
        {
            Assert.GreaterOrEqual(FlatDistance(zone.Position, heard), MinTravel);
            Assert.LessOrEqual(FlatDistance(zone.Position, heard), radius + Spacing + 0.01f);
        }
    }

    // ── Case 67: the Hub ─────────────────────────────────────────────────────

    [Test]
    public void ValueThatRanIntoTheHub_IsNothingWorthTheWalk_WhateverIsLeftAtItsDoor()
    {
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < 6; i++) graph.AddNode(At(i));
        for (int i = 0; i + 1 < 6; i++) graph.AddEdge(i, i + 1, Spacing);
        graph.MarkDrain(5);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SeedPoint(At(3), Band, Vector3.zero, 0f);

        // Just lost, two nodes from the door: the corridor is still where they most likely are.
        map.Spread(Tick, Speed, Tick, 0f, 0f);
        map.Drain();
        map.Normalize();
        Assert.IsFalse(SearchCooling.NothingWorthTheWalk(0.2f, 0.015f, map.SinkValue));

        for (int t = 2; t <= 40; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 0f, 0f);
            map.Drain();
            map.Normalize();
        }

        // Ten seconds on, most of it went in. The corridor that leads to the door still holds a place
        // with a good share of what is left — and walking to it would be camping the Hub (C5).
        var zones = new List<PossibilityMap.Zone>();
        map.CollectZones(ZoneRadius, Band, Max, At(0), MinTravel, zones);

        Assert.Greater(map.SinkValue, SearchCooling.SinkMajority);
        Assert.IsNotEmpty(zones);
        Assert.IsTrue(SearchCooling.NothingWorthTheWalk(SearchPickRules.Worth(zones[0].Share, 1f), 0.015f,
                                                        map.SinkValue));
    }
}
