using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Dents a mesh where it is hit: the vertices round a point are pushed one way, most at the point and
/// fading out smoothly to nothing at the dent's radius. One job. Who hits it, where and how hard is
/// the caller's (today <see cref="NemesisGateEntrance"/>, beating a gate from the far side).
///
/// Dents add up: each one starts from the shape the last one left. The mesh is a runtime copy made
/// on the first dent, so the asset and every other object that shares it are untouched;
/// <see cref="Restore"/> puts the original back.
///
/// Measured in WORLD space (point, direction, radius, depth in metres), so the scale the mesh is
/// imported or placed at does not matter: the gate's leaf is a centimetre mesh scaled by ~100.
///
/// NEEDS the model's Read/Write Enabled in its import settings: a build cannot read the vertices of a
/// mesh without it (the editor can, which hides the problem until a build). Without it this warns
/// once and does nothing.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class MeshDent : MonoBehaviour
{
    [Tooltip("Largo máximo (m) de un lado de triángulo en la copia que se abolla. Una malla con " +
             "pocos vértices (la hoja del portón: 146, casi todos en los bordes de las chapas) no " +
             "tiene nada que mover en el medio; se subdivide hasta este tamaño la primera vez que " +
             "se abolla. 0 = no subdividir.")]
    [SerializeField, Min(0f)] private float maxEdge = 0.2f;

    [Tooltip("Tope de pasadas de subdivisión (cada una multiplica los triángulos por 4).")]
    [SerializeField, Range(0, 6)] private int maxSubdivisions = 5;

    private MeshFilter filter;
    private Mesh original;
    private Mesh working;

    private Vector3[] shown;
    private Vector3[] from;
    private Vector3[] to;

    private CancellationTokenSource motionCts;
    private bool animating;
    private bool warned;

    /// <summary>The mesh can be dented (it has one and it is readable).</summary>
    public bool CanDent => EnsureWorkingMesh();

    /// <summary>Makes the dentable copy now (the subdivision costs a few milliseconds), so the
    /// first hit does not pay for it. Safe to call more than once.</summary>
    public void Prepare() => EnsureWorkingMesh();

    /// <summary>
    /// Pushes the mesh in <paramref name="worldDirection"/> round <paramref name="worldPoint"/>: by
    /// <paramref name="depth"/> metres at the point, smoothly less out to <paramref name="radius"/>.
    /// Played over <paramref name="seconds"/> (0 = at once). A dent still playing lands at its end
    /// first, so a fast second hit adds to the first instead of cutting it short.
    /// </summary>
    public void Dent(Vector3 worldPoint, Vector3 worldDirection, float radius, float depth, float seconds)
    {
        if (radius <= 0f || Mathf.Approximately(depth, 0f) || !EnsureWorkingMesh()) return;

        if (animating)
        {
            CancelMotion();
            Show(to);
        }

        Vector3 localPush = transform.InverseTransformVector(worldDirection.normalized * depth);
        Matrix4x4 toWorld = transform.localToWorldMatrix;

        for (int i = 0; i < shown.Length; i++)
        {
            from[i] = shown[i];

            float distance = Vector3.Distance(toWorld.MultiplyPoint3x4(shown[i]), worldPoint);
            if (distance >= radius)
            {
                to[i] = shown[i];
                continue;
            }

            float k = 1f - distance / radius;
            k = k * k * (3f - 2f * k);
            to[i] = shown[i] + localPush * k;
        }

        if (seconds <= 0f)
        {
            Show(to);
            return;
        }

        motionCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        PlayAsync(seconds, motionCts.Token).Forget();
    }

    /// <summary>The mesh as it was before any dent.</summary>
    public void Restore()
    {
        CancelMotion();
        if (working == null) return;

        if (filter != null) filter.sharedMesh = original;
        Destroy(working);
        working = null;
    }

    private void OnDestroy()
    {
        CancelMotion();
        if (working != null) Destroy(working);
    }

    private async UniTaskVoid PlayAsync(float seconds, CancellationToken token)
    {
        animating = true;
        float elapsed = 0f;
        Vector3[] frame = new Vector3[shown.Length];

        try
        {
            while (elapsed < seconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;

                // Ease-out: a hit lands at once and settles, it does not grow.
                float t = Mathf.Clamp01(elapsed / seconds);
                t = 1f - (1f - t) * (1f - t);

                for (int i = 0; i < frame.Length; i++) frame[i] = Vector3.LerpUnclamped(from[i], to[i], t);
                Show(frame);
            }

            Show(to);
        }
        catch (OperationCanceledException)
        {
            // Whoever cancelled decides where the vertices end up.
        }
        finally
        {
            animating = false;
        }
    }

    private void Show(Vector3[] vertices)
    {
        Array.Copy(vertices, shown, shown.Length);
        working.vertices = shown;
        working.RecalculateNormals();
        working.RecalculateBounds();
    }

    private bool EnsureWorkingMesh()
    {
        if (working != null) return true;

        if (filter == null) filter = GetComponent<MeshFilter>();
        Mesh source = filter != null ? filter.sharedMesh : null;
        if (source == null) return false;

        if (!source.isReadable)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogWarning($"[{nameof(MeshDent)}] '{name}': the mesh '{source.name}' is not " +
                                 "readable (Read/Write Enabled off in its import settings): no dents.", this);
            }
            return false;
        }

        original = source;
        working = Instantiate(source);
        working.name = source.name + " (dented)";
        Subdivide(working);
        working.MarkDynamic();
        filter.sharedMesh = working;

        shown = working.vertices;
        from = new Vector3[shown.Length];
        to = new Vector3[shown.Length];
        return true;
    }

    /// <summary>
    /// Splits every triangle in four (a vertex in the middle of each edge, shared by the two
    /// triangles on it) until no edge is longer than <see cref="maxEdge"/> in world space, or
    /// <see cref="maxSubdivisions"/> passes. Positions and the first UV set are carried; normals are
    /// recalculated by every dent anyway. Submeshes are kept apart.
    /// </summary>
    private void Subdivide(Mesh mesh)
    {
        if (maxEdge <= 0f) return;

        Matrix4x4 toWorld = transform.localToWorldMatrix;
        var positions = new System.Collections.Generic.List<Vector3>(mesh.vertices);
        var uvs = new System.Collections.Generic.List<Vector2>();
        mesh.GetUVs(0, uvs);
        bool hasUv = uvs.Count == positions.Count;

        int subMeshCount = mesh.subMeshCount;
        var triangles = new int[subMeshCount][];
        for (int s = 0; s < subMeshCount; s++) triangles[s] = mesh.GetTriangles(s);

        for (int pass = 0; pass < maxSubdivisions; pass++)
        {
            if (LongestEdge(positions, triangles, toWorld) <= maxEdge) break;

            var midpoints = new System.Collections.Generic.Dictionary<long, int>();
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] tris = triangles[s];
                var split = new int[tris.Length * 4];
                for (int t = 0, o = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                    split[o++] = a;  split[o++] = ab; split[o++] = ca;
                    split[o++] = ab; split[o++] = b;  split[o++] = bc;
                    split[o++] = ca; split[o++] = bc; split[o++] = c;
                    split[o++] = ab; split[o++] = bc; split[o++] = ca;
                }
                triangles[s] = split;
            }

            int Midpoint(int i, int j)
            {
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                if (midpoints.TryGetValue(key, out int existing)) return existing;

                positions.Add((positions[i] + positions[j]) * 0.5f);
                if (hasUv) uvs.Add((uvs[i] + uvs[j]) * 0.5f);
                int index = positions.Count - 1;
                midpoints.Add(key, index);
                return index;
            }
        }

        if (positions.Count == mesh.vertexCount) return;

        int before = mesh.vertexCount;
        mesh.Clear();
        mesh.indexFormat = positions.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(positions);
        if (hasUv) mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshCount;
        for (int s = 0; s < subMeshCount; s++) mesh.SetTriangles(triangles[s], s);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log($"[{nameof(MeshDent)}] '{name}': subdivided for dents, {before} -> {positions.Count} vertices.", this);
    }

    private static float LongestEdge(System.Collections.Generic.List<Vector3> positions, int[][] triangles,
                                     Matrix4x4 toWorld)
    {
        float longest = 0f;
        foreach (int[] tris in triangles)
        {
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = toWorld.MultiplyPoint3x4(positions[tris[t]]);
                Vector3 b = toWorld.MultiplyPoint3x4(positions[tris[t + 1]]);
                Vector3 c = toWorld.MultiplyPoint3x4(positions[tris[t + 2]]);
                longest = Mathf.Max(longest, Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a));
            }
        }
        return longest;
    }

    private void CancelMotion()
    {
        motionCts?.Cancel();
        motionCts?.Dispose();
        motionCts = null;
    }
}
