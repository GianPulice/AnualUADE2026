using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A wall-mounted security camera. Idle, it sweeps its arc from side to side; the moment the
/// player is in view it turns to follow them; once it has lost them for a moment it goes back to
/// sweeping.
///
/// HOW IT SEES. A vertical wedge, not a cone (<see cref="LineOfSight.CheckWedgeSampled"/>): only the
/// horizontal angle off the lens is tested, so height never hides anyone - standing, crouched or
/// straight underneath, the player is caught as soon as the sweep brings an edge of the wedge
/// across them. What still hides them is range, a wall (<see cref="obstacleMask"/>) and a hiding
/// spot (<see cref="PlayerStateManager.IsHidden"/>), the same three things that hide them from
/// <see cref="FieldOfView"/>.
///
/// HOW IT MOVES. Procedurally, by turning two joints of the rig: the pan joint around the mount's
/// up axis and the tilt joint around the lens's horizontal axis. The Sweep_90_Left clip that ships
/// in the FBX is not used - a clip cannot take its speed from an asset, and it cannot turn towards
/// a target.
///
/// THE SCAN. While it is following the player and can see them, the camera scans them: the scan
/// fills their body from the head down behind a bright band, a single ray from the lens follows the
/// band, as wide as the body is at that height (both in <see cref="SecurityScanOverlay"/>), and the
/// lens lights up through the fog (<see cref="scanBeacon"/>). Purely visual - nothing reads whether a
/// scan has completed. <see cref="scanPlayer"/> turns all of it off for a camera that only follows the
/// player with its gaze (SecurityCamera_TrackOnly, a Variant of this prefab).
///
/// Angles are measured in the MOUNT's space, i.e. this transform's: yaw 0 is straight out of the
/// wall (+Z), positive yaw is to the right, elevation 0 is the horizon and positive is up. That is
/// what makes the sweep and the limits independent of how the rig's bones happen to be oriented.
/// </summary>
public class SecurityCamera : MonoBehaviour
{
    public enum EState { Sweeping, Tracking }

    /// <summary>Why the last vision check did or did not see the player.</summary>
    public enum EScanResult { NotScanned, NoPlayer, PlayerHidden, OutOfRange, OutsideWedge, Occluded, Seen }

    [Header("Data")]
    [Tooltip("Sweep, vision and tracking tuning. Shared by every camera that points at the same asset.")]
    [SerializeField] private SO_SecurityCameraData data;

    [Header("Rig")]
    [Tooltip("Joint that pans the camera left and right. On the SecurityCamera prefab: StemJoint.")]
    [SerializeField] private Transform panJoint;

    [Tooltip("Joint that tilts the camera up and down. Must be a descendant of the pan joint. On " +
             "the SecurityCamera prefab: CameraJoint.")]
    [SerializeField] private Transform tiltJoint;

    [Tooltip("Where vision is cast from and where the lens points (its blue +Z axis). A child of " +
             "the tilt joint, placed on the lens. Move it in the prefab if the wedge gizmo does not " +
             "come out of the lens.")]
    [SerializeField] private Transform eye;

    [Header("Placement (this camera only)")]
    [Tooltip("Turns the centre of the sweep away from straight out of the wall, in degrees " +
             "(+ = right). For cameras mounted in a corner.")]
    [SerializeField, Range(-90f, 90f)] private float sweepCenterOffset = 0f;

    [Tooltip("Where in its sweep this camera starts: 0 the left end, 1 the right end. Stagger it " +
             "between neighbouring cameras so they do not move in lockstep.")]
    [SerializeField, Range(0f, 1f)] private float sweepStartPhase = 0.5f;

    [Tooltip("How far below the horizon the camera looks while sweeping, in degrees. Higher " +
             "mounts usually want more. Cosmetic: vision has no vertical limit.")]
    [SerializeField, Range(0f, 80f)] private float sweepTilt = 20f;

    [Header("Wiring")]
    [Tooltip("What blocks the camera's view. Must match FieldOfView.obstacleMask on the Nemesis " +
             "(Default, Ground, Wall, Props) - see docs/CLAUDE.md, Layers. A mask that is wrong " +
             "does not fail: the camera quietly sees through whatever is missing from it.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Seconds between vision checks. A performance knob, not a design value - same as " +
             "FieldOfView.viewDelay.")]
    [SerializeField, Min(0.02f)] private float scanInterval = 0.1f;

