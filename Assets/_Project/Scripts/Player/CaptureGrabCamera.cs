using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// The shot of the grab. On a capture the view leaves the gameplay rig, goes to a spot from where
/// the grab can actually be seen, and holds there until the capture's black cover has closed.
///
/// ── WHY A CAMERA OF ITS OWN ────────────────────────────────────────────────
///
/// The stand-up pans drive the gameplay rig's own axes; this cannot. That rig orbits the player's
/// pivot, within ±39° of pitch, with a Deoccluder pulling it in: a framing of two bodies 1.7 m
/// apart, one of them 2.5 m tall, is not a pose it has. And the player's camera loses its signal
/// at the grab (PlayerCameraFeed drops to black in 0.6 s) for as long as that rig is the live
/// one — a cut to another camera is precisely what takes the feed off the screen.
///
/// So, the same technique as ModuleExplosionSequence: a CinemachineCamera spawned on the output
/// camera's own pose, which makes the cut invisible, and moved by this component.
///
/// ── THE SPOT IS CHOSEN BY WHAT IT SEES ─────────────────────────────────────
///
/// A fixed offset is wrong half the time: in a corridor it is inside the wall, and from behind
/// either body the other one is hidden. So every candidate — a few angles round the pair on both
/// sides, a few distances, three heights — is scored on five points of the grab itself: the
/// Nemesis's two wrists where they hold, the player's head and chest, the Nemesis's head. A point
/// counts as seen when no level geometry stands between it and the camera AND the other body is
/// not in the way (the wrists disappear behind the player from the back, the player behind the
/// Nemesis from its back). A spot that sees a wrist and the player's head beats any that does not;
/// past that, more points seen wins, then how close it is to the authored framing
/// (<see cref="SO_CaptureGrabConfig.CameraYaw"/> and friends).
///
/// Three details that make the answer hold:
///   - the sight lines are cast FROM the grab out to the camera. Cast the other way, a camera
///     inside a wall sees everything: a collider is not hit from within;
///   - "solid" is the gameplay rig's own CinemachineDeoccluder mask, the one answer the project
///     already keeps for what the camera may not go through;
///   - the pair it scores is where the two bodies WILL stand (<see cref="CaptureGrabStaging"/>),
///     not where they are on the frame of the grab, and the spot is chosen once: re-picked while
///     they shuffle into place it would jump with them.
///
/// The view ORBITS to the spot — angle, radius and height eased separately — because the gameplay
/// camera usually starts behind the Nemesis, and the straight line from there to a side view goes
/// through its arms. With something in the way of the orbit it cuts instead. The lens opens when
/// the spot is closer than authored, so the pair stays the same size in frame.
///
/// ── THE FOG ────────────────────────────────────────────────────────────────
///
/// The vision fog is measured from the player and can start 2 m out, which leaves the Nemesis's
/// spread arms in it. The shot holds <see cref="SO_CaptureGrabConfig.FogPreset"/> on the fog's
/// stack for as long as it is up — only where that opens the view: in an area whose fog already
/// starts further out, it is left alone.
///
/// ── HOW LONG ───────────────────────────────────────────────────────────────
///
/// It holds <see cref="PlayerEvents.CaptureShotSeconds"/> while enabled: the black cover, the
/// respawn and the escape's defeat all wait that long before their own delays. The shot itself
/// ends when the capture does — the respawn, a defeat screen, a scripted cinematic taking over —
/// with a cut back to the rig, under whatever is covering the screen by then.
///
/// SETUP: on the Player prefab's root, with SO_CaptureGrabConfig. Tools > Player > Setup Capture
/// Grab adds it. Without it the capture is covered at once, as it always was.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Player/Capture Grab Camera")]
public class CaptureGrabCamera : MonoBehaviour
{
    [SerializeField] private SO_CaptureGrabConfig config;

    // Above a hiding spot's interior camera (100), below the escape's shots and the defeat camera
    // (1000): those take the screen from this one, never the other way round.
    private const int ShotPriority = 500;

