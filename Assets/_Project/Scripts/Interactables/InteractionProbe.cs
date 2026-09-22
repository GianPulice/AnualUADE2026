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
/// 3. OCCLUSION is solid geometry only, and it is judged from that same start point. Interaction
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

    /// <summary>How much nearer a solid collider has to be before it counts as occluding, in
    /// metres. A trigger volume and the mesh it wraps land within millimetres of each other and
    /// must not fight over which one is "in front".</summary>
    private const float OcclusionEpsilon = 0.02f;

    private static readonly RaycastHit[] Buffer = new RaycastHit[MaxHits];

    /// <summary>
    /// Returns the interactable under the crosshair, or null. <paramref name="hit"/> is the hit
    /// that produced it and is only meaningful when the result is non-null.
    /// </summary>
    public static IInteractable Find(Camera camera, PlayerStateManager player,
                                     SO_InteractionManager config, out RaycastHit hit)
    {
        hit = default;

        if (camera == null || config == null) return null;
        if (!TryBuildCast(camera, player, config, out Ray cast, out float reach, out float along))
            return null;

        float radius = config.CastRadius;
        Transform self = player != null ? player.transform : null;

        // What is in front of the player always wins. Only when there is nothing there does the
        // probe look at what the player is standing beside or backed into (rule 4) — otherwise a
        // crate right behind the player, between them and the camera, would steal the prompt from
        // the one they are facing, which in the box puzzle is most of the time.
        IInteractable ahead = FindAhead(cast, radius, reach, config, self, out hit);
        if (ahead != null) return ahead;

        float lead = Mathf.Min(config.CloseRangeLead, along);
        if (lead <= 0f) return null;

        Ray sweep = new Ray(cast.origin - cast.direction * lead, cast.direction);
        return FindBesidePlayer(sweep, radius, lead, config, self, out hit);
    }

    /// <summary>
    /// The crosshair target from the player onwards: rules 1 to 3. <paramref name="cast"/> starts
    /// at the player.
    /// </summary>
    private static IInteractable FindAhead(Ray cast, float radius, float reach,
                                           SO_InteractionManager config, Transform self,
                                           out RaycastHit hit)
    {
        hit = default;

        // Pass A — interaction volumes. QueryTriggerInteraction.Collide on purpose: an interaction
        // box has no business being solid, and the ones that are solid still show up here.
        IInteractable target = NearestInteractable(cast, radius, reach, config.InteractableLayers,
                                                   self, out RaycastHit targetHit);

        // Pass B — solid geometry, the only thing allowed to occlude.
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

            hit = blockerHit;
            return fromBlocker;
        }

        // Something solid stands in front of the volume — unless it IS the interactable. A door's
        // leaf mesh sits on Default and is unavoidably a hair in front of the trigger box wrapped
        // around it; letting an object occlude itself would make every such door unusable.
        if (blocked && blockerHit.distance < targetHit.distance - OcclusionEpsilon)
        {
            IInteractable front = Resolve(blockerHit.collider);
            if (!ReferenceEquals(front, target))
            {
                // What is in front is itself an interactable on a solid layer (a crate, a door
                // leaf): that, not the volume behind it, is what the crosshair is on.
                if (front == null) return null;

                hit = blockerHit;
                return front;
            }
        }

        hit = targetHit;
        return target;
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
