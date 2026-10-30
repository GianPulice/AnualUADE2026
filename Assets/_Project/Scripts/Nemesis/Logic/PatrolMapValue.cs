using UnityEngine;

/// <summary>
/// The weight the cluster patrol gives a zone for the value the search left behind (plan §19.5, E3).
/// Pure, so it can be tested without a map or a scene.
/// </summary>
public static class PatrolMapValue
{
    /// <summary>1 with no value, <paramref name="strength"/> from <paramref name="fullShare"/> of the
    /// map's value up, linear in between.</summary>
    public static float Weight(float share, float strength, float fullShare)
    {
        if (strength <= 1f || fullShare <= 0f) return 1f;

        float t = Mathf.Clamp01(share / fullShare);
        return Mathf.Lerp(1f, strength, t);
    }
}
