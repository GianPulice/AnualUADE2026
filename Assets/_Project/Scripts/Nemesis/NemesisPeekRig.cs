using UnityEngine;

/// <summary>
/// The peek: the Nemesis at the edge of a doorway, one hand flat on the wall and the other reaching
/// out past the corner towards the player, posed by two-bone IK over whatever the Animator plays.
///
/// It exists for the moment the Director brings the Nemesis back to the door of the room the player
/// is in, when they thought they had lost it. The body stays out of sight behind the jamb; the long
/// arm is the part that shows. The model's own E_First_Contact clip is the base pose; this puts the
/// two hands where the place needs them, which no clip can know.
///
/// Procedural on purpose: where the wall is, and which side the player is on, change every time.
///
///   - The GRIP arm is the one on the wall's side. Its wrist is put on the wall surface.
///   - The REACH arm is the other one. Its wrist is stretched towards the player, at about head height
///     and as far as the arm goes.
///
/// It reads the bones from <see cref="NemesisArmWallGuard"/>, which already knows the rig, and stands
/// that guard down for the length of the peek: the guard folds an arm that is pointing at a wall, and
/// this arm is pointing at one on purpose. SETUP: nothing; <c>NemesisStateManager.PlayPeek</c> adds it
/// to the model when there is a guard on it. Without a guard there are no bones, and the peek is
/// just the clip.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // After the arm wall guard and whatever else writes these bones.
[AddComponentMenu("WIRED/Nemesis/Peek Rig")]
public class NemesisPeekRig : MonoBehaviour
{
    [Tooltip("Seconds to bring the hands to their targets, and to let them go.")]
    [SerializeField, Min(0.05f)] private float blendSeconds = 0.5f;

    [Tooltip("Metres off the wall the gripping wrist is kept, so the hand rests on it instead of in it.")]
    [SerializeField, Min(0f)] private float gripStandoff = 0.06f;

    [Tooltip("Height above the feet the gripping wrist goes to, as a fraction of the body's height.")]
    [SerializeField, Range(0.3f, 1f)] private float gripHeight = 0.7f;

    [Tooltip("Height above the feet the reaching wrist goes to, as a fraction of the body's height.")]
    [SerializeField, Range(0.3f, 1f)] private float reachHeight = 0.62f;

    [Tooltip("How much of the reaching arm's length is stretched out. Under 1 keeps a small bend in " +
             "the elbow, which reads as reaching and not as a stiff pole.")]
    [SerializeField, Range(0.5f, 1f)] private float reachFraction = 0.97f;

    private NemesisArmWallGuard guard;
    private Transform body;
    private float bodyHeight = 2f;

    private bool peeking;
    private float weight;
    private Vector3 wallPoint;
    private Vector3 wallNormal;
    private Transform reachTarget;
    private Vector3 reachPoint;

    /// <summary>Which of the guard's two arms is on the wall. Decided ONCE when the peek starts: asked
    /// every frame it flipped whenever the wall was ahead of or behind the shoulders, the arms trading
    /// jobs mid-pose.</summary>
    private int gripArm;

    /// <summary>How fast the reaching hand follows the player, per second. Slow on purpose: a hand that
    /// tracks a walking player exactly looks like a turret, and one that does not moves at all reads as
    /// a scarecrow.</summary>
    private const float ReachFollow = 4f;

    /// <summary>The peek is posing the arms right now, blended in at all.</summary>
    public bool IsActive => peeking || weight > 0.001f;

    private void Awake() => Resolve();

    private void Resolve()
    {
        if (guard != null) return;

        guard = GetComponent<NemesisArmWallGuard>();

        UnityEngine.AI.NavMeshAgent agent = GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
        body = agent != null ? agent.transform : transform;
        bodyHeight = agent != null ? agent.height : 2f;
    }

    /// <summary>
    /// Starts the peek. <paramref name="wallPoint"/> and <paramref name="wallNormal"/> are where the
    /// wall is (normal pointing out of it, towards the Nemesis), <paramref name="reachToward"/> the
    /// point the long arm is stretched at. Returns false when there are no arm bones to pose.
    /// </summary>
    public bool Begin(Vector3 wallPoint, Vector3 wallNormal, Transform reachToward)
    {
        Resolve();
        if (guard == null || guard.ArmCount < 2 || reachToward == null) return false;

        this.wallPoint = wallPoint;
        this.wallNormal = wallNormal.sqrMagnitude > 1e-6f ? wallNormal.normalized : -transform.forward;
        reachTarget = reachToward;
        reachPoint = reachToward.position;

        // The shoulder closer to the wall grips it.
        if (guard.TryGetArmBones(0, out Transform s0, out _, out _) &&
            guard.TryGetArmBones(1, out Transform s1, out _, out _))
        {
            gripArm = (s0.position - wallPoint).sqrMagnitude <= (s1.position - wallPoint).sqrMagnitude ? 0 : 1;
        }

        peeking = true;
        guard.SetExternalSuppression(true);
        return true;
    }