    // ── Candidates ──
    // Each combination is tried on both sides of the pair, the gameplay camera's own side first.
    private static readonly float[] YawSteps = { 0f, -20f, 20f, -40f, 40f };   // degrees, added to the authored yaw
    private static readonly float[] DistanceSteps = { 1f, 0.8f, 0.6f, 0.45f }; // of the authored distance
    private static readonly float[] HeightSteps = { 0f, 0.9f, -0.7f };         // metres, added to the authored height

    // Outside this range one body stands in front of the other.
    private const float MinYaw = 30f;
    private const float MaxYaw = 135f;

    // ── Scoring ──
    // Seeing the grab at all outweighs everything else put together; one more point seen outweighs
    // any single preference, though not all four of them at once.
    private const float SeesGrabScore = 100f;
    private const float PointScore = 10f;
    private const float YawPenaltyPerDegree = 0.1f;
    private const float DistancePenalty = 10f;
    private const float HeightPenalty = 3f;
    private const float OtherSidePenalty = 2f;

    // Staying where the gameplay camera already was is the floor of the search: kept only when it
    // sees two points more than every authored spot does.
    private const float StayPutPenalty = 20f;

    // ── What is tested ──
    private const int WristLeft = 0, WristRight = 1, PlayerHead = 2, PlayerChest = 3, NemesisHead = 4;
    private const int KeyPointCount = 5;
    private const float PlayerChestBelowHead = 0.45f;

    // The two bodies as capsules, for "is the other one in the way". The Nemesis's is its torso,
    // leaning from the hips to the head; its legs hide nothing that matters.
    private const float PlayerBodyRadius = 0.25f;
    private const float PlayerBodyFloor = 0.3f;
    private const float NemesisBodyRadius = 0.35f;
    private const float NemesisHipHeight = 1.2f;

    private const float SightRadius = 0.08f;
    private const float CameraClearance = 0.25f;
    private const int PathSamples = 3;

    // Doors live on this layer, and so does every pickup, valve and handle: anything whose box
    // measures less than MinOccluderSize across is too small to hide a body and is seen past.
    private const string DoorLayerName = "Interactable";
    private const float MinOccluderSize = 0.6f;

    // The lens never opens past this to make up for a short distance.
    private const float MaxFieldOfView = 85f;

    private struct Shot
    {
        public float Angle;    // degrees round the pair from behind the player's back; the sign is the side
        public float Radius;   // flat metres from the focus
        public float Height;   // metres above the focus
    }

    private readonly Vector3[] keyPoints = new Vector3[KeyPointCount];
    private readonly RaycastHit[] castHits = new RaycastHit[16];
    private readonly Collider[] overlapHits = new Collider[16];
    private Vector3 playerLow, playerHigh, nemesisLow, nemesisHigh;

    private PlayerStateManager player;
    private NemesisStateManager nemesis;
    private CaptureGrabStaging staging;
    private LayerMask solidMask;

    private CinemachineCamera shotCamera;
    private CancellationTokenSource shotCts;
    private VisionRangeController fog;
    private SO_VisionFogConfig pushedFog;
    private bool warnedNoBrain;

    private void Awake()
    {
        player = GetComponentInParent<PlayerStateManager>();
        staging = GetComponent<CaptureGrabStaging>();

        // What the camera may not go through: the gameplay rig's own answer, so the two cannot
        // drift apart (see Layers in docs/CLAUDE.md). Plus the doors, which that rig is allowed to
        // swing through and a shot is not allowed to look at the back of.
        CinemachineDeoccluder deoccluder = player != null ? player.GetComponentInChildren<CinemachineDeoccluder>(true) : null;
        solidMask = deoccluder != null ? deoccluder.CollideAgainst : (LayerMask)Physics.DefaultRaycastLayers;

        int doors = LayerMask.NameToLayer(DoorLayerName);
        if (doors >= 0) solidMask |= 1 << doors;

        // Static events: subscribed in Awake and released in OnDestroy, per docs/CLAUDE.md.
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        GameResultManager.OnGameResult -= HandleGameResult;
    }

    // The standing value follows the component's enabled state and not the subscription above:
    // switching the shot off has to give the capture its old timing back.
    private void OnEnable()
    {
        if (config != null) PlayerEvents.CaptureShotSeconds = config.ShotSeconds;
    }

