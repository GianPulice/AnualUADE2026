using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="GateKnockOff"/>: the button that bakes its fall.
/// </summary>
[CustomEditor(typeof(GateKnockOff))]
public class GateKnockOffEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gate = (GateKnockOff)target;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || gate.FallTrack == null))
        {
            if (GUILayout.Button("Hornear caída con física"))
                GateFallBaker.Bake(gate);
        }

        SO_GateFallTrack track = gate.FallTrack;
        if (track == null)
            EditorGUILayout.HelpBox("Falta el Fall Track (un SO_GateFallTrack) para hornear.", MessageType.Warning);
        else if (!track.HasData)
            EditorGUILayout.HelpBox("Sin hornear: la hoja no va a caer.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"Horneada: {track.Duration:0.00} s, primer golpe a los {track.ImpactTime:0.00} s. " +
                                    "Con el objeto seleccionado, la caja amarilla es donde queda.", MessageType.Info);
    }
}

/// <summary>
/// Simulates a <see cref="GateKnockOff"/>'s leaf falling, with Unity's physics, ONCE, and records it
/// into its <see cref="SO_GateFallTrack"/> so play shows the same fall every time.
///
/// In edit mode there is only the scene's own physics (a preview scene does not get one of its
/// own), so the bake steps that and keeps everything else still while it does: every loose
/// rigidbody in the open scenes is made kinematic, and the colliders of the gate's frame and of the
/// opening's blocker are turned off (the corridor behind stays: its floor catches the leaf's foot
/// if it slides back). The leaf is a
/// temporary box the size of its mesh (undented) with a rigidbody; the last blow is an impulse on
/// it. All of it is put back as it was when the bake ends; nothing is saved but the track.
/// </summary>
public static class GateFallBaker
{
    private const int StepsPerSample = 4;
    private const float MinThickness = 0.05f;
    private const float StartLift = 0.02f;

