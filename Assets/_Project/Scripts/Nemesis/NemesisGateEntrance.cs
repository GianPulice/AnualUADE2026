using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Nemesis's entrance in Zona 2, seen on a security camera: something beats on the shut gate of
/// the arrival vestibule from the far side, the leaf dents at every blow, the last one sends it
/// flying, and out of the dust the Nemesis's eyes light up and it walks out into the level. Then
/// the player gets control back and the Nemesis patrols. It is the conductor and nothing else: it
/// decides WHEN each piece moves (<see cref="GateKnockOff"/> the leaf, <see cref="MeshDent"/> the
/// dents, <see cref="NemesisCinematicActor"/> the Nemesis, <see cref="EscapeShotCamera"/> the shot,
/// <see cref="RewardBoxCinematic"/> taking and giving back the screen).
///
/// WHY: on the playday nobody noticed the Nemesis had come out. Before this it woke in silence, far
/// away, out of sight (<see cref="NemesisZoneWake"/> still does that when this cannot play).
///
/// Started by <see cref="NemesisZoneWake"/> (<see cref="TryPlay"/>), once, after it has handed the
/// Nemesis Zona 2's routes. If the screen does not come free (another cinematic, a hiding spot, a
/// menu) within the config's wait, or the player skips, it lands in the state the cinematic ends in:
/// the gate down, the Nemesis awake and given back to its machine. Then it raises
/// <see cref="Finished"/>, and where the Nemesis goes from there is the caller's: the vestibule's
/// NavMesh does not reach Zona 2's floors (measured 26/11/2026: it stops halfway down the stairs), so
/// <see cref="NemesisZoneWake"/> moves it, out of sight, to the safest of the zone's spawn points.
///
/// The Nemesis is not in the shot while the blows land: behind the gate there is no floor and no
/// NavMesh to put it on, and the shot could not see it there anyway. It is put in the opening on the
/// frame the leaf hits the floor, under the dust — never earlier, or the falling leaf sweeps through
/// it — and walks out over the leaf and past the camera, without stopping.
///
/// SETUP: everything hangs off the "Entrada Porton" object under "Nemesis Zona 2" in Zona 1's scene.
/// </summary>
public class NemesisGateEntrance : MonoBehaviour
{
    [SerializeField] private SO_NemesisGateEntranceConfig config;

    [Header("Escena")]
    [Tooltip("La cámara de seguridad que mira el portón desde el vestíbulo (con SecurityCameraFeed: " +
             "la vista en blanco y negro).")]
    [SerializeField] private EscapeShotCamera shot;

    [Tooltip("La hoja del portón: la mueve y la tira.")]
    [SerializeField] private GateKnockOff gate;

    [Tooltip("Las abolladuras de la hoja (en el hijo Door del portón). Vacío = sin abolladuras.")]
    [SerializeField] private MeshDent dent;

    [Tooltip("Dónde cae cada golpe, sobre la hoja: uno por golpe del config, en el mismo orden. Si " +
             "faltan, los golpes que sobran usan el último.")]
    [SerializeField] private Transform[] hitPoints = Array.Empty<Transform>();

    [Tooltip("El polvo donde cae la hoja: se levanta cuando golpea el piso.")]
    [SerializeField] private ParticleSystem[] dust = Array.Empty<ParticleSystem>();

    [Header("Nemesis")]
    [Tooltip("Lo mueve durante la cinemática. Uno propio, en este objeto.")]
    [SerializeField] private NemesisCinematicActor actor;

    [Tooltip("Detrás del portón, en el pedazo de pasillo de Zona 1 copiado ahí, donde quedó trabado " +
             "al final del escape: está acá durante los golpes, se lo ve cuando cae la hoja, y de " +
             "acá camina hasta la abertura. Ahí no hay NavMesh: ese tramo lo camina por fuera.\n\n" +
             "Vacío = aparece directamente en la abertura cuando cae la hoja.")]
    [SerializeField] private Transform nemesisInside;

    [Tooltip("Metros por segundo del tramo de adentro hasta la abertura.")]
    [SerializeField, Min(0.1f)] private float insideWalkSpeed = 2.2f;

    [Tooltip("En la abertura, del lado del vestíbulo, sobre el NavMesh, mirando hacia afuera: acá " +
             "llega caminando desde adentro (o aparece, si no hay marca de adentro).")]
    [SerializeField] private Transform nemesisOpening;

