using UnityEngine;

/// <summary>
/// How a drop comes down, picked from its height alone (plan §15.3), never from a field somebody
/// could set wrong.
///
/// APPEND-ONLY, like every enum of the Nemesis. Nothing serialises it today (the kind is always
/// derived), but the day an asset stores one, a value inserted in the middle would silently turn
/// every Hang into something else.
/// </summary>
public enum EDropKind
{
    /// <summary>Up to the hang threshold: it stands at the edge, flexes and jumps forward and down.
    /// </summary>
    Hop,

    /// <summary>Taller: it turns its back to the gap, hangs from the edge by its hands and lets go.
    /// </summary>
    Hang,
}

/// <summary>
/// The numbers that shape a drop, gathered so the traversal, the gizmo and the validator all build
/// the SAME path.
///
/// The defaults are the ones SO_NemesisMovement and SO_NemesisData ship with, and those assets
/// take their initialisers from here. A gizmo has no Nemesis to ask, so it draws with these: it
/// shows the arc a freshly created asset would fly.
/// </summary>
public readonly struct DropTuning
{
    public const float DefaultHangThreshold = 2.5f;
    public const float DefaultHopApexHeight = 0.3f;
    public const float DefaultGravity = 12f;
    public const float DefaultMinAirTime = 0.35f;
    public const float DefaultHangDepth = 1.9f;
    public const float DefaultBodyRadius = 0.3f;

    /// <summary>Height from which a drop is a Hang instead of a Hop. The Nemesis passes its own
    /// FloorHeightThreshold: the number that already separates "another floor" from "a step down".
    /// </summary>
    public readonly float HangThreshold;

    /// <summary>How far a Hop rises above the edge before it falls, in metres.</summary>
    public readonly float HopApexHeight;

    /// <summary>Metres per second squared, downwards.</summary>
    public readonly float Gravity;

    /// <summary>The shortest a fall may last, in seconds.</summary>
    public readonly float MinAirTime;

    /// <summary>How far below the edge the body hangs by its hands before letting go, in metres.
    /// </summary>
    public readonly float HangDepth;

    /// <summary>The body's radius: how far from the edge it crouches and how far out it hangs.
    /// </summary>
    public readonly float BodyRadius;

    public DropTuning(float hangThreshold, float hopApexHeight, float gravity, float minAirTime,
                      float hangDepth, float bodyRadius)
    {
        HangThreshold = hangThreshold;
        HopApexHeight = hopApexHeight;
        Gravity = gravity;
        MinAirTime = minAirTime;
        HangDepth = hangDepth;
        BodyRadius = bodyRadius;
    }

    public static DropTuning Default => new DropTuning(DefaultHangThreshold, DefaultHopApexHeight,
                                                       DefaultGravity, DefaultMinAirTime,
                                                       DefaultHangDepth, DefaultBodyRadius);
}

/// <summary>
/// The airborne part of a drop (plan §15.3, phase 4): horizontal at constant speed, vertical under
/// gravity.
///
/// A pure value, with no scene and no clock, so it can be pinned down in EditMode.
///
/// Built from where it leaves, where it lands, how high it rises first and how hard it falls; the
/// duration falls out of those. The one exception is the minimum air time, for drops so short that
/// the honest ballistic answer is a blink. There the launch speed is solved for the minimum
/// instead, which gives a short hop a visible arc rather than a teleport.
/// </summary>
public readonly struct DropArc
{
    /// <summary>Floor for the gravity, so a zero typed into the asset cannot divide by zero.</summary>
    private const float MinGravity = 0.1f;

    /// <summary>Floor for the duration of an arc that is not a drop at all (the landing above the
    /// apex). The validator forbids those; this only keeps them finite.</summary>
    private const float MinDuration = 0.05f;

    public readonly Vector3 From;
    public readonly Vector3 To;

    /// <summary>Metres per second squared, downwards.</summary>
    public readonly float Gravity;

    /// <summary>Seconds from leaving to landing.</summary>
    public readonly float Duration;

    /// <summary>Vertical speed on leaving, in m/s, positive upwards. Zero is a fall from rest.
    /// </summary>
    public readonly float LaunchSpeed;

    private DropArc(Vector3 from, Vector3 to, float gravity, float duration, float launchSpeed)
    {
        From = from;
        To = to;
        Gravity = gravity;
        Duration = duration;
        LaunchSpeed = launchSpeed;
    }

    /// <param name="apexHeight">How far above <paramref name="from"/> the arc peaks. Zero falls from
    /// rest.</param>
    /// <param name="minDuration">The shortest the fall may last. When the ballistic answer is
    /// shorter, the launch speed is raised to fill it, so the arc rises a little first.</param>
    public static DropArc Create(Vector3 from, Vector3 to, float gravity, float apexHeight, float minDuration)
    {
        gravity = Mathf.Max(MinGravity, gravity);
        apexHeight = Mathf.Max(0f, apexHeight);
        minDuration = Mathf.Max(0f, minDuration);

        float fall = from.y - to.y;
        float launch = Mathf.Sqrt(2f * gravity * apexHeight);

        // from.y + launch·t − g·t²/2 = to.y, taking the later root: the one on the way DOWN.
        float discriminant = launch * launch + 2f * gravity * fall;
        float duration = discriminant > 0f ? (launch + Mathf.Sqrt(discriminant)) / gravity : 0f;

        if (duration < minDuration || duration < MinDuration)
        {
            duration = Mathf.Max(minDuration, MinDuration);
            launch = (0.5f * gravity * duration * duration - fall) / duration;
        }

        return new DropArc(from, to, gravity, duration, launch);
    }

    /// <summary>Where the body is <paramref name="time"/> seconds after leaving. Clamped to the
    /// arc: before it is the take-off point, after it the landing.</summary>
    public Vector3 PointAt(float time)
    {
        float t = Mathf.Clamp(time, 0f, Duration);
        float fraction = Duration > 0f ? t / Duration : 1f;

        Vector3 point = Vector3.Lerp(From, To, fraction);
        point.y = From.y + LaunchSpeed * t - 0.5f * Gravity * t * t;
        return point;
    }

    /// <summary>Metres above <see cref="From"/> at the top of the arc. Zero for a fall from rest.
    /// </summary>
    public float ApexHeight => LaunchSpeed > 0f ? LaunchSpeed * LaunchSpeed / (2f * Gravity) : 0f;
}

