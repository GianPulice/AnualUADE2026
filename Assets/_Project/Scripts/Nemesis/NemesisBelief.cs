using UnityEngine;

/// <summary>
/// What the Nemesis believes about the player, fused from both senses (plan §17).
///
/// WHY THIS EXISTS. The belief used to be a SELECTOR, not a model: NemesisStateManager.TryGetBelief
/// took whichever sensor fired last and threw the other away, and FieldOfListening kept only the
/// loudest noise of each sweep, whoever made it. Three things came out of that, all felt in
/// playtests as "the senses fight each other":
///   - Seen and heard at the same time, the answer depended on which sensor's timer ticked last.
///   - A decoy or a Director pulse WAS the player. The fire alarm, audible across the whole level,
///     kept the belief about the player "fresh" for thirty seconds and pointed it at the alarm.
///   - Every state patched around it in its own way (NemesisPursuit's "seen point, not the belief",
///     Searching's forced sight anchor), so the monster's behaviour changed with its state.
///
/// WHAT IT KNOWS — and it only knows. NemesisDecision decides the state; choosing what to go after
/// is plan §17.4.
///   - THE PLAYER: a position, a radius (how sure it is of the spot), the time of the last evidence,
///     and whether a sighting anchors it. Built only from evidence that IS the player: sightings and
///     the player's own noise emitter. Evidence that agrees narrows the radius — that is the senses
///     adding up — but a noise never below its own radius: hearing the same footsteps ten times is
///     not ten times as precise (Plan-Busqueda-Nemesis Fase 1). Evidence that cannot be the same
///     spot replaces it. The noise's position is the PERCEIVED one (FieldOfListening), never the
///     player's real transform. With no evidence the radius
///     grows at the player's top speed: it is "where they could have got to by now".
///   - LEADS: the latest noise that is NOT the player — a decoy, a Director pulse. Worth walking to;
///     never the player's position, and never what keeps the player belief young (D18, D19).
///   - GLIMPSES: where something was in the corner of its eye before the meter resolved it — or felt
///     behind it (FieldOfView's rear zone). Not the player's position: a glimpse that agrees with a
///     fresh belief is made a SIGHTING by the eyes themselves (FieldOfView, corroboration), and one
///     that does not is only somewhere to go and look.
///
/// SETUP: none. NemesisStateManager adds it next to itself, initializes it, and ticks it right after
/// sampling the sensors, before anything reads the belief that frame. Its numbers live on
/// SO_NemesisData (Creencia).
/// </summary>
[DisallowMultipleComponent]
public class NemesisBelief : MonoBehaviour
{
    /// <summary>Which kind of evidence moved the belief last. Runtime only, never serialised.</summary>
    public enum ESource
    {
        None,
        Sight,
        PlayerNoise,
    }

    /// <summary>
    /// How long after the last sighting the belief still counts as anchored by sight, in seconds.
    ///
    /// Sight and hearing sweep on independent 0.1 s timers. Seeing and hearing the player at once,
    /// the last evidence alternates between the two every sweep — and "fromSight" used to flip with
    /// it, which is what decides whether a search commits to sweeping the room. A little over two
    /// sweeps keeps it steady while the player is in view, and lets it go promptly once they are not.
    /// </summary>
    private const float SightAnchorWindow = 0.25f;

    /// <summary>Cap on the grown radius. Past this it means "anywhere", and a bigger number only
    /// makes the arithmetic worse.</summary>
    private const float MaxRadius = 100f;

    /// <summary>A lead within this many metres of the last one, from the same source and without a
    /// gap, is the same lead still sounding — not a new one.</summary>
    private const float LeadSameSpot = 1f;

    /// <summary>Seconds of silence after which the same source sounding again counts as a new lead.
    /// A few sweeps: a decoy that is still ringing never leaves a gap this long.</summary>
    private const float LeadGap = 1f;

    // Fallbacks for a missing SO, which ValidateReferences already reports as an error. They exist
    // so a broken prefab still believes something sensible instead of dividing by zero.
    private const float FallbackSightRadius = 0.5f;
    private const float FallbackNoiseBaseRadius = 1f;
    private const float FallbackNoisePerMetre = 0.15f;
    private const float FallbackWallFactor = 1.5f;
    private const float FallbackFloorFactor = 1.3f;
    private const float FallbackHidingSpotFactor = 2f;
    private const float FallbackGrowthSpeed = 4.5f;
    private const float FallbackFloorHeight = 2.5f;

    private NemesisStateManager stateManager;
    private FieldOfView eyes;
    private FieldOfListening ears;

    // ── The player ───────────────────────────────────────────────────────────

    private bool hasBelief;
    private Vector3 position;
    private float radiusAtStamp;
    private float stamp;
    private ESource source;
    private float lastSightEvidence = float.NegativeInfinity;
    private bool lastEvidenceMuffled;
    private bool lastEvidenceFromHidingSpot;

