using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sheet of cloth hanging from its top edge: a small grid of particles (Verlet), held together by
/// distance constraints, pulled down by gravity and pushed aside by bodies (vertical capsules). Pure
/// simulation, no scene: <c>HangingCloth</c> owns one per curtain panel and bends its mesh with it.
///
/// The model is Blender's pinned cloth, cut down to what a curtain someone walks through needs: the
/// top row is pinned, a body shoves the cloth ahead of it, the cloth swings back and settles.
///
/// IT CANNOT BLOW UP, by construction rather than by tuning. Every way a cloth simulation "goes to
/// hell" is closed here, each one after the constraints so it has the last word:
///
///   reach      no particle ever gets farther from where it hangs than <see cref="Settings.maxSwing"/>
///              times its depth below the pinned edge. This is also the design: a body carries the
///              cloth forward UP TO A POINT and then walks through it. Whatever else goes wrong, the
///              mesh stays inside that envelope.
///   stretch    each particle is pulled back within <see cref="Settings.maxStretch"/> of its rest
///              distance to the one above it, top row down: cloth, not rubber.
///   speed      a particle cannot move more than <see cref="Settings.maxSpeed"/> in a step, so one bad
///              shove cannot become energy the next steps amplify.
///   time       the step is fixed and the caller's frame time is capped: a hitch simulates at most
///              <see cref="MaxStepsPerTick"/> steps and drops the rest.
///   numbers    a particle that comes out NaN or infinite goes back to where it hangs, at rest.
///   bodies     a body that jumped (a teleport, a respawn) does not push on that step: it would be a
///              shove at the speed of the jump.
///   rest       once it has settled it snaps exactly onto its rest shape, so error cannot build up
///              over an afternoon of being walked through.
/// </summary>
public sealed class HangingClothSolver
{
    public struct Settings
    {
        /// <summary>m/s² downwards.</summary>
        public float gravity;

        /// <summary>Fraction of its velocity a particle keeps per step (air drag). 0.97–0.995.</summary>
        public float damping;

        /// <summary>Constraint passes per step. More = stiffer cloth.</summary>
        public int iterations;

        /// <summary>How far the BOTTOM edge may travel from where it hangs, in metres. Rows above it
        /// get proportionally less, down to nothing at the pinned edge.</summary>
        public float maxSwing;

        /// <summary>How long a vertical link may get, as a ratio of its rest length (1.05–1.2).</summary>
        public float maxStretch;

        /// <summary>Metres per second a particle may move.</summary>
        public float maxSpeed;

        /// <summary>Metres kept between the cloth and a body's surface.</summary>
        public float thickness;

        /// <summary>Fraction of a particle's velocity a body it touches takes away (0–1).</summary>
        public float bodyFriction;

        /// <summary>How hard the cloth is drawn back to where it hangs, per step (0–0.2). It is the
        /// fabric's own stiffness, and what makes it come to rest ON its shape instead of a few
        /// millimetres off it, sagging under the solver's leftover error.</summary>
        public float restPull;

        public static Settings Default => new Settings
        {
            gravity = 9.81f, damping = 0.985f, iterations = 6, maxSwing = 0.8f, maxStretch = 1.1f,
            maxSpeed = 6f, thickness = 0.05f, bodyFriction = 0.35f, restPull = 0.03f,
        };
    }

    /// <summary>A body: the segment between the centres of its two end spheres, and its radius.</summary>
    public readonly struct Capsule
    {
        public readonly Vector3 Bottom;
        public readonly Vector3 Top;
        public readonly float Radius;

        public Capsule(Vector3 bottom, Vector3 top, float radius)
        {
            Bottom = bottom;
            Top = top;
            Radius = radius;
        }
    }

    public const float FixedStep = 1f / 60f;
    public const int MaxStepsPerTick = 3;

    /// <summary>A body that moved farther than this in one step teleported.</summary>
    private const float BodyJumpDistance = 0.6f;

    private const float SettledSpeed = 0.03f;
    private const float SettledDisplacement = 0.01f;

    public int Columns { get; }
    public int Rows { get; }
    public int Count => rest.Length;

    private readonly Vector3[] rest;
    private readonly Vector3[] position;
    private readonly Vector3[] previous;
    private readonly float[] depth;          // 0 at the pinned row, 1 at the bottom
    private readonly float restAcross, restDown, restDiagonal;
    private readonly List<Vector3> lastBodyBottoms = new List<Vector3>();
    private float accumulator;

