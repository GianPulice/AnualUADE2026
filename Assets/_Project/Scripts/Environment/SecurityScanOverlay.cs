using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The security cameras' scan on the player, shown while at least one <see cref="SecurityCamera"/>
/// is scanning them. Two parts:
///
///  - The SCAN on the body (shader WIRED/Security Scan Overlay): scanlines, silhouette and tint over
///    the part already scanned, and a bright band at its edge. An extra material on the body
///    renderer, shared by every camera.
///  - The RAY from each camera (shader WIRED/Security Scan Beam): one fan of light from that
///    camera's lens to the band, whose far edge wraps the front of the body at that height - it
///    ends on the body's surface, following its curve: round over the chest, around each arm, on
///    each leg. Drawn as its own mesh, one per camera.
///
/// THE RAY LANDS FIRST. A fresh scan opens with the ray holding on the top of the head for
/// <see cref="SO_SecurityCameraData.ScanLockOnTime"/> while the body is left untouched; only then does
/// the red start spreading down from where the ray hits.
///
/// THE SCAN FILLS. It covers the body from the head down over
/// <see cref="SO_SecurityCameraData.ScanFillDuration"/>, with the band - and the rays with it -
/// riding the edge of the fill; once the body is full the band keeps running up and down it. When no
/// camera sees the player any more the fill drains back up instead of vanishing, and a camera that
/// catches them again carries on from whatever is left: <see cref="Progress"/> is a memory, not a
/// switch. Every camera shares it - two cameras do not fill the body twice as fast.
///
/// HOW THE RAY KNOWS THE BODY'S SHAPE. Every frame a ray is showing, the body's skinned mesh is baked
/// in its current pose and cut by the horizontal plane at the band's height: every triangle edge that
/// crosses it gives one point on the body's surface. For each ray those points are laid out across
/// the camera's line of sight, the width is split into <see cref="RaySegments"/> strips, and in each
/// strip the point nearest the lens is where the ray ends - the front of the body, as the camera
/// sees it. Strips with no body in them (between the legs) are bridged straight. The result is eased
/// over time so the edge glides over the body instead of jittering vertex to vertex. (Building the
/// ray out of the body mesh itself was tried and rejected: that gives a hollow volume, and a slice of
/// a hollow volume is several thin rays, not one.)
///
/// HOW THE SCAN LAYER GOES ON. Appended after the body's own material while it is needed and removed
/// when it is not - the technique ItemProximityHighlight uses for its overlay, with the same
/// restriction: an extra material draws the renderer's LAST submesh, so it only works when the body
/// has exactly one material over a one-submesh mesh. Anything else is refused with a warning.
///
/// Added at runtime, onto the body renderer's GameObject, by the first camera that needs it, so the
/// player prefab carries nothing for a feature that belongs to the cameras.
/// </summary>
[DisallowMultipleComponent]
public class SecurityScanOverlay : MonoBehaviour
{
    private static readonly int ScanYId = Shader.PropertyToID("_ScanY");
    private static readonly int FillYId = Shader.PropertyToID("_FillY");
    private static readonly int StrengthId = Shader.PropertyToID("_Strength");

    // How far past the top and bottom of the body the fill starts and ends, so an empty scan shows
    // nothing at the crown and a full one covers the soles.
    private const float FillMargin = 0.03f;

    // How far inside the body's top and bottom the rays cut it, so a cut at the very crown or soles
    // still crosses enough triangles to give the ray a shape.
    private const float CutInset = 0.02f;

    // How many strips the ray's far edge is split into across the body. Enough to follow an arm
    // and the curve of the chest; past that the low-poly body has no more detail to give.
    private const int RaySegments = 24;

    // How fast the ray's far edge eases towards the newest measurement, per second.
    private const float EdgeEasing = 18f;

    private class Ray
    {
        public Object Owner;
        public Vector3 Lens;
        public float Strength;
        public Mesh Mesh;
        public bool HasShape;
        public float Left;
        public float Right;
        public readonly float[] Depth = new float[RaySegments];
    }

    private Renderer body;
    private Collider heightSource;
    private Material scanMaterial;
    private MaterialPropertyBlock block;
    private bool canDraw;

    private readonly HashSet<Object> scanners = new HashSet<Object>();
    private readonly List<Ray> rays = new List<Ray>();
    private readonly List<Material> materialBuffer = new List<Material>();