    // What has already been folded in, per sensor, so one sighting or one noise is applied once.
    private float consumedSight = float.NegativeInfinity;
    private float consumedPlayerNoise = float.NegativeInfinity;
    private float consumedLead = float.NegativeInfinity;

    // ── Leads and glimpses ───────────────────────────────────────────────────

    private bool hasLead;
    private FieldOfListening.HeardNoise lead;

    private bool hasGlimpse;
    private Vector3 glimpse;
    private float glimpseTime;

    /// <summary>Goes up every time evidence about the player is folded in. What a state compares to
    /// know that something new arrived, rather than reacting to a sensor merely being on.</summary>
    public int Sequence { get; private set; }

    /// <summary>Goes up when a NEW lead arrives — a different source, a different place, or the same
    /// one again after a silence. A decoy that keeps ringing does not move it.</summary>
    public int LeadSequence { get; private set; }

    public bool HasBelief => hasBelief;
    public Vector3 Position => position;
    public ESource Source => hasBelief ? source : ESource.None;

    /// <summary>A sighting is part of what the belief rests on right now. See
    /// <see cref="SightAnchorWindow"/>.</summary>
    public bool IsAnchoredBySight => hasBelief && stamp - lastSightEvidence <= SightAnchorWindow;

    /// <summary>
    /// The last evidence was a noise that came through a wall, a floor or out of a hiding spot:
    /// "over there somewhere" rather than "here". A search tolerates less silence after it than after
    /// a clear footstep, and more after a sighting (plan §18.5 B, SearchCooling.Quality). False after
    /// a sighting.
    /// </summary>
    public bool LastEvidenceMuffled => hasBelief && lastEvidenceMuffled;

    /// <summary>
    /// The last evidence was the player's noise from inside a hiding spot (D22): it marks the area,
    /// not the door. The search sweeps around it but does not walk to the point itself, which would
    /// be walking to the locker.
    /// </summary>
    public bool LastEvidenceFromHidingSpot => hasBelief && lastEvidenceFromHidingSpot;

    /// <summary>Seconds since the player was last sensed — seen, or heard through their own noise.
    /// Leads do not count. Infinity if never.</summary>
    public float Age => hasBelief ? Time.time - stamp : float.PositiveInfinity;

    /// <summary>How far from <see cref="Position"/> the player may be right now, in metres.</summary>
    public float Radius => hasBelief ? RadiusAt(Time.time) : float.PositiveInfinity;

    /// <summary>
    /// How precise the belief was when its last evidence came in, in metres: a sighting is a point,
    /// a footstep through a wall is a room. Unlike <see cref="Radius"/> it does not grow with time,
    /// which is what makes it usable for sizing a search: by the time the Nemesis has walked to the
    /// spot, the grown radius is at its maximum whatever the evidence was (plan §18.4).
    /// </summary>
    public float EvidenceRadius => hasBelief ? radiusAtStamp : float.PositiveInfinity;

    /// <summary>
    /// Where the eyes last had the player, and how long ago. Exposed here so the states can ask the
    /// belief rather than reach for FieldOfView (plan §10: "los estados leen la creencia").
    /// </summary>
    public bool TryGetLastSeen(out Vector3 seenAt, out float age)
    {
        seenAt = Vector3.zero;
        age = float.PositiveInfinity;
        if (eyes == null || !eyes.HasLastKnownPosition) return false;

        seenAt = eyes.LastKnownPosition;
        age = eyes.TimeSinceLastSighting;
        return true;
    }

    /// <summary>How fast and which way the player was seen moving, measured between sightings
    /// (FieldOfView.LastKnownVelocity). Zero when never seen moving.</summary>
    public Vector3 ObservedVelocity => eyes != null ? eyes.LastKnownVelocity : Vector3.zero;

    /// <summary>Seconds since the latest lead was heard. Infinity if there is none.</summary>
    public float LeadAge => hasLead ? Time.time - lead.HeardAt : float.PositiveInfinity;

    private SO_NemesisData Data => stateManager != null ? stateManager.NemesisData : null;

    private float SightRadius => Data != null ? Data.BeliefSightRadius : FallbackSightRadius;
    private float GrowthSpeed => Data != null ? Data.BeliefGrowthSpeed : FallbackGrowthSpeed;
    private float FloorHeight => Data != null ? Data.FloorHeightThreshold : FallbackFloorHeight;

    /// <summary>Called once by NemesisStateManager, after its references are resolved.</summary>
    public void Initialize(NemesisStateManager manager)
    {
        stateManager = manager;
        eyes = manager != null ? manager.FieldOfView : null;
        ears = manager != null ? manager.FieldOfListening : null;
    }