    /// <summary>Largest distance of any particle from where it hangs, after the last tick.</summary>
    public float MaxDisplacement { get; private set; }

    /// <summary>Fastest particle after the last tick, m/s.</summary>
    public float MaxSpeed { get; private set; }

    /// <summary>Particles put back at rest because they came out NaN or infinite. Should stay 0.</summary>
    public int Recoveries { get; private set; }

    /// <summary>Hanging still, on its rest shape.</summary>
    public bool IsAtRest { get; private set; } = true;

    /// <param name="topLeft">The pinned edge's first corner.</param>
    /// <param name="across">The whole pinned edge, as a vector.</param>
    /// <param name="down">From the pinned edge to the bottom edge, as a vector.</param>
    public HangingClothSolver(int columns, int rows, Vector3 topLeft, Vector3 across, Vector3 down)
    {
        Columns = Mathf.Max(2, columns);
        Rows = Mathf.Max(2, rows);

        int count = Columns * Rows;
        rest = new Vector3[count];
        position = new Vector3[count];
        previous = new Vector3[count];
        depth = new float[count];

        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int i = Index(x, y);
                float u = x / (float)(Columns - 1);
                float v = y / (float)(Rows - 1);
                rest[i] = position[i] = previous[i] = topLeft + across * u + down * v;
                depth[i] = v;
            }
        }

        restAcross = across.magnitude / (Columns - 1);
        restDown = down.magnitude / (Rows - 1);
        restDiagonal = Mathf.Sqrt(restAcross * restAcross + restDown * restDown);
    }

    public int Index(int x, int y) => y * Columns + x;
    public Vector3 Rest(int i) => rest[i];
    public Vector3 Position(int i) => position[i];

    /// <summary>Where particle <paramref name="i"/> is, relative to where it hangs.</summary>
    public Vector3 Displacement(int i) => position[i] - rest[i];

    /// <summary>Back onto the rest shape, still.</summary>
    public void SnapToRest()
    {
        for (int i = 0; i < rest.Length; i++) position[i] = previous[i] = rest[i];
        accumulator = 0f;
        MaxDisplacement = 0f;
        MaxSpeed = 0f;
        IsAtRest = true;
        lastBodyBottoms.Clear();
    }

    /// <summary>Advances the cloth by a frame. Bodies must come in the same order every frame.</summary>
    public void Tick(float deltaTime, in Settings settings, IReadOnlyList<Capsule> bodies)
    {
        if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;

        accumulator = Mathf.Min(accumulator + deltaTime, FixedStep * MaxStepsPerTick);
        while (accumulator >= FixedStep)
        {
            accumulator -= FixedStep;
            Step(settings, bodies);
        }
    }

    private void Step(in Settings settings, IReadOnlyList<Capsule> bodies)
    {
        const float dt = FixedStep;
        float damping = Mathf.Clamp01(settings.damping);
        float maxMove = Mathf.Max(0.001f, settings.maxSpeed) * dt;
        Vector3 gravity = Vector3.down * (settings.gravity * dt * dt);
        float restPull = Mathf.Clamp(settings.restPull, 0f, 0.5f);

        // Integrate. The pinned row never moves.
        for (int i = Columns; i < position.Length; i++)
        {
            Vector3 velocity = (position[i] - previous[i]) * damping;
            previous[i] = position[i];
            position[i] += Vector3.ClampMagnitude(velocity + gravity, maxMove);
            position[i] += (rest[i] - position[i]) * restPull;
        }

        int iterations = Mathf.Clamp(settings.iterations, 1, 16);
        for (int pass = 0; pass < iterations; pass++)
        {
            SolveLinks();
            SolveBodies(settings, bodies);
        }

        RememberBodies(bodies);
        LimitStretch(settings.maxStretch);
        Enclose(settings.maxSwing, maxMove);
        Measure(dt);
    }

    private void SolveLinks()
    {
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int i = Index(x, y);
                if (x + 1 < Columns) Link(i, Index(x + 1, y), restAcross);
                if (y + 1 < Rows) Link(i, Index(x, y + 1), restDown);
                if (x + 1 < Columns && y + 1 < Rows)
                {
                    Link(i, Index(x + 1, y + 1), restDiagonal);
                    Link(Index(x + 1, y), Index(x, y + 1), restDiagonal);
                }
            }
        }
    }

    private void Link(int a, int b, float length)
    {
        Vector3 delta = position[b] - position[a];
        float distance = delta.magnitude;
        if (distance < 1e-6f) return;

        Vector3 correction = delta * ((distance - length) / distance);
        bool aPinned = a < Columns;
        bool bPinned = b < Columns;

        if (aPinned && bPinned) return;
        if (aPinned) position[b] -= correction;
        else if (bPinned) position[a] += correction;
        else
        {
            position[a] += correction * 0.5f;
            position[b] -= correction * 0.5f;
        }
    }

    private void SolveBodies(in Settings settings, IReadOnlyList<Capsule> bodies)
    {
        if (bodies == null) return;

        for (int b = 0; b < bodies.Count; b++)
        {
            Capsule body = bodies[b];
            if (!IsFinite(body.Bottom) || !IsFinite(body.Top) || !(body.Radius > 0f)) continue;
            if (Jumped(b, body)) continue;

            float reach = body.Radius + settings.thickness;
            Vector3 axis = body.Top - body.Bottom;
            float axisSqr = axis.sqrMagnitude;

            for (int i = Columns; i < position.Length; i++)
            {
                float t = axisSqr > 1e-8f ? Mathf.Clamp01(Vector3.Dot(position[i] - body.Bottom, axis) / axisSqr) : 0f;
                Vector3 closest = body.Bottom + axis * t;
                Vector3 away = position[i] - closest;
                float distance = away.magnitude;
                if (distance >= reach) continue;

                // Dead centre on the axis: there is no "away". Out the way it was already hanging from.
                Vector3 direction = distance > 1e-5f ? away / distance : SafeDirection(rest[i] - closest);
                position[i] = closest + direction * reach;

                // The body drags on what it touches: without it the cloth slides off like wet glass.
                previous[i] = Vector3.Lerp(previous[i], position[i], Mathf.Clamp01(settings.bodyFriction));
            }
        }
    }

    private bool Jumped(int index, in Capsule body)
    {
        if (index >= lastBodyBottoms.Count) return false;
        return (lastBodyBottoms[index] - body.Bottom).sqrMagnitude > BodyJumpDistance * BodyJumpDistance;
    }

    private void RememberBodies(IReadOnlyList<Capsule> bodies)
    {
        lastBodyBottoms.Clear();
        if (bodies == null) return;
        for (int b = 0; b < bodies.Count; b++) lastBodyBottoms.Add(bodies[b].Bottom);
    }

    /// <summary>Top row down: each particle within reach of the one it hangs from.</summary>
    private void LimitStretch(float maxStretch)
    {
        float limit = restDown * Mathf.Max(1f, maxStretch);
        for (int y = 1; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int i = Index(x, y);
                Vector3 hang = position[i] - position[Index(x, y - 1)];
                if (hang.sqrMagnitude > limit * limit) position[i] = position[Index(x, y - 1)] + hang.normalized * limit;
            }
        }
    }

    /// <summary>The last word: every particle finite, inside its reach, and not having moved faster
    /// than allowed this step.</summary>
    private void Enclose(float maxSwing, float maxMove)
    {
        float swing = Mathf.Max(0f, maxSwing);

        for (int i = Columns; i < position.Length; i++)
        {
            if (!IsFinite(position[i]) || !IsFinite(previous[i]))
            {
                position[i] = previous[i] = rest[i];
                Recoveries++;
                continue;
            }

            Vector3 offset = position[i] - rest[i];
            float reach = swing * depth[i];
            if (offset.sqrMagnitude > reach * reach)
            {
                position[i] = rest[i] + offset.normalized * reach;

                // It hit the end of its travel: it does not keep the speed it arrived with.
                previous[i] = Vector3.Lerp(previous[i], position[i], 0.5f);
            }

            Vector3 moved = position[i] - previous[i];
            if (moved.sqrMagnitude > maxMove * maxMove) previous[i] = position[i] - moved.normalized * maxMove;
        }
    }

    private void Measure(float dt)
    {
        float maxDisplacement = 0f, maxMoved = 0f;
        for (int i = Columns; i < position.Length; i++)
        {
            maxDisplacement = Mathf.Max(maxDisplacement, (position[i] - rest[i]).sqrMagnitude);
            maxMoved = Mathf.Max(maxMoved, (position[i] - previous[i]).sqrMagnitude);
        }

        MaxDisplacement = Mathf.Sqrt(maxDisplacement);
        MaxSpeed = Mathf.Sqrt(maxMoved) / dt;
        IsAtRest = MaxSpeed < SettledSpeed && MaxDisplacement < SettledDisplacement;
    }

    private static Vector3 SafeDirection(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
          float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
}