    [Tooltip("Más allá de la cámara, sobre el NavMesh y del otro lado del vestíbulo: hacia acá " +
             "camina (el plano corta apenas pasa la cámara), y acá queda si se saltea.")]
    [SerializeField] private Transform nemesisExit;

    [Tooltip("La luz del plano (Light + FogLightBypass, como la lámpara del portón al final del " +
             "escape): frente al portón mientras lo golpean, y sobre el Nemesis desde que aparece " +
             "hasta el corte, para que se lo vea entre la niebla. Va apagada en la escena.")]
    [SerializeField] private GameObject nemesisLight;

    [Tooltip("Metros por encima de los pies del Nemesis a los que va la luz.")]
    [SerializeField] private float nemesisLightHeight = 2.2f;

    private readonly RewardBoxCinematic screen = new RewardBoxCinematic();
    private CancellationTokenSource runCts;
    private SO_VisionFogConfig pushedFog;
    private VisionRangeController fog;
    private bool started;
    private bool skipRequested;
    private int blowsLanded;
    private bool lightOnNemesis;
    private NavMeshAgent offMeshAgent;

    /// <summary>The cinematic is on screen.</summary>
    public bool IsPlaying => screen.IsPlaying;

    /// <summary>Raised once the entrance is over and the screen is the player's again (also after a
    /// skip, or when it played without the shot).</summary>
    public event Action Finished;

    /// <summary>
    /// Plays the entrance (once). The Nemesis must already have its Zona 2 routes. False when a piece
    /// is missing — nothing was taken, and the caller wakes the Nemesis its own way.
    /// </summary>
    public bool TryPlay()
    {
        if (started) return false;

        if (config == null || shot == null || gate == null || actor == null ||
            nemesisOpening == null || nemesisExit == null)
        {
            Debug.LogWarning($"[{nameof(NemesisGateEntrance)}] '{name}' is missing a piece (config, " +
                             "shot, gate, actor or a Nemesis mark): the Nemesis wakes without its " +
                             "entrance.", this);
            return false;
        }

        started = true;
        runCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunAsync(runCts.Token).Forget();
        return true;
    }

    private void Update()
    {
        if (IsPlaying && !skipRequested && SkipPressed()) skipRequested = true;
    }

    // After the actor has moved the body this frame. Before the Nemesis is out, the light hangs in
    // front of the gate, so the blows and the dents are seen; then it rides on the Nemesis.
    private void LateUpdate()
    {
        if (nemesisLight == null || !nemesisLight.activeSelf) return;

        Transform anchor = lightOnNemesis && actor != null && actor.HasControl && actor.Body != null
            ? actor.Body
            : nemesisOpening;
        if (anchor != null) nemesisLight.transform.position = anchor.position + Vector3.up * nemesisLightHeight;
    }

    private void SetNemesisLight(bool on)
    {
        if (nemesisLight == null) return;
        nemesisLight.SetActive(on);
        if (on) LateUpdate();
    }

