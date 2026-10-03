using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps the Nemesis's long arms out of walls. One job: when an arm would end up beyond an obstacle,
/// lower it — fold it down against the body, the way the creature already carries its dragging arm
/// — just far enough to clear, keep it there while the wall lasts, and let it come back up once the
/// way is free.
///
/// ── WHY THIS EXISTS ────────────────────────────────────────────────────────
///
/// The walk carries one arm stretched out in front at shoulder height, fingertips 3 m ahead of the
/// pivot, and paddles the other one along the floor with the elbow 0.8 m out to the side, while
/// the NavMesh only keeps the body's AXIS clear, by the agent's radius: 0.3 m. The path is fine; the
/// arms are not, and no path can fix that: the arm reaches into rooms the agent never plans to
/// enter. Making the animation shorter would change the creature, and an agent wide enough for the
/// arms would change every route in every level.
///
/// ── WHY IT FOLDS, AND DOES NOT SWING OR SHRINK ─────────────────────────────
///
/// The arm measures 2.25 m from shoulder to fingertip and the shoulder rides between 1.5 and 2 m
/// above the floor. Swung down straight, like a pole, it passes through the floor for a third of
/// the way. Scaled down towards the shoulder (what this component used to do) it stays in the air
/// and turns into a small arm on a big body. So it comes down the way an arm does: the wrist is
/// taken to a low spot beside the body and the elbow bends to get it there.
///
/// ── HOW IT WORKS ───────────────────────────────────────────────────────────
///
/// SEEN FROM THE BODY'S AXIS. The one place the NavMesh guarantees is clear is the agent's own
/// axis, at every height. So an arm is where it may be when every part of it — the elbow, the
/// wrist, the middle of each bone, every fingertip — can be reached from that axis in a straight
/// level line without meeting anything upright. A part that cannot is beyond a wall, by as far as
/// it lies behind that wall's face. Testing the arm from its own shoulder, which this used to do,
/// fails exactly where it matters: with the body hugging a wall the shoulder is already inside it,
/// and a cast that starts inside a collider sees nothing.
///
/// After the Animator has posed the rig, each arm is tested as it is. If the animated pose is
/// beyond something, the same test is run on the pose a little lower, then lower still, then with
/// the hand drawn in towards the middle of the body (a wall at its side), drawn back beside the hip
/// (a wall in front), or both, and the first one that clears is the one the arm takes. With no
/// pose clear — a passage narrower than the shoulders — it takes the one that is beyond the least.
///
/// IT HOLDS FOR A STRIDE. A walk asks for something different on every frame of its cycle: the
/// arm swings into the wall and out again. Giving way the moment a frame is clear pumped the arm
/// up and down on every stride, so a pose is kept until a whole cycle of the clip has gone by
/// without needing it; it is dropped at once only for one that is needed more.
///
/// Each pose is a two-bone solve: the shoulder and the elbow are turned so the wrist lands where
/// it is wanted, with the elbow pointing back the way the dragging arm's does (while it walks,
/// behind it is where it has just been). Nothing is scaled and the hand keeps the pose the clip
/// gave it. The fingers are kept on the floor rather than under it: measured on their bones once
/// the arm is posed, and the wrist raised by what they would sink.
///
/// THE SOLVE RUNS IN THE MODEL'S OWN SPACE, not in the world. The model is scaled unevenly
/// (0.75 wide, 0.61 tall and deep), and under an uneven scale a bone turned in world space does
/// not carry its children round rigidly: the pose that was tested and the pose that was applied
/// came apart by up to 20 cm at the wrist, enough to test a pose as clear and put the hand half a
/// metre into the wall. Inside the model nothing is scaled, the bones are rigid, and the tested
/// pose is the applied one; only the casts, which need real distances, are done in the world.
///
/// Only upright surfaces count: floors and ceilings are ignored (the hand dragging on the ground
/// is the animation, not a collision), and so is anything small enough to be a pickup or a valve
/// (<see cref="minObstacleSize"/>).
///
/// ── WHAT IT IS NOT ─────────────────────────────────────────────────────────
///
/// It does not touch the body, the agent or the animation clips: the creature is wider than its
/// agent, so walking along a wall its shoulder is in it, and the head leans past the pivot into a
/// wall it walks straight up to. It stands down while the Animator is in
/// <see cref="suppressWhileState"/>: the grab spreads the arms several metres to either side on
/// purpose and holds the player in them, and lowering them there would undo it.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Nemesis/Arm Wall Guard")]
public class NemesisArmWallGuard : MonoBehaviour
{
    [System.Serializable]
    public class Arm
    {
        [Tooltip("The bone the arm hangs from. It is turned to bring the elbow down.")]
        public Transform shoulder;

