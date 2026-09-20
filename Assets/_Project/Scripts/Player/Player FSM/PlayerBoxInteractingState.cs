using UnityEngine;
using UnityEngine.Assemblies;

public class PlayerBoxInteractingState : BaseState<PlayerStateManager.EPlayerState>
{
    private PlayerStateManager playerStateManager;
    private bool finishAnim = false;
    private float animTimer = 0.2f;
    private float currentTimer = 0f;

    // ── Eight-way push ──────────────────────────────────────────────────────────
    //
    // W pushes the box away, S pulls it back, D / A slide it right / left — relative to the face
    // the player grabbed, not to the camera, so the box always travels along its own axes. The two
    // axes combine: W+D moves the pair forward AND right at once.
    //
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

    // BoxColl never collides with the grabbed box. It sits in front of the body right where the
    // box's face is, so the two overlap once latched and the solver keeps shoving them apart: that
    // shove threw the player backwards, which made a pull clearly faster than a push and skewed the
    // two slides as well. DriveBox already keeps the pair together, so the contact did nothing but
    // make the four directions travel at different speeds. BoxColl still collides with everything
    // else. Kept up every frame while latched, because PushableBox re-enables every player/box pair
    // when its grab-snap suppression ends.
    private Collider[] boxSolidColliders;

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

        box = playerStateManager.PushedBox;
        boxCollider = box != null ? box.GetComponent<BoxCollider>() : null;
        hasBoxOffset = false;
        playerStateManager.PushDirection = Vector3.zero;

        pushBlendTarget = Vector2.zero;
        playerStateManager.AnimController.SetFloat(PushXHash, 0f);
        playerStateManager.AnimController.SetFloat(PushYHash, 0f);

        CacheBoxSolidColliders();
        BackSnapTargetOffBoxFace();
    }

    /// <summary>
    /// Moves the grab snap's target back so BoxColl's front edge rests on the box face instead of
    /// inside it. The push animations are aligned to that distance: it is where the BoxColl/box
    /// contact used to hold the player before the two stopped colliding (see the note on
    /// <see cref="boxSolidColliders"/>). The side anchors sit closer than that — and not all at the
    /// same distance — so without this the hands and head sink into the box. Only ever moves the
    /// target back, like the contact did; an anchor already far enough is left alone.
    /// </summary>
    private void BackSnapTargetOffBoxFace()
    {
        BoxCollider boxColl = playerStateManager.BoxColl;
        if (boxCollider == null || boxColl == null) return;

        Vector3 direction = playerStateManager.NextDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();

        // How far BoxColl's front edge reaches ahead of the player's pivot, along the body's forward.
        Transform collTransform = boxColl.transform;
        Vector3 frontEdge = collTransform.TransformPoint(boxColl.center + Vector3.forward * (boxColl.size.z * 0.5f));
        Vector3 reach = frontEdge - playerStateManager.transform.position;
        reach.y = 0f;
        float reachAhead = Vector3.Dot(reach, playerStateManager.PlayerBody.forward);

        // How far the box face is from the snap target, measured at the box's mid height.
        Vector3 target = playerStateManager.NextPosition;
        Vector3 origin = new Vector3(target.x, boxCollider.bounds.center.y, target.z);
        if (!boxCollider.Raycast(new Ray(origin, direction), out RaycastHit hit, 5f)) return;

        float gap = reachAhead - hit.distance;
        if (gap <= 0f) return;

        playerStateManager.SetPlayerPositionAndDirection(target - direction * gap, playerStateManager.NextDirection);
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
        SetBoxCollIgnoresBox(false);
        boxSolidColliders = null;
        box = null;
        boxCollider = null;
    }

    public override void UpdateState()
    {
        if (playerStateManager.IsImmobilized)
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
                SetBoxCollIgnoresBox(true);

                // Already blocked-tested per axis inside ReadPushDirection — nothing left to gate
                // here. A second pass over the (already-safe) composed result would just reopen
                // the same false-negative-near-corners risk this whole rewrite exists to close.
                Vector3 pushDir = ReadPushDirection();

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
    /// The world-space push direction for this frame, or zero. Both axes contribute, so holding
    /// forward and right pushes the pair diagonally rather than picking one of the two.
    ///
    /// Each axis is blocked-tested ON ITS OWN, never as the composed diagonal. A cast along the
    /// diagonal can clear a corner that a cast along either cardinal axis alone would catch — the
    /// box's and the player's half-extents do not sample the exact 45° line, so a corner that
    /// truly blocks "forward" can read as clear along "forward-and-right" and let the pair cut
    /// straight through it. Testing forward and right independently, with the same cast that
    /// already worked correctly for a single-direction push, cannot produce that false clear: if
    /// either axis is genuinely blocked, that axis is not in the result no matter what the other
    /// one is doing.
    /// </summary>
    private Vector3 ReadPushDirection()
    {
        // Player/Move, same as the rest of the player's input.
        Vector2 move = GameInput.MoveValue;

        // Snapped to -1 / 0 / +1 rather than used raw: a diagonal has to travel at the same speed
        // as a straight push, and a half-deflected stick must not make the box crawl.
        float vertical = Mathf.Abs(move.y) > AxisThreshold ? Mathf.Sign(move.y) : 0f;
        float horizontal = Mathf.Abs(move.x) > AxisThreshold ? Mathf.Sign(move.x) : 0f;
        if (vertical == 0f && horizontal == 0f) return Vector3.zero;

        GetPushAxes(out Vector3 forward, out Vector3 right);

        Vector3 forwardAxis = vertical != 0f ? forward * vertical : Vector3.zero;
        Vector3 rightAxis = horizontal != 0f ? right * horizontal : Vector3.zero;

        if (forwardAxis != Vector3.zero && IsPushBlocked(forwardAxis)) forwardAxis = Vector3.zero;
        if (rightAxis != Vector3.zero && IsPushBlocked(rightAxis)) rightAxis = Vector3.zero;

        Vector3 result = forwardAxis + rightAxis;
        return result == Vector3.zero ? Vector3.zero : result.normalized;
    }

    /// <summary>
    /// The body faces the grabbed face for the whole push (the snap set it and nothing turns it
    /// afterwards), so its forward is the box axis the player is working along.
    /// </summary>
    private void GetPushAxes(out Vector3 forward, out Vector3 right)
    {
        forward = playerStateManager.PlayerBody.forward;
        forward.y = 0f;
        forward.Normalize();
        right = Vector3.Cross(Vector3.up, forward);
    }

    private void CacheBoxSolidColliders()
    {
        boxSolidColliders = null;
        if (box == null) return;

        Collider[] all = box.GetComponentsInChildren<Collider>();
        int solid = 0;
        for (int i = 0; i < all.Length; i++) if (!all[i].isTrigger) solid++;

        boxSolidColliders = new Collider[solid];
        for (int i = 0, j = 0; i < all.Length; i++)
        {
            if (!all[i].isTrigger) boxSolidColliders[j++] = all[i];
        }
    }

    /// <summary>See the note on <see cref="boxSolidColliders"/>.</summary>
    private void SetBoxCollIgnoresBox(bool ignore)
    {
        Collider boxColl = playerStateManager.BoxColl;
        if (boxColl == null || boxSolidColliders == null) return;

        for (int i = 0; i < boxSolidColliders.Length; i++)
        {
            if (boxSolidColliders[i] != null) Physics.IgnoreCollision(boxColl, boxSolidColliders[i], ignore);
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
