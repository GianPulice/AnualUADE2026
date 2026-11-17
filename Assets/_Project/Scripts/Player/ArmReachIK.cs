using UnityEngine;

/// <summary>
/// Two-bone IK for an arm of the player's Generic rig, applied over the pose the Animator left.
///
/// Unity's own IK is out: <c>OnAnimatorIK</c> is Humanoid-only, and the Animation Rigging package is
/// not in the project. The solve is the textbook one — the law of cosines for the elbow, then each
/// bone turned by the shortest arc onto its new direction — with no state of its own, so it can be
/// called every LateUpdate with whatever pose the Animator has just produced.
///
/// The bones are blended, not replaced: with <c>weight</c> 0 the arm is exactly as animated, so the
/// reach starts from the walking arm and hands it back the same way, with no bind pose in between.
/// </summary>
public static class ArmReachIK
{
    /// <summary>
    /// Bends <paramref name="upper"/> (the arm) and <paramref name="fore"/> (the forearm) so that
    /// <paramref name="hand"/> goes to <paramref name="target"/>. A target out of reach leaves the arm
    /// stretched toward it, short of the spot: the hand never goes past what the arm can do.
    /// </summary>
    /// <param name="elbowHint">World direction the elbow should point to, roughly. Only its
    /// component across the arm counts: it picks the plane the arm bends in.</param>
    /// <param name="maxReachFraction">Share of the arm's full length it may stretch to (below 1, so
    /// the elbow is never locked straight).</param>
    /// <param name="weight">0 = the animated pose, 1 = the solved one.</param>
    public static void Apply(Transform upper, Transform fore, Transform hand, Vector3 target,
                             Vector3 elbowHint, float maxReachFraction, float weight)
    {
        if (upper == null || fore == null || hand == null || weight <= 0f) return;

        Quaternion upperAnimated = upper.rotation;
        Quaternion foreAnimated = fore.rotation;

        Vector3 shoulder = upper.position;
        Vector3 elbowNow = fore.position;
        Vector3 handNow = hand.position;

        // Measured on the pose in front of us each frame, so the rig's scale never has to be known.
        float upperLength = (elbowNow - shoulder).magnitude;
        float foreLength = (handNow - elbowNow).magnitude;
        if (upperLength < 1e-4f || foreLength < 1e-4f) return;

        Vector3 toTarget = target - shoulder;
        float distance = toTarget.magnitude;
        if (distance < 1e-4f) return;
        Vector3 direction = toTarget / distance;

        float farthest = (upperLength + foreLength) * Mathf.Clamp(maxReachFraction, 0.5f, 0.999f);
        float nearest = Mathf.Abs(upperLength - foreLength) + 0.01f;
        float reach = Mathf.Clamp(distance, Mathf.Min(nearest, farthest), farthest);

        // The elbow sits on the circle of points at upperLength from the shoulder and foreLength from
        // the hand's spot; the hint says which side of the shoulder-to-target line it is on.
        Vector3 bend = elbowHint - direction * Vector3.Dot(elbowHint, direction);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(direction, Vector3.right);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(direction, Vector3.forward);
        bend.Normalize();

        float cosShoulder = Mathf.Clamp(
            (upperLength * upperLength + reach * reach - foreLength * foreLength) / (2f * upperLength * reach),
            -1f, 1f);
        float sinShoulder = Mathf.Sqrt(1f - cosShoulder * cosShoulder);

        Vector3 elbowSolved = shoulder + direction * (upperLength * cosShoulder) + bend * (upperLength * sinShoulder);
        Vector3 handSolved = shoulder + direction * reach;

        // The arm: its axis (shoulder to elbow) onto shoulder to the solved elbow.
        Quaternion upperTurn = Quaternion.FromToRotation(elbowNow - shoulder, elbowSolved - shoulder);
        Quaternion upperSolved = upperTurn * upperAnimated;

        // The forearm rides on the arm's turn first (that is where it ends up once the arm moves),
        // then turns the rest of the way onto elbow to hand.
        Vector3 foreAxisAfterArm = upperTurn * (handNow - elbowNow);
        Quaternion foreSolved = Quaternion.FromToRotation(foreAxisAfterArm, handSolved - elbowSolved)
                              * (upperTurn * foreAnimated);

        // The arm first: moving it carries the forearm along, and the forearm's world rotation is
        // then set outright, so the order does not matter for the result.
        upper.rotation = Quaternion.Slerp(upperAnimated, upperSolved, weight);
        fore.rotation = Quaternion.Slerp(foreAnimated, foreSolved, weight);
    }

    // ── The body's frame, and paths measured in it ──────────────────────────

    /// <summary>
    /// The frame a reach is measured in: up is the world's, forward is where the body faces, level, and
    /// right follows. Level, so leaning or looking down does not tilt what was baked. The bake tool and
    /// the player read it through this one function, so they can never disagree.
    /// </summary>
    public static void BodyAxes(Transform body, out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        up = Vector3.up;
        forward = Vector3.ProjectOnPlane(body.forward, up);
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(body.up, up);
        forward.Normalize();
        right = Vector3.Cross(up, forward);
    }

    /// <summary>
    /// A point on a path of samples spread evenly over 0 to 1, with <paramref name="progress"/> in
    /// that range: straight lines between samples (they are a few frames apart, so it is smooth).
    /// </summary>
    public static Vector3 SamplePath(Vector3[] path, float progress)
    {
        if (path == null || path.Length == 0) return Vector3.zero;
        if (path.Length == 1) return path[0];

        float at = Mathf.Clamp01(progress) * (path.Length - 1);
        int i = Mathf.Min((int)at, path.Length - 2);
        return Vector3.Lerp(path[i], path[i + 1], at - i);
    }
}
