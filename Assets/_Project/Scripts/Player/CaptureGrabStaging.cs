using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Stages the grab on a capture: puts the Nemesis and the player the distance apart the two clips
/// were animated at, turns the player to face it, and starts the player's half of the animation.
///
/// ── WHY THIS EXISTS ────────────────────────────────────────────────────────
///
/// E_KillPlayer (the Nemesis) and Grabbed (the player) are one animation in two halves: the player
/// stands <see cref="SO_CaptureGrabConfig.PairDistance"/> in front of the Nemesis, facing it, and
/// both clips start on the same frame — the hands close 0.63 s in, exactly where the player's
/// shoulders are, and lift. A capture happens anywhere inside CatchMaxReach (1 m), with the player
/// facing wherever they were running. Played like that the arms reach 70 cm past the body they are
/// meant to hold.
///
/// ── WHAT IT DOES ───────────────────────────────────────────────────────────
///
/// Over the first <see cref="SO_CaptureGrabConfig.AlignSeconds"/>, before the hands arrive:
///   - the Nemesis backs off along the line between the two, as far as its NavMesh has room. It
///     goes first: the ground behind it is the ground it has just come over;
///   - whatever it could not give, the player is moved forward — over the NavMesh as well, so
///     nobody ends up in a wall or off a ledge. With no room on either side the pair plays closer
///     than it was animated rather than through geometry;
///   - the player's model turns to face the Nemesis (the model, not the root: see
///     NemesisCatchState.FaceEachOther);
///   - the player's Animator crossfades into Grabbed on the frame of the capture, which is the
///     frame the Nemesis's own goes into Catch.
///
/// ── WHAT IT IS NOT ─────────────────────────────────────────────────────────
///
/// It does not decide the capture or how long it lasts (NemesisCatchState, CheckpointManager), it
/// does not touch the camera (<see cref="CaptureGrabCamera"/>), and it does not play the Nemesis's
/// clip: that is the Grabbing gait, set by NemesisCatchState on the same frame. Bodies a platform
/// is carrying are not moved either — the cabin is; they still turn and play.
///
/// SETUP: on the Player prefab's root, with SO_CaptureGrabConfig. Tools > Player > Setup Capture
/// Grab adds it, builds the Grabbed clip and its Animator state, and measures the distance.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Player/Capture Grab Staging")]
public class CaptureGrabStaging : MonoBehaviour
{
    [SerializeField] private SO_CaptureGrabConfig config;

    // Animator state the player goes back to when a capture ends without a respawn.
    private const string IdleStateName = "Idle";
    private const float IdleBlendSeconds = 0.2f;

    // How far off the NavMesh the player may stand and still be moved along it.
    private const float NavSampleRadius = 0.5f;

    // A gap smaller than this is not worth moving anybody for.
    private const float MinGap = 0.02f;

    private struct Plan
    {
        public bool MoveNemesis, MovePlayer;
        public Vector3 NemesisStart, NemesisTarget;
        public Vector3 PlayerStart, PlayerTarget;
        public Quaternion PlayerFacing;
    }

    private PlayerStateManager player;
    private NemesisStateManager nemesis;
    private CancellationTokenSource stagingCts;
    private bool warnedNoState;

    private Plan plan;
    private bool hasPlan;

    /// <summary>
    /// Where the two bodies will be standing once the pair is staged, from the end of the frame of
    /// the capture until the capture is over. <see cref="CaptureGrabCamera"/> frames this and not
    /// where they are: they are still on their way there while the shot is being chosen.
    /// </summary>
    public bool TryGetStagedPair(out Vector3 nemesisAt, out Vector3 playerAt)
    {
        nemesisAt = plan.NemesisTarget;
        playerAt = plan.PlayerTarget;
        return hasPlan;
    }

    private void Awake()
    {
        player = GetComponentInParent<PlayerStateManager>();

        // Static event: subscribed in Awake and released in OnDestroy, per docs/CLAUDE.md.
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
    }

