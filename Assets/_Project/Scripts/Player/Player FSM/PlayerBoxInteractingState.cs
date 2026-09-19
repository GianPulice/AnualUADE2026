using UnityEngine;
using UnityEngine.Assemblies;

public class PlayerBoxInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    private bool finishAnim = false;
    private float animTimer = 0.2f;
    private float currentTimer = 0f;

    // ── Four-way push ───────────────────────────────────────────────────────────
    //
    // W pushes the box away, S pulls it back, D / A slide it right / left — relative to the face
    // the player grabbed, not to the camera, so the box always travels along its own axes. Only one
    // direction at a time: the most recently pressed key that is still held wins, so holding W and
    // then pressing D switches to D, and releasing D falls back to W.
    //
    // Frame the key went down for each direction, or -1 while it is not held. Frames rather than
    // time so two presses are ordered even when they land within the same millisecond.
    private const int Forward = 0, Back = 1, Right = 2, Left = 3;
    private readonly int[] pressedFrame = { -1, -1, -1, -1 };

    // A raw axis past this counts as held. Keyboard gives exactly ±1; the margin keeps a resting
    // gamepad stick from registering.
    private const float AxisThreshold = 0.5f;

    // Only a forward push moves the box by contact. The other three drive the box's Rigidbody
    // directly at the player's velocity, and this is the box's position relative to the player,
    // captured once the grab snap has settled, so any drift between the two can be pulled back.
    private Rigidbody box;
    private BoxCollider boxCollider;
    private Vector3 boxOffset;
    private bool hasBoxOffset;

    // How hard drift from boxOffset is corrected, per second, and the most that correction may add
    // to the box's speed — enough to hold the pair together, never enough to yank the box.
    private const float BoxFollowGain = 10f;
    private const float MaxBoxCorrection = 1f;

    // Obstacle probes are shrunk by this so shapes resting on the floor or against each other do
    // not report a hit from where they already stand.
    private const float ProbeSkin = 0.05f;

    // Contacts with a normal steeper than this are floor or ramp and never block a push.
    private const float FloorNormalY = 0.7f;

    // Drive the directional push blend tree (Pushing state): pushY is +1 forward / -1 pull,
    // pushX is +1 right / -1 left, both in the player's local frame. Damped so switching direction
    // cross-fades instead of popping. The last direction is kept while idle, so easing back into
    // PushIdle never slides the pose through a different direction on the way.
    private static readonly int PushXHash = Animator.StringToHash("pushX");
    private static readonly int PushYHash = Animator.StringToHash("pushY");
    private const float PushBlendDamp = 0.1f;
    private Vector2 pushBlendTarget;

    public PlayerBoxInteractingState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        Debug.Log("Enter Interacting State");
        // Box push speed lives on SO_Movement (BoxPushSpeed) and is read directly in UpdateState.
        // SpeedMultiplier is kept at 1 so nothing else (CameraSprintEffect, etc.) misreads it as
        // a stance change — the tempo is capped by the SO value, not by the multiplier.
        playerStateManager.SpeedMultiplier = 1f;
        playerStateManager.BoxColl.enabled = true;
        playerStateManager.IsCrouch = false;
        playerStateManager.AnimController.SetBool("isPushing", true);
        playerStateManager.AudioEmitingZone.radius = playerStateManager.Movement.FootstepNoiseRadius;
        NextState = StateKey;
        finishAnim = false;
        currentTimer = 0f;

        for (int i = 0; i < pressedFrame.Length; i++) pressedFrame[i] = -1;
        box = playerStateManager.PushedBox;
        boxCollider = box != null ? box.GetComponent<BoxCollider>() : null;
        hasBoxOffset = false;
        playerStateManager.PushDirection = Vector3.zero;

        pushBlendTarget = Vector2.zero;
        playerStateManager.AnimController.SetFloat(PushXHash, 0f);
        playerStateManager.AnimController.SetFloat(PushYHash, 0f);
    }

    public override void ExitState()
    {
        Debug.Log("Exit Interacting State");
        playerStateManager.SpeedMultiplier = 1f;
        playerStateManager.BoxColl.enabled = false;
        playerStateManager.AnimController.SetBool("isPushing", false);
        playerStateManager.AnimController.SetFloat("moveSpeed", 0);
        playerStateManager.PushDirection = Vector3.zero;

        // The box is driven by velocity now, so letting go mid-push would leave it gliding at the
        // push speed. Gravity is left alone.
        if (box != null)
        {
            Vector3 v = box.linearVelocity;
            box.linearVelocity = new Vector3(0f, v.y, 0f);
        }
        box = null;
        boxCollider = null;
    }

    public override void UpdateState()
    {
        if (playerStateManager.IsDisabled)
        {
            NextState = PlayerStateManager.EPlayerState.Disabled;
        }
        else if (!playerStateManager.IsInteracting) NextState = PlayerStateManager.EPlayerState.Idle;
        else
        {
            if (!finishAnim)
            {
                if (currentTimer < animTimer)
                {
                    playerStateManager.transform.position = Vector3.Slerp(playerStateManager.transform.position, playerStateManager.NextPosition,currentTimer/animTimer);
                    playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.NextDirection, currentTimer / animTimer);
                    currentTimer += Time.deltaTime;
                }
                else
                {
                    playerStateManager.transform.position = Vector3.Slerp(playerStateManager.transform.position, playerStateManager.NextPosition, 1);
                    playerStateManager.PlayerBody.forward = Vector3.Slerp(playerStateManager.PlayerBody.forward, playerStateManager.NextDirection, 1);
                    finishAnim = true;
                }
            }
            else
            {
                // Latched: the player now stands exactly where the snap put it, beside the box.
                if (box != null && !hasBoxOffset)
                {
                    boxOffset = box.position - playerStateManager.RigBody.position;
                    hasBoxOffset = true;
                }

                Vector3 pushDir = ReadPushDirection();

                // Blocked is treated like no input: the pair stops together instead of the player
                // walking off sideways while the box sits against a wall.
                if (pushDir != Vector3.zero && IsPushBlocked(pushDir)) pushDir = Vector3.zero;

                playerStateManager.PushDirection = pushDir;

                if (pushDir != Vector3.zero)
                {
                    float pushCap = playerStateManager.Movement.BoxPushSpeed;
                    if (playerStateManager.CurrentVelocity < pushCap)
                    {
                        playerStateManager.CurrentVelocity += playerStateManager.Movement.Acceleration * Time.deltaTime;
                        if (playerStateManager.CurrentVelocity > pushCap) playerStateManager.CurrentVelocity = pushCap;
                    }
                    else
                    {
                        playerStateManager.CurrentVelocity = pushCap;
                    }
                }
                else playerStateManager.CurrentVelocity = 0;

                Vector3 move = pushDir * playerStateManager.CurrentVelocity;
                playerStateManager.RigBody.linearVelocity = move + Vector3.down;
                DriveBox(move);
                UpdatePushBlend(pushDir);
                playerStateManager.AnimController.SetFloat("moveSpeed", playerStateManager.CurrentVelocity);
            }
        }
    }

    /// <summary>
    /// The world-space push direction for this frame, or zero. Tracks when each direction was
    /// pressed and returns the latest one still held, so the box only ever moves along one axis.
    /// </summary>
    private Vector3 ReadPushDirection()
    {
        // Raw, same as the rest of the player's input: the smoothed axis keeps reporting a value
        // for a third of a second after release.
        float vertical = Input.GetAxisRaw("Vertical");
        float horizontal = Input.GetAxisRaw("Horizontal");

        Track(Forward, vertical > AxisThreshold);
        Track(Back, vertical < -AxisThreshold);
        Track(Right, horizontal > AxisThreshold);
        Track(Left, horizontal < -AxisThreshold);

        int latest = -1;
        for (int i = 0; i < pressedFrame.Length; i++)
        {
            if (pressedFrame[i] < 0) continue;
            if (latest < 0 || pressedFrame[i] > pressedFrame[latest]) latest = i;
        }
        if (latest < 0) return Vector3.zero;

        // The body faces the grabbed face for the whole push (the snap set it and nothing turns it
        // afterwards), so its forward is the box axis the player is working along.
        Vector3 forward = playerStateManager.PlayerBody.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        switch (latest)
        {
            case Forward: return forward;
            case Back:    return -forward;
            case Right:   return right;
            default:      return -right;
        }
    }

    /// <summary>
    /// Feeds the push direction, in the body's local frame, to the Pushing blend tree.
    /// </summary>
    private void UpdatePushBlend(Vector3 pushDir)
    {
        if (pushDir != Vector3.zero)
        {
            Transform body = playerStateManager.PlayerBody;
            pushBlendTarget = new Vector2(Vector3.Dot(pushDir, body.right), Vector3.Dot(pushDir, body.forward));
        }

        Animator anim = playerStateManager.AnimController;
        anim.SetFloat(PushXHash, pushBlendTarget.x, PushBlendDamp, Time.deltaTime);
        anim.SetFloat(PushYHash, pushBlendTarget.y, PushBlendDamp, Time.deltaTime);
    }

    private void Track(int direction, bool held)
    {
        if (!held) pressedFrame[direction] = -1;
        else if (pressedFrame[direction] < 0) pressedFrame[direction] = Time.frameCount;
    }

    /// <summary>
    /// Moves the box with the player at the same velocity, plus a small correction back to the
    /// offset captured at latch time so the two cannot slowly drift apart over a long push.
    /// </summary>
    private void DriveBox(Vector3 move)
    {
        if (box == null) return;

        Vector3 velocity = move;
        if (hasBoxOffset)
        {
            Vector3 drift = playerStateManager.RigBody.position + boxOffset - box.position;
            drift.y = 0f;
            velocity += Vector3.ClampMagnitude(drift * BoxFollowGain, MaxBoxCorrection);
        }

        // Vertical left to the physics so the box still rests on and falls with the floor.
        velocity.y = box.linearVelocity.y;
        box.linearVelocity = velocity;
    }

    /// <summary>
    /// True if either the box or the player would run into something within this frame's travel.
    /// Both are checked because the leading one depends on the direction: the box leads a forward
    /// push, the player leads a pull, and on a slide either can clip a wall first.
    /// </summary>
    private bool IsPushBlocked(Vector3 direction)
    {
        float distance = Mathf.Max(playerStateManager.Movement.BoxPushSpeed * Time.fixedDeltaTime, ProbeSkin)
                         + ProbeSkin;
        return IsBoxBlocked(direction, distance) || IsPlayerBlocked(direction, distance);
    }

    private bool IsBoxBlocked(Vector3 direction, float distance)
    {
        if (boxCollider == null) return false;

        Transform t = boxCollider.transform;
        Vector3 centre = t.TransformPoint(boxCollider.center);
        Vector3 halfExtents = Vector3.Scale(boxCollider.size, t.lossyScale) * 0.5f;
        halfExtents = new Vector3(Mathf.Max(0.01f, Mathf.Abs(halfExtents.x) - ProbeSkin),
                                  Mathf.Max(0.01f, Mathf.Abs(halfExtents.y) - ProbeSkin),
                                  Mathf.Max(0.01f, Mathf.Abs(halfExtents.z) - ProbeSkin));

        RaycastHit[] hits = Physics.BoxCastAll(centre, halfExtents, direction, t.rotation, distance,
                                              Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return AnyBlockingHit(hits);
    }

    private bool IsPlayerBlocked(Vector3 direction, float distance)
    {
        CapsuleCollider capsule = playerStateManager.CapsuleColl;
        if (capsule == null) return false;

        Transform t = capsule.transform;
        float radius = Mathf.Max(0.01f, capsule.radius - ProbeSkin);
        Vector3 centre = t.TransformPoint(capsule.center);
        float halfSpine = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);

        RaycastHit[] hits = Physics.CapsuleCastAll(centre - Vector3.up * halfSpine,
                                                   centre + Vector3.up * halfSpine,
                                                   radius, direction, distance,
                                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return AnyBlockingHit(hits);
    }

    private bool AnyBlockingHit(RaycastHit[] hits)
    {
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            // The player and the box always touch each other; neither blocks the pair.
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body != null && (body == playerStateManager.RigBody || body == box)) continue;

            // Already overlapping at the start of the cast: no usable normal, and treating it as a
            // wall would lock the push for good. The solver separates real overlaps on its own.
            if (hit.distance <= 0f && hit.point == Vector3.zero) continue;

            if (hit.normal.y > FloorNormalY) continue;

            return true;
        }
        return false;
    }
}
