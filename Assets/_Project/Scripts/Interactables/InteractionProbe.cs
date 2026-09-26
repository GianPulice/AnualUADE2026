using UnityEngine;

/// <summary>
/// The one place that answers "what is the player pointing the crosshair at right now".
///
/// Shared by <see cref="InteractionManager"/> (runtime) and <see cref="InteractionRangeGizmo"/>
/// (Scene view). They used to hold two copies of the same cast that had to be kept in step by
/// hand, which is exactly how a gizmo starts lying about the range it is supposed to measure.
///
/// Four rules it enforces, the first three of which the single combined cast from the camera got
/// wrong on a third person rig:
///
/// 1. AIM comes from the crosshair. The cast is built with <c>ViewportPointToRay</c> through the
///    reticle's own viewport point, so it survives lens shift, a physical camera, an ultrawide
///    aspect, and a crosshair that is later moved off centre.
///
/// 2. REACH is measured from the PLAYER. The camera orbits ~3.4 m behind and above the character;
///    a distance spent from the lens is nearly all empty air before it reaches the player's hands.
///    The cast therefore starts at the point of the crosshair ray closest to the player.
///
/// 3. OCCLUSION is solid geometry only, judged along a THIN line of sight from that same start
///    point. The thick ray only picks the candidate; whether something hides it is a plain raycast
///    to the point the thick ray touched. Judged with the thick ray itself, the sphere grazed the
///    surface an item rests on before reaching the item, and a key on a shelf seen at a shallow
///    angle counted as behind the shelf. Two kinds of solid hit never hide the candidate: its own
///    parts (a door leaf a hair in front of the door's volume) and its SUPPORT — what it rests on,
///    met within <see cref="SupportGrace"/> of the aimed point, or what it lies wholly inside (the
///    convex hull a MeshCollider wraps around a toilet, and the key sitting in it). Interaction
///    volumes may be triggers — which is what lets a door's interaction box stop being a wall that
///    seals its own doorway — while walls stay solid and still block. Which layers count as solid
///    is <see cref="SO_InteractionManager.BlockingLayers"/>; Props is left out of it, so set
///    dressing (barrels, cabinets, pallets) never hides a pickup behind or on top of it.
///
/// 4. CLOSE RANGE works. Starting exactly at the player breaks down when the player is pressed
///    against something at an angle: the start lands INSIDE the crate or door, or already past the
///    one beside them, and a single sweep never reports a collider it starts inside — the player
///    had to swing the camera around until the start happened to fall outside it. So the solid pass
///    uses the multi-hit query, which does report it, and when nothing is found ahead of the player
///    the probe falls back to the last <see cref="SO_InteractionManager.CloseRangeLead"/> metres
///    before them.
/// </summary>
public static class InteractionProbe
{
    /// <summary>Interaction volumes stacked on one line of sight. Twelve is far past anything the
    /// scene actually layers; the buffer only exists so the query never allocates.</summary>
    private const int MaxHits = 12;

    /// <summary>How close to the aimed point, in metres, a solid hit on the line of sight still
    /// counts as the candidate's support rather than as something in front of it: the surface the
    /// item lies on, grazed right at the item. Well under a wall's thickness, so a wall between the
    /// player and a panel mounted on its far side still hides the panel.</summary>
    private const float SupportGrace = 0.03f;

    /// <summary>Squared distance under which ClosestPoint counts as "the point is inside".</summary>
    private const float EnclosedEpsilonSqr = 1e-8f;

    private static readonly RaycastHit[] Buffer = new RaycastHit[MaxHits];

    /// <summary>The candidate's eight corners, filled once per line-of-sight test.</summary>
    private static readonly Vector3[] Corners = new Vector3[8];

    /// <summary>
    /// Why <see cref="Find(Camera, PlayerStateManager, SO_InteractionManager, out RaycastHit, out ProbeReport)"/>
    /// answered what it did. Debugging only: <see cref="InteractionRangeGizmo"/> draws it, and the
    /// game calls the overload without it.
    /// </summary>
    public struct ProbeReport
    {
        /// <summary>What the thick ray found ahead of the player before the line of sight was
        /// judged. Null when it found nothing.</summary>
        public IInteractable Candidate;
        public RaycastHit CandidateHit;