        [Tooltip("The elbow: a bone under the shoulder. It is turned to bring the wrist in.")]
        public Transform elbow;

        [Tooltip("The wrist (the hand bone). It has to hang from the elbow.")]
        public Transform tip;

        // Where the arm is right now, each from 0 to 1: how far down (0 = as animated), how far
        // the lowered hand is drawn back beside the hip, and how far it is drawn in to the middle.
        [System.NonSerialized] internal float lower;
        [System.NonSerialized] internal float retract;
        [System.NonSerialized] internal float tuck;

        // The pose being kept, as (lower, retract, tuck), and for how long it has been more than
        // the frame needed. See Hold.
        [System.NonSerialized] internal Vector3 held;
        [System.NonSerialized] internal float spare;

        // What the Animator wrote, and what this component wrote over it. See RestoreAnimated.
        [System.NonSerialized] internal Quaternion shoulderAnimated, elbowAnimated;
        [System.NonSerialized] internal Quaternion shoulderWritten, elbowWritten;
        [System.NonSerialized] internal bool hasWritten;

        // The ends of the fingers (the bones under the wrist with nothing under them), and this
        // frame's stretch from the wrist to each fingertip, in model space.
        [System.NonSerialized] internal Transform[] fingerEnds;
        [System.NonSerialized] internal Vector3[] fingers;
    }

    [SerializeField] private Arm[] arms;

    [Header("What counts as a wall")]
    [Tooltip("Default, Interactable (doors), Wall and Props. The floor is not needed: horizontal " +
             "surfaces are ignored anyway.")]
    [SerializeField] private LayerMask obstacleMask = (1 << 0) | (1 << 6) | (1 << 11) | (1 << 12);

    [Tooltip("Obstacles whose box measures less than this across (metres) are ignored: items, " +
             "valves, handles. Walls, doors and big crates pass.")]
    [SerializeField, Min(0f)] private float minObstacleSize = 0.6f;

    [Tooltip("A surface whose normal is more vertical than this is floor or ceiling and does not " +
             "count as a wall.")]
    [SerializeField, Range(0f, 1f)] private float floorNormalY = 0.6f;

    [Tooltip("Thickness given to the arm when it is tested, in metres. Keep it under the agent's " +
             "radius: the test starts on the body's axis.")]
    [SerializeField, Min(0.01f)] private float armRadius = 0.12f;

    [Tooltip("Thickness given to each finger when it is tested, in metres.")]
    [SerializeField, Min(0.01f)] private float fingerRadius = 0.05f;

    [Tooltip("How much of its own length a finger's last bone is given past its joint, to reach " +
             "the fingertip: the rig has no bone there.")]
    [SerializeField, Range(0f, 1f)] private float fingertipPastLastJoint = 0.6f;

    [Header("Lowered pose")]
    [Tooltip("Metres above the floor the wrist comes down to.")]
    [SerializeField, Min(0f)] private float loweredWristHeight = 0.45f;

    [Tooltip("Metres in front of the shoulder the lowered wrist rests at.")]
    [SerializeField] private float loweredForward = 0.3f;

    [Tooltip("Metres towards the body's centre line the lowered wrist rests at, from the shoulder.")]
    [SerializeField] private float loweredInward = 0.05f;

    [Tooltip("Metres the lowered hand can be drawn back beside the hip, when lowering alone still " +
             "leaves it beyond a wall right in front.")]
    [SerializeField, Min(0f)] private float retractDistance = 0.8f;

    [Tooltip("Metres from the body's centre line the lowered wrist is drawn in to, when lowering " +
             "alone still leaves it beyond a wall at its side. Not 0: the two hands would meet.")]
    [SerializeField, Min(0f)] private float tuckedOffset = 0.12f;

