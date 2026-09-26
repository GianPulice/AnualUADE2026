using UnityEngine;

/// <summary>
/// Draws the interaction cast in the Scene view: where it starts, how far it reaches, how thick it
/// is, and what it is currently touching.
///
/// It is the measuring stick for the interaction range, and the number it measures is not what the
/// name suggests. The reach is spent FROM THE PLAYER, not from the camera — this is a third person
/// rig and the lens orbits ~3.4 m behind and above the character, so a range drawn from there
/// describes nothing the player can act on. The wire sphere below is therefore centred on the
/// character's chest, and the segment leaves from the point of the crosshair line closest to it.
///
/// It calls <see cref="InteractionProbe"/> rather than mirroring it. The two used to be separate
/// copies of the same SphereCast kept in step by a comment, which is how a gizmo quietly starts
/// drawing a range the game does not actually have.
///
/// It also says WHY the cast answered what it did, with labels: the candidate the thick ray
/// found, the thin line of sight it was judged along (green clear, red blocked), what blocked it
/// and what was skipped as its support, each with its layer. "The key is not detected" turns into
/// "blocked by X on layer Y" at a glance.
///
/// SETUP: drop it anywhere under the player (the camera rig is the natural home) and give it the
/// SO_InteractionManager asset.
/// </summary>
[ExecuteAlways]
public class InteractionRangeGizmo : MonoBehaviour
{
    [Tooltip("The same asset the InteractionManager uses. Without it there is nothing to draw — " +
             "the reach, the layers and the crosshair position all live in there.")]
    [SerializeField] private SO_InteractionManager config;

    [Tooltip("Draw a live hit marker where the cast currently lands, and tint the ray green while " +
             "it is on an interactable. Also labels why: the candidate, what blocked it and what " +
             "was skipped as its support. Costs one cast per Scene view repaint, which is why it " +
             "is opt-in.")]
    [SerializeField] private bool showHitPoint = true;

    [Tooltip("Player this rig belongs to. Left empty it is found in the parents, then in the " +
             "PlayerRegistry — which is what resolves it in Play mode when the rig has been " +
             "pulled out of the character hierarchy.")]
    [SerializeField] private PlayerStateManager player;

    [Tooltip("Camera the crosshair is drawn over. Left empty it uses Camera.main, which in Play " +
             "mode is the CinemachineBrain camera the InteractionManager also casts from.")]
    [SerializeField] private Camera aimCamera;

    private static readonly Color RayColor = new Color(1f, 0.784f, 0.314f);
    private static readonly Color HitColor = new Color(0.31f, 0.78f, 0.47f);
    private static readonly Color ReachColor = new Color(1f, 0.784f, 0.314f, 0.35f);
    private static readonly Color BlockedColor = new Color(1f, 0.3f, 0.3f);
    private static readonly Color SupportColor = new Color(0.4f, 0.8f, 1f);

#if UNITY_EDITOR
    // Built lazily and recoloured per label: a GUIStyle can be created outside OnGUI, GUI.skin
    // cannot be read there.
    private static GUIStyle labelStyle;
#endif

    // Resolved into a non-serialized field and never back into `player`: OnDrawGizmos runs in edit
    // mode, and writing a serialized field from there marks the scene dirty on every repaint.
    private PlayerStateManager resolvedPlayer;

    private PlayerStateManager Player
    {
        get
        {
            if (player != null) return player;
            if (resolvedPlayer != null) return resolvedPlayer;

            resolvedPlayer = GetComponentInParent<PlayerStateManager>();
            return resolvedPlayer != null ? resolvedPlayer : PlayerRegistry.Current;
        }
    }

    private Camera AimCamera => aimCamera != null ? aimCamera : Camera.main;

    /// <summary>
    /// OnDrawGizmos and not OnDrawGizmosSelected: the point is to see the reach while dragging
    /// props around, not while the camera rig happens to be the selected object.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (config == null) return;

        Camera camera = AimCamera;
        PlayerStateManager owner = Player;

        if (!InteractionProbe.TryBuildCast(camera, owner, config, out Ray cast, out float reach))
            return;

