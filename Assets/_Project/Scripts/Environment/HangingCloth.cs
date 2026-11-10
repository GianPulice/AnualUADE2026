using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A panel of cloth that hangs from its top edge and behaves like cloth: a body walking into it
/// (<see cref="ClothPusher"/>: the player, the Nemesis) carries it forward up to a point, walks
/// through, and it swings back and settles. Its one job: bending this object's mesh with a
/// <see cref="HangingClothSolver"/>. How the cloth behaves is the shared
/// <see cref="SO_HangingClothConfig"/>'s business.
///
/// HOW. The mesh is too dense and too uneven to simulate vertex by vertex (a curtain panel is 7,600
/// vertices in six rows), so a coarse grid of particles is laid over the panel's own plane — worked out
/// from the mesh: top edge, height, and the direction it is widest in — and every vertex rides the grid
/// with the displacement of the four particles around it. The pleats modelled into the mesh survive,
/// because only the DIFFERENCE from the rest shape is applied.
///
/// COST. Asleep (nobody near, or off screen) it does nothing: no simulation, no mesh write, and the
/// object still uses the shared mesh. The first time a body comes within reach it makes its own copy
/// of the mesh and wakes; settled again, it snaps exactly back to its rest shape and sleeps.
///
/// WHAT CAN GO WRONG, and what happens then. The mesh is not readable (Read/Write off in the model's
/// import settings): it says so once and stays a static curtain. The object is Batching Static: same.
/// The simulation itself cannot leave its envelope — see <see cref="HangingClothSolver"/> — and should
/// a vertex still come out as garbage, the whole panel goes back to its rest shape for that frame.
///
/// LIMITS. The panel must not be moved or animated: its rest shape is read once, in world space. No
/// self-collision and no collision between panels. It does not block movement, sight or the NavMesh.
///
/// SETUP: <c>Tools/Level/Setup Hanging Cloth (Cortinas)</c> adds this to every curtain panel in the
/// open scene, with the shared config, clears their static flags and makes the model readable.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HangingCloth : MonoBehaviour
{
    [SerializeField] private SO_HangingClothConfig config;

    private const float SleepCheckInterval = 0.15f;
    private const float QuietTimeToSleep = 0.3f;
    private const float SwishCooldown = 1.5f;

    private MeshFilter filter;
    private MeshRenderer meshRenderer;
    private Mesh sharedMesh;
    private Mesh ownMesh;

    private HangingClothSolver solver;
    private Vector3[] restLocal;
    private Vector3[] scratch;
    private int[] cell;            // index of the particle up-left of each vertex
    private Vector2[] weight;      // how far across and down inside that cell
    private Vector3[] particleOffset;
    private Bounds reach;          // world space: the cloth plus how far it can swing

    private readonly List<HangingClothSolver.Capsule> bodies = new List<HangingClothSolver.Capsule>();
    private bool usable;
    private bool awake;
    private float nextCheck;
    private float quietTime;
    private float nextSwishAt;

    /// <summary>Simulating right now.</summary>
    public bool IsAwake => awake;

    /// <summary>The simulation, once the cloth has been woken at least once. For tools and tests.</summary>
    public HangingClothSolver Solver => solver;

    private void Awake()
    {
        filter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        sharedMesh = filter.sharedMesh;
        usable = Validate();
    }

    private bool Validate()
    {
        if (config == null)
            return Refuse("has no SO_HangingClothConfig assigned");
        if (sharedMesh == null)
            return Refuse("has no mesh");
        if (!sharedMesh.isReadable)
            return Refuse($"uses the mesh '{sharedMesh.name}', which is not readable. Turn on Read/Write in the " +
                          "model's import settings (Tools/Level/Setup Hanging Cloth does it)");
        if (meshRenderer.isPartOfStaticBatch)
            return Refuse("is Batching Static: a batched mesh cannot be bent. Clear its static flags");

        return true;
    }

    private bool Refuse(string why)
    {
        Debug.LogWarning($"[{nameof(HangingCloth)}] '{name}' {why}: it stays a still curtain.", this);
        return false;
    }

    private void OnDisable()
    {
        awake = false;
        if (ownMesh == null) return;

        filter.sharedMesh = sharedMesh;
        Destroy(ownMesh);
        ownMesh = null;
        solver = null;
    }

    private void Update()
    {
        if (!usable) return;

        if (!awake)
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + SleepCheckInterval;

            if (!meshRenderer.isVisible) return;
            if (solver == null && !AnyBodyNear(EstimatedReach())) return;
            if (solver != null && !AnyBodyNear(reach)) return;

            Wake();
            if (!awake) return;
        }

        // Off screen there is nobody to see it swing: back to rest and asleep.
        if (!meshRenderer.isVisible)
        {
            Sleep();
            return;
        }

        CollectBodies();
        solver.Tick(Time.deltaTime, config.SolverSettings, bodies);
        WriteMesh();

        quietTime = bodies.Count == 0 && solver.IsAtRest ? quietTime + Time.deltaTime : 0f;
        if (quietTime >= QuietTimeToSleep) Sleep();
    }

    private void Wake()
    {
        if (solver == null && !Build())
        {
            usable = false;
            return;
        }

        awake = true;
        quietTime = 0f;

        if (!string.IsNullOrEmpty(config.SwishSoundId) && Time.time >= nextSwishAt && AudioManager.Exists)
        {
            nextSwishAt = Time.time + SwishCooldown;
            AudioManager.Instance.PlaySFX(config.SwishSoundId, reach.center);
        }
    }

    private void Sleep()
    {
        awake = false;
        solver.SnapToRest();
        ownMesh.SetVertices(restLocal);
        if (config.RecalculateNormals) ownMesh.RecalculateNormals();
        ownMesh.RecalculateBounds();
    }

    // ── Building ─────────────────────────────────────────────────────────────

    /// <summary>The renderer's bounds grown by the swing: good enough to decide whether to build.</summary>
    private Bounds EstimatedReach()
    {
        Bounds bounds = meshRenderer.bounds;
        bounds.Expand(2f * (config.MaxSwing + config.WakeDistance));
        return bounds;
    }

    /// <summary>
    /// Lays the particle grid over the panel and ties every vertex to it. The panel's frame comes from
    /// the mesh itself, in world space: up is up, across is the horizontal direction the vertices
    /// spread widest in (their principal axis), and the grid sits at their mean depth, in the middle
    /// of the pleats.
    /// </summary>
    private bool Build()
    {
        restLocal = sharedMesh.vertices;
        int count = restLocal.Length;
        if (count == 0) return Refuse("has an empty mesh");

        Matrix4x4 toWorld = transform.localToWorldMatrix;
        var world = new Vector3[count];
        Vector3 mean = Vector3.zero;
        float top = float.MinValue, bottom = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            world[i] = toWorld.MultiplyPoint3x4(restLocal[i]);
            mean += world[i];
            top = Mathf.Max(top, world[i].y);
            bottom = Mathf.Min(bottom, world[i].y);
        }

        mean /= count;
        float height = top - bottom;
        if (height < 0.1f) return Refuse("is less than 10 cm tall: nothing to hang");

        // Principal horizontal axis of the vertex cloud.
        float xx = 0f, zz = 0f, xz = 0f;
        for (int i = 0; i < count; i++)
        {
            float dx = world[i].x - mean.x, dz = world[i].z - mean.z;
            xx += dx * dx;
            zz += dz * dz;
            xz += dx * dz;
        }

        float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
        var across = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 normal = Vector3.Cross(Vector3.up, across);

        float min = float.MaxValue, max = float.MinValue, depth = 0f;
        for (int i = 0; i < count; i++)
        {
            float a = Vector3.Dot(world[i], across);
            min = Mathf.Min(min, a);
            max = Mathf.Max(max, a);
            depth += Vector3.Dot(world[i], normal);
        }

        depth /= count;
        float width = Mathf.Max(0.05f, max - min);

        float spacing = Mathf.Max(0.05f, config.ParticleSpacing);
        int columns = Mathf.Clamp(Mathf.CeilToInt(width / spacing) + 1, 2, 16);
        int rows = Mathf.Clamp(Mathf.CeilToInt(height / spacing) + 1, 3, 16);

        Vector3 topLeft = across * min + normal * depth + Vector3.up * top;
        solver = new HangingClothSolver(columns, rows, topLeft, across * width, Vector3.down * height);

        cell = new int[count];
        weight = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float gx = (Vector3.Dot(world[i], across) - min) / width * (columns - 1);
            float gy = (top - world[i].y) / height * (rows - 1);
            int x = Mathf.Clamp(Mathf.FloorToInt(gx), 0, columns - 2);
            int y = Mathf.Clamp(Mathf.FloorToInt(gy), 0, rows - 2);

            cell[i] = solver.Index(x, y);
            weight[i] = new Vector2(Mathf.Clamp01(gx - x), Mathf.Clamp01(gy - y));
        }

        scratch = new Vector3[count];
        particleOffset = new Vector3[solver.Count];

        reach = new Bounds(solver.Rest(0), Vector3.zero);
        for (int i = 1; i < solver.Count; i++) reach.Encapsulate(solver.Rest(i));
        reach.Expand(2f * (config.MaxSwing + config.WakeDistance));

        ownMesh = Instantiate(sharedMesh);
        ownMesh.name = sharedMesh.name + " (cloth)";
        ownMesh.MarkDynamic();
        filter.sharedMesh = ownMesh;
        return true;
    }

    // ── Each frame, awake ────────────────────────────────────────────────────

    private void CollectBodies()
    {
        bodies.Clear();

        IReadOnlyList<ClothPusher> pushers = ClothPusher.Active;
        for (int i = 0; i < pushers.Count; i++)
        {
            if (pushers[i] == null) continue;

            HangingClothSolver.Capsule capsule = pushers[i].Capsule;
            if (Touches(reach, capsule)) bodies.Add(capsule);
        }
    }

    private static bool AnyBodyNear(Bounds bounds)
    {
        IReadOnlyList<ClothPusher> pushers = ClothPusher.Active;
        for (int i = 0; i < pushers.Count; i++)
        {
            if (pushers[i] != null && Touches(bounds, pushers[i].Capsule)) return true;
        }

        return false;
    }

    private static bool Touches(Bounds bounds, in HangingClothSolver.Capsule capsule)
    {
        float r = capsule.Radius;
        return capsule.Bottom.x > bounds.min.x - r && capsule.Bottom.x < bounds.max.x + r &&
               capsule.Bottom.z > bounds.min.z - r && capsule.Bottom.z < bounds.max.z + r &&
               capsule.Top.y + r > bounds.min.y && capsule.Bottom.y - r < bounds.max.y;
    }

    private void WriteMesh()
    {
        // The offsets go from world to the mesh's own space: a direction, so no translation.
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < particleOffset.Length; i++) particleOffset[i] = toLocal.MultiplyVector(solver.Displacement(i));

        int columns = solver.Columns;
        bool garbage = false;

        for (int i = 0; i < scratch.Length; i++)
        {
            int c = cell[i];
            Vector2 w = weight[i];
            Vector3 upper = Vector3.LerpUnclamped(particleOffset[c], particleOffset[c + 1], w.x);
            Vector3 lower = Vector3.LerpUnclamped(particleOffset[c + columns], particleOffset[c + columns + 1], w.x);
            Vector3 vertex = restLocal[i] + Vector3.LerpUnclamped(upper, lower, w.y);

            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z) || float.IsInfinity(vertex.sqrMagnitude))
            {
                garbage = true;
                break;
            }

            scratch[i] = vertex;
        }

        // Should be impossible (the solver encloses every particle). If it happens anyway, the
        // curtain is whole and at rest this frame rather than a spike across the level.
        if (garbage)
        {
            solver.SnapToRest();
            ownMesh.SetVertices(restLocal);
        }
        else
        {
            ownMesh.SetVertices(scratch);
        }

        if (config.RecalculateNormals) ownMesh.RecalculateNormals();
        ownMesh.RecalculateBounds();
    }

    private void OnDrawGizmosSelected()
    {
        if (solver == null) return;

        Gizmos.color = awake ? new Color(1f, 0.6f, 0.1f) : new Color(0.3f, 0.8f, 1f);
        for (int y = 0; y < solver.Rows; y++)
        {
            for (int x = 0; x < solver.Columns; x++)
            {
                Vector3 p = solver.Position(solver.Index(x, y));
                if (x + 1 < solver.Columns) Gizmos.DrawLine(p, solver.Position(solver.Index(x + 1, y)));
                if (y + 1 < solver.Rows) Gizmos.DrawLine(p, solver.Position(solver.Index(x, y + 1)));
            }
        }
    }
}