    [Tooltip("Metres the finger bones are kept above the floor. They run down the middle of the " +
             "finger, so this is about half its thickness.")]
    [SerializeField, Min(0f)] private float floorClearance = 0.04f;

    [Header("Speed")]
    [Tooltip("How fast it comes down, in full lowerings per second. High on purpose: a hand that " +
             "is inside a wall for a quarter of a second shows more than a quick tuck.")]
    [SerializeField, Min(0.1f)] private float lowerSpeed = 8f;

    [Tooltip("How fast it comes back up once the way is clear.")]
    [SerializeField, Min(0.1f)] private float raiseSpeed = 1.5f;

    [Tooltip("How many cycles of the clip being played have to go by without needing a pose before " +
             "the arm lets go of it. Under 1 it pumps on every stride beside a wall.")]
    [SerializeField, Min(0f)] private float holdCycles = 1f;

    [Tooltip("The longest that wait is allowed to be, in seconds, whatever the clip's length.")]
    [SerializeField, Min(0f)] private float maxHoldSeconds = 3f;

    [Tooltip("While the Animator is in this state the guard does not act. Empty = always acts.")]
    [SerializeField] private string suppressWhileState = "Catch";

    [Tooltip("More states it does not act in, besides the one above. E_Attack is the strike the " +
             "escape's ending throws at the gate it is stuck against: the arm is aimed at a wall " +
             "on purpose, and folded for reaching beyond it, the strike would be gone from the clip.")]
    [SerializeField] private string[] alsoSuppressWhileStates = { "E_Attack" };