    // The body this frame: baked vertices in world space, its triangles, and where it crosses the
    // band's height.
    private Mesh bakedBody;
    private readonly List<Vector3> bodyVertices = new List<Vector3>();
    private readonly List<int> bodyTriangles = new List<int>();
    private readonly List<Vector3> sectionPoints = new List<Vector3>();
    private float cutHeight;

    // Which space BakeMesh hands its vertices back in, decided once against the renderer's real
    // bounds. It should be the renderer's position and rotation with its scale already applied, but
    // the player's body sits under a scale of 100 from its FBX, and getting this wrong would put
    // every vertex metres away from the band: the ray would find no body and silently not draw.
    private enum BakeSpace { Unknown, PositionRotation, Matrix }
    private BakeSpace bakeSpace;

    // Per-ray scratch, reused every frame.
    private readonly float[] measuredDepth = new float[RaySegments];
    private readonly bool[] measuredAny = new bool[RaySegments];
    private readonly Vector3[] rayVertices = new Vector3[RaySegments * 2];
    private readonly Vector3[] rayNormals = new Vector3[RaySegments * 2];
    private readonly Vector3[] farPoints = new Vector3[RaySegments];

    // The tuning of the last camera that started scanning, and the ray material it brought. Cameras
    // sharing one asset - the normal case - make the choice moot.
    private SO_SecurityCameraData tuning;
    private Material rayMaterial;

    private bool appliedScan;

    private float progress;
    private float strength;
    private float lockOnRemaining;
    private float passPhase;

    // The body's real height range in its current pose, from the last bake. The scan runs over this
    // rather than the player's capsule, whose top sits above the head: a scan starting there lit the
    // head with the band's glow while the band itself was still in the air, before any ray could
    // reach it.
    private bool hasMeshRange;
    private float meshTop;
    private float meshBottom;
    private float scanHeight;
    private bool hasScanHeight;
    private float fillHeight;
    private Vector3 bodyCenter;

    /// <summary>World height of the band right now. What the rays aim at.</summary>
    public float ScanHeight => scanHeight;

    /// <summary>How much of the body has been scanned: 0 nothing, 1 all of it.</summary>
    public float Progress => progress;

    /// <summary>
    /// The overlay on <paramref name="body"/>, created the first time it is asked for.
    /// <paramref name="heightSource"/> is what the scan runs along - the player's capsule, which
    /// shrinks when they crouch, rather than the mesh bounds, which do not.
    /// </summary>
    public static SecurityScanOverlay For(Renderer body, Collider heightSource, Material scanMaterial)
    {
        if (body == null) return null;

        if (!body.TryGetComponent(out SecurityScanOverlay overlay))
        {
            overlay = body.gameObject.AddComponent<SecurityScanOverlay>();
            overlay.Init(body, heightSource, scanMaterial);
        }

        return overlay;
    }

    private void Init(Renderer bodyRenderer, Collider height, Material material)
    {
        body = bodyRenderer;
        heightSource = height;
        scanMaterial = material;
        block = new MaterialPropertyBlock();

        canDraw = CanTakeOverlay(body);
        if (!canDraw)
        {
            Debug.LogWarning($"[{nameof(SecurityScanOverlay)}] '{body.name}' needs exactly one material " +
                             $"over a one-submesh mesh for the scan to cover the whole body, so the " +
                             $"scan is not drawn on it. The rays still show.", this);
        }
        else if (scanMaterial == null)
        {
            Debug.LogWarning($"[{nameof(SecurityScanOverlay)}] No scan overlay material: the cameras " +
                             $"will scan '{body.name}' without drawing the scan on the body. Assign one " +
                             $"in SO_SecurityCameraData.", this);
        }

        Mesh source = body is SkinnedMeshRenderer skinned
            ? skinned.sharedMesh
            : body.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (source != null) source.GetTriangles(bodyTriangles, 0);
    }

    // Same rule as ItemProximityHighlight.CanTakeOverlay, for the same reason.
    private static bool CanTakeOverlay(Renderer renderer)
    {
        if (renderer.sharedMaterials.Length != 1) return false;

        Mesh mesh = renderer is SkinnedMeshRenderer skinned
            ? skinned.sharedMesh
            : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        return mesh != null && mesh.subMeshCount == 1;
    }