        /// <summary>The thin line of sight that was tested (rule 3): from the start of the cast to
        /// the point the thick ray touched on the candidate.</summary>
        public bool HasSightLine;
        public Vector3 SightFrom;
        public Vector3 SightTo;

        /// <summary>The candidate was dropped: <see cref="OccluderHit"/> stands in front of it.</summary>
        public bool Occluded;

        /// <summary>A solid interactable in front of the candidate, <see cref="OccluderHit"/>,
        /// was picked instead of it.</summary>
        public bool ReplacedByFront;
        public RaycastHit OccluderHit;

        /// <summary>The nearest solid hit the line of sight skipped as the candidate's support.</summary>
        public bool SupportIgnored;
        public RaycastHit SupportHit;

        /// <summary>The answer came from the close-range fallback (rule 4), not from ahead.</summary>
        public bool FromCloseRange;
    }

    /// <summary>
    /// Returns the interactable under the crosshair, or null. <paramref name="hit"/> is the hit
    /// that produced it and is only meaningful when the result is non-null.
    /// </summary>
    public static IInteractable Find(Camera camera, PlayerStateManager player,
                                     SO_InteractionManager config, out RaycastHit hit)
    {
        return Find(camera, player, config, out hit, out _);
    }

    /// <summary>
    /// Same answer as the overload above, plus <paramref name="report"/>: what the thick ray
    /// found, the line of sight it was judged along, and what blocked it or was skipped as its
    /// support.
    /// </summary>
    public static IInteractable Find(Camera camera, PlayerStateManager player,
                                     SO_InteractionManager config, out RaycastHit hit,
                                     out ProbeReport report)
    {
        hit = default;
        report = default;

        if (camera == null || config == null) return null;
        if (!TryBuildCast(camera, player, config, out Ray cast, out float reach, out float along))
            return null;

        float radius = config.CastRadius;
        Transform self = player != null ? player.transform : null;

        // What is in front of the player always wins. Only when there is nothing there does the
        // probe look at what the player is standing beside or backed into (rule 4) — otherwise a
        // crate right behind the player, between them and the camera, would steal the prompt from
        // the one they are facing, which in the box puzzle is most of the time.
        IInteractable ahead = FindAhead(cast, radius, reach, config, self, out hit, ref report);
        if (ahead != null) return ahead;

        float lead = Mathf.Min(config.CloseRangeLead, along);
        if (lead <= 0f) return null;

        Ray sweep = new Ray(cast.origin - cast.direction * lead, cast.direction);
        IInteractable beside = FindBesidePlayer(sweep, radius, lead, config, self, out hit);
        report.FromCloseRange = beside != null;
        return beside;
    }

    /// <summary>
    /// The crosshair target from the player onwards: rules 1 to 3. <paramref name="cast"/> starts
    /// at the player.
    /// </summary>
    private static IInteractable FindAhead(Ray cast, float radius, float reach,
                                           SO_InteractionManager config, Transform self,
                                           out RaycastHit hit, ref ProbeReport report)
    {
        hit = default;

        // Pass A — interaction volumes, with the thick ray so aiming stays forgiving.
        // QueryTriggerInteraction.Collide on purpose: an interaction box has no business being
        // solid, and the ones that are solid still show up here.
        IInteractable target = NearestInteractable(cast, radius, reach, config.InteractableLayers,
                                                   self, out RaycastHit targetHit);

        // Pass B — solid geometry, thick. It no longer judges occlusion (the thin line of sight
        // below does); it is what the legacy layout resolves through, and what catches the solid
        // interactable the player is pressed into (rule 4).
        bool blocked = NearestBlocker(cast, radius, reach, config.BlockingLayers, self,
                                      out RaycastHit blockerHit);

        if (target == null)
        {
            // Legacy layout: the interactable's own SOLID collider lives on a blocking layer — a
            // door leaf or a push box on Default. Resolving through the blocker keeps every
            // prop that works today working, without re-layering the scene.
            if (!blocked) return null;

            IInteractable fromBlocker = Resolve(blockerHit.collider);
            if (fromBlocker == null) return null;

            report.Candidate = fromBlocker;
            report.CandidateHit = blockerHit;
            hit = blockerHit;
            return fromBlocker;
        }

        report.Candidate = target;
        report.CandidateHit = targetHit;

        // Pressed into a crate or a door leaf: the cast starts inside it, and it stands in front
        // of whatever the thick ray found past it. The thin line of sight cannot see it — a raycast
        // never reports the collider it starts in — so it is taken from pass B, whose distance 0
        // is only ever kept for an interactable (see NearestBlocker).
        if (blocked && blockerHit.distance <= 0f && targetHit.distance > SupportGrace)
        {
            IInteractable pressed = Resolve(blockerHit.collider);
            if (pressed != null && !ReferenceEquals(pressed, target))
            {
                report.OccluderHit = blockerHit;
                report.ReplacedByFront = true;
                hit = blockerHit;
                return pressed;
            }
        }

        // The line of sight to the candidate, with a thin ray (rule 3).
        if (!FindOccluder(cast.origin, target, targetHit, config.BlockingLayers, self,
                          out RaycastHit occluderHit, ref report))
        {
            hit = targetHit;
            return target;
        }

        report.OccluderHit = occluderHit;

        // What is in front is itself an interactable on a solid layer (a crate, a door leaf):
        // that, not the candidate behind it, is what the crosshair is on.
        IInteractable front = Resolve(occluderHit.collider);
        if (front == null)
        {
            report.Occluded = true;
            return null;
        }

        report.ReplacedByFront = true;
        hit = occluderHit;
        return front;
    }