    [Header("Scan FX")]
    [Tooltip("On: while it follows the player the camera also scans them - the ray, the scan on the " +
             "body and the light on the lens. Off: it only follows them with its gaze. Everything " +
             "else (sweep, vision, tracking, events) works the same either way.")]
    [SerializeField] private bool scanPlayer = true;

    [Tooltip("Light on the lens while the camera scans, readable through the vision fog. Keep its " +
             "GameObject INACTIVE in the prefab: an active FogBeacon takes one of the fog's 16 " +
             "beacon slots even when it is dark, and the Nemesis's eyes are the first to lose theirs.")]
    [SerializeField] private FogBeacon scanBeacon;

    private EState state = EState.Sweeping;

    // Where the lens points right now, in mount space (see the class comment).
    private float yaw;
    private float elevation;

    private int sweepDirection = 1;
    private float endPauseTimer;
    private float scanTimer;

    private bool seesPlayer;
    private float timeSinceSeen;
    private Vector3 lastSeenPoint;

    private EScanResult lastScanResult = EScanResult.NotScanned;
    private Collider lastBlocker;

    // The scan: on while the camera is following the player AND seeing them. scanStrength is this
    // camera's beam and lens light fading in and out; the scan on the body keeps its own clock.
    private bool scanning;
    private float scanStrength;
    private SecurityScanOverlay scanOverlay;

    // The rig at rest, captured once in Awake. Every frame the joints are rebuilt from these rather
    // than rotated incrementally, so no drift can accumulate over a long session.
    private bool rigReady;
    private Quaternion panRest;
    private Quaternion tiltRest;
    private Vector3 panAxisLocal;
    private Vector3 tiltAxisLocal;
    private float restYaw;
    private float restElevation;

    public EState State => state;
    public bool IsTrackingPlayer => state == EState.Tracking;
    public SO_SecurityCameraData Data => data;

    /// <summary>Where the camera last saw the player. Only meaningful while
    /// <see cref="IsTrackingPlayer"/>.</summary>
    public Vector3 LastSeenPoint => lastSeenPoint;

    /// <summary>
    /// Why the last vision check did or did not see the player. Diagnostic: a camera that "sees
    /// nothing" fails silently - an eye buried in a ceiling, a prop in the way, a player who is
    /// simply out of range all look identical from the outside - and this names which one it is.
    /// Visible in the inspector's Debug mode and on the gizmo.
    /// </summary>
    public EScanResult LastScanResult => lastScanResult;

    /// <summary>What stopped the last ray when <see cref="LastScanResult"/> is
    /// <see cref="EScanResult.Occluded"/>; null otherwise.</summary>
    public Collider LastBlocker => lastBlocker;

    private float SweepCenter => sweepCenterOffset;
    private float SweepMin => Mathf.Clamp(SweepCenter - data.SweepAngle * 0.5f, -data.PanLimit, data.PanLimit);
    private float SweepMax => Mathf.Clamp(SweepCenter + data.SweepAngle * 0.5f, -data.PanLimit, data.PanLimit);

    private void Awake()
    {
        // One aggregated error and the component switches itself off, rather than a
        // NullReferenceException every frame: a camera with a broken rig would otherwise look
        // like a camera that is simply not seeing anybody.
        string missing = "";
        if (data == null) missing += " data";
        if (panJoint == null) missing += " panJoint";
        if (tiltJoint == null) missing += " tiltJoint";
        if (eye == null) missing += " eye";

        if (missing.Length > 0)
        {
            Debug.LogError($"[{nameof(SecurityCamera)}] '{name}' is missing:{missing}. The camera " +
                           $"is disabled.", this);
            enabled = false;
            return;
        }

        if (!tiltJoint.IsChildOf(panJoint) || !eye.IsChildOf(tiltJoint))
        {
            Debug.LogError($"[{nameof(SecurityCamera)}] '{name}': the eye must be under the tilt " +
                           $"joint and the tilt joint under the pan joint, or panning would leave " +
                           $"the lens behind. The camera is disabled.", this);
            enabled = false;
            return;
        }

        if (obstacleMask.value == 0)
        {
            Debug.LogWarning($"[{nameof(SecurityCamera)}] '{name}' has an empty obstacleMask: it " +
                             $"will see the player through every wall.", this);
        }

        CaptureRest();
        InitScanFx();

        yaw = Mathf.Lerp(SweepMin, SweepMax, sweepStartPhase);
        elevation = -sweepTilt;
        ApplyRig();
    }