    /// <summary><paramref name="scanner"/> starts scanning: the fill advances while any scanner is
    /// registered.</summary>
    public void AddScanner(Object scanner, SO_SecurityCameraData data)
    {
        if (scanner == null) return;

        // A fresh scan - nobody scanning and nothing left of the last one - starts with the ray
        // holding on the head. One that picks up a draining scan carries on at once.
        bool fresh = scanners.Count == 0 && progress <= 0f;

        scanners.Add(scanner);
        if (data == null) return;

        tuning = data;
        if (fresh) lockOnRemaining = data.ScanLockOnTime;
        if (data.ScanBeamMaterial != null) rayMaterial = data.ScanBeamMaterial;
        else Debug.LogWarning($"[{nameof(SecurityScanOverlay)}] '{data.name}' has no scan beam " +
                              $"material: the camera scans without a ray.", data);
    }

    public void RemoveScanner(Object scanner) => scanners.Remove(scanner);

    /// <summary>
    /// Shows <paramref name="owner"/>'s ray this frame, from <paramref name="lens"/> at
    /// <paramref name="rayStrength"/>. The camera calls it every frame its ray is visible and
    /// <see cref="ClearBeam"/> once it has faded out - the ray fades with the camera, the scan on the
    /// body drains on its own clock.
    /// </summary>
    public void SetBeam(Object owner, Vector3 lens, float rayStrength)
    {
        Ray ray = FindRay(owner);
        if (ray == null)
        {
            ray = new Ray { Owner = owner, Mesh = CreateRayMesh() };
            rays.Add(ray);
        }

        ray.Lens = lens;
        ray.Strength = rayStrength;
    }

    public void ClearBeam(Object owner)
    {
        Ray ray = FindRay(owner);
        if (ray == null) return;

        Destroy(ray.Mesh);
        rays.Remove(ray);
    }

    private Ray FindRay(Object owner)
    {
        for (int i = 0; i < rays.Count; i++)
            if (rays[i].Owner == owner) return rays[i];
        return null;
    }

    private void Update()
    {
        // Something destroyed without telling us (scene unload) must not keep the scan up forever.
        scanners.RemoveWhere(s => s == null);
        for (int i = rays.Count - 1; i >= 0; i--)
        {
            if (rays[i].Owner != null) continue;
            Destroy(rays[i].Mesh);
            rays.RemoveAt(i);
        }

        if (tuning == null) return;

        float deltaTime = Time.deltaTime;
        bool scanning = scanners.Count > 0;
        if (!scanning) lockOnRemaining = 0f;

        // While locking on, the ray holds on the head and the body is not touched yet: the ray
        // lands first, and the red spreads from where it hits.
        bool lockingOn = scanning && lockOnRemaining > 0f;
        if (lockingOn) lockOnRemaining -= deltaTime;

        if (!lockingOn)
        {
            progress = scanning
                ? Mathf.Min(1f, progress + deltaTime / tuning.ScanFillDuration)
                : Mathf.Max(0f, progress - deltaTime / tuning.ScanDrainDuration);
        }

        float fade = tuning.ScanFadeTime;
        float targetStrength = (scanning && !lockingOn) || progress > 0f ? 1f : 0f;
        strength = fade > 0f ? Mathf.MoveTowards(strength, targetStrength, deltaTime / fade) : targetStrength;

        Bounds bounds = heightSource != null ? heightSource.bounds : body.bounds;
        bodyCenter = bounds.center;

        // The body's real top and bottom when a bake has measured them, the capsule's until then.
        float top = hasMeshRange ? meshTop : bounds.max.y;
        float bottom = hasMeshRange ? meshBottom : bounds.min.y;

        fillHeight = Mathf.Lerp(top + FillMargin, bottom - FillMargin, progress);
        UpdateScanHeight(scanning, top, bottom, deltaTime);

        if (canDraw) UpdateScanLayer();
    }

    // After every camera has moved and reported where its lens is this frame.
    private void LateUpdate()
    {
        if (tuning == null || rayMaterial == null || rays.Count == 0) return;

        BakeBody();
        CutBodyAtBand();
        for (int i = 0; i < rays.Count; i++) DrawRay(rays[i], Time.deltaTime);
    }