    private void OnDestroy()
    {
        runCts?.Cancel();
        runCts?.Dispose();
        runCts = null;

        // No waiting here: the object is going away. Whatever the shot held is given back now.
        EndStepOverLeaf();
        CinematicState.SetHudHidden(false);
        PopShotFog();
        screen.End(null);
    }

    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        try
        {
            await Wait(config.StartDelay, token, skippable: false);

            bool shown = await WaitForScreenAsync(token);
            if (shown && screen.Begin(shot, null, config.Skippable, config.SkipPromptText))
            {
                // The facility watching, not the player's view: no gameplay HUD over it.
                CinematicState.SetHudHidden(true);
                PushShotFog();
                SetNemesisLight(true);
                if (dent != null) dent.Prepare();
                await PlayBeatsAsync(token);
            }
            else
            {
                Debug.Log($"[{nameof(NemesisGateEntrance)}] The screen never came free: the gate goes " +
                          "down and the Nemesis comes out without the shot.", this);
            }

            Finish();
        }
        catch (OperationCanceledException)
        {
            // Destroyed mid-run: OnDestroy gives the screen back.
        }
    }

    private async UniTask<bool> WaitForScreenAsync(CancellationToken token)
    {
        float waited = 0f;
        while (!RewardBoxCinematic.ScreenIsFree())
        {
            if (waited >= config.MaxWaitForScreen) return false;
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            waited += Time.unscaledDeltaTime;
        }
        return true;
    }

    private async UniTask PlayBeatsAsync(CancellationToken token)
    {
        // Already behind the gate, in the dark, where the escape left it: the leaf hides it until
        // it falls.
        if (nemesisInside != null) TakeNemesisInside();

        await Wait(config.HoldBefore, token);

        // The blows. The last one dents, and then knocks the leaf over.
        SO_NemesisGateEntranceConfig.Blow[] blows = config.Blows;
        for (int i = 0; i < blows.Length; i++)
        {
            if (skipRequested) return;
            await Wait(blows[i].delay, token);
            if (skipRequested) return;

            bool last = i == blows.Length - 1;
            Strike(blows[i], HitPoint(i), lean: !last);
        }

        await Wait(config.KnockOffDelay, token);
        if (skipRequested) return;

        // The fall is not cut by a skip halfway: Finish lands the leaf where it was going.
        await gate.KnockOffAsync(token);
        Land();

        // With no corridor behind the gate, it appears in the opening only now, under the dust: put
        // there any earlier, the falling leaf sweeps through it (every point in front of the gate
        // is in its way down).
        if (!actor.HasControl) TakeNemesis();

        // The light leaves the gate for the Nemesis: it is seen standing there.
        lightOnNemesis = true;

        await Wait(config.SettleSeconds, token);
        if (skipRequested) return;

        actor.SetEyesVisible(true);
        PlaySound(config.AppearSoundId, actor.Body != null ? actor.Body.position : nemesisOpening.position);
        FlareEyesAsync(token).Forget();

        await Wait(config.EyesToWalkSeconds, token);
        if (skipRequested) return;

        // From the corridor to the opening, where the NavMesh starts.
        if (offMeshAgent != null) await WalkOffMeshAsync(nemesisOpening, token);
        if (skipRequested) return;

        // Out past the camera, without stopping: the shot cuts a moment after it has gone by.
        actor.WalkTo(nemesisExit);
        float walked = 0f;
        while (actor.IsMoving && walked < config.WalkMaxSeconds && !skipRequested && !HasPassedCamera())
        {
            TickStepOverLeaf();
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            walked += Time.deltaTime;
        }
        EndStepOverLeaf();
        if (skipRequested) return;

        await Wait(config.HoldEnd, token);
    }

    /// <summary>The Nemesis is level with the camera or beyond it, along the way out of the gate.</summary>
    private bool HasPassedCamera()
    {
        Transform body = actor.Body;
        return body != null && Vector3.Dot(body.position - shot.transform.position, gate.Outward) >= 0f;
    }

    /// <summary>
    /// The state the cinematic ends in, from wherever it got to (the end, a skip, or no screen): the
    /// gate down, the Nemesis outside it with its eyes as authored, given back to its machine, and
    /// the screen given back to the player.
    /// </summary>
    private void Finish()
    {
        DentRemaining();
        gate.SnapKnockedOff();

        bool offMesh = offMeshAgent != null;
        BackOnNavMesh(nemesisOpening);
        EndStepOverLeaf();

        bool hadNemesis = actor.HasControl;
        if (!hadNemesis) hadNemesis = TakeNemesis();

        if (hadNemesis)
        {
            if (skipRequested || !IsPlaying || offMesh) actor.WarpTo(nemesisExit);
            actor.SetEyesFlare(0f, config.EyesFlarePower, config.EyesFlareCoreRadius,
                               config.EyesFlareHaloIntensity, config.EyesFlareHaloRadius);
            actor.SetEyesVisible(true);
            actor.Release();
        }

        if (skipRequested) StopDust();
        SetNemesisLight(false);

        CinematicState.SetHudHidden(false);
        PopShotFog();
        screen.End(this);

        Finished?.Invoke();
    }

    private void Strike(SO_NemesisGateEntranceConfig.Blow blow, Transform point, bool lean)
    {
        blowsLanded++;
        gate.Shudder(blow.push, lean ? blow.leanDegrees : 0f);

        if (dent != null && point != null)
            dent.Dent(point.position, gate.Outward, blow.dentRadius, blow.dentDepth, 0.08f);

        shot.Shake(blow.shake, blow.shakeSeconds);
        PlayClip(config.BlowClip, point != null ? point.position : gate.transform.position, blow.volume);
    }

    private void Land()
    {
        PlayDust();
        shot.Shake(config.LandShake, config.LandShakeSeconds);
        PlayClip(config.LandClip, nemesisOpening.position, config.LandVolume);
    }

    /// <summary>
    /// Takes the Nemesis and puts it in the opening, eyes shut. A Nemesis the escape left parked at
    /// Zona 1's gate is still that cinematic's: it is handed back first.
    /// </summary>
    private bool TakeNemesis()
    {
        foreach (NemesisCinematicActor other in FindObjectsByType<NemesisCinematicActor>())
            if (other != actor && other.HasControl) other.Release();

        if (!actor.TryTakeControl()) return false;

        actor.WarpTo(nemesisOpening);
        actor.SetEyesVisible(false);
        return true;
    }

    /// <summary>
    /// Takes the Nemesis and stands it in the corridor behind the gate, eyes shut. There is no
    /// NavMesh there (it is a copy of Zona 1's corridor, not part of the bake), so its agent is
    /// switched off and the body placed by hand until <see cref="BackOnNavMesh"/>.
    /// </summary>
    private void TakeNemesisInside()
    {
        foreach (NemesisCinematicActor other in FindObjectsByType<NemesisCinematicActor>())
            if (other != actor && other.HasControl) other.Release();

        if (!actor.TryTakeControl() || actor.Body == null) return;

        NemesisStateManager nemesis = actor.Body.GetComponent<NemesisStateManager>();
        offMeshAgent = nemesis != null ? nemesis.NavAgent : null;
        if (offMeshAgent != null) offMeshAgent.enabled = false;

        actor.Body.SetPositionAndRotation(nemesisInside.position, Quaternion.Euler(0f, nemesisInside.eulerAngles.y, 0f));
        actor.SetEyesVisible(false);
    }

    /// <summary>
    /// Walks the body, by hand, straight to <paramref name="target"/> (the opening): the legs follow
    /// on their own, measured off how far the body moved each frame. Then it is back on the NavMesh.
    /// </summary>
    private async UniTask WalkOffMeshAsync(Transform target, CancellationToken token)
    {
        Transform body = actor.Body;
        NemesisStateManager nemesis = body != null ? body.GetComponent<NemesisStateManager>() : null;
        if (nemesis == null || target == null) return;

        nemesis.SetGait(NemesisStateManager.EGait.Walking, insideWalkSpeed);

        Vector3 start = body.position;
        Vector3 goal = target.position;
        Vector3 flat = goal - start;
        flat.y = 0f;
        float length = flat.magnitude;
        float covered = 0f;

        while (!skipRequested && covered < length)
        {
            covered = Mathf.Min(length, covered + insideWalkSpeed * Time.deltaTime);
            float t = length > 0f ? covered / length : 1f;

            // On the floor, raised by the fallen leaf where it steps onto it.
            Vector3 next = Vector3.Lerp(start, goal, t);
            next.y = Mathf.Lerp(start.y, goal.y, t) + gate.StepHeightAt(next);
            body.SetPositionAndRotation(next, Quaternion.Slerp(body.rotation, Quaternion.LookRotation(flat),
                                                               1f - Mathf.Exp(-Time.deltaTime / 0.15f)));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        BackOnNavMesh(target);
    }

    // The agent's own height over the NavMesh, raised while the Nemesis walks over the fallen leaf
    // (the NavMesh does not know about the leaf: without this its feet sink into the sheet).
    private NavMeshAgent steppingAgent;
    private float steppingBaseOffset;

    private void TickStepOverLeaf()
    {
        if (actor.Body == null) return;

        if (steppingAgent == null)
        {
            NemesisStateManager nemesis = actor.Body.GetComponent<NemesisStateManager>();
            steppingAgent = nemesis != null ? nemesis.NavAgent : null;
            if (steppingAgent == null) return;
            steppingBaseOffset = steppingAgent.baseOffset;
        }

        float target = steppingBaseOffset + gate.StepHeightAt(actor.Body.position);
        steppingAgent.baseOffset = Mathf.Lerp(steppingAgent.baseOffset, target, 1f - Mathf.Exp(-Time.deltaTime / 0.08f));
    }

    private void EndStepOverLeaf()
    {
        if (steppingAgent != null) steppingAgent.baseOffset = steppingBaseOffset;
        steppingAgent = null;
    }

    /// <summary>Switches the agent back on at <paramref name="at"/> (on the NavMesh) if this cinematic
    /// had switched it off.</summary>
    private void BackOnNavMesh(Transform at)
    {
        if (offMeshAgent == null) return;

        if (actor.Body != null && at != null) actor.Body.position = at.position;
        offMeshAgent.enabled = true;
        offMeshAgent = null;
        if (at != null) actor.WarpTo(at.position, at.eulerAngles.y);

        // Already standing on the leaf when it lands back on the NavMesh: no drop into the sheet.
        TickStepOverLeaf();
        if (steppingAgent != null && actor.Body != null)
            steppingAgent.baseOffset = steppingBaseOffset + gate.StepHeightAt(actor.Body.position);
    }

    /// <summary>The flare of the escape's eyes shot: up, held, back down to the eyes as authored.</summary>
    private async UniTaskVoid FlareEyesAsync(CancellationToken token)
    {
        float rise = config.EyesFlareRiseSeconds;
        float hold = config.EyesFlareHoldSeconds;
        float fall = config.EyesFlareFallSeconds;
        float total = rise + hold + fall;
        float elapsed = 0f;

        try
        {
            while (elapsed < total && actor.HasControl)
            {
                float up = rise > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / rise) : 1f;
                float down = fall > 0f ? Mathf.SmoothStep(0f, 1f, (elapsed - rise - hold) / fall) : 0f;
                actor.SetEyesFlare(up * (1f - down), config.EyesFlarePower, config.EyesFlareCoreRadius,
                                   config.EyesFlareHaloIntensity, config.EyesFlareHaloRadius);

                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
        }
        catch (OperationCanceledException)
        {
            // Finish or OnDestroy puts the eyes back.
        }
        finally
        {
            if (actor != null)
                actor.SetEyesFlare(0f, config.EyesFlarePower, config.EyesFlareCoreRadius,
                                   config.EyesFlareHaloIntensity, config.EyesFlareHaloRadius);
        }
    }

    /// <summary>
    /// A skip still leaves the dents the blows that had not landed yet would have made, all at once.
    /// Called while the leaf still stands in its frame: the hit points are fixed in the scene, on the
    /// standing leaf, and mean nothing once it has moved. Every blow lands before the leaf flies, so
    /// a skip after that has nothing left to do here.
    /// </summary>
    private void DentRemaining()
    {
        if (dent == null || gate.IsKnockedOff) return;

        SO_NemesisGateEntranceConfig.Blow[] blows = config.Blows;
        for (int i = blowsLanded; i < blows.Length; i++)
        {
            Transform point = HitPoint(i);
            if (point != null) dent.Dent(point.position, gate.Outward, blows[i].dentRadius, blows[i].dentDepth, 0f);
        }
        blowsLanded = blows.Length;
    }

    private Transform HitPoint(int index)
    {
        if (hitPoints == null || hitPoints.Length == 0) return null;
        return hitPoints[Mathf.Min(index, hitPoints.Length - 1)];
    }

    private void PlayDust()
    {
        foreach (ParticleSystem ps in dust)
        {
            if (ps == null) continue;
            ps.Clear(false);
            ps.Play(false);
        }
    }

    private void StopDust()
    {
        foreach (ParticleSystem ps in dust)
            if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void PushShotFog()
    {
        if (config.ShotFog == null) return;
        fog = FindAnyObjectByType<VisionRangeController>();
        if (fog == null) return;

        fog.PushConfig(config.ShotFog);
        pushedFog = config.ShotFog;
    }

    private void PopShotFog()
    {
        if (fog != null && pushedFog != null) fog.PopConfig(pushedFog);
        pushedFog = null;
        fog = null;
    }

    private static void PlayClip(AudioClip clip, Vector3 position, float volume)
    {
        if (clip != null && AudioManager.Exists) AudioManager.Instance.PlaySFX(clip, position, volume);
    }

    private static void PlaySound(string id, Vector3 position)
    {
        if (!string.IsNullOrWhiteSpace(id) && AudioManager.Exists) AudioManager.Instance.PlaySFX(id, position);
    }

    /// <summary>Waits <paramref name="seconds"/> of game time; a skip ends it early.</summary>
    private async UniTask Wait(float seconds, CancellationToken token, bool skippable = true)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (skippable && skipRequested) return;
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;
        }
    }

    // Same rules as the escape and the reward box: legacy input, because the project runs both
    // backends, and never through a menu or a loading screen.
    private bool SkipPressed()
    {
        if (config == null || !config.Skippable) return false;
        if (PauseManager.IsGameplayInputBlocked || LoadingScreen.IsLoading) return false;
        return Input.GetKeyDown(config.SkipKey);
    }
}
