using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Read-only view of the refuges (Not Walkable volumes carrying a <see cref="SafeZoneMarker"/>: the Hub), so the Director never sends the Nemesis to their door (C5).</summary>
public static class NemesisSafeZones
{
    /// <summary>Metres from a safe zone the Director's levers must stay. A rule, not a tunable: lowering it brings C5 back.</summary>
    public const float Clearance = 6f;

    private const float LevelSlack = 1f;
    private const int BuiltInNotWalkableArea = 1;

    private static readonly Vector3[] CornerScratch = new Vector3[4];

    public static IEnumerable<NavMeshModifierVolume> Volumes
    {
        get
        {
            int notWalkable = NotWalkableArea;
            List<NavMeshModifierVolume> all = NavMeshModifierVolume.activeModifiers;

            for (int i = 0; i < all.Count; i++)
            {
                NavMeshModifierVolume volume = all[i];
                if (volume == null || !volume.isActiveAndEnabled || volume.area != notWalkable) continue;

                // Unmarked Not Walkable volumes are blockers inside solid props, not refuges.
                if (!volume.TryGetComponent(out SafeZoneMarker _)) continue;

                if (IsBaked(volume)) yield return volume;
            }
        }
    }

    /// <summary>A volume only makes a hole if a surface collects it (same filter as the bake); a dropped one is walkable.</summary>
    private static bool IsBaked(NavMeshModifierVolume volume)
    {
        List<NavMeshSurface> surfaces = NavMeshSurface.activeSurfaces;
        if (surfaces.Count == 0) return true;

        int layerBit = 1 << volume.gameObject.layer;

        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null || !surface.isActiveAndEnabled) continue;
            if ((surface.layerMask.value & layerBit) == 0) continue;
            if (!volume.AffectsAgentType(surface.agentTypeID)) continue;
            if (surface.collectObjects == CollectObjects.Children && !volume.transform.IsChildOf(surface.transform)) continue;

            return true;
        }

        return false;
    }

    private static int NotWalkableArea
    {
        get
        {
            int byName = NavMesh.GetAreaFromName("Not Walkable");
            return byName >= 0 ? byName : BuiltInNotWalkableArea;
        }
    }

    /// <summary>Inside a safe zone in 3D: standing in the Hub, not on the floor above it.</summary>
    public static bool Contains(Vector3 point)
    {
        foreach (NavMeshModifierVolume volume in Volumes)
        {
            Vector3 local = volume.transform.InverseTransformPoint(point) - volume.center;
            Vector3 half = volume.size * 0.5f;

            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y &&
                Mathf.Abs(local.z) <= half.z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Plan-view distance ignoring height (0 inside, infinity with no volumes). For zone centres, which are floor-agnostic.</summary>
    public static float FlatDistance(Vector3 point) => Distance(point, sameLevelOnly: false);

    /// <summary>Plan-view distance to volumes on the point's own floor only. For concrete NavMesh positions.</summary>
    public static float DistanceOnSameLevel(Vector3 point) => Distance(point, sameLevelOnly: true);

    public static bool IsClear(Vector3 point) => DistanceOnSameLevel(point) >= Clearance;

    public static bool IsCentreClear(Vector3 centre) => FlatDistance(centre) >= Clearance;

    /// <summary>Four world corners of the box at mid height, in winding order. Exact for Y-rotated volumes.</summary>
    public static void GetFootprint(NavMeshModifierVolume volume, Vector3[] corners)
    {
        Vector3 c = volume.center;
        Vector3 h = volume.size * 0.5f;
        Transform t = volume.transform;

        corners[0] = t.TransformPoint(new Vector3(c.x - h.x, c.y, c.z - h.z));
        corners[1] = t.TransformPoint(new Vector3(c.x + h.x, c.y, c.z - h.z));
        corners[2] = t.TransformPoint(new Vector3(c.x + h.x, c.y, c.z + h.z));
        corners[3] = t.TransformPoint(new Vector3(c.x - h.x, c.y, c.z + h.z));
    }

    public static void GetVerticalSpan(NavMeshModifierVolume volume, out float minY, out float maxY)
    {
        Vector3 c = volume.center;
        Vector3 h = volume.size * 0.5f;
        Transform t = volume.transform;

        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                c.x + ((i & 1) == 0 ? -h.x : h.x),
                c.y + ((i & 2) == 0 ? -h.y : h.y),
                c.z + ((i & 4) == 0 ? -h.z : h.z));

            float y = t.TransformPoint(corner).y;
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
        }
    }

    private static float Distance(Vector3 point, bool sameLevelOnly)
    {
        float best = float.PositiveInfinity;

        foreach (NavMeshModifierVolume volume in Volumes)
        {
            if (sameLevelOnly)
            {
                GetVerticalSpan(volume, out float minY, out float maxY);
                if (point.y < minY - LevelSlack || point.y > maxY + LevelSlack) continue;
            }

            GetFootprint(volume, CornerScratch);
            best = Mathf.Min(best, FlatDistanceToQuad(point, CornerScratch));

            if (best <= 0f) return 0f;
        }

        return best;
    }

    private static float FlatDistanceToQuad(Vector3 point, Vector3[] quad)
    {
        Vector2 p = new Vector2(point.x, point.z);

        bool allLeft = true;
        bool allRight = true;
        float nearest = float.PositiveInfinity;

        for (int i = 0; i < 4; i++)
        {
            Vector2 a = new Vector2(quad[i].x, quad[i].z);
            Vector2 b = new Vector2(quad[(i + 1) % 4].x, quad[(i + 1) % 4].z);

            float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            if (cross < 0f) allLeft = false;
            if (cross > 0f) allRight = false;

            nearest = Mathf.Min(nearest, DistanceToSegment(p, a, b));
        }

        return allLeft || allRight ? 0f : nearest;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr < 0.0001f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
        return Vector2.Distance(p, a + ab * t);
    }
}