/// <summary>
/// The whole way down, from where the Nemesis stands on the link to where it lands (plan §15.3).
///
/// A Hop is only the arc. A Hang also has the two legs the Nemesis is driven through by hand
/// before it: to the edge with its back to the gap, then over it until it hangs by its hands. The
/// arc starts from there, so a four-metre Hang is a two-metre fall, which is what makes it read as
/// climbing down rather than as jumping off a roof.
///
/// Pure, like <see cref="DropArc"/>. Finding the edge on the NavMesh is the caller's job (see
/// NemesisDropLink.FindEdge), and the edge is taken as given.
/// </summary>
public readonly struct DropPath
{
    /// <summary>The least it still falls after hanging. Without it, a drop barely taller than the
    /// body would hang with its feet on the floor below and fall nowhere.</summary>
    public const float MinFallAfterHang = 0.3f;

    public readonly EDropKind Kind;

    /// <summary>Where it stands on the link when the drop starts.</summary>
    public readonly Vector3 Start;

    /// <summary>Hang only: crouched at the edge with its back to the gap. The start, for a Hop.
    /// </summary>
    public readonly Vector3 EdgeStand;

    /// <summary>Hang only: hanging by its hands just outside the edge, where it lets go. The start,
    /// for a Hop.</summary>
    public readonly Vector3 HangPoint;

    public readonly Vector3 End;

    /// <summary>Flat unit vector from the start towards the gap: what it faces while it looks down.
    /// A Hang turns its back to it.</summary>
    public readonly Vector3 Facing;

    /// <summary>The fall: from the start for a Hop, from the hang point for a Hang.</summary>
    public readonly DropArc Arc;

    private DropPath(EDropKind kind, Vector3 start, Vector3 edgeStand, Vector3 hangPoint, Vector3 end,
                     Vector3 facing, DropArc arc)
    {
        Kind = kind;
        Start = start;
        EdgeStand = edgeStand;
        HangPoint = hangPoint;
        End = end;
        Facing = facing;
        Arc = arc;
    }

    /// <summary>Metres from where it stands down to where it lands.</summary>
    public float Height => Start.y - End.y;

    public static EDropKind KindFor(float height, float hangThreshold) =>
        height >= hangThreshold ? EDropKind.Hang : EDropKind.Hop;

    /// <param name="edge">The rim of the upper floor, in the direction of the landing. Only a Hang
    /// uses it, at the start's height whatever height it comes with.</param>
    /// <param name="fallbackFacing">For a landing straight below the start, where the direction to
    /// it is not defined.</param>
    public static DropPath Create(Vector3 start, Vector3 edge, Vector3 end, Vector3 fallbackFacing,
                                  in DropTuning tuning)
    {
        Vector3 facing = Flat(end - start);
        if (facing.sqrMagnitude < 0.0001f) facing = Flat(fallbackFacing);
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
        facing.Normalize();

        float height = start.y - end.y;
        EDropKind kind = KindFor(height, tuning.HangThreshold);

        if (kind == EDropKind.Hop)
        {
            DropArc hop = DropArc.Create(start, end, tuning.Gravity, tuning.HopApexHeight, tuning.MinAirTime);
            return new DropPath(kind, start, start, start, end, facing, hop);
        }

        edge.y = start.y;
        float radius = Mathf.Max(0f, tuning.BodyRadius);

        Vector3 edgeStand = edge - facing * radius;

        float depth = Mathf.Clamp(tuning.HangDepth, 0f, Mathf.Max(0f, height - MinFallAfterHang));
        Vector3 hangPoint = edge + facing * radius + Vector3.down * depth;

        DropArc fall = DropArc.Create(hangPoint, end, tuning.Gravity, 0f, tuning.MinAirTime);
        return new DropPath(kind, start, edgeStand, hangPoint, end, facing, fall);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
