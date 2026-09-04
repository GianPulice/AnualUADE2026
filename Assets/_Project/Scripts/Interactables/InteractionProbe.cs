using UnityEngine;

/// <summary>
/// The one place that answers "what is the player pointing the crosshair at right now".
///
/// Shared by <see cref="InteractionManager"/> (runtime) and <see cref="InteractionRangeGizmo"/>
/// (Scene view). They used to hold two copies of the same cast that had to be kept in step by
/// hand, which is exactly how a gizmo starts lying about the range it is supposed to measure.
///
/// Three rules it enforces, all of which the single combined cast from the camera got wrong on a
/// third person rig:
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
///    seals its own doorway — while walls and props stay solid and still block.
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
        if (!TryBuildCast(camera, player, config, out Ray cast, out float reach)) return null;

        float radius = config.CastRadius;

        // Pass A — interaction volumes. QueryTriggerInteraction.Collide on purpose: an interaction
        // box has no business being solid, and the ones that are solid still show up here.
        IInteractable target = NearestInteractable(cast, radius, reach, config.InteractableLayers,
                                                   out RaycastHit targetHit);

        // Pass B — solid geometry, the only thing allowed to occlude.
        bool blocked = Physics.SphereCast(cast, radius, out RaycastHit blockerHit, reach,
                                          config.BlockingLayers, QueryTriggerInteraction.Ignore);

        // A zero distance means the cast started inside that collider, which physics reports with
        // a meaningless point and normal. It happens when the player is clipped a few centimetres
        // into a wall, and treating it as occlusion would make interaction cut out exactly there.
        if (blocked && blockerHit.distance <= 0f) blocked = false;

        if (target == null)
        {
            // Legacy layout: the interactable's own SOLID collider lives on a blocking layer — a
            // door leaf on Default, a crate on Props. Resolving through the blocker keeps every
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
        if (blocked &&
            blockerHit.distance < targetHit.distance - OcclusionEpsilon &&
            !ReferenceEquals(Resolve(blockerHit.collider), target))
        {
            return null;
        }

        hit = targetHit;
        return target;
    }

    /// <summary>
    /// Builds the crosshair ray and moves its start to the player. Public so the gizmo can draw
    /// the exact segment the manager casts instead of an approximation of it.
    /// </summary>
    public static bool TryBuildCast(Camera camera, PlayerStateManager player,
                                    SO_InteractionManager config, out Ray cast, out float reach)
    {
        cast = default;
        reach = 0f;

        if (camera == null || config == null) return false;

        Vector2 vp = config.CrosshairViewportPoint;
        Ray crosshairRay = camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));

        Vector3 anchor = ReachAnchor(camera, player, config);

        // Where the player sits along the crosshair line. Starting there is what turns the reach
        // into "arm's length from the character" instead of "distance from the lens", and it also
        // drops everything between the camera and the player out of the query for free: their own
        // body, and the wall the Deoccluder pinched the camera into when they backed up to it.
        float along = Mathf.Max(0f, Vector3.Dot(anchor - crosshairRay.origin, crosshairRay.direction));

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
                                                     LayerMask layers, out RaycastHit nearest)
    {
        nearest = default;

        int count = Physics.SphereCastNonAlloc(cast, radius, Buffer, reach, layers,
                                               QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = Buffer[i];
            if (candidate.distance >= bestDistance) continue;

            IInteractable resolved = Resolve(candidate.collider);
            if (resolved == null) continue;

            best = resolved;
            bestDistance = candidate.distance;
            nearest = candidate;
        }

        return best;
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
