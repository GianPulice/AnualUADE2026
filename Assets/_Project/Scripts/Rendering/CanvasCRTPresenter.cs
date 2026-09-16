using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Shows a Screen Space - Overlay canvas through a CRT tube. At runtime the canvas is re-routed to a
/// camera of its own that renders it into a screen-sized texture, and that texture goes back on
/// screen through an Overlay RawImage drawn with a CRT material — curvature, wobble, chromatic
/// aberration, PS1 colour. There is no other way to bend an Overlay canvas: it is drawn after every
/// camera, full-screen passes included, so nothing can ever sample it.
///
/// Edit mode is untouched: the canvas stays Overlay in the prefab, everything here is built in Awake
/// and torn down in OnDestroy. Disabling the component puts the canvas back on screen as a plain
/// Overlay, so the tube can be switched off without touching the prefab.
///
/// Three things keep this invisible to the rest of the game:
///   - The UI camera must not run the world's full-screen features (PS1, fog): hence
///     <see cref="rendererIndex"/>, a URP renderer without them.
///   - Clicks must land where things are seen, not where they sit in the texture: that is
///     <see cref="CRTWarpedRaycaster"/>, which asks <see cref="TryUnwarp"/>.
///   - Nothing renders while <see cref="content"/> is hidden, or while <see cref="visibility"/> is at
///     alpha 0, so a closed inventory — or a result screen waiting at alpha 0 — costs nothing.
/// </summary>
[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/UI/Canvas CRT Presenter")]
public class CanvasCRTPresenter : MonoBehaviour
{
    [Tooltip("Rendered only while this is active in the hierarchy — the inventory's LAYOUT. Empty = always.")]
    [SerializeField] private GameObject content;

    [Tooltip("Optional. Rendered only while this group is active with alpha above zero — for screens " +
             "that stay active and hide by alpha (Result, Win). Empty = content alone decides.")]
    [SerializeField] private CanvasGroup visibility;

    [Tooltip("CRT material for the screen. UIPSXSettingsApplier makes the runtime copy.")]
    [SerializeField] private Material screenMaterial;

    [Tooltip("URP renderer (index in the pipeline asset) for the UI camera. It must NOT carry the " +
             "world's full-screen features, or PS1 and the fog would land on the UI. -1 = default.")]
    [SerializeField] private int rendererIndex = -1;

    [Tooltip("Bilinear keeps the curvature smooth; Point keeps text pixel-sharp but steps along the curve.")]
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

    private static readonly int PropWarp = Shader.PropertyToID("_WarpStrength");

    // Far from anything the world camera could see; the canvas follows its camera there.
    private static readonly Vector3 CameraPosition = new Vector3(0f, -10000f, 0f);

    private Canvas canvas;
    private Camera uiCamera;
    private Canvas screenCanvas;
    private RawImage screen;
    private RenderTexture target;
    private bool built;

    /// <summary>True while the canvas is shown through the tube — and so is warped.</summary>
    public bool IsPresenting => built && enabled && screenCanvas != null && screenCanvas.enabled;

    /// <summary>
    /// The CRT material actually on screen: the runtime copy <see cref="UIPSXSettingsApplier"/> makes,
    /// never the asset, so anything written here is thrown away with the scene. For effects that drive
    /// the tube itself — <c>UISignalStaticBurst</c> tearing it during a burst. Null before Awake, and
    /// whenever the presenter has no material to work with.
    /// </summary>
    public Material ScreenMaterial => screen != null ? screen.material : null;

    // -- Unity -------------------

    private void Awake()
    {
        canvas = GetComponent<Canvas>();

        if (screenMaterial == null)
        {
            Debug.LogWarning($"[CanvasCRTPresenter] '{name}' has no screen material; it stays a plain overlay.", this);
            enabled = false;
            return;
        }

        BuildCamera();
        BuildScreen();
        built = true;
    }

    private void OnEnable()
    {
        if (!built) return;

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 1f;
        Refresh();
    }

    private void OnDisable()
    {
        if (!built) return;

        SetPresenting(false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private void LateUpdate() => Refresh();

    private void OnDestroy()
    {
        if (uiCamera != null) Destroy(uiCamera.gameObject);
        if (screenCanvas != null) Destroy(screenCanvas.gameObject);
        ReleaseTarget();
    }

    // -- Mapping -------------------

    /// <summary>
    /// Maps a point on the physical screen to the point of the canvas shown there. False when the
    /// point is off the curved tube, where nothing is shown and so nothing may be clicked.
    ///
    /// Only the curvature is undone (<see cref="CRTWarp"/>, the same function the shader uses). The
    /// wobble and glitch offsets are left out on purpose: at a pixel or two they are smaller than any
    /// button, and chasing them would make hover states flicker.
    /// </summary>
    public bool TryUnwarp(Vector2 screenPosition, out Vector2 canvasPosition)
    {
        canvasPosition = screenPosition;
        if (!IsPresenting) return true;

        Vector2 size = new Vector2(Screen.width, Screen.height);
        float strength = screen.material.GetFloat(PropWarp);
        Vector2 uv = CRTWarp.Warp(new Vector2(screenPosition.x / size.x, screenPosition.y / size.y),
                                  strength, size.x / size.y);

        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return false;

        // The target is screen-sized, so texture UV and screen UV share one pixel grid.
        canvasPosition = new Vector2(uv.x * size.x, uv.y * size.y);
        return true;
    }

    // -- Core -------------------

    private void Refresh()
    {
        if (!built) return;

        bool show = (content == null || content.activeInHierarchy) &&
                    (visibility == null || (visibility.gameObject.activeInHierarchy && visibility.alpha > 0f));
        if (show) EnsureTarget();
        SetPresenting(show);
    }

    private void SetPresenting(bool on)
    {
        if (uiCamera != null) uiCamera.enabled = on;
        if (screenCanvas != null) screenCanvas.enabled = on;
    }

    private void BuildCamera()
    {
        GameObject go = new GameObject($"{name} (CRT Camera)");
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.transform.position = CameraPosition;

        uiCamera = go.AddComponent<Camera>();
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        // The tube composites over the world, so everything that is not UI must stay transparent.
        uiCamera.backgroundColor = Color.clear;
        // Everything, not just the canvas's layer. As an Overlay a canvas draws its children whatever
        // their layer; through a camera, anything outside the culling mask silently vanishes — the
        // buttons' SweepBars sit on Default. The camera is alone 10 km away with a 10-unit far plane,
        // so there is nothing else for it to pick up.
        uiCamera.cullingMask = ~0;
        uiCamera.orthographic = true;
        uiCamera.nearClipPlane = 0.1f;
        uiCamera.farClipPlane = 10f;
        uiCamera.allowHDR = false;
        uiCamera.allowMSAA = false;
        uiCamera.useOcclusionCulling = false;
        // Above the world camera: if both ever raycast, the UI wins, as it did as an overlay.
        uiCamera.depth = 100f;
        uiCamera.enabled = false;

        UniversalAdditionalCameraData data = uiCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.renderShadows = false;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        if (rendererIndex >= 0) data.SetRenderer(rendererIndex);
    }

    private void BuildScreen()
    {
        GameObject go = new GameObject($"{name} (CRT Screen)", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.layer = gameObject.layer;

        screenCanvas = go.AddComponent<Canvas>();
        screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Takes the source canvas's place in the overlay order, so the pause menu still goes on top.
        screenCanvas.sortingLayerID = canvas.sortingLayerID;
        screenCanvas.sortingOrder = canvas.sortingOrder;
        screenCanvas.enabled = false;

        GameObject imageGo = new GameObject("Screen", typeof(RectTransform));
        imageGo.layer = go.layer;
        RectTransform rt = (RectTransform)imageGo.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        screen = imageGo.AddComponent<RawImage>();
        screen.raycastTarget = false;   // clicks go to the source canvas, through CRTWarpedRaycaster
        screen.material = screenMaterial;
        imageGo.AddComponent<UIPSXSettingsApplier>();
    }

    private void EnsureTarget()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (target != null && target.width == width && target.height == height) return;

        ReleaseTarget();

        // 24 bits buys a stencil buffer along with depth: the item list's Mask writes the stencil,
        // and without one it would mask nothing and the list would spill out of its box.
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = $"{name} CRT Target",
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
        };
        target.Create();

        uiCamera.targetTexture = target;
        screen.texture = target;
    }

    private void ReleaseTarget()
    {
        if (target == null) return;

        if (uiCamera != null) uiCamera.targetTexture = null;
        if (screen != null) screen.texture = null;
        target.Release();
        Destroy(target);
        target = null;
    }
}