    // The poses tried, in order, as (lowered, drawn back, drawn in): the animated one, lower by
    // quarters, then lowered with the hand drawn in, drawn back, or both.
    private static readonly Vector3[] Candidates =
    {
        new Vector3(0f, 0f, 0f), new Vector3(0.25f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.75f, 0f, 0f),
        new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f),
        new Vector3(1f, 0.5f, 0f), new Vector3(1f, 0.5f, 1f),
        new Vector3(1f, 1f, 0f), new Vector3(1f, 1f, 1f),
    };

    // The hand keeps the pose the clip gave it, so how low the fingers end up is only known once
    // the arm is posed: what is still under the floor is lifted out in this many passes.
    private const int FloorPasses = 2;

    // Letting go for the grab is not a raise: the arms are needed at full length at once.
    private const float ReleaseSpeed = 6f;

    // What a part of the arm weighs when it only brushes a wall. Metres, like the depths it is
    // ranked against.
    private const float Touching = 0.01f;

    // The axis is tested between these margins of the body's height: under the first the floor's
    // own edges get in the way, over the second the ceiling's.
    private const float AxisMargin = 0.15f;

    private const float DefaultBodyHeight = 2f;

    private const int MaxHits = 16;
    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];

    private Animator animator;
    private Transform body;
    private float bodyHeight;
    private int suppressHash;
    private int[] alsoSuppressHashes;
    private bool initialised;

    /// <summary>How far down an arm is right now: 0 = as animated, 1 = fully lowered. For tests
    /// and debugging.</summary>
    public float LoweredOf(int armIndex) =>
        arms != null && armIndex >= 0 && armIndex < arms.Length && arms[armIndex] != null ? arms[armIndex].lower : 0f;

    private void OnDisable()
    {
        // Give the arms back as the Animator posed them: with the guard off, nothing else would
        // on a frame the Animator does not write.
        if (arms == null) return;

        foreach (Arm arm in arms)
        {
            if (!IsUsable(arm)) continue;

            RestoreAnimated(arm);
            Release(arm);
        }
    }

    private void LateUpdate() => Evaluate(Time.deltaTime);

    /// <summary>Tests every arm and applies the result. Runs from LateUpdate, after the Animator;
    /// public so a test can drive it by hand, with a large <paramref name="deltaTime"/> to settle
    /// in one call.</summary>
    public void Evaluate(float deltaTime)
    {
        if (arms == null) return;

        if (!initialised)
        {
            initialised = true;
            animator = GetComponent<Animator>();
            suppressHash = string.IsNullOrEmpty(suppressWhileState) ? 0 : Animator.StringToHash(suppressWhileState);

            int extras = alsoSuppressWhileStates != null ? alsoSuppressWhileStates.Length : 0;
            alsoSuppressHashes = new int[extras];
            for (int i = 0; i < extras; i++)
            {
                string state = alsoSuppressWhileStates[i];
                alsoSuppressHashes[i] = string.IsNullOrEmpty(state) ? 0 : Animator.StringToHash(state);
            }

            // The axis the NavMesh keeps clear is the agent's, and the agent is on the root.
            NavMeshAgent agent = GetComponentInParent<NavMeshAgent>();
            body = agent != null ? agent.transform : transform;
            bodyHeight = agent != null ? agent.height : DefaultBodyHeight;
        }

        bool suppressed = IsSuppressed();
        float hold = HoldSeconds();
        float down = suppressed ? ReleaseSpeed : lowerSpeed;
        float up = suppressed ? ReleaseSpeed : raiseSpeed;

        foreach (Arm arm in arms)
        {
            if (!IsUsable(arm)) continue;

            // Measured on the pose the Animator gave, not on the one lowered last frame.
            RestoreAnimated(arm);

            Vector3 shoulder = ToModel(arm.shoulder.position);
            Vector3 elbow = ToModel(arm.elbow.position);
            Vector3 wrist = ToModel(arm.tip.position);
            ReadFingers(arm, wrist);

            if (suppressed)
            {
                arm.held = Vector3.zero;
                arm.spare = 0f;
            }
            else
            {
                Hold(arm, shoulder, elbow, wrist, hold, deltaTime);
            }

            arm.lower = Ease(arm.lower, arm.held.x, down, up, deltaTime);
            arm.retract = Ease(arm.retract, arm.held.y, down, up, deltaTime);
            arm.tuck = Ease(arm.tuck, arm.held.z, down, up, deltaTime);

            if (arm.lower < 0.001f)
            {
                // Back as animated. The other two only say where the lowered hand goes.
                arm.lower = 0f;
                arm.retract = 0f;
                arm.tuck = 0f;
                arm.hasWritten = false;
                continue;
            }

            var pose = new Vector3(arm.lower, arm.retract, arm.tuck);
            float lift = 0f;

            for (int pass = 0; pass <= FloorPasses; pass++)
            {
                arm.shoulder.localRotation = arm.shoulderAnimated;
                arm.elbow.localRotation = arm.elbowAnimated;

                Solve(shoulder, elbow, wrist, pose, lift, out Vector3 elbowAt, out Vector3 wristAt, out Quaternion _);
                Apply(arm, elbowAt, wristAt);

                // Fingers on the floor, not under it. Scaled by how far down the arm is, so the
                // lift fades in with the lowering instead of jumping the hand of a clip that
                // already drags it along the ground.
                float sunk = (transform.position.y + floorClearance - LowestFingerPoint(arm)) * arm.lower;
                if (sunk <= 0.005f) break;
                lift += sunk;
            }
        }
    }

    private static bool IsUsable(Arm arm) =>
        arm != null && arm.shoulder != null && arm.elbow != null && arm.tip != null;

    private static void Release(Arm arm)
    {
        arm.hasWritten = false;
        arm.lower = 0f;
        arm.retract = 0f;
        arm.tuck = 0f;
        arm.held = Vector3.zero;
        arm.spare = 0f;
    }

    private static float Ease(float current, float target, float down, float up, float deltaTime) =>
        Mathf.MoveTowards(current, target, (target > current ? down : up) * deltaTime);

    private bool IsSuppressed()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;

        // The state it is in, or the one it is blending into: the arm is let go as the clip comes in.
        if (SuppressesIn(animator.GetCurrentAnimatorStateInfo(0).shortNameHash)) return true;
        return animator.IsInTransition(0) && SuppressesIn(animator.GetNextAnimatorStateInfo(0).shortNameHash);
    }

    private bool SuppressesIn(int stateHash)
    {
        if (stateHash == 0) return false;
        if (stateHash == suppressHash) return true;

        foreach (int hash in alsoSuppressHashes)
        {
            if (hash == stateHash) return true;
        }
        return false;
    }

    /// <summary>
    /// Puts back what the Animator wrote, on a frame it did not write at all. The two bones are
    /// keyed in every clip, so normally the pose read here IS the animated one; when it is still
    /// exactly what this component left last frame, the Animator skipped the frame and the real
    /// baseline is the one remembered.
    /// </summary>
    private static void RestoreAnimated(Arm arm)
    {
        Quaternion shoulderNow = arm.shoulder.localRotation;
        Quaternion elbowNow = arm.elbow.localRotation;

        bool untouched = arm.hasWritten &&
                         Quaternion.Angle(shoulderNow, arm.shoulderWritten) < 0.01f &&
                         Quaternion.Angle(elbowNow, arm.elbowWritten) < 0.01f;
        if (untouched)
        {
            arm.shoulder.localRotation = arm.shoulderAnimated;
            arm.elbow.localRotation = arm.elbowAnimated;
            return;
        }

        arm.shoulderAnimated = shoulderNow;
        arm.elbowAnimated = elbowNow;
    }

    // ── Model space ─────────────────────────────────────────────────────────────────────
    // This transform's own frame, before its scale: where the bones are rigid. See the class
    // comment.

    private Vector3 ToModel(Vector3 world) => transform.InverseTransformPoint(world);

    private Vector3 ToWorld(Vector3 model) => transform.TransformPoint(model);

    // ── The pose ────────────────────────────────────────────────────────────────────────

    /// <summary>How long a pose is kept after the frame stopped needing it: a cycle of the clip
    /// being played. A walk needs it again within the stride.</summary>
    private float HoldSeconds()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return maxHoldSeconds;

        return Mathf.Min(animator.GetCurrentAnimatorStateInfo(0).length * holdCycles, maxHoldSeconds);
    }

    /// <summary>
    /// Decides the pose the arm keeps (<see cref="Arm.held"/>). What this frame needs is taken at
    /// once when the pose being kept has stopped being good enough; when the kept pose is still
    /// good and merely more than the frame asks for, it stays until <paramref name="hold"/> seconds
    /// have gone by like that.
    /// </summary>
    private void Hold(Arm arm, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float hold, float deltaTime)
    {
        Vector3 need = RequiredPose(arm, shoulder, elbow, wrist, out float beyond);

        if (need == arm.held)
        {
            arm.spare = 0f;
            return;
        }

        // The pose being kept is beyond a wall and this one is not, or is by clearly less (with
        // nothing clear, two poses about as bad as each other would otherwise take turns).
        float kept = Beyond(arm, shoulder, elbow, wrist, arm.held);
        if (kept > 0f && (beyond <= 0f || kept > beyond + Touching))
        {
            arm.held = need;
            arm.spare = 0f;
            return;
        }

        arm.spare += deltaTime;
        if (arm.spare < hold) return;

        arm.held = need;
        arm.spare = 0f;
    }

    /// <summary>The first pose in <see cref="Candidates"/> that is beyond no wall, and with none
    /// clear, the one that is beyond the least. <paramref name="beyond"/> is by how much.</summary>
    private Vector3 RequiredPose(Arm arm, Vector3 shoulder, Vector3 elbow, Vector3 wrist, out float beyond)
    {
        Vector3 best = Candidates[0];
        beyond = float.MaxValue;

        for (int i = 0; i < Candidates.Length; i++)
        {
            float past = Beyond(arm, shoulder, elbow, wrist, Candidates[i]);

            if (past <= 0f)
            {
                beyond = 0f;
                return Candidates[i];
            }

            if (past < beyond)
            {
                beyond = past;
                best = Candidates[i];
            }
        }

        return best;
    }

    /// <summary>How far beyond walls the arm is in a pose, in metres, added up over its parts:
    /// the elbow, the wrist, the middle of each bone and every fingertip. 0 = none of it.</summary>
    private float Beyond(Arm arm, Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 pose)
    {
        Solve(shoulder, elbow, wrist, pose, 0f, out Vector3 elbowAt, out Vector3 wristAt, out Quaternion handTurn);

        float beyond = Past(ToWorld((shoulder + elbowAt) * 0.5f), armRadius) +
                       Past(ToWorld(elbowAt), armRadius) +
                       Past(ToWorld((elbowAt + wristAt) * 0.5f), armRadius) +
                       Past(ToWorld(wristAt), armRadius);

        for (int f = 0; f < arm.fingers.Length; f++)
            beyond += Past(ToWorld(wristAt + handTurn * arm.fingers[f]), fingerRadius);

        return beyond;
    }

    /// <summary>
    /// Where the elbow and the wrist are, in model space, in a pose — x how far lowered, y how far
    /// drawn back, z how far drawn in — and how the hand has turned with them. At x = 0 it is the
    /// animated pose, untouched. <paramref name="lift"/> raises the lowered wrist, in metres.
    /// </summary>
    private void Solve(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 pose, float lift,
                       out Vector3 elbowAt, out Vector3 wristAt, out Quaternion handTurn)
    {
        if (pose.x <= 0f)
        {
            elbowAt = elbow;
            wristAt = wrist;
            handTurn = Quaternion.identity;
            return;
        }

        // Beside the body, low: under the shoulder, a little ahead and a little in. Laid out in
        // the world, round the body's axis, where metres are metres; then taken into the model.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 foot = body.position;

        Vector3 fromAxis = ToWorld(shoulder) - foot;
        float across = Vector3.Dot(fromAxis, right);
        float ahead = Vector3.Dot(fromAxis, forward);
        float side = across >= 0f ? 1f : -1f;

        float restAcross = Mathf.Lerp(across - side * loweredInward, side * tuckedOffset, pose.z);
        float restAhead = ahead + loweredForward - retractDistance * pose.y;
        Vector3 restWorld = new Vector3(foot.x, transform.position.y + loweredWristHeight + lift, foot.z)
                            + right * restAcross + forward * restAhead;
        Vector3 wanted = Vector3.Lerp(wrist, ToModel(restWorld), pose.x);

        // The elbow goes straight back, like the dragging arm's: out to the side it is the first
        // thing into the wall of a corridor.
        Bend(shoulder, elbow, wrist, wanted, Vector3.back, pose.x, out elbowAt, out wristAt);

        // The hand turns with the two bones that are turned, exactly as Apply turns them.
        Quaternion atShoulder = Quaternion.FromToRotation(elbow - shoulder, elbowAt - shoulder);
        Quaternion atElbow = Quaternion.FromToRotation(atShoulder * (wrist - elbow), wristAt - elbowAt);
        handTurn = atElbow * atShoulder;
    }

    /// <summary>
    /// Two-bone solve: where the elbow has to be for the wrist to reach <paramref name="wanted"/>
    /// with both segments keeping their length. The elbow starts pointing where the animation has
    /// it and turns towards <paramref name="pole"/> as <paramref name="blend"/> goes to 1, so at
    /// 0 the result is the animated pose exactly.
    /// </summary>
    private static void Bend(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 wanted, Vector3 pole,
                             float blend, out Vector3 elbowAt, out Vector3 wristAt)
    {
        float upper = (elbow - shoulder).magnitude;
        float fore = (wrist - elbow).magnitude;

        Vector3 toWanted = wanted - shoulder;
        float reach = Mathf.Clamp(toWanted.magnitude, Mathf.Abs(upper - fore) + 0.01f, upper + fore - 0.01f);
        Vector3 axis = toWanted.sqrMagnitude > 1e-8f ? toWanted.normalized : Vector3.down;

        // The elbow sits on a circle round the shoulder-to-wrist line.
        float along = (upper * upper - fore * fore + reach * reach) / (2f * reach);
        float radius = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));

        Vector3 animated = Vector3.ProjectOnPlane(elbow - shoulder, axis);
        Vector3 lowered = Vector3.ProjectOnPlane(pole, axis);
        Vector3 pointing = Vector3.Slerp(animated.sqrMagnitude > 1e-6f ? animated.normalized : lowered.normalized,
                                         lowered.sqrMagnitude > 1e-6f ? lowered.normalized : animated.normalized,
                                         blend);
        pointing = Vector3.ProjectOnPlane(pointing, axis);
        pointing = pointing.sqrMagnitude > 1e-6f ? pointing.normalized : Vector3.zero;

        elbowAt = shoulder + axis * along + pointing * radius;
        wristAt = shoulder + axis * reach;
    }

    /// <summary>Turns the shoulder and the elbow so the arm takes the solved pose (model space).
    /// Everything below the elbow keeps what the clip gave it.</summary>
    private void Apply(Arm arm, Vector3 elbowAt, Vector3 wristAt)
    {
        // A turn worked out in model space is applied in the world between the model's own
        // rotation and its inverse.
        Quaternion frame = transform.rotation;
        Quaternion toModel = Quaternion.Inverse(frame);

        Vector3 shoulder = ToModel(arm.shoulder.position);
        Quaternion turn = Quaternion.FromToRotation(ToModel(arm.elbow.position) - shoulder, elbowAt - shoulder);
        arm.shoulder.rotation = frame * turn * toModel * arm.shoulder.rotation;

        Vector3 elbow = ToModel(arm.elbow.position);
        turn = Quaternion.FromToRotation(ToModel(arm.tip.position) - elbow, wristAt - elbow);
        arm.elbow.rotation = frame * turn * toModel * arm.elbow.rotation;

        arm.shoulderWritten = arm.shoulder.localRotation;
        arm.elbowWritten = arm.elbow.localRotation;
        arm.hasWritten = true;
    }

    // ── The hand ────────────────────────────────────────────────────────────────────────

    /// <summary>This frame's stretch from the wrist to every fingertip, in model space.</summary>
    private void ReadFingers(Arm arm, Vector3 wrist)
    {
        if (arm.fingerEnds == null)
        {
            var ends = new System.Collections.Generic.List<Transform>();
            foreach (Transform bone in arm.tip.GetComponentsInChildren<Transform>(true))
            {
                if (bone != arm.tip && bone.childCount == 0) ends.Add(bone);
            }

            arm.fingerEnds = ends.ToArray();
            arm.fingers = new Vector3[arm.fingerEnds.Length];
        }

        for (int i = 0; i < arm.fingerEnds.Length; i++)
            arm.fingers[i] = ToModel(Fingertip(arm.fingerEnds[i])) - wrist;
    }

    /// <summary>Where a finger ends: past its last joint, along its last bone.</summary>
    private Vector3 Fingertip(Transform end) =>
        end.position + (end.position - end.parent.position) * fingertipPastLastJoint;

    /// <summary>Height of the lowest point of the hand's bones, the wrist included.</summary>
    private float LowestFingerPoint(Arm arm)
    {
        float lowest = arm.tip.position.y;

        foreach (Transform end in arm.fingerEnds)
        {
            lowest = Mathf.Min(lowest, end.position.y, Fingertip(end).y);
        }

        return lowest;
    }

    // ── Walls ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// How far a point of the arm (world space) lies beyond something upright and big enough to be
    /// a wall, seen in a straight level line from the body's axis, in metres; 0 when nothing stands
    /// between the two. A point that only comes within <paramref name="radius"/> of a wall counts
    /// as <see cref="Touching"/>.
    /// </summary>
    private float Past(Vector3 point, float radius)
    {
        // From the axis at the point's own height, so what is tested is the wall and not the top
        // of a crate the line would otherwise drop in through.
        Vector3 foot = body.position;
        float height = Mathf.Clamp(point.y, foot.y + AxisMargin, foot.y + bodyHeight - AxisMargin);
        var from = new Vector3(foot.x, height, foot.z);

        Vector3 along = point - from;
        float length = along.magnitude;
        if (length < 0.02f) return 0f;

        Vector3 direction = along / length;
        int found = Physics.SphereCastNonAlloc(from, radius, direction, hits, length,
                                               obstacleMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        float past = 0f;

        for (int i = 0; i < found; i++)
        {
            RaycastHit hit = hits[i];

            // Overlapping where the cast starts: something is on the axis itself (a door swinging
            // through the body). Not a wall an arm can be kept out of.
            if (hit.distance <= 0f || hit.distance >= nearest) continue;

            if (hit.collider.transform.IsChildOf(transform.root)) continue;
            if (hit.collider.bounds.size.magnitude < minObstacleSize) continue;
            if (Mathf.Abs(hit.normal.y) > floorNormalY) continue;

            nearest = hit.distance;
            past = Mathf.Max(Vector3.Dot(hit.point - point, hit.normal), Touching);
        }

        return past;
    }
}
