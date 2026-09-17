using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Resident Evil "there is something here" glint: a small four-point star that flashes on a
/// pickup every few seconds, so an item lying in a dark corner can be spotted from across the room.
///
/// It hands over to <see cref="ItemProximityHighlight"/> up close. The highlight answers the
/// crosshair within interaction reach, so the glint fades out as the player walks into that reach
/// and goes dark the moment the crosshair lands on the item: the two never show at once. Past the
/// profile's Max Distance it stays dark too.
///
/// Drawn with <see cref="Graphics.RenderMesh"/> instead of a child quad. A child would be one more
/// Renderer under the interactable, and the highlight drives — and its validator judges — every
/// Renderer below it; it would also be one more object in every pickup prefab. Being drawn from
/// Update also means it stops on its own when the pickup is taken (destroyed) or switched off.
///
/// The star is procedural, in ItemGlint.shader, and faces the camera there: its shape is tuned on
/// the material, its timing, size, colour and range on the <see cref="SO_GlintProfile"/>.
///
/// Nothing to wire: put it on the pickup's root, in the Father prefab (Tools ▸ Interactables ▸ Set
/// Up Item Glints). A pickup with no item never glints — there is nothing for the player to find.
/// </summary>
[DisallowMultipleComponent]
public class ItemGlint : MonoBehaviour
{
    [Tooltip("Timing, size, colour and range. SO_Glint_Items on every pickup, assigned in the Father prefab.")]
    [SerializeField] private SO_GlintProfile profile;

    [Tooltip("Nudge, in world metres, from where the star sits by default: the centre of the item's " +
             "own renderers, lifted by the profile's Height Offset. Leave at zero unless the star " +
             "lands somewhere odd on this item.")]
    [SerializeField] private Vector3 offset;

    private static readonly int ColorId    = Shader.PropertyToID("_GlintColor");
    private static readonly int AlphaId    = Shader.PropertyToID("_GlintAlpha");
    private static readonly int RotationId = Shader.PropertyToID("_GlintRotation");

    private static Mesh s_quad;

    private IInteractable _owner;
    private MaterialPropertyBlock _block;
    private Vector3 _localAnchor;
    private float _flashStart = float.NegativeInfinity;
    private float _nextFlash;

    // 1 while the crosshair is elsewhere, 0 while it is on this item; eased by TargetFadeTime.
    private bool _isTargeted;
    private float _targetWeight = 1f;

    public SO_GlintProfile Profile => profile;

    private void Awake()
    {
        _owner = GetComponentInParent<IInteractable>(true);
        _block = new MaterialPropertyBlock();

        if (profile == null || profile.Material == null)
        {
            Debug.LogWarning($"[{nameof(ItemGlint)}] '{name}' has no {nameof(SO_GlintProfile)}, or its " +
                             "profile has no material, so it never glints. Assign SO_Glint_Items on the " +
                             "Father prefab (Tools > Interactables > Set Up Item Glints).", this);
            enabled = false;
            return;
        }

        // Captured once, relative to the item: pickups do not change shape while they lie there.
        _localAnchor = transform.InverseTransformPoint(RendererCentre());

        // Spread over a whole interval: items that woke up in the same frame would glint in unison.
        _nextFlash = Time.time + Random.Range(0f, profile.Interval);
    }

    private void OnEnable() => InteractionEvents.OnTargetChanged += HandleTargetChanged;

