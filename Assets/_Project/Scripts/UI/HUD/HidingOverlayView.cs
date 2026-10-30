using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the player sees of the hiding spot from inside it: the louvers of a locker door, the
/// underside and legs of a table, the seam between the doors of a sealed container. One look per
/// <see cref="EHidingSpotType"/>, brought in on <see cref="HidingEvents.OnEntered"/> and taken away
/// on <see cref="HidingEvents.OnExited"/>.
///
/// Each look is an authored child with a <see cref="HidingOverlayLayer"/>: a full-screen Graphic
/// whose material draws it (shaders in Art/Materials/UI, HidingOverlay_*.shader). This view does not
/// build anything — it finds the layers under it, fades the one that matches the spot, and tells
/// their shaders where the camera is looking.
///
/// Why shaders and not textures or sprites: HUDCanvas is a Screen Space - Overlay canvas, drawn
/// after the world's PSX pass, so nothing pixelates it. The looks have to land on the same grid of
/// blocks as the picture behind them, in flat tones with hard edges, at any resolution, and their
/// proportions have to stay tunable (how many louvers, how thick a leg) without a round trip
/// through an image editor. A shader evaluated once per PSX block does both, on one quad.
///
/// It writes no material and no asset. The fade is the CanvasRenderer's alpha, which is not
/// serialized; the look offset is the shader global <c>_HidingOverlayLook</c>. The three .mat
/// assets hold exactly what the designer left in them, in Play and out of it.
///
/// THE LOOK FOLLOWS THE CAMERA. Inside a spot the player can still turn the interior camera. A
/// look that stayed glued to the screen while the room moved behind it would read as dirt on the
/// lens, so the look slides against the camera's turn (each layer's Look Parallax says how far)
/// and the door stays where the door is. On top of that it drifts a fraction of a block by itself:
/// nobody holds perfectly still.
///
/// Sits under the rest of the HUD (the vignettes, the alerts, the breath meter draw over it) and
/// never takes a click. Lives in HUDCanvas.prefab, which is the source of truth for its layout.
/// It does not draw the breath: that is <see cref="BreathHoldMeterView"/>.
///
/// PREVIEW WITHOUT PLAY: tick Preview In Edit Mode, pick a type, and tune the layer's material
/// while looking at it. The preview only moves a CanvasRenderer alpha, so it leaves nothing in the
/// prefab but its own toggle — untick it when done.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HidingOverlayView : MonoBehaviour
{
    [Header("Fade")]
    [Tooltip("Seconds for the look to come in once the player is inside the spot — the end of the " +
             "climb-in, not the key press. Unscaled. Keep it close to the camera blend into the " +
             "spot (CB_HidingSpotBlends), so the door closes as the camera arrives behind it.")]
    [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.35f;

    [Tooltip("Seconds for the look to go, on every way out: getting out, a capture, a respawn, a " +
             "cinematic. Unscaled.")]
    [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.25f;

    [Header("Following the camera")]
    [Tooltip("Seconds, counted from the moment the look is fully in, for it to go from sitting " +
             "still in the middle of the screen to following the camera.\n\n" +
             "The camera is still swinging round into the spot while the look fades in. Following " +
             "it from the first frame would throw the look across the screen.")]
    [SerializeField, Min(0.01f)] private float lookEaseSeconds = 0.4f;

    [Tooltip("The furthest the look may slide from the middle of the screen, in screen heights (a " +
             "16:9 screen is 1.78 wide). A safety limit for a camera pointing somewhere a spot " +
             "never lets it; the under-table camera's 45 degrees of pan needs about 0.7.")]
    [SerializeField, Range(0f, 3f)] private float maxLookShift = 1.2f;

    [Header("Idle sway")]
    [Tooltip("How far the look drifts by itself, across and up, in screen heights. One PSX block " +
             "is 1/256, about 0.004: at that size an edge shifts by a block now and then, which is " +
             "all it takes for the look to stop reading as a still image. 0 = dead still.\n\n" +
             "Constant on purpose. It is not the breath: the breath meter draws that.")]
    [SerializeField] private Vector2 swayAmplitude = new Vector2(0.004f, 0.0025f);

    [Tooltip("Seconds for one full drift, across and up. Two different lengths, so the path does " +
             "not visibly repeat.")]
    [SerializeField] private Vector2 swaySeconds = new Vector2(5.3f, 3.7f);

    [Header("Edit-mode preview")]
    [Tooltip("Shows one look on the Game view outside Play, to tune its material. It moves no " +
             "serialized value on the layers, so it cannot be saved into them. In Play this does " +
             "nothing: the hiding spots drive the overlay.\n\n" +
             "UNTICK IT WHEN DONE. Left on in the prefab, the look stays over the Game view " +
             "whenever the editor is not playing.")]
    [SerializeField] private bool previewInEditMode;

    [Tooltip("Which look the preview shows.")]
    [SerializeField] private EHidingSpotType previewType = EHidingSpotType.Locker;

    [Tooltip("How far in the previewed look is. Below 1 it shows the fade, which goes in the " +
             "material's Fade Steps and not smoothly.")]
    [SerializeField, Range(0f, 1f)] private float previewFade = 1f;

    [Tooltip("Where the previewed camera looks, in degrees off the spot's forward: x = to the " +
             "right, y = up. What the interior cameras allow (HidingSpotFather prefabs): locker " +
             "15 either side and 10 up or down, under table 45 either side and 15 up / 5 down, " +
             "container 10 every way.")]
    [SerializeField] private Vector2 previewLookDegrees;

    [Tooltip("Vertical field of view the preview assumes when the open scenes have no main " +
             "camera. The interior cameras of the hiding spots use 75.")]
    [SerializeField, Range(20f, 120f)] private float previewFieldOfView = 75f;

    // Where the middle of the look sits on screen, in screen heights. Read by the three
    // HidingOverlay_* shaders through HidingOverlayCommon.hlsl.
    private static readonly int LookId = Shader.PropertyToID("_HidingOverlayLook");

    private const float TwoPi = Mathf.PI * 2f;

    // The spot's forward seen from the camera has to stay in front of it for the projection to
    // mean anything. Behind this (about 87 degrees off) the shift is simply pinned at its limit.
    private const float MinForward = 0.05f;

    private HidingOverlayLayer[] layers = Array.Empty<HidingOverlayLayer>();
    private float[] alphas = Array.Empty<float>();

    private int activeLayer = -1;   // the layer of the spot the player is in, or -1
    private int shownLayer = -1;    // the last one that was: still on screen while it fades out
    private bool anyVisible;

    private HidingSpot activeSpot;
    private Camera viewCamera;
    private Vector2 lookShift;      // where the spot's forward lands on screen, in screen heights
    private float lookWeight;       // 0 = the look sits still, 1 = it follows the camera

    // [ExecuteAlways] also runs this on the copy in a prefab stage opened DURING Play, which is an
    // edit-mode object and must not behave like the live HUD.
    private bool IsLive => Application.IsPlaying(gameObject);

    // Awake/OnDestroy for the static events, as the project does everywhere (docs/UI-System.md
    // §7.1): OnEnable would silently stop hearing the spots the day someone toggles this object.
    private void Awake()
    {
        if (!IsLive) return;

        CollectLayers();
        for (int i = 0; i < layers.Length; i++)
        {
            Graphic graphic = layers[i].Graphic;
            if (graphic == null)
            {
                Debug.LogWarning($"[{nameof(HidingOverlayView)}] Layer '{layers[i].name}' has no " +
                                 "Graphic to draw its look with, so that look never shows.", layers[i]);
                continue;
            }

            // Never a raycast target: a full-screen graphic swallowing clicks for as long as the
            // player hides would be a bug, not a feature.
            graphic.raycastTarget = false;
            SetAlpha(i, 0f);
        }

        HidingEvents.OnEntered += HandleEntered;
        HidingEvents.OnExited += HandleExited;
    }

    private void OnDestroy()
    {
        HidingEvents.OnEntered -= HandleEntered;
        HidingEvents.OnExited -= HandleExited;
    }

    private void OnEnable()
    {
        if (!IsLive)
        {
            ApplyPreview();
            return;
        }

        // After every LateUpdate, Cinemachine's included: the look is placed against the camera
        // this frame is drawn with. Read in Update it would trail the room by a frame and swim.
        Canvas.willRenderCanvases += PublishLook;

        // Switched on with the player already inside (the event came while this was off).
        if (activeLayer < 0 && HidingSpot.Occupied != null) HandleEntered(HidingSpot.Occupied);
    }

    private void OnDisable()
    {
        if (!IsLive)
        {
            HidePreview();
            return;
        }

        Canvas.willRenderCanvases -= PublishLook;

        // Update will not be there to finish a fade: nothing is left half way on the screen.
        activeLayer = -1;
        for (int i = 0; i < layers.Length; i++) SetAlpha(i, 0f);
        anyVisible = false;
    }

    private void HandleEntered(HidingSpot spot)
    {
        activeSpot = spot;
        activeLayer = spot != null ? IndexOf(spot.Type) : -1;

        if (activeLayer >= 0)
        {
            shownLayer = activeLayer;
        }
        else if (spot != null)
        {
            Debug.LogWarning($"[{nameof(HidingOverlayView)}] No look for hiding spot type " +
                             $"'{spot.Type}': add a child with a {nameof(HidingOverlayLayer)} of " +
                             $"that type under '{name}'. The player sees the spot's camera bare.", this);
        }

        // Comes in sitting still in the middle of the screen — see lookEaseSeconds.
        lookWeight = 0f;
        lookShift = Vector2.zero;

        // The HUD lives in its own scene and cannot hold a reference to the level's camera.
        if (viewCamera == null) viewCamera = Camera.main;
    }

    private void HandleExited(HidingSpot spot)
    {
        activeLayer = -1;
        activeSpot = null;
    }

    private void Update()
    {
        if (!IsLive)
        {
            // Outside Play this runs when something in the scene changes — a layer added, a
            // material swapped — which is exactly when the preview has to be put back.
            ApplyPreview();
            return;
        }

        // Unscaled: the fade belongs with the camera blend, which a pause frozen half way through
        // would leave as a half-dark screen under the menu.
        float dt = Time.unscaledDeltaTime;
        bool visible = false;

        for (int i = 0; i < layers.Length; i++)
        {
            float target = i == activeLayer ? 1f : 0f;
            float alpha = alphas[i];

            if (alpha != target)
            {
                float seconds = target > alpha ? fadeInSeconds : fadeOutSeconds;
                alpha = Mathf.MoveTowards(alpha, target, dt / seconds);
                SetAlpha(i, alpha);
            }

            if (alpha > 0f) visible = true;
        }

        anyVisible = visible;

        // Only once the look is fully in does it start following the camera. On the way out the
        // weight is left alone: PublishLook stops measuring, so the look holds where it was while
        // the camera swings back to the player's rig.
        if (activeLayer >= 0 && alphas[activeLayer] >= 1f)
            lookWeight = Mathf.MoveTowards(lookWeight, 1f, dt / lookEaseSeconds);
    }

    /// <summary>
    /// Publishes where the middle of the look sits on screen this frame. Runs right before the
    /// canvases are drawn, and does nothing while no look is on screen.
    /// </summary>
    private void PublishLook()
    {
        if (!anyVisible) return;

        if (activeLayer >= 0) lookShift = MeasureLook();

        float parallax = shownLayer >= 0 && shownLayer < layers.Length ? layers[shownLayer].LookParallax : 0f;
        Vector2 look = lookShift * (Mathf.SmoothStep(0f, 1f, lookWeight) * parallax) + Sway(Time.unscaledTime);

        Shader.SetGlobalVector(LookId, new Vector4(look.x, look.y, 0f, 0f));
    }

    /// <summary>
    /// Where the spot's forward — the direction the door, the table edge or the seam is in — lands
    /// on screen, in screen heights from the middle. Projecting it through the live camera gives
    /// the direction for free: turn right and it lands to the left, look up and it lands lower,
    /// which is how anything fixed in the world moves across the screen.
    /// </summary>
    private Vector2 MeasureLook()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || activeSpot == null) return lookShift;

        Vector3 forward = viewCamera.transform.InverseTransformDirection(activeSpot.InteriorPose.forward);

        // Half a screen height is tan(half the vertical FOV) away from the middle.
        float focal = 0.5f / Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector2 shift = new Vector2(forward.x, forward.y) * (focal / Mathf.Max(forward.z, MinForward));

        return Vector2.ClampMagnitude(shift, maxLookShift);
    }

    private Vector2 Sway(float time)
    {
        return new Vector2(
            swayAmplitude.x * Mathf.Sin(time * TwoPi / Mathf.Max(swaySeconds.x, 0.01f)),
            swayAmplitude.y * Mathf.Sin(time * TwoPi / Mathf.Max(swaySeconds.y, 0.01f) + 1.3f));
    }

    private void SetAlpha(int index, float alpha)
    {
        alphas[index] = alpha;

        Graphic graphic = layers[index].Graphic;
        if (graphic == null) return;

        // Off the canvas altogether while there is nothing to show: at alpha 0 a full-screen quad
        // still costs its fill. Enabled first, so the alpha lands on a renderer that is drawing.
        bool shown = alpha > 0f;
        if (graphic.enabled != shown) graphic.enabled = shown;

        // The renderer's alpha, not Graphic.color: it reaches the shader as the vertex alpha
        // without rebuilding the mesh every frame of the fade.
        graphic.canvasRenderer.SetAlpha(alpha);
    }

    private int IndexOf(EHidingSpotType type)
    {
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && layers[i].Type == type) return i;

        return -1;
    }

    private void CollectLayers()
    {
        // Inactive ones included: a layer a designer switched off in the prefab is still that
        // type's look, and it showing nothing is then visibly their choice rather than a warning.
        layers = GetComponentsInChildren<HidingOverlayLayer>(true);
        if (alphas.Length != layers.Length) alphas = new float[layers.Length];
    }

    // ── Edit-mode preview ───────────────────────────────────────────────────
    //
    // Nothing here touches a serialized value. A layer is shown or hidden through its
    // CanvasRenderer's alpha, which lives only in memory, so there is no preview state to save by
    // accident and the layers' Graphics stay exactly as authored: enabled, white, opaque.
    // That is also why they do not cover the Game view between previews — OnEnable puts every
    // renderer back to 0.

    private void ApplyPreview()
    {
        // The prefab asset itself, outside any scene or prefab stage: nothing is drawn there.
        if (!gameObject.scene.IsValid()) return;

        CollectLayers();

        bool previewing = previewInEditMode && isActiveAndEnabled;
        Vector2 look = Vector2.zero;

        for (int i = 0; i < layers.Length; i++)
        {
            Graphic graphic = layers[i].Graphic;
            if (graphic == null) continue;

            bool shown = previewing && layers[i].Type == previewType;
            graphic.canvasRenderer.SetAlpha(shown ? previewFade : 0f);

            if (shown) look = PreviewLook() * layers[i].LookParallax;
        }

        // A prefab stage opened during Play must not move the look of the live HUD.
        if (!Application.isPlaying) Shader.SetGlobalVector(LookId, new Vector4(look.x, look.y, 0f, 0f));
    }

    private void HidePreview()
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) continue;

            Graphic graphic = layers[i].Graphic;
            if (graphic != null) graphic.canvasRenderer.SetAlpha(0f);
        }

        if (!Application.isPlaying) Shader.SetGlobalVector(LookId, Vector4.zero);
    }

    /// <summary>The shift a camera turned by <see cref="previewLookDegrees"/> would measure.</summary>
    private Vector2 PreviewLook()
    {
        Camera main = Camera.main;
        float fieldOfView = main != null ? main.fieldOfView : previewFieldOfView;
        float focal = 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Short of 90 degrees, where the tangent runs off.
        float right = Mathf.Clamp(previewLookDegrees.x, -80f, 80f) * Mathf.Deg2Rad;
        float up = Mathf.Clamp(previewLookDegrees.y, -80f, 80f) * Mathf.Deg2Rad;

        // Turning right puts the spot's forward to the left; looking up puts it lower.
        Vector2 shift = new Vector2(-Mathf.Tan(right), -Mathf.Tan(up)) * focal;
        return Vector2.ClampMagnitude(shift, maxLookShift);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Also called while the object is still loading, before it is enabled: OnEnable covers that.
        if (IsLive || !isActiveAndEnabled) return;

        ApplyPreview();

        // Outside Play the Game view only redraws when asked to.
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
