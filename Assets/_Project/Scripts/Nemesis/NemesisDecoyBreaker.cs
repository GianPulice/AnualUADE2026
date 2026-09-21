using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Makes the Nemesis smash the decoy it came to investigate (the radio).
///
/// Goes on the Nemesis root, next to <see cref="NemesisStateManager"/>, the same way
/// <see cref="NemesisDoorUser"/> does: it adds no state to the FSM. Getting there is plain
/// Investigating — the decoy is a noise like any other. This only watches: while investigating,
/// if the noise that brought it here is a breakable decoy and it is within reach, it holds the
/// body, turns it to face the decoy, waits the windup, breaks it, and waits the recovery.
///
/// "The noise that brought it here" is <see cref="FieldOfListening.LastHeardDecoy"/> and not
/// whatever breakable is nearest, so a radio it walks past while chasing a footstep is left alone.
///
/// If the FSM leaves Investigating during the beat (it saw the player), the beat is abandoned:
/// before the hit the decoy is told it was aborted and keeps sounding; after it, the recovery is
/// cut short. The hold is the body only — decisions keep running underneath, as with
/// <see cref="NemesisStateManager.SetExternalHold"/>'s other caller.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
[AddComponentMenu("WIRED/Nemesis/Nemesis Decoy Breaker")]
public class NemesisDecoyBreaker : MonoBehaviour
{
    [Tooltip("Trigger del Animator del Nemesis que dispara el golpe. Vacío = ninguno (todavía no " +
             "hay animación).")]
    [SerializeField] private string breakAnimatorTrigger = "";

    [Tooltip("Grados por segundo con los que gira hacia el señuelo durante el corte.")]
    [SerializeField, Min(0f)] private float turnSpeed = 360f;

    [Tooltip("Diferencia de altura máxima con el señuelo. El alcance se mide en horizontal, y sin " +
             "esto rompería una radio que está en el piso de arriba, a través de la losa.")]
    [SerializeField, Min(0f)] private float maxHeightDifference = 2f;

    [Tooltip("Segundos entre chequeos. Perilla de performance, no de diseño.")]
    [SerializeField, Min(0.02f)] private float checkInterval = 0.15f;

    /// <summary>The beat starts / ends (after the hit and recovery, or when it was abandoned). For
    /// whoever stages it on the Nemesis's side — camera, audio. The decoy has its own events.</summary>
    public event Action<INemesisBreakableDecoy> BreakStarted;
    public event Action<INemesisBreakableDecoy> BreakFinished;

    public bool IsBreaking { get; private set; }

    private NemesisStateManager stateManager;
    private float checkTimer;
    private int breakTriggerHash;

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();
        breakTriggerHash = string.IsNullOrEmpty(breakAnimatorTrigger) ? 0 : Animator.StringToHash(breakAnimatorTrigger);
    }

    private void Update()
    {
        if (IsBreaking) return;
        if (stateManager == null || !stateManager.IsActive) return;
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;

        if (!TryFindDecoyInReach(out INemesisBreakableDecoy decoy)) return;

        BreakAsync(decoy, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private bool TryFindDecoyInReach(out INemesisBreakableDecoy decoy)
    {
        decoy = null;
        if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Investigating) return false;

        FieldOfListening ears = stateManager.FieldOfListening;
        if (ears == null || ears.LastHeardDecoy == null) return false;

        INemesisBreakableDecoy candidate = ears.LastHeardDecoy.GetComponent<INemesisBreakableDecoy>();
        if (candidate == null || !candidate.CanBeBroken) return false;

        Vector3 target = candidate.BreakTargetPosition;
        if (Mathf.Abs(target.y - transform.position.y) > maxHeightDifference) return false;
        if (FlatDistance(transform.position, target) > candidate.BreakReach) return false;

        decoy = candidate;
        return true;
    }

    /// <summary>
    /// UniTask and a try/finally for the same reason as NemesisDoorUser.WaitForDoorAsync: the
    /// hold and the stuck suppression must be released however this ends, and a coroutine on a
    /// disabled component never reaches its finally.
    /// </summary>
    private async UniTaskVoid BreakAsync(INemesisBreakableDecoy decoy, CancellationToken token)
    {
        IsBreaking = true;
        bool hit = false;

        // Read once: the hit may swap the model by destroying the decoy.
        Vector3 target = decoy.BreakTargetPosition;
        float recovery = decoy.BreakRecovery;

        stateManager.PushStuckSuppression();
        stateManager.SetExternalHold(true);
        try
        {
            decoy.OnBreakStarted();
            BreakStarted?.Invoke(decoy);

            if (breakTriggerHash != 0 && stateManager.AnimController != null)
                stateManager.AnimController.SetTrigger(breakTriggerHash);

            if (!await HoldPhaseAsync(target, decoy.BreakWindup, token)) return;

            decoy.Break();
            hit = true;

            await HoldPhaseAsync(target, recovery, token);
        }
        finally
        {
            // The decoy may be gone with its scene; Unity's null is only visible through Object.
            bool decoyAlive = decoy is UnityEngine.Object o && o != null;
            if (!hit && decoyAlive) decoy.OnBreakAborted();

            if (stateManager != null)
            {
                stateManager.SetExternalHold(false);
                stateManager.PopStuckSuppression();
            }

            IsBreaking = false;
            BreakFinished?.Invoke(decoy);
        }
    }

    /// <summary>
    /// Stands still facing the decoy for the given seconds. False if the FSM left Investigating
    /// before the time was up. Scaled time, so the pause menu (timeScale 0) freezes it.
    /// </summary>
    private async UniTask<bool> HoldPhaseAsync(Vector3 target, float seconds, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Investigating) return false;

            FaceTowards(target);

            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;
        }

        return stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Investigating;
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion wanted = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * Time.deltaTime);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
