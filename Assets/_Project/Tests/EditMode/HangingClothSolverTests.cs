using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The curtain's cloth: it hangs still, a body pushes it out of the way up to a point and no farther,
/// it comes back, and nothing — a teleporting body, a hitch, garbage numbers — makes it explode.
/// </summary>
public class HangingClothSolverTests
{
    private const float Width = 1f;
    private const float Height = 2f;

    private static HangingClothSolver NewCurtain() =>
        new HangingClothSolver(5, 9, new Vector3(-Width * 0.5f, Height, 0f), Vector3.right * Width, Vector3.down * Height);

    private static List<HangingClothSolver.Capsule> Body(Vector3 feet, float radius = 0.3f, float height = 1.86f) =>
        new List<HangingClothSolver.Capsule>
        {
            new HangingClothSolver.Capsule(feet + Vector3.up * radius, feet + Vector3.up * (height - radius), radius),
        };

    private static void AssertInsideEnvelope(HangingClothSolver cloth, float maxSwing, string when)
    {
        for (int i = 0; i < cloth.Count; i++)
        {
            Vector3 p = cloth.Position(i);
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.sqrMagnitude),
                           $"particle {i} is not a number {when}");

            float depth = (i / cloth.Columns) / (float)(cloth.Rows - 1);
            Assert.LessOrEqual(cloth.Displacement(i).magnitude, maxSwing * depth + 1e-3f,
                               $"particle {i} left its reach {when}");
        }
    }

    [Test]
    public void HangingAlone_ItDoesNotMove()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;

        for (int frame = 0; frame < 600; frame++) cloth.Tick(1f / 60f, settings, null);

        Assert.Less(cloth.MaxDisplacement, 0.01f, "a curtain nobody touches sags or drifts");
        Assert.IsTrue(cloth.IsAtRest);
    }

    [Test]
    public void TheTopEdge_NeverMoves()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;

        for (int frame = 0; frame < 240; frame++)
            cloth.Tick(1f / 60f, settings, Body(new Vector3(0f, 0f, -1.5f + frame * 0.0125f)));

        for (int x = 0; x < cloth.Columns; x++)
            Assert.AreEqual(Vector3.zero, cloth.Displacement(cloth.Index(x, 0)), "the pinned row moved");
    }

    [Test]
    public void ABodyStandingInIt_PushesTheClothOffItself()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;
        List<HangingClothSolver.Capsule> body = Body(new Vector3(0f, 0f, 0.1f));

        for (int frame = 0; frame < 120; frame++) cloth.Tick(1f / 60f, settings, body);

        // Every particle at the body's height is outside it (the limit is far from reached here).
        HangingClothSolver.Capsule capsule = body[0];
        for (int i = cloth.Columns; i < cloth.Count; i++)
        {
            Vector3 p = cloth.Position(i);
            if (p.y < capsule.Bottom.y || p.y > capsule.Top.y) continue;

            float distance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(capsule.Bottom.x, capsule.Bottom.z));
            Assert.GreaterOrEqual(distance, capsule.Radius - 0.02f, $"particle {i} is inside the body");
        }

        Assert.Greater(cloth.MaxDisplacement, 0.1f, "the body did not move the cloth at all");
    }

    [Test]
    public void WalkedThrough_ItGoesForwardOnlyUpToItsLimit_AndComesBack()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;
        settings.maxSwing = 0.6f;
        float farthest = 0f;

        // 2.5 m/s straight through the middle, from 1.5 m before to 2.5 m past it.
        for (int frame = 0; frame < 96; frame++)
        {
            cloth.Tick(1f / 60f, settings, Body(new Vector3(0f, 0f, -1.5f + frame * (2.5f / 60f))));
            AssertInsideEnvelope(cloth, settings.maxSwing, $"on frame {frame} of the walk");
            farthest = Mathf.Max(farthest, cloth.MaxDisplacement);
        }

        Assert.Greater(farthest, 0.3f, "the walk barely moved it");
        Assert.LessOrEqual(farthest, settings.maxSwing + 1e-3f, "it went past its limit");

        // Gone: it swings back and ends exactly where it hung.
        bool settled = false;
        for (int frame = 0; frame < 900 && !settled; frame++)
        {
            cloth.Tick(1f / 60f, settings, null);
            AssertInsideEnvelope(cloth, settings.maxSwing, "while settling");
            settled = cloth.IsAtRest;
        }

        Assert.IsTrue(settled, "it never settled after the body left");
    }

    [Test]
    public void NothingMakesItExplode()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;
        var random = new System.Random(1234);
        float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);

        for (int frame = 0; frame < 2000; frame++)
        {
            var bodies = new List<HangingClothSolver.Capsule>();

            // Bodies teleporting in and out, sprinting through, huge, tiny, degenerate.
            bodies.AddRange(Body(new Vector3(Next(-1f, 1f), Next(-0.5f, 0.5f), Next(-1f, 1f)), Next(0.05f, 1.5f), Next(0.1f, 3f)));
            bodies.AddRange(Body(new Vector3(0f, 0f, -3f + (frame % 60) * 0.1f), 0.45f, 2.3f));
            if (frame % 37 == 0) bodies.Add(new HangingClothSolver.Capsule(Vector3.zero, Vector3.zero, 0.5f));
            if (frame % 53 == 0) bodies.Add(new HangingClothSolver.Capsule(new Vector3(float.NaN, 0f, 0f), Vector3.up, 0.4f));
            if (frame % 71 == 0) bodies.Add(new HangingClothSolver.Capsule(Vector3.one * float.PositiveInfinity, Vector3.up, 0.4f));

            // Frame times from a smooth 240 fps to a two-second hitch, and a few nonsense ones.
            float dt = frame % 97 == 0 ? 2f : frame % 41 == 0 ? float.NaN : frame % 43 == 0 ? -1f : Next(1f / 240f, 1f / 15f);

            cloth.Tick(dt, settings, bodies);
            AssertInsideEnvelope(cloth, settings.maxSwing, $"on frame {frame} of the abuse");
        }

        Assert.AreEqual(0, cloth.Recoveries, "a particle had to be rescued from a NaN: something upstream let one in");
    }

    [Test]
    public void ItStaysCloth_NotRubber()
    {
        HangingClothSolver cloth = NewCurtain();
        var settings = HangingClothSolver.Settings.Default;
        float restLink = Height / (cloth.Rows - 1);

        for (int frame = 0; frame < 96; frame++)
        {
            cloth.Tick(1f / 60f, settings, Body(new Vector3(0f, 0f, -1.5f + frame * (4.5f / 60f)), 0.45f, 2.3f));

            for (int y = 1; y < cloth.Rows; y++)
            {
                for (int x = 0; x < cloth.Columns; x++)
                {
                    float link = Vector3.Distance(cloth.Position(cloth.Index(x, y)), cloth.Position(cloth.Index(x, y - 1)));
                    Assert.LessOrEqual(link, restLink * settings.maxStretch + 0.03f, $"a link stretched on frame {frame}");
                }
            }
        }
    }
}
