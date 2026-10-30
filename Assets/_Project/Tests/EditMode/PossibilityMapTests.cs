using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The possibility map (Plan-Busqueda-Nemesis §3, Fase 2a) on graphs built by hand: a corridor with
/// one exit, a T, a Hub door, a locker, a lift. Each case is the rule the plan states, run as a few
/// seconds of ticks — spread, clear what it looks at, drain, renormalise — the same order the scene
/// component uses.
/// </summary>
public class PossibilityMapTests
{
    private const float Spacing = 2f;
    private const float Band = 1.5f;
    private const float Tick = 0.25f;
    private const float Speed = 4.5f;
    private const float ViewNodes = 3.5f;   // 7 m of clear range, in corridor nodes

    /// <summary>A straight corridor along +x, one node every Spacing metres.</summary>
    private static PossibilityGraph Corridor(int count)
    {
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < count; i++) graph.AddNode(new Vector3(i * Spacing, 0f, 0f));
        for (int i = 0; i + 1 < count; i++) graph.AddEdge(i, i + 1, Spacing);
        graph.Freeze();
        return graph;
    }

    private static Vector3 At(int corridorNode) => new Vector3(corridorNode * Spacing, 0f, 0f);

    /// <summary>One tick with the Nemesis at <paramref name="nemesis"/> looking +x down the corridor.
    /// </summary>
    private static void TickCorridor(PossibilityMap map, float nemesis, float now, float bias = 1f)
    {
        map.Spread(Tick, Speed, now, bias, 3f);
        for (int i = 0; i < map.Graph.NodeCount; i++)
        {
            float rel = i - nemesis;
            if (rel >= 0f && rel <= ViewNodes) map.ClearSeen(i);
        }
        map.Drain();
        map.Normalize();
    }

    private static float SumRange(PossibilityMap map, int from, int to)
    {
        float sum = 0f;
        for (int i = from; i <= to; i++) sum += map.Value(i);
        return sum;
    }

    // ── WIR-062: one exit, never backwards ───────────────────────────────────

    [Test]
    public void Corridor_ValueNeverAppearsBehindTheNemesis()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        Assert.IsTrue(map.SeedPoint(At(6), Band, Vector3.right * 4f, 0f));

        // It walks towards where it lost them at 3 m/s, looking ahead the whole time.
        float nemesis = 0f;
        for (int t = 1; t <= 24; t++)
        {
            float now = t * Tick;
            nemesis = Mathf.Min(6f, 1.5f * now);
            TickCorridor(map, nemesis, now);

            int behind = Mathf.FloorToInt(nemesis);
            Assert.Less(SumRange(map, 0, behind), 1e-5f, $"value behind the Nemesis at t={now:0.00}");
        }

        // What is left is past what it can see, on the way to the exit.
        Assert.IsTrue(map.HasValue);
        Assert.Greater(map.BestNode(), nemesis + ViewNodes);
    }

    [Test]
    public void Corridor_LostAheadOfTheChase_ValueKeepsHeadingForTheExit()
    {
        // It lost them six metres ahead, the way a chase does, and stands looking down the corridor.
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(6), Band, Vector3.right * 4f, 0f);

        int lastBest = -1;
        for (int t = 1; t <= 16; t++)
        {
            TickCorridor(map, 3f, t * Tick);
            int best = map.BestNode();
            Assert.GreaterOrEqual(best, lastBest, "the most likely place walked back towards the Nemesis");
            lastBest = best;
        }

        // All of it is past what it can see: nothing behind it, nothing in view.
        Assert.AreEqual(1f, SumRange(map, 7, 19), 1e-4f);
        Assert.Greater(lastBest, 6);
    }

    // ── A T: ahead is likelier, not certain ──────────────────────────────────

    [Test]
    public void TJunction_HeadingLeft_LeftArmGetsMoreButNotAll()
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
        var left = new List<int>();
        var right = new List<int>();
        for (int i = 1; i <= 6; i++)
        {
            int l = graph.AddNode(new Vector3(-i * Spacing, 0f, 8f));
            graph.AddEdge(previousLeft, l, Spacing);
            previousLeft = l;
            left.Add(l);

            int r = graph.AddNode(new Vector3(i * Spacing, 0f, 8f));
            graph.AddEdge(previousRight, r, Spacing);
            previousRight = r;
            right.Add(r);
        }
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SeedPoint(new Vector3(0f, 0f, 6f), Band, new Vector3(-3f, 0f, 1f), 0f);

        for (int t = 1; t <= 8; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 1f, 3f);

            // The Nemesis is down the stem looking +z: the stem is all it sees.
            for (int s = 0; s < 4; s++) map.ClearSeen(s);
            map.Normalize();
        }

        float leftSum = 0f, rightSum = 0f;
        foreach (int n in left) leftSum += map.Value(n);
        foreach (int n in right) rightSum += map.Value(n);

        Assert.Greater(leftSum, rightSum, "the way it saw them going should be likelier");
        Assert.Greater(rightSum, 0.05f, "but the other arm still has to be possible");
    }

    [Test]
    public void HeadingTilt_FadesToNothing()
    {
        PossibilityMap map = new PossibilityMap(Corridor(5));
        map.SeedPoint(At(2), Band, Vector3.right * 4f, 10f);

        Assert.AreEqual(1f, map.HeadingTilt(10f, 1f, 3f), 1e-5f);
        Assert.AreEqual(0.5f, map.HeadingTilt(11.5f, 1f, 3f), 1e-5f);
        Assert.AreEqual(0f, map.HeadingTilt(13.5f, 1f, 3f), 1e-5f);

        // Past the duration it spreads evenly both ways.
        map.Spread(Tick, Speed, 20f, 1f, 3f);
        Assert.AreEqual(map.Value(1), map.Value(3), 1e-6f);
    }

    // ── Evidence ─────────────────────────────────────────────────────────────

    [Test]
    public void SeedArea_HeardManyTimes_NeverSharperThanOnce()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedArea(At(10), 5f, Band);
        float once = map.EffectiveNodeCount;

        for (int i = 0; i < 30; i++) map.SeedArea(At(10), 5f, Band);

        Assert.GreaterOrEqual(map.EffectiveNodeCount, once - 1e-3f);
    }

    [Test]
    public void SeedArea_NothingOutsideTheHeardAreaSurvives()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(2), Band, Vector3.zero, 0f);
        map.SeedArea(At(15), 3f, Band);

        Assert.AreEqual(0f, SumRange(map, 0, 11), 1e-6f);
        Assert.AreEqual(1f, SumRange(map, 12, 19), 1e-4f);
    }

    [Test]
    public void SeedPoint_OffTheMap_LeavesItAsItWas()
    {
        PossibilityMap map = new PossibilityMap(Corridor(5));
        map.SeedPoint(At(2), Band, Vector3.zero, 0f);
        float before = map.Value(2);

        Assert.IsFalse(map.SeedPoint(new Vector3(100f, 0f, 100f), Band, Vector3.zero, 1f));
        Assert.AreEqual(before, map.Value(2), 1e-6f);
        Assert.Greater(before, 0.5f, "a sighting puts most of it on the nearest node");
    }

    // ── C5: the Hub drains ───────────────────────────────────────────────────

    [Test]
    public void HubDoor_DrainsIntoTheSink_AndNeverCollects()
    {
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < 6; i++) graph.AddNode(At(i));
        for (int i = 0; i + 1 < 6; i++) graph.AddEdge(i, i + 1, Spacing);
        graph.MarkDrain(5);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SeedPoint(At(3), Band, Vector3.zero, 0f);

        for (int t = 1; t <= 40; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 0f, 0f);
            map.Drain();
            Assert.AreEqual(0f, map.Value(5), 1e-7f, "the door must never hold value");
            map.Normalize();
        }

        Assert.Greater(map.SinkValue, 0.2f);
    }

    [Test]
    public void SeedSink_IsAllInTheHub()
    {
        PossibilityMap map = new PossibilityMap(Corridor(5));
        map.SeedSink();
        map.Normalize();

        Assert.AreEqual(1f, map.SinkValue, 1e-6f);
        Assert.AreEqual(-1, map.BestNode());
    }

    // ── Hiding spots (D38) ───────────────────────────────────────────────────

    [Test]
    public void HidingSpot_LookingAtItDoesNotClearIt_OpeningItDoes()
    {
        var graph = new PossibilityGraph(Spacing);
        int floor = graph.AddNode(At(0));
        int locker = graph.AddNode(At(0) + Vector3.forward, PossibilityGraph.ENodeKind.HidingSpot);
        graph.AddEdge(floor, locker, 1f);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SeedArea(At(0), 2f, Band);

        Assert.IsFalse(map.ClearSeen(locker));
        Assert.Greater(map.Value(locker), 0f);

        // Everything in view empty: what is left is in the locker.
        map.ClearSeen(floor);
        map.Normalize();
        Assert.AreEqual(1f, map.Value(locker), 1e-5f);

        map.Open(locker);
        map.Normalize();
        Assert.IsFalse(map.HasValue);
    }

    // ── Gates: the lift without power ────────────────────────────────────────

    [Test]
    public void ClosedGate_CarriesNothing()
    {
        var graph = new PossibilityGraph(Spacing);
        int a = graph.AddNode(At(0));
        int b = graph.AddNode(At(1));
        int c = graph.AddNode(new Vector3(0f, 5f, 0f));
        graph.AddEdge(a, b, Spacing);
        int lift = graph.NewGate();
        graph.AddEdge(b, c, 20f, lift);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        map.SetGateOpen(lift, false);
        map.SeedPoint(At(0), Band, Vector3.zero, 0f);
        for (int t = 1; t <= 40; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 0f, 0f);
            map.Normalize();
        }
        Assert.AreEqual(0f, map.Value(c), 1e-7f);

        map.SetGateOpen(lift, true);
        for (int t = 1; t <= 40; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 0f, 0f);
            map.Normalize();
        }
        Assert.Greater(map.Value(c), 0f);
    }

    // ── Bookkeeping ──────────────────────────────────────────────────────────

    [Test]
    public void Spread_KeepsTheTotal()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(10), Band, Vector3.right * 4f, 0f);

        // A long tick is split into several steps rather than overshooting.
        map.Spread(2f, Speed, 0.5f, 1f, 3f);

        Assert.AreEqual(1f, SumRange(map, 0, 19), 1e-4f);
        for (int i = 0; i < 20; i++) Assert.GreaterOrEqual(map.Value(i), 0f);
    }

    [Test]
    public void ClearingEverything_BelievesNothing()
    {
        PossibilityMap map = new PossibilityMap(Corridor(4));
        map.SeedPoint(At(1), Band, Vector3.zero, 0f);
        for (int i = 0; i < 4; i++) map.ClearSeen(i);
        map.Normalize();

        Assert.IsFalse(map.HasValue);
        Assert.AreEqual(-1, map.BestNode());
    }

    // ── 07/10, WIR-062: evidence is measured walking ─────────────────────────

    /// <summary>Two corridors along +x, 2.5 m apart with a wall between them, joined only at the
    /// x = 0 end. Corridor A is nodes 0..count-1 at z = 0; B is count..2·count-1 at z = 2.5.</summary>
    private static PossibilityGraph TwoCorridorsWithAWall(int count)
    {
        var graph = new PossibilityGraph(Spacing);
        for (int i = 0; i < count; i++) graph.AddNode(new Vector3(i * Spacing, 0f, 0f));
        for (int i = 0; i < count; i++) graph.AddNode(new Vector3(i * Spacing, 0f, 2.5f));
        for (int i = 0; i + 1 < count; i++)
        {
            graph.AddEdge(i, i + 1, Spacing);
            graph.AddEdge(count + i, count + i + 1, Spacing);
        }
        graph.AddEdge(0, count, 2.5f);
        graph.Freeze();
        return graph;
    }

    [Test]
    public void SeedArea_ThroughAWall_LeavesNothingOnTheOtherSide()
    {
        const int count = 10;
        PossibilityMap map = new PossibilityMap(TwoCorridorsWithAWall(count));

        // A footstep in corridor A, near its far end: a 3 m disc in plan view took in three nodes of
        // B through the wall, the way back to which is the whole length of both corridors.
        Assert.IsTrue(map.SeedArea(At(8), 3f, Band));

        Assert.AreEqual(1f, SumRange(map, 0, count - 1), 1e-4f);
        Assert.AreEqual(0f, SumRange(map, count, 2 * count - 1), 1e-6f, "value through the wall");
    }

    [Test]
    public void SeedPoint_AgainstAWall_IsNotHalfInTheNextRoom()
    {
        const int count = 10;
        PossibilityMap map = new PossibilityMap(TwoCorridorsWithAWall(count));

        // Seen pressed against A's wall: B's node is 1.5 m away in plan, A's 1 m.
        Vector3 seen = new Vector3(8 * Spacing, 0f, 1f);
        Assert.IsTrue(map.SeedPoint(seen, Band, Vector3.zero, 0f));
        Assert.AreEqual(0f, SumRange(map, count, 2 * count - 1), 1e-6f);

        // And the caller's start node decides the side when it knows better (NavMesh check).
        Assert.IsTrue(map.SeedPoint(seen, Band, Vector3.zero, 0f, start: count + 8));
        Assert.AreEqual(1f, SumRange(map, count, 2 * count - 1), 1e-4f);
    }

    [Test]
    public void SeedArea_InACorridor_CoversTheSameStretchAsBefore()
    {
        // No walls in the way, walking and plan view agree: nothing about an open corridor changes.
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedArea(At(10), 3f, Band);

        Assert.AreEqual(0f, SumRange(map, 0, 7), 1e-6f);
        Assert.AreEqual(0f, SumRange(map, 13, 19), 1e-6f);
        Assert.AreEqual(1f, SumRange(map, 8, 12), 1e-4f);
    }

    // ── 07/10, WIR-062: where the Nemesis is blocking ────────────────────────

    [Test]
    public void Blocked_NothingFlowsIntoIt_NorPastIt()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(10), Band, Vector3.zero, 0f);
        Assert.IsTrue(map.Block(7));

        for (int t = 1; t <= 40; t++)
        {
            map.Spread(Tick, Speed, t * Tick, 0f, 0f);
            map.Normalize();
        }

        Assert.AreEqual(0f, SumRange(map, 0, 7), 1e-6f, "value got past the blocked node");
        Assert.AreEqual(1f, SumRange(map, 8, 19), 1e-4f);
    }

    [Test]
    public void Blocked_ClearsNothing_WhatIsThereCanLeave()
    {
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(7), Band, Vector3.zero, 0f);
        float before = map.Value(7);

        map.Block(7);
        Assert.AreEqual(before, map.Value(7), 1e-6f, "blocking is not looking");

        map.Spread(Tick, Speed, Tick, 0f, 0f);
        Assert.Less(map.Value(7), before);
        Assert.AreEqual(1f, SumRange(map, 0, 19), 1e-4f);
    }

    [Test]
    public void Blocked_OnlyFloor_AndClearBlocksOpensEverything()
    {
        var graph = new PossibilityGraph(Spacing);
        int floor = graph.AddNode(Vector3.zero);
        int locker = graph.AddNode(new Vector3(0f, 0f, 1f), PossibilityGraph.ENodeKind.HidingSpot);
        graph.AddEdge(floor, locker, 1f);
        graph.Freeze();

        var map = new PossibilityMap(graph);
        Assert.IsFalse(map.Block(locker), "someone can be in a locker the Nemesis walks past");
        Assert.IsTrue(map.Block(floor));
        Assert.IsFalse(map.Block(floor), "already blocked");
        Assert.AreEqual(1, map.BlockedCount);

        map.ClearBlocks();
        Assert.AreEqual(0, map.BlockedCount);
        Assert.IsFalse(map.IsBlocked(floor));
    }

    [Test]
    public void SeedArea_DoesNotReachThroughWhereTheNemesisIs()
    {
        // The Nemesis stands at node 6 and walked there along 2..6. A footstep just ahead of it, with
        // a doubt wide enough to reach behind it: none of that doubt lands behind it.
        PossibilityMap map = new PossibilityMap(Corridor(20));
        for (int i = 2; i <= 6; i++) map.Block(i);

        Assert.IsTrue(map.SeedArea(At(8), 4f, Band));
        Assert.AreEqual(0f, SumRange(map, 0, 6), 1e-6f);
        Assert.AreEqual(1f, SumRange(map, 7, 19), 1e-4f);
    }

    [Test]
    public void SeedArea_HeardOnTheTrailItself_IsStillEvidence()
    {
        // A noise right on the floor it is blocking (the scene component forgets that part of the
        // trail first; here, the map alone): the node it was heard at takes the value.
        PossibilityMap map = new PossibilityMap(Corridor(20));
        for (int i = 6; i <= 10; i++) map.Block(i);

        Assert.IsTrue(map.SeedArea(At(8), 1f, Band));
        Assert.AreEqual(1f, SumRange(map, 7, 9), 1e-4f, "it stays where it was heard");
        Assert.Greater(map.Value(8), map.Value(7));
    }

    [Test]
    public void Corridor_ChaseLostAhead_WithItsTrail_NothingEverGoesBackPastIt()
    {
        // WIR-062 as played: lost round the far end of a corridor, then heard running. The trail
        // blocks the floor around where the Nemesis walked; it stops at node 6 and looks back down
        // the way it came for a moment (the look-around), so its eyes clear nothing ahead.
        PossibilityMap map = new PossibilityMap(Corridor(20));
        map.SeedPoint(At(9), Band, Vector3.right * 4f, 0f);

        for (int t = 1; t <= 24; t++)
        {
            map.ClearBlocks();
            for (int i = 2; i <= 6; i++) map.Block(i);

            // Heard once, a second in: a doubt wide enough to reach back to it.
            if (t == 4) map.SeedArea(At(9), 5f, Band);

            map.Spread(Tick, Speed, t * Tick, 1f, 3f);
            map.Normalize();

            Assert.AreEqual(0f, SumRange(map, 0, 6), 1e-6f, $"value behind it at t={t * Tick:0.00}");
        }
    }
}