    private void OnDisable()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
        _isTargeted = false;
        _targetWeight = 1f;
    }

    private void HandleTargetChanged(IInteractable target)
    {
        _isTargeted = _owner != null && ReferenceEquals(target, _owner);
    }

    private void Update()
    {
        if (_owner != null && !_owner.CanInteract()) return;

        float goal = _isTargeted ? 0f : 1f;
        _targetWeight = profile.TargetFadeTime > 0f
            ? Mathf.MoveTowards(_targetWeight, goal, Time.deltaTime / profile.TargetFadeTime)
            : goal;

        float now = Time.time;
        if (now >= _nextFlash)
        {
            _flashStart = now;
            _nextFlash = now + profile.RollInterval();
        }

        float t = (now - _flashStart) / profile.FlashDuration;
        if (t < 0f || t >= 1f) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 anchor = AnchorPosition;
        float alpha = profile.AlphaCurve.Evaluate(t) * _targetWeight * RangeWeight(anchor, cam);
        if (alpha <= 0.001f) return;

        float cameraDistance = Vector3.Distance(cam.transform.position, anchor);
        float size = Mathf.Max(profile.Size, MinWorldSize(cam, cameraDistance)) * profile.ScaleCurve.Evaluate(t);
        if (size <= 0.0001f) return;

        Draw(anchor, size, alpha, profile.SpinDegrees * t);
    }

    private Vector3 AnchorPosition =>
        transform.TransformPoint(_localAnchor) + Vector3.up * profile.HeightOffset + offset;

    /// <summary>
    /// 0 within Hide Within of the player and past Max Distance, 1 in between, eased over Fade Band
    /// at both ends. Measured from the player, like the interaction reach it hands over to; from the
    /// camera only while no player is registered.
    /// </summary>
    private float RangeWeight(Vector3 anchor, Camera cam)
    {
        Transform player = PlayerRegistry.CurrentTransform;
        Vector3 from = player != null ? player.position : cam.transform.position;
        float distance = Vector3.Distance(from, anchor);

        float pastNear  = Mathf.Clamp01((distance - profile.HideWithin) / profile.FadeBand);
        float beforeFar = Mathf.Clamp01((profile.MaxDistance - distance) / profile.FadeBand);
        return pastNear * beforeFar;
    }

    /// <summary>
    /// The world size that fills Min Screen Height at this distance. Below a few PS1 blocks the
    /// star would shimmer: the PS1 filter keeps a single texel per block.
    /// </summary>
    private float MinWorldSize(Camera cam, float distance)
    {
        float viewHeight = cam.orthographic
            ? 2f * cam.orthographicSize
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return profile.MinScreenHeight * viewHeight;
    }

    private void Draw(Vector3 anchor, float size, float alpha, float spinDegrees)
    {
        // Resolved per glint, not cached, so tuning the profile in Play shows on the next one.
        _block.SetColor(ColorId, profile.ResolveColor(_owner));
        _block.SetFloat(AlphaId, alpha);
        _block.SetFloat(RotationId, spinDegrees * Mathf.Deg2Rad);

        var renderParams = new RenderParams(profile.Material)
        {
            layer                = gameObject.layer,
            shadowCastingMode    = ShadowCastingMode.Off,
            receiveShadows       = false,
            lightProbeUsage      = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            matProps             = _block,
        };

        // Only position and scale: the shader turns the quad to the camera and reads its size back
        // from the matrix.
        Graphics.RenderMesh(renderParams, Quad, 0, Matrix4x4.TRS(anchor, Quaternion.identity, Vector3.one * size));
    }

    /// <summary>
    /// World centre of the renderers the highlight drives, so the star sits on the item's visible
    /// body whatever its pivot. The item's own position when none is visible.
    /// </summary>
    private Vector3 RendererCentre()
    {
        bool found = false;
        Bounds bounds = default;

        foreach (Renderer part in ItemProximityHighlight.GatherRenderers(transform))
        {
            if (!part.enabled || !part.gameObject.activeInHierarchy) continue;

            if (found) bounds.Encapsulate(part.bounds);
            else       { bounds = part.bounds; found = true; }
        }

        return found ? bounds.center : transform.position;
    }

    /// <summary>A unit quad in XY, shared by every glint.</summary>
    private static Mesh Quad
    {
        get
        {
            if (s_quad != null) return s_quad;

            s_quad = new Mesh { name = "ItemGlintQuad", hideFlags = HideFlags.HideAndDontSave };
            s_quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            s_quad.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            });
            s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);

            // A cube that holds the quad at any angle: the shader turns it to face the camera, so
            // culling cannot trust its authored orientation.
            s_quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            return s_quad;
        }
    }

    /// <summary>Starts a glint right away, to tune the profile in Play without waiting for one.
    /// Still hidden within Hide Within of the player.</summary>
    [ContextMenu("Glint Now")]
    private void GlintNow()
    {
        if (Application.isPlaying) _nextFlash = Time.time;
    }

#if UNITY_EDITOR
    /// <summary>Where the star sits, and the two radii it shows between.</summary>
    private void OnDrawGizmosSelected()
    {
        float height = profile != null ? profile.HeightOffset : 0f;
        float arm    = profile != null ? profile.Size * 0.5f : 0.125f;
        Vector3 anchor = RendererCentre() + Vector3.up * height + offset;

        Gizmos.color = Color.white;
        Gizmos.DrawLine(anchor - Vector3.right   * arm, anchor + Vector3.right   * arm);
        Gizmos.DrawLine(anchor - Vector3.up      * arm, anchor + Vector3.up      * arm);
        Gizmos.DrawLine(anchor - Vector3.forward * arm, anchor + Vector3.forward * arm);

        if (profile == null) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(anchor, profile.HideWithin);
        Gizmos.DrawWireSphere(anchor, profile.MaxDistance);
    }
#endif
}
