using UnityEngine;

/// <summary>
/// The CRT curvature, in C#. It has to match CRTWarpUV in InventoryCRT.shader exactly: the shader uses
/// it to pick which pixel of the UI texture each screen pixel shows, and CRTWarpedRaycaster uses it to
/// send a click to that same pixel — so what gets clicked is what is seen. Change one, change both.
/// </summary>
public static class CRTWarp
{
    /// <param name="uv">Screen UV, 0..1, origin bottom-left.</param>
    /// <param name="strength">Curvature. 0 = flat.</param>
    /// <param name="aspect">Screen width / height, so the bulge stays round on a wide screen.</param>
    public static Vector2 Warp(Vector2 uv, float strength, float aspect)
    {
        Vector2 c = uv - new Vector2(0.5f, 0.5f);
        Vector2 scaled = new Vector2(c.x * aspect, c.y);

        // Scaled so the corners land exactly on the screen corners. Without it the curvature pushes
        // the edges of the canvas off screen, and the inventory's panels run almost edge to edge.
        float fit = 1f / (1f + strength * (0.25f * aspect * aspect + 0.25f));
        float bulge = 1f + strength * Vector2.Dot(scaled, scaled);

        return new Vector2(0.5f, 0.5f) + c * (bulge * fit);
    }
}
