using UnityEngine;

/// <summary>
/// Makes a wall of living darkness react to being looked at. One job: measure how much the player
/// is looking at it, smooth that into an agitation value, and hand it to the darkness's renderers.
/// Everything visible — the churn, the swell, the reach — is the material's
/// (<c>WIRED/Environment/Living Darkness</c>, <c>Art/Materials/Environment/Darkness/</c>).
///
/// WHAT IT WRITES, per renderer through a MaterialPropertyBlock (the material asset stays shared):
///   _Agitation  — 0 calm .. 1 fully agitated, eased in and out.
///   _LookTarget — where the darkness reaches toward: the player's chest (<see cref="reachHeight"/>
///                 above the pivot), or the camera while no player is registered. w = 1 once set.
///   _GazePoint  — the point on the darkness the gaze lands on (or the nearest part of it to where
///                 the gaze goes), where the reach grows from. It glides to a new spot over
///                 <see cref="gazeFollowSeconds"/> instead of jumping. w = 1 once set.
///
/// HOW "LOOKING AT IT" IS MEASURED. From Camera.main — the CinemachineBrain camera, the one the
/// crosshair is drawn over (see InteractionManager) — not from the player's body. The view ray is
/// carried as far as the darkness's nearest point and then snapped onto its volume; the angle
/// between the view direction and that snapped point is ~0 when the crosshair is on the darkness
/// and grows as the view leaves it. Between <see cref="fullGazeAngle"/> and
/// <see cref="noGazeAngle"/> the reaction fades. Distance is measured from the player's chest when
/// there is a player (the Vision Fog is centred on the body, so that is what decides whether the
/// darkness can be seen at all), from the camera otherwise. One occlusion ray, against
/// <see cref="obstacleMask"/>, to the snapped point.
///
/// WHAT IT DOES NOT DO.
///   - Block anything. A plain non-trigger BoxCollider on the same object does that, on the Wall
///     layer (see docs/CLAUDE.md, "Living darkness barrier"). The reach is vertex displacement only:
///     the collider never moves.
///   - Sound. <see cref="Agitation"/> is public for a follow-up that wants to drive an AudioSource
///     from it; it does not belong here.
///   - Decide when the barrier exists. Switch the GameObject on from whatever closes the way back.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Environment/Darkness Gaze")]
public class DarknessGaze : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Renderers using the Living Darkness material. Left empty, every Renderer on this object " +
             "and its children is used.")]
    [SerializeField] private Renderer[] targets;

    [Tooltip("Volume the gaze is measured against: the blocking BoxCollider, normally. Left empty, the " +
             "first Collider on this object or its children; without one (or with a non-convex " +
             "MeshCollider, which Collider.ClosestPoint does not support) the renderers' bounds.")]
    [SerializeField] private Collider gazeVolume;

    [Tooltip("Camera that counts as the player's eyes. Left empty it uses Camera.main, the " +
             "CinemachineBrain camera that renders the frame.")]
    [SerializeField] private Camera viewCamera;

    [Header("Gaze")]
    [Tooltip("Degrees between the view direction and the darkness inside which it counts as fully " +
             "looked at. 0 = only with the crosshair on it.")]
    [SerializeField, Range(0f, 90f)] private float fullGazeAngle = 12f;

    [Tooltip("Degrees past which it does not count as looked at at all. Between Full Gaze Angle and " +
             "this the reaction fades out.")]
    [SerializeField, Range(0f, 180f)] private float noGazeAngle = 40f;

    [Tooltip("Metres from the player's chest to the nearest part of the darkness within which the " +
             "reaction is full.")]
    [SerializeField, Min(0f)] private float fullReactionDistance = 4f;

    [Tooltip("Metres past which it does not react at all. In the Dark fog preset nothing past ~7 m from " +
             "the player can be seen anyway.")]
    [SerializeField, Min(0f)] private float maxDistance = 9f;

    [Tooltip("Layers that hide the darkness from the camera. Default: Default, Ground, Wall and Props, " +
             "the four solid layers of docs/CLAUDE.md > Layers. The darkness's own collider never " +
             "counts as hiding it.")]
    [SerializeField] private LayerMask obstacleMask = (1 << 0) | (1 << 3) | (1 << 11) | (1 << 12);

    [Header("Response")]
    [Tooltip("Seconds from calm to fully agitated while it is being looked at.")]
    [SerializeField, Min(0.01f)] private float riseSeconds = 0.6f;

    [Tooltip("Seconds from fully agitated back to calm once the player looks away. Longer than the " +
             "rise: it lunges fast and settles slowly.")]
    [SerializeField, Min(0.01f)] private float settleSeconds = 2.5f;

    [Tooltip("Seconds (time constant) the reach takes to slide to a new spot when the gaze moves " +
             "across the darkness. 0 = it jumps.")]
    [SerializeField, Min(0f)] private float gazeFollowSeconds = 0.35f;

    [Header("Reach")]
    [Tooltip("Metres above the player's pivot the darkness reaches toward. Without a registered " +
             "player it reaches for the camera.")]
    [SerializeField] private float reachHeight = 1.3f;

    private static readonly int AgitationId = Shader.PropertyToID("_Agitation");
    private static readonly int LookTargetId = Shader.PropertyToID("_LookTarget");
    private static readonly int GazePointId = Shader.PropertyToID("_GazePoint");

    private MaterialPropertyBlock block;
    private bool useColliderClosestPoint;

    // Linear 0..1; the renderers get it eased (Agitation).
    private float agitation;
    private float lastPushed = -1f;

    private Vector3 lookTarget;
    private bool hasLookTarget;
    private Vector3 gazePoint;
    private bool hasGazePoint;

    /// <summary>
    /// How agitated the darkness is right now, 0..1, eased — exactly what the material receives.
    /// For a follow-up (an AudioSource, a heartbeat) that wants to react with it.
    /// </summary>
    public float Agitation => Mathf.SmoothStep(0f, 1f, agitation);

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (targets == null || targets.Length == 0)
            targets = GetComponentsInChildren<Renderer>(true);

        if (gazeVolume == null)
            gazeVolume = GetComponentInChildren<Collider>(true);

        // Collider.ClosestPoint only works on boxes, spheres, capsules and convex meshes. The built-in
        // Plane ships with a non-convex MeshCollider: fall back to bounds rather than measure garbage.
        useColliderClosestPoint = gazeVolume != null && !(gazeVolume is MeshCollider mesh && !mesh.convex);
        if (gazeVolume != null && !useColliderClosestPoint)
        {
            Debug.LogWarning($"[{nameof(DarknessGaze)}] '{name}': the gaze volume is a non-convex " +
                             "MeshCollider, so the gaze is measured against its bounding box. Use a " +
                             "BoxCollider (it is also what should block the player).", this);
        }

        if (targets.Length == 0)
        {
            Debug.LogError($"[{nameof(DarknessGaze)}] '{name}' has no Renderer to drive. The component " +
                           "has been disabled.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        // Leave it calm, not frozen mid-reach, if the barrier is switched off while agitated.
        agitation = 0f;
        Push();
    }

    private void OnValidate()
    {
        noGazeAngle = Mathf.Max(noGazeAngle, fullGazeAngle);
        maxDistance = Mathf.Max(maxDistance, fullReactionDistance);
    }

    private void LateUpdate()
    {
        float goal = MeasureGaze(out Vector3 aim);
        if (goal > 0f) FollowGaze(aim);

        float seconds = goal > agitation ? riseSeconds : settleSeconds;
        agitation = Mathf.MoveTowards(agitation, goal, Time.deltaTime / seconds);

        Push();
    }

    // ── Gaze ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 0..1: how much the player is looking at the darkness right now, before smoothing.
    /// <paramref name="aim"/> is where on the darkness the gaze lands; only meaningful above 0.
    /// </summary>
    private float MeasureGaze(out Vector3 aim)
    {
        aim = default;

        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        Transform player = PlayerRegistry.CurrentTransform;

        // The reach target follows the player every frame, looked at or not, so a darkness that
        // is settling keeps pointing at them.
        if (player != null)
        {
            lookTarget = player.position + Vector3.up * reachHeight;
            hasLookTarget = true;
        }
        else if (cam != null)
        {
            lookTarget = cam.transform.position;
            hasLookTarget = true;
        }

        if (cam == null || !AnyTargetVisible()) return 0f;

        Vector3 eye = cam.transform.position;
        Vector3 forward = cam.transform.forward;

        Vector3 body = player != null ? lookTarget : eye;
        float distanceWeight = Falloff(Vector3.Distance(body, ClosestPoint(body)), fullReactionDistance, maxDistance);
        if (distanceWeight <= 0f) return 0f;

        // Carry the view ray as far as the darkness's nearest point, then snap it onto the volume:
        // the snapped point IS the ray's hit while the crosshair is on the darkness (angle ~0), and
        // the nearest edge of it otherwise (the angle grows as the view leaves it).
        float viewDistance = Vector3.Distance(eye, ClosestPoint(eye));
        aim = ClosestPoint(eye + forward * viewDistance);

        Vector3 toAim = aim - eye;
        float angle = toAim.sqrMagnitude > 0.000001f ? Vector3.Angle(forward, toAim) : 0f;
        float angleWeight = Falloff(angle, fullGazeAngle, noGazeAngle);
        if (angleWeight <= 0f) return 0f;

        if (!IsUnobstructed(eye, aim)) return 0f;

        return angleWeight * distanceWeight;
    }

    private void FollowGaze(Vector3 aim)
    {
        // From calm the reach starts where the player looks; once it is out, it glides.
        if (!hasGazePoint || agitation <= 0f || gazeFollowSeconds <= 0f)
        {
            gazePoint = aim;
        }
        else
        {
            float t = 1f - Mathf.Exp(-Time.deltaTime / gazeFollowSeconds);
            gazePoint = Vector3.Lerp(gazePoint, aim, t);
        }

        hasGazePoint = true;
    }

    /// <summary>
    /// Whether nothing solid stands between the camera and <paramref name="point"/>.
    ///
    /// Not <see cref="LineOfSight.CheckView"/>: that one counts every hit as a block, and the
    /// darkness's own collider sits on Wall — with the crosshair on it the snapped point can lie
    /// inside the box, so the ray meets the box's front face first. Here the first hit decides:
    /// the darkness itself means seen, anything else means hidden. Triggers ignored, as there.
    /// </summary>
    private bool IsUnobstructed(Vector3 origin, Vector3 point)
    {
        Vector3 toPoint = point - origin;
        float distance = toPoint.magnitude;
        if (distance <= 0.0001f) return true;

        if (!Physics.Raycast(origin, toPoint / distance, out RaycastHit hit, distance, obstacleMask,
                             QueryTriggerInteraction.Ignore))
            return true;

        // The collider's own transform, not hit.transform: that one is the Rigidbody's when there is one.
        return hit.collider == gazeVolume || hit.collider.transform.IsChildOf(transform);
    }

    private Vector3 ClosestPoint(Vector3 point)
    {
        if (gazeVolume != null && gazeVolume.enabled && gazeVolume.gameObject.activeInHierarchy)
        {
            return useColliderClosestPoint
                ? gazeVolume.ClosestPoint(point)
                : gazeVolume.bounds.ClosestPoint(point);
        }

        return RendererBounds().ClosestPoint(point);
    }

    private Bounds RendererBounds()
    {
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer r in targets)
        {
            if (r == null) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return any ? bounds : new Bounds(transform.position, Vector3.zero);
    }

    // Culled by every camera: nobody can be looking at it, and the rest of the frame's work is skipped.
    private bool AnyTargetVisible()
    {
        foreach (Renderer r in targets)
            if (r != null && r.isVisible) return true;

        return false;
    }

    /// <summary>1 up to <paramref name="full"/>, 0 from <paramref name="none"/>, linear in between.</summary>
    private static float Falloff(float value, float full, float none)
    {
        if (value <= full) return 1f;
        if (value >= none) return 0f;
        return 1f - (value - full) / (none - full);
    }

    // ── Output ──────────────────────────────────────────────────────────────

    private void Push()
    {
        if (block == null || targets == null) return;

        float eased = Agitation;

        // Calm and already pushed calm: the shader ignores the target and the gaze point at 0.
        if (eased <= 0f && lastPushed == 0f) return;
        lastPushed = eased;

        Vector4 target = new Vector4(lookTarget.x, lookTarget.y, lookTarget.z, hasLookTarget ? 1f : 0f);
        Vector4 gaze = new Vector4(gazePoint.x, gazePoint.y, gazePoint.z, hasGazePoint ? 1f : 0f);

        foreach (Renderer r in targets)
        {
            if (r == null) continue;

            // Read first: a block set on the renderer replaces the whole previous one.
            r.GetPropertyBlock(block);
            block.SetFloat(AgitationId, eased);
            block.SetVector(LookTargetId, target);
            block.SetVector(GazePointId, gaze);
            r.SetPropertyBlock(block);
        }
    }

#if UNITY_EDITOR
    // Selected in Play: a line from the camera to where the reach grows from, grey when calm and
    // dark violet when agitated, so the gaze test can be watched while tuning the angles.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !hasGazePoint) return;

        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        if (cam == null) return;

        Gizmos.color = Color.Lerp(new Color(0.6f, 0.6f, 0.6f, 0.8f), new Color(0.55f, 0.2f, 0.9f, 1f), Agitation);
        Gizmos.DrawLine(cam.transform.position, gazePoint);
        Gizmos.DrawWireSphere(gazePoint, 0.1f + 0.2f * Agitation);
    }
#endif
}