    /// <summary>
    /// The nearest solid collider on the thin line from <paramref name="from"/> to the point the
    /// thick ray touched on the candidate, or false when that line is clear. Skips the candidate's
    /// own solid parts and its support (see <see cref="IsSupport"/>).
    /// </summary>
    private static bool FindOccluder(Vector3 from, IInteractable target, RaycastHit targetHit,
                                     LayerMask layers, Transform self, out RaycastHit occluder,
                                     ref ProbeReport report)
    {
        occluder = default;

        // Zero is "the thick ray started inside or touching the candidate": it is within the
        // cast's radius of the start, and there is no room for anything in between.
        if (targetHit.distance <= 0f) return false;

        Vector3 toTarget = targetHit.point - from;
        float length = toTarget.magnitude;
        if (length <= SupportGrace) return false;

        report.HasSightLine = true;
        report.SightFrom = from;
        report.SightTo = targetHit.point;

        int count = Physics.RaycastNonAlloc(from, toTarget / length, Buffer, length, layers,
                                            QueryTriggerInteraction.Ignore);
        if (count == 0) return false;

        FillCorners(targetHit.collider);

        bool found = false;
        float bestDistance = float.PositiveInfinity;
        float nearestSupport = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = Buffer[i];
            if (IsSelf(candidate.collider, self)) continue;

            // Its own solid parts: a door's leaf sits on Default a hair in front of the trigger box
            // wrapped around it, and an object hiding itself would make every such door unusable.
            if (ReferenceEquals(Resolve(candidate.collider), target)) continue;

            if (IsSupport(candidate, length))
            {
                if (candidate.distance < nearestSupport)
                {
                    nearestSupport = candidate.distance;
                    report.SupportIgnored = true;
                    report.SupportHit = candidate;
                }
                continue;
            }

            if (candidate.distance >= bestDistance) continue;

            found = true;
            bestDistance = candidate.distance;
            occluder = candidate;
        }

