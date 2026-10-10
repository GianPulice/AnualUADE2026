using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lamp a push box can be read by. While a box's basket symbol sits inside this lamp's light,
/// <see cref="BoxSymbolSignalLoss"/> fades the symbol in from black, without the player having
/// to look down on it from above: push the box into the pool of light and read it there.
///
/// The lit area is the real <see cref="Light"/>'s own — its position, its direction, its Spot Angle
/// and its Range — so what reveals is what the player sees lit, and retuning the lamp retunes the
/// reveal with it. The fields below only say how much of that light counts as bright enough to read
/// by. A Point light works too and reveals inside its sphere.
///
/// It only reveals while the lamp is on: a disabled Light, an inactive object or an intensity of 0
/// reveals nothing. That is what ties it to <see cref="PoweredLightSwitch"/>, which turns its lamps
/// on and off by activating their GameObjects — put this lamp in the switch's list with the others
/// and the reveal comes and goes with them.
///
/// There is no occlusion test. These lamps cast no shadows, so a box under a pipe is lit on screen,
/// and it counts as lit here too.
///
/// The Scene view always draws the lit volume and, where the beam first lands, the pool: the ring a
/// symbol has to be inside, with the hysteresis band around it. That pool is the slice at the
/// surface; a symbol on top of a box sits higher up the cone, where the cone is narrower.
///
/// The boxes ask <see cref="IsLit"/> every frame, so the lamps keep themselves in a static list
/// rather than being searched for.
/// </summary>
[DisallowMultipleComponent]
public class BoxSymbolRevealLight : MonoBehaviour
{
    // Every reveal lamp whose component is enabled on an active object. Whether its Light is
    // actually on is checked on each query, so a Light switched off by itself stops revealing too.
    private static readonly List<BoxSymbolRevealLight> s_active = new List<BoxSymbolRevealLight>();

    [Tooltip("The lamp whose light reveals the symbols. Left empty, the Light on this object or " +
             "under it is used.")]
    [SerializeField] private Light revealLight;

    [Tooltip("How far into the lamp's soft edge a symbol still counts as lit. 0 = only the fully " +
             "bright core (the Light's Inner Spot Angle); 1 = right up to the outer edge (its Spot " +
             "Angle), where the light has already faded to nothing. Ignored by a Point light.")]
    [Range(0f, 1f)] [SerializeField] private float edgeReach = 0.5f;

    [Tooltip("Share of the Light's Range that counts as lit. The light fades out towards its " +
             "Range, so the last stretch is too dim to read by.")]
    [Range(0.1f, 1f)] [SerializeField] private float rangeReach = 0.85f;

    [Tooltip("Metres a symbol must be inside the lit area before it shows, and outside it before " +
             "it is hidden again, so a box resting right on the edge of the pool does not flicker.")]
    [Min(0f)] [SerializeField] private float edgeHysteresis = 0.1f;

    /// <summary>
    /// The volume a symbol has to be in, in world space: a sphere of <see cref="reach"/> around
    /// the lamp, cut down to a cone when the lamp is a Spot.
    /// </summary>
    private readonly struct LitShape
    {
        public readonly Vector3 apex;
        public readonly Vector3 axis;
        public readonly float reach;
        public readonly bool isCone;
        public readonly float tanHalf;
        public readonly float cosHalf;

        public LitShape(Vector3 apex, Vector3 axis, float reach, bool isCone, float tanHalf, float cosHalf)
        {
            this.apex = apex;
            this.axis = axis;
            this.reach = reach;
            this.isCone = isCone;
            this.tanHalf = tanHalf;
            this.cosHalf = cosHalf;
        }

        /// <summary>
        /// Metres by which <paramref name="point"/> is inside the shape, measured to its nearest
        /// boundary. Negative outside. Being a distance is what lets the hysteresis be in metres.
        /// </summary>
        public float InsideMargin(Vector3 point)
        {
            Vector3 toPoint = point - apex;
            float margin = reach - toPoint.magnitude;
            if (!isCone) return margin;

            float along = Vector3.Dot(toPoint, axis);
            float radial = (toPoint - axis * along).magnitude;

            // Distance to the cone's side, perpendicular to it. Behind the lamp 'along' is negative
            // and so is this, whatever the radius.
            float sideMargin = (along * tanHalf - radial) * cosHalf;
            return Mathf.Min(margin, sideMargin);
        }
    }

    /// <summary>
    /// Static state survives leaving Play mode when domain reload is disabled, which would leave
    /// destroyed lamps in the list on the next run. Same reset as <see cref="PlayerRegistry"/>.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_active.Clear();

    private void Awake()
    {
        if (revealLight == null) revealLight = GetComponentInChildren<Light>(true);

        if (revealLight == null)
        {
            Debug.LogWarning($"[{nameof(BoxSymbolRevealLight)}] '{name}' has no Light on it or " +
                             "under it. It will never reveal a box symbol.", this);
        }
    }

    // OnEnable / OnDisable and not Awake / OnDestroy: this is the lamp's own on-off state, not a
    // static event. The switch deactivates the whole object, and that is what takes it off the list.
    private void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    private void OnDisable() => s_active.Remove(this);

    /// <summary>
    /// True if <paramref name="worldPoint"/> is in the light of any reveal lamp that is on.
    /// <paramref name="currentlyLit"/> is the caller's current answer, used to apply the hysteresis
    /// in the direction that keeps it. No lamp on counts as "not lit".
    /// </summary>
    public static bool IsLit(Vector3 worldPoint, bool currentlyLit)
    {
        for (int i = 0; i < s_active.Count; i++)
        {
            if (s_active[i].Reaches(worldPoint, currentlyLit)) return true;
        }

        return false;
    }