    /// <summary>
    /// Folds in whatever the sensors caught since the last tick. Ticked by NemesisStateManager right
    /// after it samples the sensors, so every reader in the frame — the ladder, the states, the
    /// pursuit — sees the same belief.
    /// </summary>
    public void Tick()
    {
        bool newSight = eyes != null && eyes.HasLastKnownPosition && eyes.LastSightingTime > consumedSight;

        FieldOfListening.HeardNoise noise = default;
        bool newNoise = ears != null && ears.TryGetLastPlayerNoise(out noise) &&
                        noise.HeardAt > consumedPlayerNoise;

        // Oldest first, so the newer of the two has the last word.
        if (newSight && newNoise && noise.HeardAt >= eyes.LastSightingTime)
        {
            ApplySight();
            ApplyPlayerNoise(noise);
        }
        else
        {
            if (newNoise) ApplyPlayerNoise(noise);
            if (newSight) ApplySight();
        }

        TickLead();
        TickGlimpse();
    }

    /// <summary>Forgets everything. The capture uses it — the checkpoint has moved the player, so
    /// whatever it believed now points at the one place they provably are not.</summary>
    public void Forget()
    {
        hasBelief = false;
        source = ESource.None;
        lastSightEvidence = float.NegativeInfinity;
        hasLead = false;
        hasGlimpse = false;

        // The consumed stamps are kept on purpose: the sensors forget too, and nothing they held
        // before the capture may be folded back in afterwards.
    }

    /// <summary>
    /// Where it believes the player is, and whether a sighting anchors it. The same contract as
    /// <see cref="NemesisStateManager.TryGetBelief(out Vector3, out bool)"/>, which forwards here.
    /// </summary>
    public bool TryGetBelief(out Vector3 believed, out bool fromSight)
    {
        believed = position;
        fromSight = IsAnchoredBySight;
        return hasBelief;
    }

    /// <summary>The latest lead: where, how old, and the decoy behind it (null for anything else).
    /// </summary>
    public bool TryGetLead(out Vector3 leadPosition, out float age, out DecoyNoiseSource decoy)
    {
        leadPosition = lead.Position;
        age = LeadAge;
        decoy = hasLead ? lead.Decoy : null;
        return hasLead;
    }

    /// <summary>Where the corner of its eye last caught something, and how long ago.</summary>
    public bool TryGetGlimpse(out Vector3 glimpsePosition, out float age)
    {
        glimpsePosition = glimpse;
        age = hasGlimpse ? Time.time - glimpseTime : float.PositiveInfinity;
        return hasGlimpse;
    }

    // ── Folding evidence in ──────────────────────────────────────────────────

    /// <summary>A sighting is a position. It replaces the belief outright, with a small radius.
    /// </summary>
    private void ApplySight()
    {
        float at = eyes.LastSightingTime;
        consumedSight = at;
        lastSightEvidence = at;
        lastEvidenceMuffled = false;
        lastEvidenceFromHidingSpot = false;
        Set(eyes.LastKnownPosition, at, SightRadius, ESource.Sight);
    }

    /// <summary>
    /// The player's own noise. Where it agrees with the belief the two are merged, weighted by how
    /// sure each one is, and the radius shrinks: THAT is the senses adding up. Where it cannot be
    /// the same spot, it replaces the belief — the player is where their noise is, and a midpoint
    /// between two places they cannot both be is a place they certainly are not.
    /// </summary>
    private void ApplyPlayerNoise(in FieldOfListening.HeardNoise noise)
    {
        consumedPlayerNoise = noise.HeardAt;
        float radius = NoiseRadius(noise);
        lastEvidenceMuffled = noise.ThroughWall || noise.ThroughFloor || noise.FromHidingSpot;
        lastEvidenceFromHidingSpot = noise.FromHidingSpot;

        if (!hasBelief || !IsSameSpot(noise.Position, noise.HeardAt, radius))
        {
            Set(noise.Position, noise.HeardAt, radius, ESource.PlayerNoise);
            return;
        }

        // Inverse-variance weighting: the tighter of the two pulls harder.
        float grown = RadiusAt(noise.HeardAt);
        float beliefWeight = 1f / (grown * grown);
        float noiseWeight = 1f / (radius * radius);
        float total = beliefWeight + noiseWeight;

        Vector3 merged = (position * beliefWeight + noise.Position * noiseWeight) / total;
        float mergedRadius = Mathf.Sqrt(1f / total);

        // No noise ever makes the belief more precise than the noise itself (D22, extended to every
        // noise by Plan-Busqueda-Nemesis Fase 1). The sensor re-hears the same footsteps or breath on
        // every 0.1 s sweep, and each one used to be folded as independent evidence, shrinking the
        // radius every time: a few seconds of running in earshot left "radio ~2 m, frescura 1.00"
        // and a search that walked straight to the locker the run ended at (WIR-057). Those sweeps
        // are one noise, not ten witnesses. Only a sighting pins the belief down finer than that.
        mergedRadius = Mathf.Max(mergedRadius, radius);

        Set(merged, Mathf.Max(stamp, noise.HeardAt), mergedRadius, ESource.PlayerNoise);
    }