    /// <summary>
    /// Where the band is. While the scan is filling or draining it rides the edge of the fill; once
    /// the body is full and still watched it runs up and down it, starting from the feet where the
    /// fill ended. The move is speed-limited, so switching between those two never makes the band
    /// jump - it travels to its new place.
    /// </summary>
    private void UpdateScanHeight(bool scanning, float top, float bottom, float deltaTime)
    {
        float target;
        if (scanning && progress >= 1f)
        {
            passPhase += deltaTime / tuning.ScanPassDuration;
            float triangle = 1f - Mathf.Abs(Mathf.Repeat(passPhase, 2f) - 1f);
            target = Mathf.Lerp(bottom, top, triangle);
        }
        else
        {
            passPhase = 0f;
            target = Mathf.Clamp(fillHeight, bottom, top);
        }

        if (!hasScanHeight)
        {
            scanHeight = target;
            hasScanHeight = true;
            return;
        }

        // Twice the fastest the band ever moves on its own: it never lags a target it is following,
        // and still crosses the body in a fraction of a second when it has to relocate.
        float fastest = (top - bottom) / Mathf.Min(tuning.ScanPassDuration, tuning.ScanFillDuration,
                                                   tuning.ScanDrainDuration);
        scanHeight = Mathf.MoveTowards(scanHeight, target, 2f * fastest * deltaTime);
    }

    private void UpdateScanLayer()
    {
        bool showScan = strength > 0f && scanMaterial != null;
        if (!ApplyScanSlot(showScan, out int slot)) return;

        body.GetPropertyBlock(block, slot);
        block.SetFloat(ScanYId, scanHeight);
        block.SetFloat(FillYId, fillHeight);
        block.SetFloat(StrengthId, strength);
        body.SetPropertyBlock(block, slot);
    }

    /// <summary>
    /// Makes the body wear its own materials, followed by the scan layer when
    /// <paramref name="show"/>. Only touches the renderer when that changes. Returns whether the
    /// layer is on, and its slot.
    /// </summary>
    private bool ApplyScanSlot(bool show, out int slot)
    {
        body.GetSharedMaterials(materialBuffer);
        int existing = materialBuffer.IndexOf(scanMaterial);
        slot = existing;

        if (show == appliedScan && (existing >= 0) == show) return show;

        if (existing >= 0)
        {
            body.SetPropertyBlock(null, existing);
            materialBuffer.RemoveAt(existing);
        }

        slot = -1;
        if (show)
        {
            slot = materialBuffer.Count;
            materialBuffer.Add(scanMaterial);
        }

        body.sharedMaterials = materialBuffer.ToArray();
        appliedScan = show;
        return show;
    }

    /// <summary>The body's vertices, in world space, in the pose it has this frame.</summary>
    private void BakeBody()
    {
        bodyVertices.Clear();

        if (body is SkinnedMeshRenderer skinned)
        {
            if (bakedBody == null) bakedBody = new Mesh { name = "SecurityScanBodyBake" };

            skinned.BakeMesh(bakedBody, true);
            bakedBody.GetVertices(bodyVertices);

            Transform t = skinned.transform;
            if (bakeSpace == BakeSpace.Unknown) bakeSpace = DetectBakeSpace(t);

            Matrix4x4 toWorld = bakeSpace == BakeSpace.Matrix
                ? t.localToWorldMatrix
                : Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            for (int i = 0; i < bodyVertices.Count; i++)
                bodyVertices[i] = toWorld.MultiplyPoint3x4(bodyVertices[i]);
        }
        else if (body.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            filter.sharedMesh.GetVertices(bodyVertices);
            Matrix4x4 toWorld = body.localToWorldMatrix;
            for (int i = 0; i < bodyVertices.Count; i++)
                bodyVertices[i] = toWorld.MultiplyPoint3x4(bodyVertices[i]);
        }

        if (bodyVertices.Count == 0) return;

        float top = float.MinValue;
        float bottom = float.MaxValue;
        for (int i = 0; i < bodyVertices.Count; i++)
        {
            float y = bodyVertices[i].y;
            if (y > top) top = y;
            if (y < bottom) bottom = y;
        }

        meshTop = top;
        meshBottom = bottom;
        hasMeshRange = true;
    }

    /// <summary>
    /// Picks the reading of the baked vertices whose centre lands closest to the renderer's own
    /// bounds - see <see cref="bakeSpace"/>. Runs once, on the first bake.
    /// </summary>
    private BakeSpace DetectBakeSpace(Transform t)
    {
        if (bodyVertices.Count == 0) return BakeSpace.PositionRotation;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < bodyVertices.Count; i++) sum += bodyVertices[i];
        Vector3 average = sum / bodyVertices.Count;