    /// <summary>
    /// Hides the lens light until there is something to scan, and checks the scan is wired. The
    /// effects are optional to the camera's logic but not to the feature, so a missing piece warns
    /// rather than failing silently - a camera that tracks the player and draws nothing looks broken
    /// in a way nothing would ever report.
    /// </summary>
    private void InitScanFx()
    {
        if (!scanPlayer)
        {
            // A camera that only watches: no lens light, and no complaints about scan assets it
            // will never use.
            if (scanBeacon != null) scanBeacon.gameObject.SetActive(false);
            return;
        }

        if (data.ScanOverlayMaterial == null)
            Debug.LogWarning($"[{nameof(SecurityCamera)}] '{name}': {nameof(SO_SecurityCameraData)} " +
                             $"'{data.name}' has no scan overlay material, so nothing is drawn on the " +
                             $"player's body.", this);

        if (data.ScanBeamMaterial == null)
            Debug.LogWarning($"[{nameof(SecurityCamera)}] '{name}': {nameof(SO_SecurityCameraData)} " +
                             $"'{data.name}' has no scan beam material, so the camera scans without a " +
                             $"beam.", this);

        if (scanBeacon != null) scanBeacon.gameObject.SetActive(false);
        else Debug.LogWarning($"[{nameof(SecurityCamera)}] '{name}' has no scan beacon: its lens " +
                              $"will not light up while it scans.", this);
    }

    private void OnDisable()
    {
        // A listener told "spotted" must always hear the matching "lost".
        if (state == EState.Tracking) StopTracking();

        // And the body must not keep a scan or a beam for a camera that is gone.
        if (scanning) SetScanning(false);
        if (scanOverlay != null) scanOverlay.ClearBeam(this);
        scanStrength = 0f;
        ShowLensLight(false);
    }

    /// <summary>
    /// Reads the rig at rest: each joint's rest rotation, the two axes it turns around expressed
    /// in its parent's space, and where the lens points at rest in mount space.
    ///
    /// The pan axis is the mount's up. The tilt axis is the lens's horizontal right, taken in the
    /// pan joint's space so that it turns along with the pan - the camera always tilts around
    /// whatever its own horizontal axis is at that moment.
    /// </summary>
    private void CaptureRest()
    {
        panRest = panJoint.localRotation;
        tiltRest = tiltJoint.localRotation;

        Vector3 up = transform.up;
        Vector3 lensForward = eye.forward;

        Vector3 right = Vector3.Cross(up, lensForward);
        if (right.sqrMagnitude < 0.000001f) right = transform.right;   // Lens straight up or down.
        right.Normalize();

        panAxisLocal = ToParentSpace(panJoint, up);
        tiltAxisLocal = ToParentSpace(tiltJoint, right);

        Vector3 restLocal = transform.InverseTransformDirection(lensForward);
        restYaw = YawOf(restLocal);
        restElevation = ElevationOf(restLocal);

        rigReady = true;
    }

    private static Vector3 ToParentSpace(Transform joint, Vector3 worldDirection)
    {
        Vector3 local = joint.parent != null
            ? joint.parent.InverseTransformDirection(worldDirection)
            : worldDirection;
        return local.normalized;
    }

    private void Update()
    {
        if (!rigReady) return;

        // Same guard as FieldOfView: this Update is its own.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        float deltaTime = Time.deltaTime;

        scanTimer += deltaTime;
        if (scanTimer >= scanInterval)
        {
            scanTimer = 0f;
            Scan();
        }

        if (state == EState.Tracking) TickTracking(deltaTime);
        else TickSweep(deltaTime);

        ApplyRig();
        TickScan(deltaTime);
    }