    private bool Reaches(Vector3 worldPoint, bool currentlyLit)
    {
        if (!IsLampOn(revealLight)) return false;

        float margin = BuildShape(revealLight).InsideMargin(worldPoint);
        return margin > (currentlyLit ? -edgeHysteresis : edgeHysteresis);
    }

    // Intensity 0 counts as off: it is how the project blacks out a lamp without deactivating it
    // (the escape sirens do it on the off half of their cycle).
    private static bool IsLampOn(Light source) =>
        source != null && source.isActiveAndEnabled && source.intensity > 0f;

    /// <summary>
    /// Reads the lit volume off the Light as it is right now, so a lamp moved, re-aimed or retuned
    /// in Play mode reveals where it shines on the very next frame.
    /// </summary>
    private LitShape BuildShape(Light source)
    {
        Transform lamp = source.transform;
        float reach = source.range * rangeReach;

        // Anything that is not a Spot reveals in a sphere, the way a Point light shines.
        if (source.type != LightType.Spot)
            return new LitShape(lamp.position, lamp.forward, reach, false, 0f, 1f);

        float aperture = Mathf.Lerp(source.innerSpotAngle, source.spotAngle, edgeReach);
        float half = aperture * 0.5f * Mathf.Deg2Rad;
        return new LitShape(lamp.position, lamp.forward, reach, true, Mathf.Tan(half), Mathf.Cos(half));
    }

#if UNITY_EDITOR
    // How the gizmo looks. Not tuning: none of these changes what is revealed.
    private const float GizmoAlphaOn = 0.9f;
    private const float GizmoAlphaOff = 0.3f;
    private const float GizmoBandAlpha = 0.35f;
    private const float GizmoPoolFillAlpha = 0.08f;
    // Lifts the pool off the surface it lands on, so it does not z-fight with the floor.
    private const float GizmoSurfaceLift = 0.02f;

    // OnDrawGizmos and not OnDrawGizmosSelected: the pool has to stay visible while the boxes, the
    // baskets and the props around it are being placed.
    private void OnDrawGizmos()
    {
        Light source = revealLight != null ? revealLight : GetComponentInChildren<Light>(true);
        if (source == null) return;

        LitShape shape = BuildShape(source);

        // Faded while the lamp is off: the volume is where it WILL reveal once it is switched on.
        Color tint = source.color;
        tint.a = isActiveAndEnabled && IsLampOn(source) ? GizmoAlphaOn : GizmoAlphaOff;

        if (!shape.isCone)
        {
            Gizmos.color = tint;
            Gizmos.DrawWireSphere(shape.apex, shape.reach);
            return;
        }

        DrawCone(shape, tint);
        DrawPool(shape, tint);
    }

    // The lit cone out to where it stops revealing: four edge rays and the far rim.
    private static void DrawCone(in LitShape shape, Color tint)
    {
        float sinHalf = shape.tanHalf * shape.cosHalf;
        Vector3 rimCentre = shape.apex + shape.axis * (shape.reach * shape.cosHalf);
        float rimRadius = shape.reach * sinHalf;

        Vector3 up = Vector3.Cross(shape.axis, Vector3.up).sqrMagnitude < 1e-4f
            ? Vector3.Cross(shape.axis, Vector3.right) : Vector3.Cross(shape.axis, Vector3.up);
        up.Normalize();
        Vector3 right = Vector3.Cross(shape.axis, up);

        Gizmos.color = tint;
        Gizmos.DrawLine(shape.apex, rimCentre + up * rimRadius);
        Gizmos.DrawLine(shape.apex, rimCentre - up * rimRadius);
        Gizmos.DrawLine(shape.apex, rimCentre + right * rimRadius);
        Gizmos.DrawLine(shape.apex, rimCentre - right * rimRadius);

        UnityEditor.Handles.color = tint;
        UnityEditor.Handles.DrawWireDisc(rimCentre, shape.axis, rimRadius);
    }

    // The pool: the cone's slice on the first solid surface under the lamp. The solid ring is the
    // edge of the lit area; a symbol shows once it is past the inner ring and is hidden again once
    // it is past the outer one. Nothing is drawn when the beam lands on nothing within its reach.
    private void DrawPool(in LitShape shape, Color tint)
    {
        if (!Physics.Raycast(shape.apex, shape.axis, out RaycastHit hit, shape.reach,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        Vector3 centre = hit.point - shape.axis * GizmoSurfaceLift;
        float radius = hit.distance * shape.tanHalf;

        // The hysteresis is measured square to the cone's side; on the slice that is a bit wider.
        float band = edgeHysteresis / shape.cosHalf;

        UnityEditor.Handles.color = new Color(tint.r, tint.g, tint.b, tint.a * GizmoPoolFillAlpha);
        UnityEditor.Handles.DrawSolidDisc(centre, shape.axis, radius);

        UnityEditor.Handles.color = tint;
        UnityEditor.Handles.DrawWireDisc(centre, shape.axis, radius);

        UnityEditor.Handles.color = new Color(tint.r, tint.g, tint.b, tint.a * GizmoBandAlpha);
        UnityEditor.Handles.DrawWireDisc(centre, shape.axis, Mathf.Max(0f, radius - band));
        UnityEditor.Handles.DrawWireDisc(centre, shape.axis, radius + band);
    }
#endif
}
