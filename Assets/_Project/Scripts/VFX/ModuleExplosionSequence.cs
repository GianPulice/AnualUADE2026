using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// What happens on screen when a module explodes.
///
/// Two cases:
///   - The explosion does NOT end the run (a penalty, see GameResultManager.ExplosionEndsRun):
///     the same camera shot as the defeat plays (SO_ModuleExplosionConfig.CinematicOnPenalty),
///     then the view blends back to the gameplay rig and the player gets control again. With the
///     option off, the effect just plays in place with the sound and a shake.
///   - The explosion ENDS the run: this component is the registered
///     <see cref="IGameOverPresenter"/>, so ReportGameOver hands it the run instead of opening the
///     GameOver screen on the same frame. It locks the player, takes the camera to a shot of the
///     body part, plays the explosion, waits for it to finish and only then commits the result,
///     whose screen fades in slowly (ResultPresentation.FadeInDuration).
///
/// Missing pieces never block the run: no VFX prefab, no sound, no AudioManager, no brain or no
/// player each log ONE warning and the sequence skips that step. The result is committed in a
/// finally, so even an exception mid-cinematic still ends the run.
///
/// Lives on the Player prefab. It finds the player through PlayerRegistry anyway, so it also works
/// anywhere else in the gameplay scene.
/// </summary>
public class ModuleExplosionSequence : MonoBehaviour, IGameOverPresenter, IModalUI
{
    [SerializeField] private SO_ModuleExplosionConfig config;

    [Tooltip("Optional fixed spot for the defeat shot. Empty = the shot is computed around the " +
             "body part from the config's distance, height and yaw.")]
    [SerializeField] private Transform cameraAnchorOverride;

    private const int DefeatCameraPriority = 1000;
    private const float MinClearDistance = 0.6f;

    private PlayerStateManager player;
    private CinemachineCamera defeatCamera;
    private CinemachineImpulseSource impulseSource;
    private CancellationTokenSource sequenceCts;

    /// <summary>A cinematic (penalty or defeat) owns the camera and the player right now.</summary>
    private bool isPlaying;

    /// <summary>The run-ending shot has been handed the run. Stays true: there is only one.</summary>
    private bool playingDefeat;

    /// <summary>This component disabled the player, so it is the one that re-enables it.</summary>
    private bool lockedPlayer;

    private bool warnedNoSfx, warnedNoVfx, warnedNoBrain;

    // ── IModalUI ─────────────────────────────────────────────────────────────────────────
    // Pushed only while the cinematic plays: it blocks pause, the inventory and the gameplay
    // camera input, and the HUD hides itself (ModalVisibilityGate). The game keeps running.
    public string ModalId => "ModuleExplosionSequence";
    public bool ConsumesEscape => true;
    public bool BlocksPause => true;
    public bool PausesGame => false;
    public void RequestClose() { }

    private void OnEnable()
    {
        GameResultManager.GameOverPresenter = this;
        ModuleEvents.OnExploded += HandleModuleExploded;

        // From here on this component decides WHEN a penalty lands (after its cinematic), so
        // ModuleManager stops raising OnPenaltyApplied on the explosion frame.
        ModuleEvents.PenaltyPresenterActive = true;
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    }

    private void OnDisable()
    {
        if (ReferenceEquals(GameResultManager.GameOverPresenter, this))
            GameResultManager.GameOverPresenter = null;

        ModuleEvents.OnExploded -= HandleModuleExploded;
        ModuleEvents.PenaltyPresenterActive = false;
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);

        // Cancelling runs the sequence's finally, which still commits the result.
        sequenceCts?.Cancel();
        sequenceCts?.Dispose();
        sequenceCts = null;