    private void Scan()
    {
        seesPlayer = CanSeePlayer(out Vector3 bodyCenter);
        if (!seesPlayer) return;

        lastSeenPoint = bodyCenter;
        timeSinceSeen = 0f;

        if (state != EState.Tracking) StartTracking();
    }

    /// <summary>
    /// Whether the player is in view right now. On success <paramref name="bodyCenter"/> is the
    /// centre of their capsule - what the camera aims at - and not the sample that got through,
    /// which jumps between feet, centre and head from one scan to the next.
    /// </summary>
    private bool CanSeePlayer(out Vector3 bodyCenter)
    {
        bodyCenter = Vector3.zero;
        lastBlocker = null;

        PlayerStateManager player = PlayerRegistry.Current;
        Collider body = player != null ? player.CapsuleColl : null;
        if (body == null)
        {
            lastScanResult = EScanResult.NoPlayer;
            return false;
        }

        if (player.IsHidden)
        {
            lastScanResult = EScanResult.PlayerHidden;
            return false;
        }

        if (!LineOfSight.CheckWedgeSampled(eye.position, eye.forward, transform.up, body,
                                           data.ViewAngle, data.ViewRange, obstacleMask, out _))
        {
            Diagnose(body.bounds.center);
            return false;
        }

        bodyCenter = body.bounds.center;
        lastScanResult = EScanResult.Seen;
        return true;
    }

    /// <summary>
    /// Works out which of the three tests rejected the player, for <see cref="LastScanResult"/>.
    /// Only runs on a miss, and only re-asks about the body centre - an approximation of the
    /// three-sample test it explains, which is all a diagnostic needs.
    /// </summary>
    private void Diagnose(Vector3 point)
    {
        Vector3 toPoint = point - eye.position;
        if (toPoint.sqrMagnitude > data.ViewRange * data.ViewRange)
        {
            lastScanResult = EScanResult.OutOfRange;
            return;
        }

        Vector3 flatFront = Vector3.ProjectOnPlane(eye.forward, transform.up);
        Vector3 flatToPoint = Vector3.ProjectOnPlane(toPoint, transform.up);
        if (flatToPoint.sqrMagnitude > 0.000001f &&
            Vector3.Angle(flatFront, flatToPoint) > data.ViewAngle * 0.5f)
        {
            lastScanResult = EScanResult.OutsideWedge;
            return;
        }

        lastScanResult = EScanResult.Occluded;
        if (Physics.Raycast(eye.position, toPoint.normalized, out RaycastHit hit, toPoint.magnitude,
                            obstacleMask, QueryTriggerInteraction.Ignore))
        {
            lastBlocker = hit.collider;
        }
    }

    private void TickSweep(float deltaTime)
    {
        float speed = data.SweepSpeed * deltaTime;

        // Back to the idle tilt at the sweep's own pace, so coming out of a chase reads as the
        // camera calmly settling back into its routine rather than snapping to it.
        elevation = Mathf.MoveTowards(elevation, -sweepTilt, speed);

        if (endPauseTimer > 0f)
        {
            endPauseTimer -= deltaTime;
            return;
        }

        // If tracking left the camera outside its arc, this same MoveTowards walks it back to the
        // nearest end, where it turns round as it would at the end of any sweep.
        float target = sweepDirection > 0 ? SweepMax : SweepMin;
        yaw = Mathf.MoveTowards(yaw, target, speed);

        if (Mathf.Approximately(yaw, target))
        {
            sweepDirection = -sweepDirection;
            endPauseTimer = data.SweepEndPause;
        }
    }

