using UnityEngine;

/// <summary>
/// The screen of the <see cref="RevealLampTablet"/>: a small map of the UV lamps, seen from above,
/// with a violet light on the one that is lit. When the tablet moves the light, a refresh band
/// sweeps down the screen, the light travels across the map to its new place (dimmed: the lamps are
/// off on the way) and pings when it lands.
///
/// The picture is drawn by the material's shader (WIRED/Puzzles/UV Lamp Tablet Screen); this only
/// feeds it, through a MaterialPropertyBlock so the material asset stays shared. Nothing on the
/// tablet takes the crosshair highlight, which would otherwise pin this renderer's block.
///
/// THE MAP IS LAID OUT AS SEEN FROM IN FRONT OF THE SCREEN: its right is the screen's right and its
/// up is straight ahead, past the screen (the screen's forward, flattened). The mesh is a Quad: its
/// UVs span the screen and its scale gives the width to height ratio.
///
/// Without power (the levers have the lamps off) the screen fades to black.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class RevealLampTabletScreen : MonoBehaviour
{
    // The shader holds this many places; a puzzle with more boxes shows the first ones.
    private const int MaxSlots = 8;

    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int PowerId = Shader.PropertyToID("_Power");
    private static readonly int SlotCountId = Shader.PropertyToID("_SlotCount");
    private static readonly int CursorId = Shader.PropertyToID("_Cursor");
    private static readonly int TravelId = Shader.PropertyToID("_Travel");
    private static readonly int LitId = Shader.PropertyToID("_Lit");
    private static readonly int PingId = Shader.PropertyToID("_Ping");
    private static readonly int RefreshId = Shader.PropertyToID("_Refresh");

    private static readonly int[] SlotIds =
    {
        Shader.PropertyToID("_Slots0"), Shader.PropertyToID("_Slots1"),
        Shader.PropertyToID("_Slots2"), Shader.PropertyToID("_Slots3"),
    };

    [Tooltip("The UV lamps shown on the map (the root of Reflector Symbol Reveal).")]
    [SerializeField] private BoxSymbolRevealLightPositions lamps;

    [Tooltip("Room kept free around the map, as a share of the screen's height.")]
    [SerializeField, Range(0f, 0.45f)] private float margin = 0.17f;

    [Tooltip("Seconds the screen takes to fade in or out when the levers switch the lamps.")]
    [SerializeField, Min(0.01f)] private float powerFadeSeconds = 0.5f;

    [Tooltip("Seconds the refresh band takes to sweep down the screen at the start of a move.")]
    [SerializeField, Min(0.01f)] private float refreshSeconds = 0.45f;

    [Tooltip("Seconds the ring that marks the light landing takes to spread and fade.")]
    [SerializeField, Min(0.01f)] private float pingSeconds = 0.7f;

    private Renderer screenRenderer;
    private MaterialPropertyBlock block;
    private Vector2[] slots = new Vector2[0];
    private float aspect = 1f;

    // The map's frame, fixed by LayOutMap: the two directions it is read along, the metres at its
    // centre, the screen units per metre and how far a dot may go from the centre.
    private Vector3 mapRight = Vector3.right;
    private Vector3 mapAhead = Vector3.forward;
    private Vector2 mapCentre;
    private float mapFit;
    private Vector2 mapHalfExtent;

    private int from;
    private int to;
    private float travel = 1f;
    private float travelSpeed;
    private float power;
    private float lit;
    private float ping = 1f;
    private float refresh = 1f;
    private bool wasLit;

    /// <summary>
    /// Shows the light leaving the place at <paramref name="fromIndex"/> and reaching the one at
    /// <paramref name="toIndex"/> in <paramref name="seconds"/>.
    /// </summary>
    public void ShowMove(int fromIndex, int toIndex, float seconds)
    {
        from = fromIndex;
        to = toIndex;
        travel = 0f;
        travelSpeed = seconds > 0f ? 1f / seconds : float.PositiveInfinity;
        refresh = 0f;
    }

    private void Awake()
    {
        screenRenderer = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (lamps == null)
        {
            Debug.LogWarning($"[{nameof(RevealLampTabletScreen)}] '{name}' has no lamps assigned. The " +
                             "screen stays dark.", this);
            enabled = false;
            Apply();
            return;
        }

        LayOutMap();

        from = to = lamps.Selected;
        wasLit = lamps.IsLit;
        power = lamps.IsPowered ? 1f : 0f;
        lit = wasLit ? 1f : 0f;
        Apply();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        power = Mathf.MoveTowards(power, lamps.IsPowered ? 1f : 0f, dt / powerFadeSeconds);
        travel = Mathf.MoveTowards(travel, 1f, dt * travelSpeed);
        refresh = Mathf.MoveTowards(refresh, 1f, dt / refreshSeconds);

        bool isLit = lamps.IsLit;
        if (isLit && !wasLit) ping = 0f;
        wasLit = isLit;

        // Not on a move: the light sits wherever the lamps say, whoever moved it.
        if (travel >= 1f) from = to = lamps.Selected;

        TrackLamps();

        lit = Mathf.MoveTowards(lit, isLit ? 1f : 0f, dt * 8f);
        ping = Mathf.MoveTowards(ping, 1f, dt / pingSeconds);

        Apply();
    }

    /// <summary>
    /// Places each lamp on the screen, in screen units: x from -aspect/2 to aspect/2, y from -0.5 to
    /// 0.5. The lamps keep their real proportions and the map is centred and fitted inside the margin.
    ///
    /// Done once, with the lamps where the level starts: that fixes the map's centre and scale.
    /// After that <see cref="TrackLamps"/> moves the dots as the lamps move, with the same centre and
    /// scale, so a lamp that is not lit shows where its box is, which is where it will appear.
    /// </summary>
    private void LayOutMap()
    {
        Vector3 scale = transform.lossyScale;
        aspect = Mathf.Abs(scale.y) > 1e-4f ? Mathf.Abs(scale.x / scale.y) : 1f;

        Vector3 ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (ahead.sqrMagnitude < 1e-6f) ahead = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        mapAhead = ahead.normalized;
        mapRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        int count = Mathf.Min(lamps.Count, MaxSlots);
        slots = new Vector2[count];
        if (count == 0) return;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < count; i++)
        {
            Vector2 raw = ToMapPlane(lamps.PositionOf(i));
            min = Vector2.Min(min, raw);
            max = Vector2.Max(max, raw);
        }

        mapCentre = (min + max) * 0.5f;
        Vector2 halfSize = (max - min) * 0.5f;
        float halfWidth = Mathf.Max(0.01f, aspect * 0.5f - margin);
        float halfHeight = Mathf.Max(0.01f, 0.5f - margin);
        mapHalfExtent = new Vector2(halfWidth, halfHeight);

        float fit = float.PositiveInfinity;
        if (halfSize.x > 1e-4f) fit = Mathf.Min(fit, halfWidth / halfSize.x);
        if (halfSize.y > 1e-4f) fit = Mathf.Min(fit, halfHeight / halfSize.y);
        if (float.IsInfinity(fit)) fit = 0f;
        mapFit = fit;

        TrackLamps();
    }

    /// <summary>A world position on the map's plane: its right and its up, in metres.</summary>
    private Vector2 ToMapPlane(Vector3 position) =>
        new Vector2(Vector3.Dot(position, mapRight), Vector3.Dot(position, mapAhead));

    /// <summary>
    /// Puts each dot where its lamp is now. The lit lamp stays where it hangs; the dot of a box's
    /// lamp that is off follows its box, as that is where the lamp will appear. One pushed past the
    /// edge of the map sticks to the edge of the picture instead of leaving it.
    /// </summary>
    private void TrackLamps()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 onMap = (ToMapPlane(lamps.PositionOf(i)) - mapCentre) * mapFit;
            slots[i] = new Vector2(Mathf.Clamp(onMap.x, -mapHalfExtent.x, mapHalfExtent.x),
                                   Mathf.Clamp(onMap.y, -mapHalfExtent.y, mapHalfExtent.y));
        }
    }

    private void Apply()
    {
        screenRenderer.GetPropertyBlock(block);

        block.SetFloat(AspectId, aspect);
        block.SetFloat(PowerId, power);
        block.SetFloat(SlotCountId, slots.Length);
        for (int i = 0; i < SlotIds.Length; i++)
            block.SetVector(SlotIds[i], new Vector4(SlotX(2 * i), SlotY(2 * i), SlotX(2 * i + 1), SlotY(2 * i + 1)));

        block.SetVector(CursorId, new Vector4(SlotX(from), SlotY(from), SlotX(to), SlotY(to)));
        block.SetFloat(TravelId, travel);
        block.SetFloat(LitId, lit);
        block.SetFloat(PingId, ping);
        block.SetFloat(RefreshId, refresh);

        screenRenderer.SetPropertyBlock(block);
    }

    private float SlotX(int index) => index >= 0 && index < slots.Length ? slots[index].x : 0f;
    private float SlotY(int index) => index >= 0 && index < slots.Length ? slots[index].y : 0f;
}