    private void OnDestroy()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        CancelStaging();
    }

    private void OnDisable() => CancelStaging();

    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        if (!isActiveAndEnabled || config == null || captured == null || captured != player) return;

        // A scripted cinematic owns both bodies while it plays.
        if (CinematicState.IsPlaying) return;

        // Only the Nemesis's own grab is staged. OnCaptured raised from anywhere else (the test
        // console's button) has nobody at the other end of the arms.
        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis == null || nemesis.CurrentStateKey != NemesisStateManager.ENemesisState.Catch) return;

        PlayGrabbed();

        CancelStaging();
        stagingCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        StageAsync(stagingCts.Token).Forget();
    }

    private void CancelStaging()
    {
        stagingCts?.Cancel();
        stagingCts?.Dispose();
        stagingCts = null;
        hasPlan = false;
    }

    private void PlayGrabbed()
    {
        Animator animator = player.AnimController;
        int grabbed = Animator.StringToHash(config.GrabbedState);

        if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, grabbed))
        {
            if (!warnedNoState)
            {
                warnedNoState = true;
                Debug.LogWarning($"[{nameof(CaptureGrabStaging)}] The Animator has no state " +
                                 $"'{config.GrabbedState}', so the player does not play its half of " +
                                 "the grab. Run Tools > Player > Setup Capture Grab.", this);
            }
            return;
        }

        // From its first frame, blending in for as long as the Nemesis's Any State -> Catch does:
        // the two clips then share their frame 0.
        animator.CrossFadeInFixedTime(grabbed, config.ClipBlendSeconds, 0, 0f);
    }

    private async UniTaskVoid StageAsync(CancellationToken token)
    {
        // Read now, inside the event: NemesisCatchState snaps the model round to face the Nemesis
        // right after it. The turn is replayed from here instead, over the align time.
        Transform body = player.PlayerBody;
        Quaternion startFacing = body != null ? body.rotation : player.transform.rotation;

        try
        {
            // LastUpdate, starting with this same frame's: by then the Nemesis has turned and
            // stopped and a hiding spot has put its player outside, so the plan is made off where
            // the two really are. Every step after it lands behind both state machines and ahead
            // of the Animator and the camera.
            await UniTask.Yield(PlayerLoopTiming.LastUpdate, token);

            plan = MakePlan();
            hasPlan = true;
            float duration = config.AlignSeconds;
            float elapsed = 0f;

            while (IsCapturing())
            {
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                Apply(plan, startFacing, t * t * (3f - 2f * t));
                if (t >= 1f) break;

                await UniTask.Yield(PlayerLoopTiming.LastUpdate, token);

                // Scaled, like the two Animators: the hands close at a time of the clip, and a
                // pause has to hold the bodies with it.
                elapsed += Time.deltaTime;
            }

            await UniTask.WaitWhile(IsCapturing, PlayerLoopTiming.LastUpdate, token);
        }
        catch (OperationCanceledException)
        {
            // Disabled, destroyed or a new capture. The finally still runs.
        }
        finally
        {
            // Not when cancelled (a newer capture, or this component going away): with a newer
            // capture the plan, the Grabbed state and the lock are already its own, and letting
            // this one take the player out of the pose would cut that grab short.
            if (!token.IsCancellationRequested)
            {
                hasPlan = false;
                LeaveGrabbed();
            }
        }
    }

    /// <summary>The grab is still on: the player is held and nothing else has taken the bodies.</summary>
    private bool IsCapturing() =>
        player != null && nemesis != null && player.IsDisabled && player.IsRecoveringFromCapture &&
        !CinematicState.IsPlaying;

    /// <summary>
    /// The Nemesis as it is seen: the pivot of its animated model, which is what the arms reach
    /// from. On the prefab that is its root; a scene that offsets the model under the root (the
    /// test bed did once, by 1.3 m) takes the grab along with the body instead of leaving the
    /// hands that far short.
    /// </summary>
    private Transform NemesisBody =>
        nemesis.AnimController != null ? nemesis.AnimController.transform : nemesis.transform;

    private Plan MakePlan()
    {
        Vector3 nemesisAt = NemesisBody.position;
        Vector3 playerAt = player.transform.position;

        Vector3 axis = playerAt - nemesisAt;
        axis.y = 0f;
        float current = axis.magnitude;

        if (current > 0.05f)
        {
            axis /= current;
        }
        else
        {
            // One on top of the other: a hiding spot with no exit pose puts its player on the
            // approach point the Nemesis is standing on. It is still facing the spot it opened.
            axis = Vector3.ProjectOnPlane(nemesis.transform.forward, Vector3.up).normalized;
            current = 0f;
        }

        Plan made = new Plan
        {
            NemesisStart = nemesisAt,
            NemesisTarget = nemesisAt,
            PlayerStart = playerAt,
            PlayerTarget = playerAt,
            PlayerFacing = Quaternion.AngleAxis(config.PlayerYawOffset, Vector3.up) * Quaternion.LookRotation(-axis),
        };

        bool nemesisFree = nemesis.IsAgentReady;
        bool playerFree = !player.IsCarriedByPlatform;

        float gap = config.PairDistance - current;
        if (gap > MinGap)
        {
            float back = nemesisFree ? NemesisRoom(-axis, gap) : 0f;
            made.NemesisTarget = nemesisAt - axis * back;

            float rest = gap - back;
            if (rest > MinGap && playerFree) made.PlayerTarget = playerAt + axis * PlayerRoom(playerAt, axis, rest);
        }
        else if (gap < -MinGap && nemesisFree)
        {
            // Further apart than the arms reach, which a hiding spot's exit pose can be: the
            // Nemesis steps in.
            made.NemesisTarget = nemesisAt + axis * NemesisRoom(axis, -gap);
        }

        made.MoveNemesis = (made.NemesisTarget - nemesisAt).sqrMagnitude > MinGap * MinGap;
        made.MovePlayer = (made.PlayerTarget - playerAt).sqrMagnitude > MinGap * MinGap;
        return made;
    }

    /// <summary>Metres the Nemesis can go in a straight line over its own NavMesh, up to
    /// <paramref name="distance"/>.</summary>
    private float NemesisRoom(Vector3 direction, float distance)
    {
        Vector3 target = nemesis.transform.position + direction * distance;
        return nemesis.NavAgent.Raycast(target, out NavMeshHit hit) ? Mathf.Min(hit.distance, distance) : distance;
    }

    /// <summary>
    /// Metres the player can be moved in a straight line, up to <paramref name="distance"/>.
    /// Measured over the Nemesis's NavMesh and not with a capsule cast: the bake already keeps
    /// more than a player's radius off every wall and stops at every ledge, which are the two
    /// things a body moved by hand has to respect. A player standing off the mesh (on a prop, in a
    /// corner the bake left out) is left where they are.
    /// </summary>
    private float PlayerRoom(Vector3 from, Vector3 direction, float distance)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = nemesis.NavAgent != null ? nemesis.NavAgent.agentTypeID : 0,
            areaMask = NavMesh.AllAreas,
        };

        if (!NavMesh.SamplePosition(from, out NavMeshHit start, NavSampleRadius, filter)) return 0f;

        Vector3 target = start.position + direction * distance;
        return NavMesh.Raycast(start.position, target, out NavMeshHit hit, filter)
            ? Mathf.Min(hit.distance, distance)
            : distance;
    }

    private void Apply(in Plan plan, Quaternion startFacing, float amount)
    {
        if (plan.MoveNemesis && nemesis.IsAgentReady)
        {
            // Move and not a warp: relative and held to the NavMesh, and the facade's WarpTo would
            // throw away a route verdict this step has no business invalidating.
            Vector3 step = Vector3.Lerp(plan.NemesisStart, plan.NemesisTarget, amount) - NemesisBody.position;
            step.y = 0f;
            nemesis.NavAgent.Move(step);
        }

        if (plan.MovePlayer)
        {
            // TeleportTo and not transform.position: it clears the velocity, takes the queued
            // position along and syncs the colliders. The height stays the body's own.
            Vector3 at = Vector3.Lerp(plan.PlayerStart, plan.PlayerTarget, amount);
            at.y = player.transform.position.y;
            player.TeleportTo(at, player.transform.rotation);
        }

        // After TeleportTo, which points the model down the root's forward.
        Transform body = player.PlayerBody;
        if (body != null) body.rotation = Quaternion.Slerp(startFacing, plan.PlayerFacing, amount);
    }

    /// <summary>
    /// Takes the player out of Grabbed when nothing else will. A respawn does it by itself: the
    /// stand-up is played from its first frame while the screen is black. A capture that ends any
    /// other way (the defeat screen taken back by Retry, a controller without the stand-up state)
    /// would leave the rig on the clip's last frame, hanging in the air with control back.
    ///
    /// Whether the player is locked again by now does not matter, and is not checked: the escape's
    /// Retry frees the player and its cinematic locks them again on that same frame, so by the time
    /// this runs they are disabled, and the cinematic shows them on screen. Grabbed has no way out
    /// of its own (no transitions, nothing from Any State), so skipping it there left the player in
    /// the pose for the rest of the run. Only a stand-up in progress is left alone: it is already
    /// taking them out of it.
    /// </summary>
    private void LeaveGrabbed()
    {
        if (player == null || config == null) return;
        if (player.IsStandingUp) return;

        Animator animator = player.AnimController;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        int grabbed = Animator.StringToHash(config.GrabbedState);
        bool inGrabbed = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == grabbed ||
                         (animator.IsInTransition(0) &&
                          animator.GetNextAnimatorStateInfo(0).shortNameHash == grabbed);
        if (!inGrabbed) return;

        int idle = Animator.StringToHash(IdleStateName);
        if (animator.HasState(0, idle)) animator.CrossFadeInFixedTime(idle, IdleBlendSeconds, 0);
    }
}