    private void TickTracking(float deltaTime)
    {
        timeSinceSeen += deltaTime;

        if (!seesPlayer && timeSinceSeen >= data.LoseSightDelay)
        {
            StopTracking();
            return;
        }

        // While in view, aim at where the player is THIS frame rather than where the last scan
        // found them, or the camera lags a tenth of a second behind and visibly jitters. Out of
        // view, hold on the last place they were seen until the delay runs out.
        Vector3 aimPoint = lastSeenPoint;
        if (seesPlayer && PlayerRegistry.Current != null && PlayerRegistry.Current.CapsuleColl != null)
            aimPoint = PlayerRegistry.Current.CapsuleColl.bounds.center;

        // Yaw from the pan joint and elevation from the tilt joint: those are the points each axis
        // actually turns around, so aiming from them puts the lens on target instead of a few
        // degrees off it.
        Vector3 fromPan = transform.InverseTransformDirection(aimPoint - panJoint.position);
        Vector3 fromTilt = transform.InverseTransformDirection(aimPoint - tiltJoint.position);

        float targetYaw = Mathf.Clamp(YawOf(fromPan), -data.PanLimit, data.PanLimit);
        float targetElevation = Mathf.Clamp(ElevationOf(fromTilt), -data.MaxTiltDown, data.MaxTiltUp);

        float speed = data.TrackingSpeed * deltaTime;
        yaw = Mathf.MoveTowards(yaw, targetYaw, speed);
        elevation = Mathf.MoveTowards(elevation, targetElevation, speed);
    }

    private void StartTracking()
    {
        state = EState.Tracking;
        endPauseTimer = 0f;
        SecurityCameraEvents.PlayerSpotted(this);
    }

    private void StopTracking()
    {
        state = EState.Sweeping;
        seesPlayer = false;

        // Carry on towards the end the camera is already facing, then turn round as usual.
        sweepDirection = yaw >= SweepCenter ? 1 : -1;

        SecurityCameraEvents.PlayerLost(this);
    }

    /// <summary>
    /// The scan runs while the camera is following the player AND can see them. During the
    /// lose-sight delay the camera is staring at where they were, and a beam still scanning that
    /// spot would hand the player a tell the camera itself no longer has.
    /// </summary>
    private void TickScan(float deltaTime)
    {
        bool wanted = scanPlayer && state == EState.Tracking && seesPlayer;
        if (wanted != scanning) SetScanning(wanted);

        float target = scanning ? 1f : 0f;
        float fade = data.ScanFadeTime;
        scanStrength = fade > 0f ? Mathf.MoveTowards(scanStrength, target, deltaTime / fade) : target;

        bool visible = scanStrength > 0f && scanOverlay != null;
        ShowLensLight(visible);

        if (!visible)
        {
            if (scanOverlay != null) scanOverlay.ClearBeam(this);
            return;
        }

        if (scanBeacon != null) scanBeacon.IntensityScale = scanStrength;

        // The beam itself is drawn on the player's body (see SecurityScanOverlay): all it needs from
        // the camera is where the light comes from and how much of it there is.
        scanOverlay.SetBeam(this, eye.position, scanStrength);
    }

    private void SetScanning(bool on)
    {
        scanning = on;

        if (!on)
        {
            // The overlay is kept: the beam still has to fade out on it.
            if (scanOverlay != null) scanOverlay.RemoveScanner(this);
            return;
        }

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return;

        Transform bodyRoot = player.PlayerBody != null ? player.PlayerBody : player.transform;
        Renderer body = bodyRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        if (body == null)
        {
            Debug.LogWarning($"[{nameof(SecurityCamera)}] '{name}': the player has no " +
                             $"SkinnedMeshRenderer under '{bodyRoot.name}', so there is no body to " +
                             $"scan.", this);
            return;
        }

        scanOverlay = SecurityScanOverlay.For(body, player.CapsuleColl, data.ScanOverlayMaterial);
        if (scanOverlay != null) scanOverlay.AddScanner(this, data);
    }

