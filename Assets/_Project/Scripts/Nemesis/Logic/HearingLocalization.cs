using UnityEngine;

/// <summary>
/// Where the ear THINKS a noise of the player's came from (Plan-Busqueda-Nemesis, Fase 1, D39).
///
/// WHY. The hearing sensor used to hand every consumer the player's exact transform. The belief's
/// radius said how unsure it was, but the point itself carried no error, so a run of footsteps
/// ending at a locker put the search at the locker door (WIR-057): the ear worked as a GPS. Now the
/// sensor reports a PERCEIVED point: the real one plus an offset no longer than a fraction of that
/// evidence's own radius. Beside the Nemesis it is still a spot; ten metres away through a wall it is
/// a couple of metres off.
///
/// SMOOTH IN TIME, not a fresh roll per sweep. The sensor hears a continuous noise ten times a
/// second, and an offset re-rolled every sweep would average out over a few of them — which is the
/// precision for free this is here to take away. The offset drifts: one noise heard twice in a row
/// comes from nearly the same wrong place.
///
/// PURE: a seed, a time and two lengths in, an offset out, so EditMode tests can reach it. It does
/// not use Mathf.PerlinNoise (native) on purpose: the tests also run outside the editor.
/// </summary>
public static class HearingLocalization
{
    /// <summary>The longest the offset may be, in metres: <paramref name="errorFraction"/> of the
    /// evidence radius.</summary>
    public static float MaxError(float evidenceRadius, float errorFraction) =>
        Mathf.Max(0f, evidenceRadius) * Mathf.Clamp01(errorFraction);

    /// <summary>
    /// A flat offset (y = 0) no longer than <paramref name="maxError"/>, drifting to a new direction
    /// and length about every <paramref name="driftTime"/> seconds.
    /// </summary>
    /// <param name="seed">Per listener, so two Nemeses do not mishear the same way.</param>
    public static Vector3 Offset(float seed, float time, float driftTime, float maxError)
    {
        if (maxError <= 0f) return Vector3.zero;

        float t = time / Mathf.Max(0.01f, driftTime);
        Vector2 v = new Vector2(SmoothNoise(seed, t), SmoothNoise(seed + 157.31f, t));
        if (v.sqrMagnitude > 1f) v.Normalize();

        return new Vector3(v.x, 0f, v.y) * maxError;
    }

    /// <summary>Value noise in [-1, 1]: one random value per whole step of <paramref name="t"/>,
    /// eased between neighbouring steps so it has no jumps.</summary>
    public static float SmoothNoise(float seed, float t)
    {
        float step = Mathf.Floor(t);
        float f = t - step;
        float eased = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(seed, step), Hash(seed, step + 1f), eased);
    }

    private static float Hash(float seed, float step)
    {
        double s = System.Math.Sin(step * 12.9898 + seed * 78.233) * 43758.5453;
        return (float)((s - System.Math.Floor(s)) * 2.0 - 1.0);
    }
}