        return found;
    }

    /// <summary>
    /// Whether a solid hit on the line of sight is what the candidate rests on or lies in, rather
    /// than something standing in front of it:
    /// - met within <see cref="SupportGrace"/> of the aimed point: the surface the item lies on,
    ///   grazed right at the item;
    /// - or a collider that holds the candidate WHOLE (<see cref="Corners"/> all inside it): the
    ///   convex hull a MeshCollider wraps around a toilet, and the key sitting in it.
    /// A wall between the player and a panel on its far side is neither: it is met a whole wall's
    /// thickness before the panel, and a panel mounted into a wall still has its front out of it.
    /// Touching is deliberately not enough — that would let every note and switch on a wall be
    /// used through the wall from the next room.
    /// </summary>
    private static bool IsSupport(RaycastHit hit, float sightLength)
    {
        if (sightLength - hit.distance <= SupportGrace) return true;
        return EnclosesCorners(hit.collider);
    }

    /// <summary>
    /// Whether all of <see cref="Corners"/> lie inside <paramref name="collider"/>. Only the shapes
    /// ClosestPoint supports are asked — boxes, spheres, capsules and convex meshes — and, being
    /// convex, holding the eight corners means holding the whole candidate. Anything else answers
    /// no: a concave mesh (most walls here) has no inside to speak of.
    /// </summary>
    private static bool EnclosesCorners(Collider collider)
    {
        bool supported = collider is BoxCollider || collider is SphereCollider ||
                         collider is CapsuleCollider ||
                         (collider is MeshCollider mesh && mesh.convex);
        if (!supported) return false;

        for (int i = 0; i < Corners.Length; i++)
        {
            if ((collider.ClosestPoint(Corners[i]) - Corners[i]).sqrMagnitude > EnclosedEpsilonSqr)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The candidate collider's eight corners, into <see cref="Corners"/>: its own box when it is
    /// a BoxCollider — exact for a rotated key — and its world bounds otherwise.
    /// </summary>
    private static void FillCorners(Collider collider)
    {
        if (collider is BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 half = box.size * 0.5f;
            for (int i = 0; i < Corners.Length; i++)
            {
                Vector3 local = box.center + new Vector3((i & 1) == 0 ? -half.x : half.x,
                                                         (i & 2) == 0 ? -half.y : half.y,
                                                         (i & 4) == 0 ? -half.z : half.z);
                Corners[i] = t.TransformPoint(local);
            }
            return;
        }

        Bounds b = collider.bounds;
        for (int i = 0; i < Corners.Length; i++)
        {
            Corners[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                     (i & 2) == 0 ? b.min.y : b.max.y,
                                     (i & 4) == 0 ? b.min.z : b.max.z);
        }
    }

    /// <summary>
    /// Rule 4: the interactable the player is pressed against or standing beside, found by
    /// sweeping the last <paramref name="lead"/> metres of the crosshair line BEFORE the player.
    /// Of those, the one nearest the player wins, so a prop further back towards the camera never
    /// beats the one the player is actually touching. Plain geometry here does not occlude, same
    /// as it never did in front of the player's start (rule 3).
    /// </summary>
    private static IInteractable FindBesidePlayer(Ray sweep, float radius, float lead,
                                                  SO_InteractionManager config, Transform self,
                                                  out RaycastHit hit)
    {
        hit = default;
        IInteractable best = null;
        float bestDistance = float.NegativeInfinity;

        // Triggers are only wanted on the interactable layers; on the solid ones they are audio
        // zones, push-box side anchors and the like.
        CollectNearestToPlayer(sweep, radius, lead, config.InteractableLayers,
                               QueryTriggerInteraction.Collide, self, ref best, ref bestDistance, ref hit);
        CollectNearestToPlayer(sweep, radius, lead, config.BlockingLayers,
                               QueryTriggerInteraction.Ignore, self, ref best, ref bestDistance, ref hit);

        return best;
    }

    private static void CollectNearestToPlayer(Ray sweep, float radius, float lead, LayerMask layers,
                                               QueryTriggerInteraction triggers, Transform self,
                                               ref IInteractable best, ref float bestDistance,
                                               ref RaycastHit bestHit)
    {
        int count = Physics.SphereCastNonAlloc(sweep, radius, Buffer, lead, layers, triggers);

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = Buffer[i];

            // Zero is "the sweep started inside it": a metre back towards the camera, nowhere near
            // the player, and with no usable hit point.
            if (candidate.distance <= 0f || candidate.distance <= bestDistance) continue;
            if (IsSelf(candidate.collider, self)) continue;

            IInteractable resolved = Resolve(candidate.collider);
            if (resolved == null) continue;

            best = resolved;
            bestDistance = candidate.distance;
            bestHit = candidate;
        }
    }

    /// <summary>
    /// Builds the crosshair ray and moves its start to the player. Public so the gizmo can draw
    /// the exact segment the manager casts instead of an approximation of it.
    /// </summary>
    public static bool TryBuildCast(Camera camera, PlayerStateManager player,
                                    SO_InteractionManager config, out Ray cast, out float reach)
    {
        return TryBuildCast(camera, player, config, out cast, out reach, out _);
    }

    /// <param name="along">How far along the crosshair ray, from the camera, the player sits —
    /// i.e. how much room there is to start the sweep earlier without starting behind the lens.</param>
    private static bool TryBuildCast(Camera camera, PlayerStateManager player,
                                     SO_InteractionManager config, out Ray cast, out float reach,
                                     out float along)
    {
        cast = default;
        reach = 0f;
        along = 0f;

        if (camera == null || config == null) return false;

        Vector2 vp = config.CrosshairViewportPoint;
        Ray crosshairRay = camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));

        Vector3 anchor = ReachAnchor(camera, player, config);

        // Where the player sits along the crosshair line. Starting there is what turns the reach
        // into "arm's length from the character" instead of "distance from the lens", and it also
        // drops everything between the camera and the player out of the query for free: their own
        // body, and the wall the Deoccluder pinched the camera into when they backed up to it.
        along = Mathf.Max(0f, Vector3.Dot(anchor - crosshairRay.origin, crosshairRay.direction));

        cast = new Ray(crosshairRay.origin + crosshairRay.direction * along, crosshairRay.direction);
        reach = config.InteractionDistance;

        return reach > 0f;
    }

    /// <summary>
    /// Chest height on the player. Read off the CapsuleCollider's bounds rather than a constant so
    /// it follows the crouch on its own — the crouch state shrinks that capsule, and a fixed
    /// height would keep reaching from where the player's head used to be.
    /// </summary>
    public static Vector3 ReachAnchor(Camera camera, PlayerStateManager player,
                                      SO_InteractionManager config)
    {
        if (player == null)
            return camera != null ? camera.transform.position : Vector3.zero;

        CapsuleCollider capsule = player.CapsuleColl;
        if (capsule != null) return capsule.bounds.center;

        return player.transform.position + Vector3.up * config.FallbackOriginHeight;
    }

    /// <summary>
    /// Nearest hit on the interactable layers that actually resolves to an IInteractable. Scanning
    /// all of them and not just the first: a collider on the layer with no component behind it —
    /// an audio trigger, a bare child mesh — would otherwise swallow the real target sitting a few
    /// centimetres further along.
    /// </summary>
    private static IInteractable NearestInteractable(Ray cast, float radius, float reach,
                                                     LayerMask layers, Transform self,
                                                     out RaycastHit nearest)
    {
        nearest = default;

        // The multi-hit query and not SphereCast: it reports a collider the sweep STARTS inside
        // (at distance 0), where the single-hit one silently skips it.
        int count = Physics.SphereCastNonAlloc(cast, radius, Buffer, reach, layers,
                                               QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = Buffer[i];
            if (candidate.distance >= bestDistance) continue;
            if (IsSelf(candidate.collider, self)) continue;

            IInteractable resolved = Resolve(candidate.collider);
            if (resolved == null) continue;

            best = resolved;
            bestDistance = candidate.distance;
            nearest = candidate;
        }

        return best;
    }

    /// <summary>
    /// Nearest solid hit that counts, or false.
    ///
    /// The multi-hit query and not SphereCast, because of what happens when the player stands
    /// pressed against a solid interactable (a crate, a door leaf): the cast starts INSIDE its
    /// collider. SphereCast skips such a collider without a word, which is the "have to swing the
    /// camera around before E works" bug; the multi-hit query reports it at distance 0.
    ///
    /// Distance 0 is then kept only for an interactable. Anything else the cast starts inside is
    /// skipped: physics gives it a meaningless point and normal, and a player clipped a few
    /// centimetres into a wall must not lose interaction exactly there.
    /// </summary>
    private static bool NearestBlocker(Ray cast, float radius, float reach, LayerMask layers,
                                       Transform self, out RaycastHit nearest)
    {
        nearest = default;

        int count = Physics.SphereCastNonAlloc(cast, radius, Buffer, reach, layers,
                                               QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = Buffer[i];
            if (candidate.distance >= bestDistance) continue;
            if (IsSelf(candidate.collider, self)) continue;

            if (candidate.distance <= 0f && Resolve(candidate.collider) == null) continue;

            found = true;
            bestDistance = candidate.distance;
            nearest = candidate;
        }

        return found;
    }

    /// <summary>The player's own colliders never block and are never a target.</summary>
    private static bool IsSelf(Collider collider, Transform self)
    {
        return self != null && collider != null && collider.transform.IsChildOf(self);
    }

    /// <summary>
    /// The component behind a collider. GetComponentInParent and not GetComponent because the
    /// collider is routinely a child of the prefab that owns the behaviour — a door's box hangs
    /// off the leaf, not off the root that carries DoorInteractable.
    /// </summary>
    private static IInteractable Resolve(Collider collider)
    {
        if (collider == null) return null;
        return collider.GetComponentInParent<IInteractable>();
    }
}