    /// <summary>
    /// Shows or hides the lens light. The beacon goes on and off with its GameObject rather than
    /// sitting at zero brightness, because a registered beacon holds a fog slot either way - see the
    /// tooltip on <see cref="scanBeacon"/>.
    /// </summary>
    private void ShowLensLight(bool visible)
    {
        if (scanBeacon != null && scanBeacon.gameObject.activeSelf != visible)
            scanBeacon.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Rebuilds both joints from their rest rotation plus the current offsets. Positive tilt
    /// rotation about the lens's right axis pitches it down, hence rest minus current elevation.
    /// </summary>
    private void ApplyRig()
    {
        float panOffset = Mathf.DeltaAngle(restYaw, yaw);
        float tiltOffset = restElevation - elevation;

        panJoint.localRotation = Quaternion.AngleAxis(panOffset, panAxisLocal) * panRest;
        tiltJoint.localRotation = Quaternion.AngleAxis(tiltOffset, tiltAxisLocal) * tiltRest;
    }

    private static float YawOf(Vector3 mountDirection) =>
        Mathf.Atan2(mountDirection.x, mountDirection.z) * Mathf.Rad2Deg;

    private static float ElevationOf(Vector3 mountDirection) =>
        Mathf.Atan2(mountDirection.y, new Vector2(mountDirection.x, mountDirection.z).magnitude) * Mathf.Rad2Deg;

#if UNITY_EDITOR
    /// <summary>
    /// The sweep arc, the pan limits and the vision wedge, to scale, with their values. OnDrawGizmos
    /// and not OnDrawGizmosSelected, like the rest of the project's sensor gizmos: a selected-only
    /// gizmo is invisible in Prefab Mode, which is where the tuning happens. Values are read from
    /// the asset, never from a local copy.
    ///
    /// Drawn flat at lens height, because the wedge has no vertical limit: read it as a wall of
    /// vision standing on that outline, from the floor to the ceiling.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (data == null) return;

        Vector3 up = transform.up;
        Vector3 origin = eye != null ? eye.position : transform.position;
        Vector3 wallOut = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (wallOut.sqrMagnitude < 0.000001f) return;

        Vector3 Heading(float yawDegrees) => Quaternion.AngleAxis(yawDegrees, up) * wallOut;

        float minYaw = Mathf.Clamp(sweepCenterOffset - data.SweepAngle * 0.5f, -data.PanLimit, data.PanLimit);
        float maxYaw = Mathf.Clamp(sweepCenterOffset + data.SweepAngle * 0.5f, -data.PanLimit, data.PanLimit);

        // Pan limits: how far tracking can ever turn.
        Handles.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        Handles.DrawLine(origin, origin + Heading(-data.PanLimit) * 1.2f);
        Handles.DrawLine(origin, origin + Heading(data.PanLimit) * 1.2f);

        // Sweep arc: where the idle camera points.
        Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Handles.DrawWireArc(origin, up, Heading(minYaw), maxYaw - minYaw, 1f);
        Handles.DrawLine(origin, origin + Heading(minYaw));
        Handles.DrawLine(origin, origin + Heading(maxYaw));

        // Vision wedge where the lens points now; in Edit mode, at the centre of the sweep.
        Vector3 look = Application.isPlaying && eye != null
            ? Vector3.ProjectOnPlane(eye.forward, up).normalized
            : Heading(sweepCenterOffset);
        if (look.sqrMagnitude < 0.000001f) look = wallOut;

        bool tracking = Application.isPlaying && state == EState.Tracking;
        Color wedge = tracking ? new Color(1f, 0.2f, 0.2f, 1f) : new Color(0.2f, 1f, 0.4f, 1f);
        Vector3 wedgeStart = Quaternion.AngleAxis(-data.ViewAngle * 0.5f, up) * look;

        Handles.color = new Color(wedge.r, wedge.g, wedge.b, 0.08f);
        Handles.DrawSolidArc(origin, up, wedgeStart, data.ViewAngle, data.ViewRange);
        Handles.color = wedge;
        Handles.DrawWireArc(origin, up, wedgeStart, data.ViewAngle, data.ViewRange);
        Handles.DrawLine(origin, origin + wedgeStart * data.ViewRange);
        Handles.DrawLine(origin, origin + Quaternion.AngleAxis(data.ViewAngle, up) * wedgeStart * data.ViewRange);

        string label = $"{data.ViewRange:0.#} m · {data.ViewAngle:0}°\nsweep {data.SweepAngle:0}° @ {data.SweepSpeed:0}°/s";
        if (Application.isPlaying)
        {
            label += $"\n{lastScanResult}";
            if (lastBlocker != null) label += $" by '{lastBlocker.name}'";
        }
        Handles.Label(origin + look * data.ViewRange, label);

        if (tracking)
        {
            Handles.color = wedge;
            Handles.DrawDottedLine(origin, lastSeenPoint, 4f);
        }
    }
#endif
}
