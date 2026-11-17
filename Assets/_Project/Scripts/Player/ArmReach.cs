using UnityEngine;

/// <summary>
/// One frame of the player's grab reach: where the arm's hand and elbow go at a given moment of the
/// gesture, and the bending of the arm to them (<see cref="ArmReachIK"/>). Stateless and with no
/// MonoBehaviour in it, so <see cref="PlayerGrabGesture"/> drives it every LateUpdate and an editor
/// check can drive it too, on a prefab that is not running.
///
/// The motion is the grab clip's own, baked into <see cref="SO_GrabGestureConfig"/>: the hand follows
/// the clip's path (mirrored for the right arm) and is pulled toward the object by how far into the
/// reach it is, so at the peak the hand is on the object and anywhere else it is on the clip's arc.
///
/// THE ARM CAN NEVER GO BEHIND THE BODY: the direction to the object is clamped to
/// <see cref="SO_GrabGestureConfig.MaxAngleFromBody"/> either side of where the body faces, and past
/// that angle the whole reach fades out (the arm goes back to the walking pose).
/// </summary>
public static class ArmReach
{
    /// <summary>Where the profile peaks (0 to 1): the moment the hand is on the object.</summary>
    public static float PeakProgress(AnimationCurve profile)
    {
        if (profile == null || profile.length == 0) return 0.5f;

        const int steps = 64;
        float best = 0f, bestValue = float.MinValue;
        for (int i = 0; i <= steps; i++)
        {
            float u = i / (float)steps;
            float value = profile.Evaluate(u);
            if (value > bestValue) { bestValue = value; best = u; }
        }
        return best;
    }

    /// <summary>
    /// Bends one arm for this frame. <paramref name="progress"/> is how far through the reach it is
    /// (0 to 1). Does nothing when the weight comes to nothing (the reach lets go, or the very ends of
    /// it, where the arm is the walking one). Returns that weight, for whoever wants to look at it.
    /// </summary>
    public static float Apply(Transform body, Transform upper, Transform fore, Transform hand, bool isLeft,
                              Vector3 target, float progress, float peakProgress, SO_GrabGestureConfig config)
    {
        if (body == null || upper == null || fore == null || hand == null || config == null) return 0f;

        ArmReachIK.BodyAxes(body, out Vector3 right, out Vector3 up, out Vector3 forward);

        Vector3 shoulder = upper.position;
        float armLength = (fore.position - shoulder).magnitude + (hand.position - fore.position).magnitude;
        if (armLength < 1e-4f) return 0f;

        // Where the object is, in the body's frame: round (yaw, from straight ahead) and up (pitch).
        Vector3 toTarget = target - shoulder;
        float distance = toTarget.magnitude;
        if (distance < 1e-4f) return 0f;

        float x = Vector3.Dot(toTarget, right);
        float y = Vector3.Dot(toTarget, up);
        float z = Vector3.Dot(toTarget, forward);
        float yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(y, Mathf.Sqrt(x * x + z * z)) * Mathf.Rad2Deg;

        // Past the limit the reach lets go: the arm goes back to the walking pose instead of being
        // dragged round to something beside or behind the body.
        float limit = config.MaxAngleFromBody;
        float held = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(limit, limit + config.LetGoDegrees, Mathf.Abs(yaw)));

        // The blend at each end of the reach: from the walking pose onto the clip's path, and back.
        float blend = Mathf.Max(0.0001f, config.ReachBlendSeconds / Mathf.Max(0.1f, config.ReachSeconds));
        float envelope = Mathf.SmoothStep(0f, 1f, progress / blend)
                       * Mathf.SmoothStep(0f, 1f, (1f - progress) / blend);
        float weight = envelope * held;
        if (weight <= 0f) return 0f;

        // And while it does reach, it can only point in front: the direction is clamped to the limit,
        // so the hand has no way to go behind the shoulder, whatever the target does.
        float yawClamped = Mathf.Clamp(yaw, -limit, limit) * Mathf.Deg2Rad;
        float pitchClamped = Mathf.Clamp(pitch, -85f, 70f) * Mathf.Deg2Rad;
        Vector3 direction = right * (Mathf.Sin(yawClamped) * Mathf.Cos(pitchClamped))
                          + up * Mathf.Sin(pitchClamped)
                          + forward * (Mathf.Cos(yawClamped) * Mathf.Cos(pitchClamped));
        Vector3 reachPoint = shoulder + direction * Mathf.Min(distance, armLength * config.MaxReachFraction);

        Vector3 outward = isLeft ? -right : right;
        Vector3 defaultHint = Vector3.down * config.ElbowDown + outward * config.ElbowOut;

        Vector3 handTarget;
        Vector3 elbowHint;

        Vector3[] handPath = config.ReachHandPath;
        Vector3[] bendPath = config.ReachElbowBend;
        bool hasPath = handPath != null && bendPath != null && handPath.Length >= 2 && bendPath.Length == handPath.Length;

        if (hasPath)
        {
            // The clip's hand and elbow at this moment, in the body's frame (the clip is the left arm;
            // the right one is its mirror image), scaled to this arm.
            Vector3 handAt = ToWorld(Mirror(ArmReachIK.SamplePath(handPath, progress), isLeft), right, up, forward) * armLength;
            Vector3 bendAt = ToWorld(Mirror(ArmReachIK.SamplePath(bendPath, progress), isLeft), right, up, forward) * armLength;
            Vector3 handAtPeak = ToWorld(Mirror(ArmReachIK.SamplePath(handPath, peakProgress), isLeft), right, up, forward) * armLength;

            // From the clip's path toward the object, by how far into the reach it is.
            float pull = Mathf.Clamp01(config.ReachProfile.Evaluate(progress));
            handTarget = shoulder + handAt + (reachPoint - (shoulder + handAtPeak)) * pull;

            // The clip's own bend, with a trace of the default so a straight arm still has a side.
            elbowHint = bendAt + defaultHint.normalized * (0.02f * armLength);
        }
        else
        {
            // No baked path: straight to the object, by the profile.
            handTarget = reachPoint;
            elbowHint = defaultHint;
            weight *= Mathf.Clamp01(config.ReachProfile.Evaluate(progress));
        }

        ArmReachIK.Apply(upper, fore, hand, handTarget, elbowHint, config.MaxReachFraction, weight);
        return weight;
    }

    // The clip is the left arm: for the right one the sideways part flips.
    private static Vector3 Mirror(Vector3 v, bool isLeft) => isLeft ? v : new Vector3(-v.x, v.y, v.z);

    private static Vector3 ToWorld(Vector3 v, Vector3 right, Vector3 up, Vector3 forward) =>
        right * v.x + up * v.y + forward * v.z;
}