    /// <summary>
    /// How precise a noise is: a base, plus a share of how far it came, widened by whatever it had to
    /// get through. A footstep beside the Nemesis is a spot; the same footstep through a wall from
    /// across a room is "over there somewhere".
    ///
    /// A noise from inside a hiding spot is widened again (plan D22): it marks the AREA, not the
    /// door. Without this, the first breath heard from a locker put a small radius on the locker
    /// itself, and a search sized off it paced back and forth in front of the door. Holding your
    /// breath hides you from a Nemesis standing there (D21), but only for as long as the air lasts
    /// (maxHoldSeconds): a search that spends longer than that at the door turns "it heard
    /// something" into a guaranteed find.
    /// </summary>
    private float NoiseRadius(in FieldOfListening.HeardNoise noise) =>
        NoiseRadiusFor(Data, noise.Distance, noise.ThroughWall, noise.ThroughFloor, noise.FromHidingSpot);

    /// <summary>
    /// The same radius, from the parts of a noise rather than the noise. Static and shared because the
    /// hearing sensor needs it BEFORE there is a noise to hand over: how far off the ear may place a
    /// noise is a fraction of exactly this radius (Plan-Busqueda-Nemesis Fase 1, D39), and two copies
    /// of the formula would drift apart.
    /// </summary>
    public static float NoiseRadiusFor(SO_NemesisData data, float distance, bool throughWall,
                                       bool throughFloor, bool fromHidingSpot)
    {
        float baseRadius = data != null ? data.BeliefNoiseBaseRadius : FallbackNoiseBaseRadius;
        float perMetre = data != null ? data.BeliefNoiseRadiusPerMetre : FallbackNoisePerMetre;

        float radius = baseRadius + perMetre * Mathf.Max(0f, distance);
        if (throughWall) radius *= data != null ? data.BeliefNoiseWallFactor : FallbackWallFactor;
        if (throughFloor) radius *= data != null ? data.BeliefNoiseFloorFactor : FallbackFloorFactor;
        if (fromHidingSpot) radius *= data != null ? data.BeliefNoiseHidingSpotFactor : FallbackHidingSpotFactor;
        return radius;
    }

    /// <summary>
    /// Whether new evidence can be the same spot as the belief: inside how far the player could have
    /// got, plus the evidence's own imprecision.
    ///
    /// A straight line, not the NavMesh, and deliberately: it can only UNDER-estimate the real
    /// distance, so the error is always towards merging — the old behaviour — and never towards
    /// jumping. The one case where that under-estimate is badly wrong, a noise on the storey
    /// above, is caught by the height test instead: two floors are never the same spot, and
    /// merging them would put the belief inside the slab.
    /// </summary>
    private bool IsSameSpot(Vector3 point, float at, float radius)
    {
        if (Mathf.Abs(point.y - position.y) > FloorHeight) return false;
        return Vector3.Distance(position, point) <= RadiusAt(at) + radius;
    }

    private float RadiusAt(float time) =>
        Mathf.Min(MaxRadius, radiusAtStamp + GrowthSpeed * Mathf.Max(0f, time - stamp));

    private void Set(Vector3 believed, float at, float radius, ESource by)
    {
        position = believed;
        stamp = at;
        radiusAtStamp = Mathf.Clamp(radius, 0.01f, MaxRadius);
        source = by;
        hasBelief = true;
        Sequence++;
    }

    // ── Leads and glimpses ───────────────────────────────────────────────────

    private void TickLead()
    {
        if (ears == null || !ears.TryGetLastLead(out FieldOfListening.HeardNoise heard)) return;
        if (heard.HeardAt <= consumedLead) return;

        consumedLead = heard.HeardAt;

        bool isNew = !hasLead ||
                     !ReferenceEquals(heard.Decoy, lead.Decoy) ||
                     (heard.Position - lead.Position).sqrMagnitude > LeadSameSpot * LeadSameSpot ||
                     heard.HeardAt - lead.HeardAt > LeadGap;

        lead = heard;
        hasLead = true;
        if (isNew) LeadSequence++;
    }

    /// <summary>The corner of its eye, or — far weaker, the same meter — a presence felt behind it
    /// ("siento que hay alguien atrás"): either is somewhere to turn and look.</summary>
    private void TickGlimpse()
    {
        if (eyes == null || eyes.HasVisualTarget) return;

        if (eyes.HasPeripheralContact) glimpse = eyes.PeripheralPoint;
        else if (eyes.HasRearContact) glimpse = eyes.RearPoint;
        else return;

        glimpseTime = Time.time;
        hasGlimpse = true;
    }
}
