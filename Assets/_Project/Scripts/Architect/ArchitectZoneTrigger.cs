using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Volume that plays an Architect context line the first time the player walks into it
/// (ARC_CTX_01 "Zona 1", ARC_CTX_02 "Zona 2", ARC_CTX_06 the box room). Place one at the entrance
/// of each zone, or over the whole room when it has more than one way in.
///
/// With <see cref="delaySeconds"/> above 0 the line waits until the player has stayed that long
/// inside: leaving earlier cancels it, and the next entry starts the count again. The box room uses
/// it so the hint lands once the player has looked around, not on the doorstep.
///
/// Walking in again does nothing: context lines play once per run, which the controller enforces.
/// Requires a trigger Collider and the "Player" tag, same as <see cref="ZoneTrigger"/>.
/// For a line tied to a puzzle instead of a place, see <see cref="ArchitectPuzzleTrigger"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ArchitectZoneTrigger : MonoBehaviour
{
    [SerializeField] private ArchitectLineID line = ArchitectLineID.ContextZone1;

    [Tooltip("Seconds the player has to stay inside before the line plays. 0 = on entry.")]
    [SerializeField, Min(0f)] private float delaySeconds;

    // The player can carry more than one collider tagged Player: count them so leaving through one
    // while another is still inside does not cancel the wait.
    private int playerCollidersInside;
    private CancellationTokenSource waitCts;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerCollidersInside++;

        if (delaySeconds <= 0f) { Play(); return; }
        if (waitCts != null) return; // already counting

        waitCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        PlayAfterDelay(waitCts.Token).Forget();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerCollidersInside = Mathf.Max(0, playerCollidersInside - 1);
        if (playerCollidersInside == 0) CancelWait();
    }

    private void OnDisable()
    {
        playerCollidersInside = 0;
        CancelWait();
    }

    private void CancelWait()
    {
        if (waitCts == null) return;
        waitCts.Cancel();
        waitCts.Dispose();
        waitCts = null;
    }

    private async UniTaskVoid PlayAfterDelay(CancellationToken token)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (waitCts != null && waitCts.Token == token)
            {
                waitCts.Dispose();
                waitCts = null;
            }
        }
        Play();
    }

    private void Play()
    {
        if (ArchitectVoiceController.Instance == null)
        {
            Debug.LogWarning($"[{nameof(ArchitectZoneTrigger)}] '{name}': no ArchitectVoiceController loaded.", this);
            return;
        }

        ArchitectVoiceController.Instance.TriggerContext(line);
    }
}