        // The reach sphere is the honest picture of the range: everything inside it is in arm's
        // length of the character, whichever way the camera happens to be orbiting.
        Gizmos.color = ReachColor;
        Gizmos.DrawWireSphere(InteractionProbe.ReachAnchor(camera, owner, config), reach);

        float radius = config.CastRadius;
        Vector3 end = cast.origin + cast.direction * reach;

        RaycastHit hit = default;
        IInteractable target = null;
        InteractionProbe.ProbeReport report = default;
        if (showHitPoint) target = InteractionProbe.Find(camera, owner, config, out hit, out report);

        Gizmos.color = target != null ? HitColor : RayColor;
        Gizmos.DrawLine(cast.origin, end);

        // Both ends drawn at the cast's real radius: it is a thick ray, and the extra forgiveness
        // at the tip is part of what the range actually is.
        Gizmos.DrawWireSphere(cast.origin, radius);
        Gizmos.DrawWireSphere(end, radius);

        if (showHitPoint) DrawDiagnosis(report, radius);

        if (target == null) return;

        Vector3 at = PointOf(hit);
        Gizmos.color = HitColor;
        Gizmos.DrawWireSphere(at, radius * 1.5f);
        Gizmos.DrawLine(at, at + hit.normal * 0.3f);
    }

    /// <summary>
    /// Why the cast answered what it did: the candidate the thick ray found, the thin line of sight
    /// it was judged along (green clear, red blocked), what blocked it — or the solid interactable
    /// in front that took its place — and what was skipped as its support.
    /// </summary>
    private static void DrawDiagnosis(in InteractionProbe.ProbeReport report, float radius)
    {
        if (report.Candidate is Component candidate && candidate != null)
        {
            string source = report.FromCloseRange ? " (close range)" : string.Empty;
            Label(candidate.transform.position, $"candidate: {candidate.name}{source}", HitColor);
        }

        if (report.HasSightLine)
        {
            Gizmos.color = report.Occluded ? BlockedColor : HitColor;
            Gizmos.DrawLine(report.SightFrom, report.SightTo);
        }

        if (report.SupportIgnored)
        {
            Gizmos.color = SupportColor;
            Gizmos.DrawWireSphere(report.SupportHit.point, radius * 0.5f);
            Label(report.SupportHit.point, $"support, ignored: {Describe(report.SupportHit.collider)}",
                  SupportColor);
        }

        if (report.Occluded || report.ReplacedByFront)
        {
            Color color = report.Occluded ? BlockedColor : HitColor;
            float candidateAt = report.HasSightLine
                ? Vector3.Distance(report.SightFrom, report.SightTo)
                : report.CandidateHit.distance;
            Vector3 at = PointOf(report.OccluderHit);

            Gizmos.color = color;
            Gizmos.DrawWireSphere(at, radius);
            Label(at,
                  $"{(report.Occluded ? "blocked by" : "in front")}: " +
                  $"{Describe(report.OccluderHit.collider)} at {report.OccluderHit.distance:0.00} m " +
                  $"(candidate at {candidateAt:0.00} m)",
                  color);
        }
    }

    /// <summary>
    /// Where to draw a hit. A cast that starts inside a collider reports it at distance 0 with a
    /// zero point, which would put the marker at the world origin: the collider's centre stands in.
    /// </summary>
    private static Vector3 PointOf(RaycastHit hit)
    {
        if (hit.distance > 0f || hit.collider == null) return hit.point;
        return hit.collider.bounds.center;
    }

    /// <summary>A collider's object and layer, the two things needed to fix what it is doing.</summary>
    private static string Describe(Collider collider)
    {
        if (collider == null) return "?";
        return $"{collider.name} [{LayerMask.LayerToName(collider.gameObject.layer)}]";
    }

    private static void Label(Vector3 position, string text, Color color)
    {
#if UNITY_EDITOR
        if (labelStyle == null) labelStyle = new GUIStyle { fontSize = 11 };
        labelStyle.normal.textColor = color;
        UnityEditor.Handles.Label(position, text, labelStyle);
#endif
    }
}
