using UnityEngine;

/// <summary>
/// The Nemesis's line-of-sight zones, from most to least trusted:
///
///   - FOCUS (inside FocusAngle, out to ViewRange): it SEES you. Instant.
///   - PERIPHERAL (the rest of ViewAngle, out to ViewRange): "creo que vi algo por acá". It fills the
///     suspicion meter at a rate that grows with closeness; full, it becomes a sighting.
///   - REAR (everything outside ViewAngle, out to RearSenseRange — a few metres): "siento que hay
///     alguien atrás mío". Much weaker: it fills the same meter at RearSenseStrength of the peripheral
///     rate and can never make it a sighting on its own. Past the suspicion threshold the Nemesis turns
///     round to look, and then its eyes decide.
///
/// Hard proximity (ProximityDetectionRange, "it is standing on me") sits under all three and is not a
/// zone: it ignores the angle entirely.
///
/// "Out to ViewRange" means the range the eyes are working at (<see cref="AdaptiveViewRange"/>): the
/// base one for a Nemesis that has not seen the player, further while it holds them in sight or
/// hunts them. The zones do not care which — they measure against the range they are handed. The
/// rear zone has its own range and never grows with it.
///
/// PURE: angles and distances in, a zone or a rate out. FieldOfView, the SO inspector's tester and the
/// gizmos all go through here, so the three cannot disagree about where a zone ends or how fast it
/// notices — the tester used to carry its own copy of the rate, with a comment begging it to stay in
/// sync.
/// </summary>
public static class VisionZones
{
    public enum EZone
    {
        None,
        Focus,
        Peripheral,
        Rear,
    }

    /// <summary>
    /// Which zone a point is in, given the angle between the gaze and the point (0 dead ahead, 180
    /// straight behind) and how far it is.
    /// </summary>
    /// <param name="viewAngle">The whole vision cone, in degrees (both sides).</param>
    /// <param name="focusAngle">The instant-detection cone, in degrees (both sides).</param>
    /// <param name="rearRange">How far behind it senses a presence; 0 switches the rear zone off.</param>
    public static EZone Classify(float angle, float distance, float viewAngle, float focusAngle,
                                 float viewRange, float rearRange)
    {
        angle = Mathf.Abs(angle);

        if (angle <= viewAngle * 0.5f)
        {
            if (distance > viewRange) return EZone.None;
            return angle <= focusAngle * 0.5f ? EZone.Focus : EZone.Peripheral;
        }

        return rearRange > 0f && distance <= rearRange ? EZone.Rear : EZone.None;
    }

    /// <summary>
    /// How fast a contact fills the suspicion meter, per second: closer is faster, floored so one at
    /// the very edge still gets there. <paramref name="closeness"/> is 1 at the eye and 0 at the edge
    /// of the zone's own range. The rear zone multiplies this by its strength.
    /// </summary>
    public static float BuildRate(float closeness, float buildTime) =>
        Mathf.Lerp(0.35f, 2f, Mathf.Clamp01(closeness)) / Mathf.Max(0.05f, buildTime);

    /// <summary>
    /// Whether something in the corner of its eye (or felt behind it) is where it already believes
    /// the player is: the belief is younger than <paramref name="window"/> and the point falls inside
    /// its (grown) radius, on its floor. THE SENSES ADDING UP: a glimpse alone is "creo que vi algo";
    /// a glimpse right where it heard or saw the player a moment ago is the player.
    ///
    /// It is what a full suspicion meter already did during a chase (the meter is full while it sees
    /// you, so the periphery kept the sighting going), extended to a search and to the walk to a noise
    /// of the player's — the cases where the meter had drained and a glimpse started from nothing.
    /// </summary>
    /// <param name="beliefAge">Seconds since the belief's last evidence; infinity without one.</param>
    /// <param name="floorBand">Height difference beyond which the point is on another floor.</param>
    public static bool Corroborates(Vector3 point, Vector3 beliefPosition, float beliefRadius,
                                    float beliefAge, float window, float floorBand)
    {
        if (window <= 0f || !(beliefAge < window)) return false;

        Vector3 offset = point - beliefPosition;
        if (Mathf.Abs(offset.y) > floorBand) return false;

        offset.y = 0f;
        return offset.sqrMagnitude <= beliefRadius * beliefRadius;
    }

    /// <summary>1 at the eye, 0 at <paramref name="range"/> and beyond.</summary>
    public static float Closeness(float distance, float range) =>
        1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, range));

    /// <summary>
    /// One step of the suspicion meter.
    ///
    /// THE EYES are the only thing that may fill it: <paramref name="eyeRate"/> comes from a
    /// peripheral contact. EVERYTHING ELSE — a soft noise (plan §17.3), a presence behind it — adds
    /// <paramref name="senseRate"/> but only up to <paramref name="senseOnlyCap"/> (under 1): it can
    /// make the Nemesis suspicious, never make it see. The cap limits what those senses ADD, never
    /// what the eyes already put there: above it, the meter holds while they go on instead of
    /// draining (review 28/09). With no contact of any kind it drains at <paramref name="decayRate"/>.
    /// </summary>
    /// <returns>The meter after the step, 0..1. The caller promotes a full meter with eye contact to a
    /// sighting.</returns>
    public static float StepMeter(float meter, float deltaTime, bool eyeContact, float eyeRate,
                                  bool senseContact, float senseRate, float decayRate, float senseOnlyCap)
    {
        if (!eyeContact && !senseContact) return Mathf.Max(0f, meter - decayRate * deltaTime);

        float rate = (eyeContact ? eyeRate : 0f) + (senseContact ? senseRate : 0f);
        float after = Mathf.Min(1f, meter + rate * deltaTime);

        if (!eyeContact) after = Mathf.Min(after, Mathf.Max(meter, senseOnlyCap));
        return after;
    }
}