    public static void Bake(GateKnockOff gate)
    {
        SO_GateFallTrack track = gate.FallTrack;
        Transform door = gate.Door;
        MeshFilter filter = door != null ? door.GetComponent<MeshFilter>() : null;
        if (track == null || filter == null || filter.sharedMesh == null)
        {
            Debug.LogError("[GateFallBaker] needs the gate's Door (with its mesh) and a Fall Track.", gate);
            return;
        }

        Bounds leaf = filter.sharedMesh.bounds;
        Vector3 outward = gate.Outward;
        Bounds worldLeaf = WorldBox(door.position, door.rotation, door.lossyScale, leaf);
        float height = worldLeaf.size.y;

        var frozen = new List<Rigidbody>();
        var silenced = new List<Collider>();
        GameObject body = null;
        PhysicsMaterial material = new PhysicsMaterial("GateFallBake")
        {
            dynamicFriction = track.Friction,
            staticFriction = track.Friction,
            bounciness = track.Bounciness,
        };

        // Physics.Simulate only steps by hand in Script mode; put back after.
        SimulationMode simulationMode = Physics.simulationMode;

        try
        {
            Physics.simulationMode = SimulationMode.Script;

            foreach (Rigidbody rb in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
            {
                if (rb.isKinematic) continue;
                rb.isKinematic = true;
                frozen.Add(rb);
            }

            PuzzleGate frame = door.GetComponentInParent<PuzzleGate>();
            Silence(frame != null ? frame.transform : door.parent != null ? door.parent : door, silenced);
            if (gate.OpeningBlocker != null) Silence(gate.OpeningBlocker.transform, silenced);

            // The leaf: a box the size of its mesh (a sheet that thin would tunnel), a rigidbody.
            body = new GameObject("GateFallBake Leaf") { hideFlags = HideFlags.HideAndDontSave };
            body.transform.SetPositionAndRotation(door.position + Vector3.up * StartLift, door.rotation);
            body.transform.localScale = door.lossyScale;

            var box = body.AddComponent<BoxCollider>();
            Trim(leaf, door, outward, track.SideTrim, track.TopTrim, out Vector3 trimmedCentre, out Vector3 trimmedSize);
            box.center = trimmedCentre;
            box.size = Thickened(trimmedSize, door.lossyScale);
            box.sharedMaterial = material;

            var leafBody = body.AddComponent<Rigidbody>();
            leafBody.mass = track.Mass;
            leafBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Physics.SyncTransforms();

            // What the leaf already touches as it stands (the walls its edges sink into hold it).
            var touching = new System.Text.StringBuilder();
            Vector3 boxCentre = body.transform.TransformPoint(box.center);
            Vector3 boxHalf = Vector3.Scale(box.size * 0.5f, Abs(body.transform.lossyScale));
            foreach (Collider c in Physics.OverlapBox(boxCentre, boxHalf, body.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == box) continue;
                bool apart = Physics.ComputePenetration(box, body.transform.position, body.transform.rotation,
                                                        c, c.transform.position, c.transform.rotation,
                                                        out Vector3 dir, out float depth);
                touching.Append($" '{c.name}'" + (apart ? $" {depth:0.00} m along {dir}" : ""));
            }
            if (touching.Length > 0) Debug.Log($"[GateFallBaker] the leaf starts inside:{touching}", gate);

            // The last blow, from behind, on the leaf's face.
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            float halfWidth = Mathf.Abs(Vector3.Dot(worldLeaf.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));
            Vector3 hit = new Vector3(worldLeaf.center.x, worldLeaf.min.y, worldLeaf.center.z)
                          + Vector3.up * (height * track.ImpulseHeight)
                          + side * (halfWidth * track.ImpulseSide);
            leafBody.AddForceAtPosition(outward * track.Impulse, hit, ForceMode.Impulse);

            float floorY = gate.FloorY;
            Record(track, body.transform, leafBody, height, door, floorY, out float impact, out int samples);

            track.FinalPose(out Vector3 finalPosition, out Quaternion finalRotation);
            Bounds lying = WorldBox(finalPosition, finalRotation, door.lossyScale, leaf);
            Debug.Log($"[GateFallBaker] '{gate.name}': {samples} samples, {track.Duration:0.00} s, first hit at " +
                      $"{impact:0.00} s. Lies at {finalPosition} rot {finalRotation.eulerAngles}; its box from " +
                      $"{lying.min.y - floorY:0.00} to {lying.max.y - floorY:0.00} m over the floor it stood on " +
                      $"({frozen.Count} rigidbodies held, {silenced.Count} colliders off meanwhile).", gate);
            CheckFloor(gate, track, door, filter.sharedMesh, floorY);
        }
        finally
        {
            if (body != null) Object.DestroyImmediate(body);
            Object.DestroyImmediate(material);
            foreach (Collider c in silenced) if (c != null) c.enabled = true;
            foreach (Rigidbody rb in frozen) if (rb != null) rb.isKinematic = false;
            Physics.SyncTransforms();
            Physics.simulationMode = simulationMode;
        }
    }

    /// <summary>Steps the physics until the leaf is still (or the track's max) and stores the poses.</summary>
    private static void Record(SO_GateFallTrack track, Transform body, Rigidbody rb, float height, Transform door,
                               float floorY, out float impact, out int samples)
    {
        float sampleInterval = 1f / track.SamplesPerSecond;
        float dt = sampleInterval / StepsPerSample;
        int maxSteps = Mathf.CeilToInt(track.MaxSeconds / dt);

        // The first pose is where the leaf really stands (the bake starts it a little off the floor).
        var positions = new List<Vector3> { door.position };
        var rotations = new List<Quaternion> { door.rotation };

        impact = -1f;
        float fastest = 0f;
        float stillFor = 0f;

        for (int step = 1; step <= maxSteps; step++)
        {
            Physics.Simulate(dt);
            float time = step * dt;

            // How fast its far edge moves: drops at once when it hits something.
            float speed = rb.linearVelocity.magnitude + rb.angularVelocity.magnitude * height * 0.5f;
            if (impact < 0f)
            {
                if (speed > fastest) fastest = speed;
                else if (fastest > 1f && speed < fastest * 0.6f)
                {
                    impact = time;
                    LogContacts(body, time);
                }
            }

            if (step % StepsPerSample == 0)
            {
                positions.Add(body.position);
                rotations.Add(body.rotation);
            }

            stillFor = speed < 0.05f ? stillFor + dt : 0f;
            if (impact >= 0f && (rb.IsSleeping() || stillFor > 0.3f) && step % StepsPerSample == 0) break;
        }

        if (impact < 0f) impact = (positions.Count - 1) * sampleInterval;
        samples = positions.Count;
        KeepAboveFloor(positions, rotations, door, floorY);

        Undo.RecordObject(track, "Bake gate fall");
        track.Store(sampleInterval, impact, positions.ToArray(), rotations.ToArray());
        EditorUtility.SetDirty(track);
        AssetDatabase.SaveAssetIfDirty(track);
    }

    /// <summary>
    /// Lifts every recorded pose whose mesh (undented) dips under the floor by as much as it dips: the
    /// solver lets a fast body sink a couple of centimetres into what it hits for a step or two.
    /// </summary>
    private static void KeepAboveFloor(List<Vector3> positions, List<Quaternion> rotations, Transform door, float floorY)
    {
        Mesh mesh = door.GetComponent<MeshFilter>().sharedMesh;
        if (!mesh.isReadable) return;
        Vector3[] vertices = mesh.vertices;
        Vector3 scale = door.lossyScale;

        int lifted = 0;
        float most = 0f;
        for (int i = 1; i < positions.Count; i++)
        {
            float lowest = float.MaxValue;
            foreach (Vector3 v in vertices)
                lowest = Mathf.Min(lowest, (rotations[i] * Vector3.Scale(scale, v)).y);
            float under = floorY - (positions[i].y + lowest);
            if (under <= 0f) continue;
            positions[i] += Vector3.up * under;
            lifted++;
            most = Mathf.Max(most, under);
        }
        if (lifted > 0) Debug.Log($"[GateFallBaker] lifted {lifted} poses out of the floor (up to {most:0.000} m).");
    }

    /// <summary>
    /// The leaf's mesh (undented) never below the floor: its lowest vertex over the whole track.
    /// Warns if it goes under by more than a centimetre (the dents' bulge is not counted: it faces
    /// down once the leaf lies, inside the floor, out of sight).
    /// </summary>
    private static void CheckFloor(GateKnockOff gate, SO_GateFallTrack track, Transform door, Mesh mesh, float floorY)
    {
        if (!mesh.isReadable) return;
        Vector3[] vertices = mesh.vertices;
        Vector3 scale = door.lossyScale;

        float lowest = float.MaxValue, at = 0f;
        for (float time = 0f; time <= track.Duration + 1e-4f; time += 1f / track.SamplesPerSecond)
        {
            track.Sample(time, out Vector3 position, out Quaternion rotation);
            foreach (Vector3 v in vertices)
            {
                float y = (position + rotation * Vector3.Scale(scale, v)).y - floorY;
                if (y < lowest) { lowest = y; at = time; }
            }
        }

        string verdict = lowest >= -0.01f ? "OK" : "GOES THROUGH THE FLOOR";
        string message = $"[GateFallBaker] floor check: lowest point of the leaf {lowest:+0.000;-0.000} m from the " +
                         $"floor (at {at:0.00} s): {verdict}.";
        if (lowest >= -0.01f) Debug.Log(message, gate);
        else Debug.LogWarning(message, gate);
    }

    /// <summary>What the leaf is touching (2 cm round it): what it hit.</summary>
    private static void LogContacts(Transform body, float time)
    {
        var box = body.GetComponent<BoxCollider>();
        Vector3 half = Vector3.Scale(box.size * 0.5f, Abs(body.lossyScale)) + Vector3.one * 0.02f;
        var names = new System.Text.StringBuilder();
        foreach (Collider c in Physics.OverlapBox(body.TransformPoint(box.center), half, body.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (c != box) names.Append($" '{c.name}' {c.GetType().Name} {c.bounds.min:F2}..{c.bounds.max:F2}");
        names.Append($"; leaf box {box.bounds.min:F2}..{box.bounds.max:F2}");
        Debug.Log($"[GateFallBaker] first hit at {time:0.00} s, leaf at {body.position} tilted " +
                  $"{Vector3.Angle(body.rotation * Vector3.forward, Vector3.up):0} deg from its forward; touching:{names}");
    }

    private static void Silence(Transform root, List<Collider> silenced)
    {
        foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
        {
            if (!c.enabled) continue;
            c.enabled = false;
            silenced.Add(c);
        }
    }

    /// <summary>
    /// The leaf's box (mesh space) <paramref name="sideTrim"/> metres narrower on each side and
    /// <paramref name="topTrim"/> shorter at the top, its bottom where it was: the edges hidden in the
    /// walls and the ceiling.
    /// </summary>
    private static void Trim(Bounds leaf, Transform door, Vector3 outward, float sideTrim, float topTrim,
                             out Vector3 centre, out Vector3 size)
    {
        centre = leaf.center;
        size = leaf.size;

        int across = AxisAlong(door, Vector3.Cross(Vector3.up, outward), out _);
        float scale = Mathf.Abs(door.lossyScale[across]);
        if (scale > 1e-6f) size[across] = Mathf.Max(size[across] * 0.5f, size[across] - 2f * sideTrim / scale);

        int up = AxisAlong(door, Vector3.up, out float sign);
        scale = Mathf.Abs(door.lossyScale[up]);
        if (scale <= 1e-6f) return;
        float cut = Mathf.Min(size[up] * 0.25f, topTrim / scale);
        size[up] -= cut;
        // Taken off the end that is up in the world: the centre moves down by half of it.
        centre[up] -= sign * Mathf.Sign(door.lossyScale[up]) * cut * 0.5f;
    }

    /// <summary>The door's local axis closest to <paramref name="world"/>, and whether it points with it (+1) or against it (-1).</summary>
    private static int AxisAlong(Transform door, Vector3 world, out float sign)
    {
        int best = 0;
        float bestDot = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 axis = Vector3.zero;
            axis[i] = 1f;
            float dot = Vector3.Dot(door.rotation * axis, world);
            if (Mathf.Abs(dot) > Mathf.Abs(bestDot)) { bestDot = dot; best = i; }
        }
        sign = Mathf.Sign(bestDot);
        return best;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static Vector3 Thickened(Vector3 size, Vector3 scale)
    {
        for (int i = 0; i < 3; i++)
        {
            float world = Mathf.Abs(size[i] * scale[i]);
            if (world < MinThickness && Mathf.Abs(scale[i]) > 1e-6f) size[i] = MinThickness / Mathf.Abs(scale[i]);
        }
        return size;
    }

    private static Bounds WorldBox(Vector3 position, Quaternion rotation, Vector3 scale, Bounds local)
    {
        var world = new Bounds(position + rotation * Vector3.Scale(scale, local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                                         (i & 2) == 0 ? local.min.y : local.max.y,
                                         (i & 4) == 0 ? local.min.z : local.max.z);
            world.Encapsulate(position + rotation * Vector3.Scale(scale, corner));
        }
        return world;
    }
}