    /// <summary>Lets the hands go. They ease back to the Animator's pose, and the guard comes back once
    /// they are there.</summary>
    public void End() => peeking = false;

    private void OnDisable()
    {
        peeking = false;
        weight = 0f;
        if (guard != null) guard.SetExternalSuppression(false);
    }

    private void LateUpdate()
    {
        if (guard == null) return;

        weight = Mathf.MoveTowards(weight, peeking ? 1f : 0f, Time.deltaTime / blendSeconds);

        if (weight <= 0.001f)
        {
            weight = 0f;
            if (!peeking) guard.SetExternalSuppression(false);
            return;
        }

        // Which arm is nearer the wall. By the shoulders' positions on the body's right.
        if (!guard.TryGetArmBones(0, out Transform sA, out Transform eA, out Transform tA)) return;
        if (!guard.TryGetArmBones(1, out Transform sB, out Transform eB, out Transform tB)) return;

        float eased = weight * weight * (3f - 2f * weight);
        Vector3 feet = body.position;

        bool aGrips = gripArm == 0;

        Transform gripS = aGrips ? sA : sB, gripE = aGrips ? eA : eB, gripT = aGrips ? tA : tB;
        Transform reachS = aGrips ? sB : sA, reachE = aGrips ? eB : eA, reachT = aGrips ? tB : tA;

        // Grip: on the wall, at chest height, held a hand's thickness off it. The elbow goes back and
        // down, away from the wall, as a hand flat against it would.
        Vector3 gripTarget = new Vector3(wallPoint.x, feet.y + bodyHeight * gripHeight, wallPoint.z)
                             + wallNormal * gripStandoff;
        Solve(gripS, gripE, gripT, gripTarget, wallNormal + Vector3.down, eased);

        // Reach: towards the player, as far as the arm goes, the elbow dropping a little.
        Vector3 reachOrigin = reachS.position;
        if (reachTarget != null)
            reachPoint = Vector3.Lerp(reachPoint, reachTarget.position, 1f - Mathf.Exp(-ReachFollow * Time.deltaTime));

        Vector3 aim = new Vector3(reachPoint.x, feet.y + bodyHeight * reachHeight, reachPoint.z) - reachOrigin;
        float armLength = (reachE.position - reachS.position).magnitude + (reachT.position - reachE.position).magnitude;
        Vector3 reachGoal = reachOrigin + aim.normalized * armLength * reachFraction;
        Solve(reachS, reachE, reachT, reachGoal, Vector3.down, eased);
    }

    /// <summary>
    /// Two-bone IK in world space: puts the wrist on <paramref name="target"/> (clamped to what the arm
    /// can reach), with the elbow bending towards <paramref name="pole"/>. <paramref name="blend"/>
    /// takes it from the animated pose (0) to the solved one (1) by turning each bone part of the way.
    /// </summary>
    private static void Solve(Transform shoulder, Transform elbow, Transform wrist, Vector3 target, Vector3 pole,
                              float blend)
    {
        Vector3 s = shoulder.position;
        Vector3 e = elbow.position;
        Vector3 w = wrist.position;

        float upper = (e - s).magnitude;
        float fore = (w - e).magnitude;
        if (upper < 1e-4f || fore < 1e-4f) return;

        Vector3 toTarget = target - s;
        float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - fore) + 0.01f, upper + fore - 0.01f);
        Vector3 axis = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;

        float along = (upper * upper - fore * fore + reach * reach) / (2f * reach);
        float radius = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));

        Vector3 bend = Vector3.ProjectOnPlane(pole, axis);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(e - s, axis);
        bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.up;

        Vector3 elbowAt = s + axis * along + bend * radius;
        Vector3 wristAt = s + axis * reach;

        // Shoulder first: it carries the elbow and the wrist with it. Then the elbow, from where the
        // shoulder's turn left the wrist.
        Quaternion atShoulder = Quaternion.FromToRotation(e - s, elbowAt - s);
        shoulder.rotation = Quaternion.Slerp(Quaternion.identity, atShoulder, blend) * shoulder.rotation;

        Vector3 elbowNow = elbow.position;
        Quaternion atElbow = Quaternion.FromToRotation(wrist.position - elbowNow, wristAt - elbowNow);
        elbow.rotation = Quaternion.Slerp(Quaternion.identity, atElbow, blend) * elbow.rotation;
    }
}