        PopModal();
        UnlockPlayer();
        isPlaying = false;
        if (defeatCamera != null) Destroy(defeatCamera.gameObject);
    }

    private void HandlePlayerRegistered(PlayerStateManager registered) => player = registered;

    // ── Penalty explosion (the run goes on) ──────────────────────────────────────────────

    private void HandleModuleExploded(ModuleRuntime runtime)
    {
        // The run-ending explosion is played by PresentGameOver, with the cinematic around it.
        // Checked here and not there because the two arrive in either order depending on which
        // path reported the GameOver.
        //
        // Its penalty lands straight away: the run is over, nobody will see the limp, and
        // holding it back would only leave it unapplied if the result screen tore this down.
        if (GameResultManager.ExplosionEndsRun(runtime))
        {
            ModuleEvents.RaisePenaltyApplied(runtime);
            return;
        }

        // Same camera shot as the defeat, then back to gameplay. Skipped (plain effect in place)
        // when turned off in the config, or when another cinematic is already on screen.
        if (config != null && config.CinematicOnPenalty && !isPlaying)
        {
            RestartSequenceToken();
            RunPenaltySequence(runtime, sequenceCts.Token).Forget();
            return;
        }

        Transform focus = FindFocusBone(runtime);
        Vector3 at = focus != null ? focus.position : FallbackFocus();
        PlayExplosion(at, runtime);
        ModuleEvents.RaisePenaltyApplied(runtime);
    }

    private async UniTaskVoid RunPenaltySequence(ModuleRuntime cause, CancellationToken token)
    {
        isPlaying = true;

        try
        {
            await PlayShot(cause, token);

            // Destroying the shot camera hands the view back to the gameplay rig through the
            // brain's default blend (the cut set in BeginDefeatCamera is restored two frames in).
            // Control only comes back once that blend has landed: moving while the camera is still
            // flying home reads as the game having left the player behind.
            ReleaseShotCamera();
            await WaitForBlendBack(token);
        }
        catch (OperationCanceledException)
        {
            // Disabled or unloaded mid-cinematic. The finally still hands control back.
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
        finally
        {
            ReleaseShotCamera();
            UnlockPlayer();
            PopModal();
            isPlaying = false;

            // Last, with the camera home and control back: this is what starts the limp or the
            // blindness. In the finally so a cancelled shot (scene unload) still applies it.
            ModuleEvents.RaisePenaltyApplied(cause);
        }
    }

    private void ReleaseShotCamera()
    {
        if (defeatCamera != null) Destroy(defeatCamera.gameObject);
        defeatCamera = null;
    }

    /// <summary>
    /// Waits for the brain to finish blending back to the gameplay camera. Bounded, so a blend that
    /// never reports done (a custom blend asset, a brain swapped mid-shot) cannot keep the player
    /// locked.
    /// </summary>
    private async UniTask WaitForBlendBack(CancellationToken token)
    {
        const float MaxBlendWait = 3f;

        // Destroy lands at the end of the frame: the blend only starts on the next one.
        await UniTask.Yield(PlayerLoopTiming.Update, token);

        CinemachineBrain brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain == null) return;

        float waited = 0f;
        while (brain != null && brain.IsBlending && waited < MaxBlendWait)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            waited += Time.unscaledDeltaTime;
        }
    }

    // ── Run-ending explosion (IGameOverPresenter) ────────────────────────────────────────

    public void PresentGameOver(ModuleRuntime cause, Action commit)
    {
        if (playingDefeat)
        {
            // Already presenting: the first commit ends the run, a second one would be dropped by
            // GameResultManager anyway.
            return;
        }

        playingDefeat = true;

        // NOT a restart of the token: a penalty cinematic still on screen is left to finish (see
        // RunDefeatSequence), and cancelling it here would race its cleanup against this shot.
        if (sequenceCts == null) sequenceCts = new CancellationTokenSource();
        RunDefeatSequence(cause, commit, sequenceCts.Token).Forget();
    }

    private async UniTaskVoid RunDefeatSequence(ModuleRuntime cause, Action commit, CancellationToken token)
    {
        bool committed = false;

        try
        {
            // A penalty cinematic can still be on screen (two explosions close together). It used
            // to be `if (isPlaying) return;` here, which dropped the commit and never ended the run.
            if (isPlaying) await UniTask.WaitWhile(() => isPlaying, PlayerLoopTiming.Update, token);

            isPlaying = true;
            await PlayShot(cause, token);
        }
        catch (OperationCanceledException)
        {
            // Disabled or unloaded mid-cinematic. The finally still ends the run.
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
        finally
        {
            isPlaying = false;

            if (!committed)
            {
                committed = true;
                commit?.Invoke();
            }

            // After the commit: the result screen has pushed its own modal by now, so popping
            // this one does not hand the cursor back to gameplay for a frame. The player stays
            // locked and the shot camera stays up: the result screen covers them.
            PopModal();
        }
    }

    // ── Steps ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The shot both cases share: lock the player, take the camera to the body part, explode, and
    /// wait for the effect. What happens afterwards is the caller's: the penalty hands control
    /// back, the defeat commits the result.
    /// </summary>
    private async UniTask PlayShot(ModuleRuntime cause, CancellationToken token)
    {
        LockPlayer();
        PushModal();

        Transform focus = FindFocusBone(cause);
        CinemachineBrain brain = FindBrain();

        if (brain != null && config != null)
        {
            BeginDefeatCamera(brain);
            await MoveCameraToShot(brain, focus, token);
            await Wait(config.PreExplosionHold, token);
        }

        Vector3 at = focus != null ? focus.position : FallbackFocus();
        float effectDuration = PlayExplosion(at, cause);

        float wait = config != null
            ? Mathf.Min(effectDuration, config.MaxEffectWait) + config.PostExplosionHold
            : 0f;
        await Wait(wait, token);
    }

    private void RestartSequenceToken()
    {
        sequenceCts?.Cancel();
        sequenceCts?.Dispose();
        sequenceCts = new CancellationTokenSource();
    }

    private void LockPlayer()
    {
        // IsDisabled drives the FSM into PlayerDisabledState (no movement, locomotion cleared) and
        // makes OnCaptured a no-op, so the Nemesis cannot start a respawn under the cinematic.
        //
        // Only taken when it was free, and only then given back (UnlockPlayer): a player already
        // disabled by something else (a capture, the wake-up) must not be freed by this shot.
        if (player == null || player.IsDisabled) return;
        player.IsDisabled = true;
        lockedPlayer = true;
    }

    private void UnlockPlayer()
    {
        if (!lockedPlayer) return;
        lockedPlayer = false;
        if (player != null) player.IsDisabled = false;
    }

    /// <returns>Seconds the effect lasts (0 when there is none).</returns>
    private float PlayExplosion(Vector3 at, ModuleRuntime cause)
    {
        float duration = 0f;

        // Same frame as the VFX: the body reads as hurt the moment the hit is seen, not only once
        // the cinematic is over and the gameplay penalty lands.
        if (player != null && cause != null && cause.Data != null)
            player.ShowInjuredAnimation(cause.Data.Penalty);

        // Same frame too: the module's LED turns red with the blast, not when the state changed.
        if (cause != null) ModuleEvents.RaiseExplosionShown(cause);

        if (config != null && config.VfxPrefab != null)
        {
            ExplosionVFX vfx = Instantiate(config.VfxPrefab, at, Quaternion.identity);
            vfx.Play();
            duration = vfx.Duration;
        }
        else
        {
            WarnOnce(ref warnedNoVfx, "No explosion VFX prefab assigned in the config. The " +
                                      "explosion plays without a visual.");
        }

        PlaySfx(at);
        Shake();

        return duration;
    }

    private void PlaySfx(Vector3 at)
    {
        if (!AudioManager.Exists)
        {
            WarnOnce(ref warnedNoSfx, "There is no AudioManager in the scene. The explosion plays " +
                                      "silently.");
            return;
        }

        if (config == null || config.SfxClip == null)
        {
            WarnOnce(ref warnedNoSfx, "No explosion SFX clip assigned in the config. The explosion " +
                                      "plays without its main sound.");
        }
        else
        {
            AudioManager.Instance.PlaySFX(config.SfxClip, at, config.SfxVolume);
        }

        // The extra layers play in parallel, each on its own pooled source.
        if (config == null || config.ExtraSfxLayers == null) return;

        foreach (SO_ModuleExplosionConfig.SfxLayer layer in config.ExtraSfxLayers)
        {
            if (layer.clip == null) continue;

            if (layer.delay <= 0f) AudioManager.Instance.PlaySFX(layer.clip, at, layer.volume);
            else PlayLayerDelayed(layer, at).Forget();
        }
    }

    private async UniTaskVoid PlayLayerDelayed(SO_ModuleExplosionConfig.SfxLayer layer, Vector3 at)
    {
        // Unscaled, like the rest of the cinematic: the result screen sets timeScale to 0.
        await UniTask.Delay(TimeSpan.FromSeconds(layer.delay), DelayType.UnscaledDeltaTime,
                            PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());

        if (AudioManager.Exists) AudioManager.Instance.PlaySFX(layer.clip, at, layer.volume);
    }

    private void Shake()
    {
        if (config == null || config.ShakeAmplitude <= 0f) return;

        CinemachineBrain brain = FindBrain();
        if (brain == null) return;

        // The listener has to be on whichever camera is live: the gameplay rig for a penalty
        // explosion, the defeat camera during the cinematic.
        if (brain.ActiveVirtualCamera is CinemachineVirtualCameraBase live)
            EnsureImpulseListener(live);

        if (impulseSource == null)
        {
            impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
            impulseSource.ImpulseDefinition.ImpulseChannel = 1;
            impulseSource.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            impulseSource.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Explosion;
        }

        impulseSource.ImpulseDefinition.ImpulseDuration = Mathf.Max(0.01f, config.ShakeDuration);
        impulseSource.GenerateImpulseWithVelocity(UnityEngine.Random.onUnitSphere * config.ShakeAmplitude);
    }

    private static void EnsureImpulseListener(CinemachineVirtualCameraBase vcam)
    {
        if (vcam.GetComponent<CinemachineImpulseListener>() != null) return;

        // AddComponent does not run Reset(), so the defaults are set by hand (same values).
        CinemachineImpulseListener listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
        listener.ApplyAfter = CinemachineCore.Stage.Noise;
        listener.ChannelMask = 1;
        listener.Gain = 1f;
        listener.UseCameraSpace = true;
        listener.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction
        {
            AmplitudeGain = 1f,
            FrequencyGain = 1f,
            Duration = 1f
        };
    }

    // ── Camera ───────────────────────────────────────────────────────────────────────────

    private void BeginDefeatCamera(CinemachineBrain brain)
    {
        Camera output = brain.OutputCamera;

        GameObject go = new GameObject("DefeatCamera");
        go.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);

        defeatCamera = go.AddComponent<CinemachineCamera>();
        defeatCamera.Lens = LensSettings.FromCamera(output);
        defeatCamera.Priority = DefeatCameraPriority;
        EnsureImpulseListener(defeatCamera);

        // It starts exactly where the gameplay camera is, so a cut is invisible and the travel is
        // entirely this component's tween. Letting the brain blend on top would stack a second,
        // differently timed ease over it.
        CinemachineBlendDefinition previousBlend = brain.DefaultBlend;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        RestoreBlendNextFrame(brain, previousBlend).Forget();
    }

    private static async UniTaskVoid RestoreBlendNextFrame(CinemachineBrain brain, CinemachineBlendDefinition blend)
    {
        await UniTask.DelayFrame(2, PlayerLoopTiming.Update);
        if (brain != null) brain.DefaultBlend = blend;
    }

    private async UniTask MoveCameraToShot(CinemachineBrain brain, Transform focus, CancellationToken token)
    {
        Transform cam = defeatCamera.transform;
        Vector3 startPos = cam.position;
        Quaternion startRot = cam.rotation;

        float duration = config.CameraMoveDuration;
        float elapsed = 0f;

        do
        {
            Vector3 focusPos = focus != null ? focus.position : FallbackFocus();
            Vector3 targetPos = ComputeShotPosition(focusPos);
            Quaternion targetRot = Quaternion.LookRotation(focusPos - targetPos, Vector3.up);

            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            float eased = t * t * (3f - 2f * t);

            cam.SetPositionAndRotation(Vector3.Lerp(startPos, targetPos, eased),
                                       Quaternion.Slerp(startRot, targetRot, eased));

            if (t >= 1f) break;

            // Update timing: CinemachineBrain reads the camera in its LateUpdate, so the pose is
            // already in place for this frame.
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.unscaledDeltaTime;
        }
        while (true);
    }

    private Vector3 ComputeShotPosition(Vector3 focusPos)
    {
        if (cameraAnchorOverride != null) return cameraAnchorOverride.position;

        Transform body = player != null && player.PlayerBody != null ? player.PlayerBody
                       : player != null ? player.transform : null;
        Vector3 forward = body != null ? Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

        // Try the authored three-quarter angle, then its mirror, then straight on; take the first
        // with a clear line to the body part, or the least blocked one.
        float yaw = config.CameraYawOffset;
        float[] candidates = { yaw, -yaw, 0f, 180f - yaw };

        Vector3 best = focusPos;
        float bestClear = -1f;

        foreach (float candidate in candidates)
        {
            Vector3 dir = Quaternion.AngleAxis(candidate, Vector3.up) * forward;
            Vector3 offset = dir * config.CameraDistance + Vector3.up * config.CameraHeight;
            float clear = ClearDistance(focusPos, offset);

            if (clear >= offset.magnitude - 0.01f) return focusPos + offset;

            if (clear > bestClear)
            {
                bestClear = clear;
                best = focusPos + offset.normalized * Mathf.Max(clear, MinClearDistance);
            }
        }

        return best;
    }

    /// <summary>How far from the focus the camera can go along the offset before hitting level geometry.</summary>
    private float ClearDistance(Vector3 focusPos, Vector3 offset)
    {
        float length = offset.magnitude;
        RaycastHit[] hits = Physics.SphereCastAll(focusPos, 0.15f, offset / length, length,
                                                  Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float nearest = length;
        foreach (RaycastHit hit in hits)
        {
            // The player's own colliders sit right on the focus point.
            if (player != null && hit.collider.transform.IsChildOf(player.transform)) continue;
            if (hit.distance > 0f && hit.distance < nearest) nearest = hit.distance - 0.1f;
        }

        return Mathf.Max(0f, nearest);
    }

    private CinemachineBrain FindBrain()
    {
        if (CinemachineBrain.ActiveBrainCount > 0) return CinemachineBrain.GetActiveBrain(0);

        WarnOnce(ref warnedNoBrain, "No active CinemachineBrain. The defeat cinematic skips the camera move.");
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private Transform FindFocusBone(ModuleRuntime runtime)
    {
        if (player == null || player.AnimController == null || !player.AnimController.isHuman) return null;

        Animator anim = player.AnimController;
        PenaltyType penalty = runtime != null && runtime.Data != null ? runtime.Data.Penalty : PenaltyType.Chest;

        switch (penalty)
        {
            case PenaltyType.Legs:
                return anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg) ??
                       anim.GetBoneTransform(HumanBodyBones.Hips);
            case PenaltyType.Head:
                return anim.GetBoneTransform(HumanBodyBones.Head);
            default:
                return anim.GetBoneTransform(HumanBodyBones.Chest) ??
                       anim.GetBoneTransform(HumanBodyBones.Spine);
        }
    }

    private Vector3 FallbackFocus()
    {
        return player != null ? player.transform.position + Vector3.up : transform.position;
    }

    private static UniTask Wait(float seconds, CancellationToken token)
    {
        if (seconds <= 0f) return UniTask.CompletedTask;
        return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime,
                             PlayerLoopTiming.Update, token);
    }

    private void PushModal()
    {
        if (UIStateManager.Exists && !UIStateManager.Instance.Contains(this))
            UIStateManager.Instance.Push(this);
    }

    private void PopModal()
    {
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    /// <summary>Testing: drains the active module's timer so it explodes on the next tick.</summary>
    [ContextMenu("Debug/Explode Active Module")]
    private void DebugExplodeActiveModule()
    {
        if (!Application.isPlaying || !ModuleManager.Exists) return;
        if (ModuleManager.Instance.GetActiveModule() == null)
        {
            Debug.LogWarning($"[{nameof(ModuleExplosionSequence)}] No active module to explode.", this);
            return;
        }
        ModuleManager.Instance.ApplyTimePenalty(float.MaxValue);
    }

    private void WarnOnce(ref bool flag, string message)
    {
        if (flag) return;
        flag = true;
        Debug.LogWarning($"[{nameof(ModuleExplosionSequence)}] {message}", this);
    }
}