        Vector3 asPositionRotation = t.position + t.rotation * average;
        Vector3 asMatrix = t.localToWorldMatrix.MultiplyPoint3x4(average);
        Vector3 actual = body.bounds.center;

        BakeSpace space = (asMatrix - actual).sqrMagnitude < (asPositionRotation - actual).sqrMagnitude
            ? BakeSpace.Matrix
            : BakeSpace.PositionRotation;

        Vector3 chosen = space == BakeSpace.Matrix ? asMatrix : asPositionRotation;
        if ((chosen - actual).magnitude > body.bounds.extents.magnitude + 0.5f)
        {
            Debug.LogWarning($"[{nameof(SecurityScanOverlay)}] Could not place '{body.name}''s baked " +
                             $"mesh on the body (off by {(chosen - actual).magnitude:0.##} m): the scan " +
                             $"rays may not find it.", this);
        }

        return space;
    }

    /// <summary>
    /// Every point where the body's surface crosses the band's height: one per triangle edge that
    /// straddles it. Edges shared by two triangles give the same point twice, which costs nothing
    /// here - only the nearest point per strip is kept.
    /// </summary>
    private void CutBodyAtBand()
    {
        sectionPoints.Clear();

        // Never cut above the crown or below the soles: there is no body there to end a ray on.
        cutHeight = hasMeshRange
            ? Mathf.Clamp(scanHeight, meshBottom + CutInset, meshTop - CutInset)
            : scanHeight;
        float h = cutHeight;

        for (int t = 0; t + 2 < bodyTriangles.Count; t += 3)
        {
            Vector3 a = bodyVertices[bodyTriangles[t]];
            Vector3 b = bodyVertices[bodyTriangles[t + 1]];
            Vector3 c = bodyVertices[bodyTriangles[t + 2]];

            AddCrossing(a, b, h);
            AddCrossing(b, c, h);
            AddCrossing(c, a, h);
        }
    }

    private void AddCrossing(Vector3 p, Vector3 q, float h)
    {
        if ((p.y - h) * (q.y - h) > 0f || Mathf.Approximately(p.y, q.y)) return;
        sectionPoints.Add(Vector3.Lerp(p, q, (h - p.y) / (q.y - p.y)));
    }

    /// <summary>
    /// Shapes and draws one ray: a fan from the lens whose far edge lies on the front of the body's
    /// cross-section at the band, as this camera sees it.
    /// </summary>
    private void DrawRay(Ray ray, float deltaTime)
    {
        // Laid out in the horizontal: the body is cut at the band's height, and what matters is how
        // that cut looks from the camera's side, whatever height the lens is at.
        Vector3 axis = bodyCenter - ray.Lens;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.0001f) return;
        axis.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        if (MeasureFront(axis, across, out float left, out float right))
        {
            if (!ray.HasShape)
            {
                ray.Left = left;
                ray.Right = right;
                System.Array.Copy(measuredDepth, ray.Depth, RaySegments);
                ray.HasShape = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-EdgeEasing * deltaTime);
                ray.Left = Mathf.Lerp(ray.Left, left, k);
                ray.Right = Mathf.Lerp(ray.Right, right, k);
                for (int i = 0; i < RaySegments; i++)
                    ray.Depth[i] = Mathf.Lerp(ray.Depth[i], measuredDepth[i], k);
            }
        }

        if (!ray.HasShape) return;

        BuildRayMesh(ray, axis, across);

        block.Clear();
        block.SetFloat(StrengthId, ray.Strength);

        var renderParams = new RenderParams(rayMaterial)
        {
            matProps = block,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            worldBounds = ray.Mesh.bounds,
        };
        Graphics.RenderMesh(renderParams, ray.Mesh, 0, Matrix4x4.identity);
    }

    /// <summary>
    /// The front of the body at the band, seen from this ray: how far the cut reaches left and right
    /// of the line of sight, and for each of the <see cref="RaySegments"/> strips across it, how far
    /// in front of the body's centre its nearest point is (into <see cref="measuredDepth"/>). Strips
    /// with nothing in them are bridged from their neighbours. False when the cut is empty.
    /// </summary>
    private bool MeasureFront(Vector3 axis, Vector3 across, out float left, out float right)
    {
        left = float.MaxValue;
        right = float.MinValue;
        if (sectionPoints.Count == 0) return false;

        for (int i = 0; i < sectionPoints.Count; i++)
        {
            float side = Vector3.Dot(sectionPoints[i] - bodyCenter, across);
            if (side < left) left = side;
            if (side > right) right = side;
        }

        float width = right - left;
        if (width < 0.001f) return false;

        for (int s = 0; s < RaySegments; s++)
        {
            measuredDepth[s] = float.MaxValue;
            measuredAny[s] = false;
        }

        for (int i = 0; i < sectionPoints.Count; i++)
        {
            Vector3 offset = sectionPoints[i] - bodyCenter;
            float side = Vector3.Dot(offset, across);
            int strip = Mathf.Clamp(Mathf.RoundToInt((side - left) / width * (RaySegments - 1)), 0, RaySegments - 1);

            float depth = Vector3.Dot(offset, axis);
            if (depth < measuredDepth[strip]) measuredDepth[strip] = depth;
            measuredAny[strip] = true;
        }

        BridgeEmptyStrips();
        return true;
    }

    /// <summary>Fills strips that caught no surface with a straight line between the nearest filled
    /// strips on each side - the gap between the legs, or a sparse stretch of a low-poly body.</summary>
    private void BridgeEmptyStrips()
    {
        int previous = -1;
        for (int s = 0; s < RaySegments; s++)
        {
            if (!measuredAny[s]) continue;

            if (previous < 0)
            {
                for (int e = 0; e < s; e++) measuredDepth[e] = measuredDepth[s];
            }
            else
            {
                for (int e = previous + 1; e < s; e++)
                    measuredDepth[e] = Mathf.Lerp(measuredDepth[previous], measuredDepth[s],
                                                  (e - previous) / (float)(s - previous));
            }

            previous = s;
        }

        if (previous < 0) return;
        for (int e = previous + 1; e < RaySegments; e++) measuredDepth[e] = measuredDepth[previous];
    }

    /// <summary>
    /// Writes the fan: for every strip, a vertex at the lens and one on the body. The lens vertex is
    /// repeated per strip so that each line from the lens keeps its own place across the ray (uv.x),
    /// and each pair shares the normal of the fan at that strip, so the shading runs smoothly round
    /// the curve instead of faceting.
    /// </summary>
    private void BuildRayMesh(Ray ray, Vector3 axis, Vector3 across)
    {
        Vector3 centre = new Vector3(bodyCenter.x, cutHeight, bodyCenter.z);

        for (int s = 0; s < RaySegments; s++)
        {
            float side = Mathf.Lerp(ray.Left, ray.Right, s / (float)(RaySegments - 1));
            farPoints[s] = centre + across * side + axis * ray.Depth[s];
        }

        for (int s = 0; s < RaySegments; s++)
        {
            Vector3 along = farPoints[s] - ray.Lens;
            Vector3 tangent = farPoints[Mathf.Min(s + 1, RaySegments - 1)] - farPoints[Mathf.Max(s - 1, 0)];
            Vector3 normal = Vector3.Cross(along, tangent);
            normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;

            rayVertices[s * 2] = ray.Lens;
            rayVertices[s * 2 + 1] = farPoints[s];
            rayNormals[s * 2] = normal;
            rayNormals[s * 2 + 1] = normal;
        }

        ray.Mesh.vertices = rayVertices;
        ray.Mesh.normals = rayNormals;
        ray.Mesh.RecalculateBounds();
    }

    private static Mesh CreateRayMesh()
    {
        var mesh = new Mesh { name = "SecurityScanRay" };
        mesh.MarkDynamic();

        var uvs = new Vector2[RaySegments * 2];
        var triangles = new int[(RaySegments - 1) * 3];
        for (int s = 0; s < RaySegments; s++)
        {
            float u = s / (float)(RaySegments - 1);
            uvs[s * 2] = new Vector2(u, 0f);
            uvs[s * 2 + 1] = new Vector2(u, 1f);

            if (s == RaySegments - 1) continue;
            int t = s * 3;
            triangles[t] = s * 2;
            triangles[t + 1] = s * 2 + 1;
            triangles[t + 2] = (s + 1) * 2 + 1;
        }

        mesh.vertices = new Vector3[RaySegments * 2];
        mesh.normals = new Vector3[RaySegments * 2];
        mesh.uv = uvs;
        mesh.triangles = triangles;
        return mesh;
    }

    private void OnDisable()
    {
        if (body != null) ApplyScanSlot(false, out _);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < rays.Count; i++) Destroy(rays[i].Mesh);
        rays.Clear();
        if (bakedBody != null) Destroy(bakedBody);
    }
}