    private void OnDisable()
    {
        PlayerEvents.CaptureShotSeconds = 0f;
        CancelShot();
        EndShot(null);
    }

    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        if (!isActiveAndEnabled || config == null || captured == null || captured != player) return;

        // A scripted cinematic owns the screen while it plays.
        if (CinematicState.IsPlaying) return;

        // Only the Nemesis's own grab has two bodies to frame.
        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis == null || nemesis.CurrentStateKey != NemesisStateManager.ENemesisState.Catch) return;

        CinemachineBrain brain = FindBrain();
        if (brain == null) return;

        CancelShot();
        shotCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunShot(brain, shotCts.Token).Forget();
    }

    /// <summary>The run ended instead of respawning. The result screen clears the black cover so
    /// it can be read, and what it has behind it is the gameplay rig, as before the shot existed.</summary>
    private void HandleGameResult(GameResultModel result) => CancelShot();

    private void CancelShot()
    {
        shotCts?.Cancel();
        shotCts?.Dispose();
        shotCts = null;
    }

    private async UniTaskVoid RunShot(CinemachineBrain brain, CancellationToken token)
    {
        try
        {
            BeginShot(brain);

            // Next frame: the event is raised before the Nemesis turns to the player and, for a
            // hidden one, around the spot putting them outside; and the staging plans the pair at
            // the end of that same frame.
            await UniTask.Yield(PlayerLoopTiming.Update, token);

            Transform cam = shotCamera.transform;
            Quaternion startRotation = cam.rotation;
            float startFov = shotCamera.Lens.FieldOfView;

            // Chosen against where the two bodies will stand; ridden on where they are.
            Transform nemesisBody = NemesisBody;
            if (staging == null || !staging.TryGetStagedPair(out Vector3 nemesisAt, out Vector3 playerAt))
            {
                nemesisAt = nemesisBody.position;
                playerAt = player.transform.position;
            }

            Vector3 stagedAxis = PairAxis(nemesisAt, playerAt);
            Vector3 stagedFocus = FocusOf(nemesisAt, playerAt);

            Shot start = ShotAt(FocusOf(nemesisBody.position, player.transform.position),
                                PairAxis(nemesisBody.position, player.transform.position), cam.position);
            Shot end = ChooseShot(nemesisAt, playerAt, stagedFocus, stagedAxis, start);

            float endFov = ShotFieldOfView(startFov, end.Radius);
            float sweep = Mathf.DeltaAngle(start.Angle, end.Angle);   // The short way round.

            float duration = PathIsClear(stagedFocus, stagedAxis, start, sweep, end) ? config.CameraMoveSeconds : 0f;
            float elapsed = 0f;

            while (IsCapturing())
            {
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float eased = t * t * (3f - 2f * t);

                // Every frame, after the move too: the bodies settle into the pair's distance
                // during it, and the shot rides on the point between them.
                Vector3 axis = PairAxis(nemesisBody.position, player.transform.position);
                Vector3 focus = FocusOf(nemesisBody.position, player.transform.position);

                Shot now = new Shot
                {
                    Angle = start.Angle + sweep * eased,
                    Radius = Mathf.Lerp(start.Radius, end.Radius, eased),
                    Height = Mathf.Lerp(start.Height, end.Height, eased),
                };

                // The aim gets there in half the time the position does: the pair is in frame for
                // the trip, instead of the trip being a pan across the room.
                float aim = Mathf.Clamp01(t * 2f);
                aim = aim * aim * (3f - 2f * aim);

                Vector3 at = PositionOf(focus, axis, now);
                Quaternion look = Quaternion.LookRotation(focus - at, Vector3.up);
                cam.SetPositionAndRotation(at, Quaternion.Slerp(startRotation, look, aim));

                LensSettings lens = shotCamera.Lens;
                lens.FieldOfView = Mathf.Lerp(startFov, endFov, eased);
                shotCamera.Lens = lens;

                // Update timing: CinemachineBrain reads the camera in its LateUpdate, so the pose
                // is already in place for this frame. Unscaled, like the capture's own chain.
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.unscaledDeltaTime;
            }
        }
        catch (OperationCanceledException)
        {
            // Disabled, destroyed, a result screen or a new capture. The finally still runs.
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
        finally
        {
            EndShot(brain);
        }
    }

    /// <summary>The grab is still on: the player is held and nothing else has taken the screen.</summary>
    private bool IsCapturing() =>
        player != null && nemesis != null && player.IsDisabled && player.IsRecoveringFromCapture &&
        !CinematicState.IsPlaying;

    // ── The pair ─────────────────────────────────────────────────────────────────────────

    /// <summary>The Nemesis as it is seen: the pivot of its animated model, not of its agent. See
    /// <see cref="CaptureGrabStaging"/>, which stages the pair off the same point.</summary>
    private Transform NemesisBody =>
        nemesis.AnimController != null ? nemesis.AnimController.transform : nemesis.transform;

    /// <summary>The flat direction from the Nemesis to the player.</summary>
    private Vector3 PairAxis(Vector3 nemesisAt, Vector3 playerAt)
    {
        Vector3 axis = playerAt - nemesisAt;
        axis.y = 0f;

        if (axis.sqrMagnitude > 0.0025f) return axis.normalized;

        // One on top of the other: the Nemesis is still facing whoever it grabbed.
        return Vector3.ProjectOnPlane(nemesis.transform.forward, Vector3.up).normalized;
    }

    /// <summary>The point the shot looks at: half way between the two, at the hands' height.</summary>
    private Vector3 FocusOf(Vector3 nemesisAt, Vector3 playerAt)
    {
        Vector3 focus = (nemesisAt + playerAt) * 0.5f;
        focus.y = Mathf.Min(nemesisAt.y, playerAt.y) + config.FocusHeight;
        return focus;
    }

    private static Vector3 PositionOf(Vector3 focus, Vector3 axis, Shot shot) =>
        focus + Quaternion.AngleAxis(shot.Angle, Vector3.up) * axis * shot.Radius + Vector3.up * shot.Height;

    private static Shot ShotAt(Vector3 focus, Vector3 axis, Vector3 position)
    {
        Vector3 fromFocus = position - focus;
        Vector3 flat = Vector3.ProjectOnPlane(fromFocus, Vector3.up);
        return new Shot
        {
            Angle = Vector3.SignedAngle(axis, flat, Vector3.up),
            Radius = flat.magnitude,
            Height = fromFocus.y,
        };
    }

    /// <summary>The five points of the grab a spot is scored on, and the two bodies as capsules.</summary>
    private void BuildKeyPoints(Vector3 nemesisAt, Vector3 playerAt, Vector3 axis)
    {
        Vector3 right = Vector3.Cross(Vector3.up, axis);

        // The wrists are the Nemesis's: they hold at its arms' reach, wherever the player ended up.
        Vector3 grip = nemesisAt + axis * config.GripReach + Vector3.up * config.GripHeight;
        keyPoints[WristLeft] = grip - right * config.GripHalfWidth;
        keyPoints[WristRight] = grip + right * config.GripHalfWidth;

        keyPoints[PlayerHead] = playerAt + Vector3.up * config.PlayerHeadHeight;
        keyPoints[PlayerChest] = playerAt + Vector3.up * (config.PlayerHeadHeight - PlayerChestBelowHead);
        keyPoints[NemesisHead] = nemesisAt + axis * config.NemesisHeadForward + Vector3.up * config.NemesisHeadHeight;

        playerLow = playerAt + Vector3.up * PlayerBodyFloor;
        playerHigh = keyPoints[PlayerHead];
        nemesisLow = nemesisAt + Vector3.up * NemesisHipHeight;
        nemesisHigh = keyPoints[NemesisHead];
    }

    // ── Choosing the spot ────────────────────────────────────────────────────────────────

    private Shot ChooseShot(Vector3 nemesisAt, Vector3 playerAt, Vector3 focus, Vector3 axis, Shot start)
    {
        BuildKeyPoints(nemesisAt, playerAt, axis);

        // Where the view already is, turned to the pair.
        Shot best = start;
        float bestScore = Score(focus, axis, start) - StayPutPenalty;

        float nearSide = start.Angle >= 0f ? 1f : -1f;

        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? nearSide : -nearSide;

            foreach (float yawStep in YawSteps)
            foreach (float distanceStep in DistanceSteps)
            for (int h = 0; h < HeightSteps.Length; h++)
            {
                Shot candidate = new Shot
                {
                    Angle = side * Mathf.Clamp(config.CameraYaw + yawStep, MinYaw, MaxYaw),
                    Radius = config.CameraDistance * distanceStep,
                    Height = config.CameraHeight + HeightSteps[h],
                };

                float score = Score(focus, axis, candidate)
                              - Mathf.Abs(yawStep) * YawPenaltyPerDegree
                              - (1f - distanceStep) * DistancePenalty
                              - (h == 0 ? 0f : HeightPenalty)
                              - (s == 0 ? 0f : OtherSidePenalty);

                // Strictly better only: the loops run in order of preference, so a tie keeps the
                // earlier, more authored one.
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    /// <summary>How much of the grab a spot sees. Negative infinity for a spot inside geometry.</summary>
    private float Score(Vector3 focus, Vector3 axis, Shot shot)
    {
        Vector3 at = PositionOf(focus, axis, shot);
        if (!IsFree(at)) return float.NegativeInfinity;

        int seen = 0;
        bool wrist = false, head = false;

        for (int i = 0; i < KeyPointCount; i++)
        {
            if (!Sees(at, i)) continue;

            seen++;
            if (i == WristLeft || i == WristRight) wrist = true;
            if (i == PlayerHead) head = true;
        }

        return seen * PointScore + (wrist && head ? SeesGrabScore : 0f);
    }

    private bool Sees(Vector3 from, int point)
    {
        Vector3 to = keyPoints[point];

        // The other body in the way: the wrists and the Nemesis's head behind the player, the
        // player behind the Nemesis.
        bool playerOwned = point == PlayerHead || point == PlayerChest;
        float gap = playerOwned
            ? SegmentDistance(from, to, nemesisLow, nemesisHigh) - NemesisBodyRadius
            : SegmentDistance(from, to, playerLow, playerHigh) - PlayerBodyRadius;
        if (gap < 0f) return false;

        return !LevelBetween(to, from);
    }

    /// <summary>
    /// Whether level geometry stands between a point of the grab and the camera. Cast from the
    /// grab outwards: a collider is not hit from within, so cast from a camera inside a wall the
    /// same line comes back clear.
    /// </summary>
    private bool LevelBetween(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= SightRadius) return false;

        int count = Physics.SphereCastNonAlloc(from, SightRadius, delta / distance, castHits, distance - SightRadius,
                                               solidMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];

            // distance 0 is something the point itself already touches: the body it belongs to.
            if (hit.distance <= 0f || IsBody(hit.collider)) continue;
            if (hit.collider.bounds.size.sqrMagnitude < MinOccluderSize * MinOccluderSize) continue;

            return true;
        }
        return false;
    }

    /// <summary>Whether the camera fits at <paramref name="at"/> with nothing of the level on it.</summary>
    private bool IsFree(Vector3 at)
    {
        int count = Physics.OverlapSphereNonAlloc(at, CameraClearance, overlapHits, solidMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsBody(overlapHits[i])) return false;
        }
        return true;
    }

    private bool IsBody(Collider other)
    {
        Transform t = other.transform;
        return t.IsChildOf(player.transform) || t.IsChildOf(nemesis.transform);
    }

    /// <summary>
    /// Whether the orbit from <paramref name="start"/> to <paramref name="end"/> stays out of the
    /// level and in sight of the pair. Sampled, not swept: enough to tell a pillar or a wall end
    /// in the way, which is when the shot cuts instead of travelling.
    /// </summary>
    private bool PathIsClear(Vector3 focus, Vector3 axis, Shot start, float sweep, Shot end)
    {
        for (int i = 1; i <= PathSamples; i++)
        {
            float t = i / (PathSamples + 1f);
            Shot sample = new Shot
            {
                Angle = start.Angle + sweep * t,
                Radius = Mathf.Lerp(start.Radius, end.Radius, t),
                Height = Mathf.Lerp(start.Height, end.Height, t),
            };

            Vector3 at = PositionOf(focus, axis, sample);
            if (!IsFree(at) || LevelBetween(focus, at)) return false;
        }
        return true;
    }

    /// <summary>
    /// The shot's field of view: the authored one at the authored distance, opened up when the
    /// camera had to stop short so the pair fills the frame the same.
    /// </summary>
    private float ShotFieldOfView(float gameplayFov, float radius)
    {
        if (config.FieldOfView <= 0f) return gameplayFov;

        float half = Mathf.Tan(config.FieldOfView * 0.5f * Mathf.Deg2Rad);
        float widened = 2f * Mathf.Atan(half * config.CameraDistance / Mathf.Max(radius, 0.01f)) * Mathf.Rad2Deg;
        return Mathf.Clamp(widened, config.FieldOfView, MaxFieldOfView);
    }

    /// <summary>Shortest distance between the segments <paramref name="p1"/>-<paramref name="q1"/>
    /// and <paramref name="p2"/>-<paramref name="q2"/>.</summary>
    private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        const float Epsilon = 1e-6f;

        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        float s, t;

        if (a <= Epsilon && e <= Epsilon) return r.magnitude;

        if (a <= Epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;

                s = denominator > Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                t = (b * s + f) / e;

                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
    }

    // ── Camera and fog ───────────────────────────────────────────────────────────────────

    private void BeginShot(CinemachineBrain brain)
    {
        EndShot(null);

        Camera output = brain.OutputCamera;

        GameObject go = new GameObject("CaptureGrabCamera");
        go.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);

        shotCamera = go.AddComponent<CinemachineCamera>();
        shotCamera.Lens = LensSettings.FromCamera(output);
        shotCamera.Priority = ShotPriority;

        // It starts exactly where the gameplay camera is, so the cut is invisible and the travel
        // is entirely this component's.
        CutFor(brain);

        // The fog's controller drives shader globals, so there is only ever one; a scene played
        // on its own has none, and then there is no fog to open either.
        if (config.FogPreset == null) return;
        if (fog == null) fog = FindAnyObjectByType<VisionRangeController>();
        if (fog == null) return;

        // Only ever to open the view. In a lit room the area's own fog already starts further
        // out than the shot's, and pushing it there would close the room in around the grab.
        SO_VisionFogConfig area = fog.ActiveConfig;
        if (area != null && area.visionStart >= config.FogPreset.visionStart) return;

        pushedFog = config.FogPreset;
        fog.PushConfig(pushedFog);
    }

    /// <summary>Takes the shot off: its camera and its fog. With a brain, the view goes back to
    /// the gameplay rig on a cut — it happens under the black cover, and the brain's own
    /// two-second blend would still be travelling when that lifts.</summary>
    private void EndShot(CinemachineBrain brain)
    {
        if (pushedFog != null)
        {
            if (fog != null) fog.PopConfig(pushedFog);
            pushedFog = null;
        }

        if (shotCamera == null) return;

        Destroy(shotCamera.gameObject);
        shotCamera = null;

        if (brain != null) CutFor(brain);
    }

    private static void CutFor(CinemachineBrain brain)
    {
        CinemachineBlendDefinition previous = brain.DefaultBlend;

        // Already a cut: somebody else's, and theirs to restore. Taking it as "previous" would
        // put a cut back for good.
        if (previous.Style == CinemachineBlendDefinition.Styles.Cut) return;

        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        RestoreBlendAfterCut(brain, previous).Forget();
    }

    private static async UniTaskVoid RestoreBlendAfterCut(CinemachineBrain brain, CinemachineBlendDefinition blend)
    {
        await UniTask.DelayFrame(2, PlayerLoopTiming.Update);
        if (brain != null) brain.DefaultBlend = blend;
    }

    private CinemachineBrain FindBrain()
    {
        if (CinemachineBrain.ActiveBrainCount > 0) return CinemachineBrain.GetActiveBrain(0);

        if (!warnedNoBrain)
        {
            warnedNoBrain = true;
            Debug.LogWarning($"[{nameof(CaptureGrabCamera)}] No active CinemachineBrain. The capture " +
                             "plays without its shot.", this);
        }
        return null;
    }
}
