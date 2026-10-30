using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// A trigger volume that makes the Nemesis do the peek at a fixed spot when the player walks through it,
/// and again every time they do: a way to SEE the pose (and tune it) without waiting for the Director's
/// random entrance, which only ever happens out of the player's sight.
///
/// Put in the corridor the player walks, with a <see cref="peekSpot"/> a few metres ahead against a wall.
/// On entering the volume the Nemesis is woken where it stands if it is asleep, put at the spot facing
/// the player, holds the peek for <see cref="holdSeconds"/> and is sent back to sleep. After
/// <see cref="cooldown"/> seconds the volume arms again, so going back and forth plays it again.
///
/// A TEST AND LEVEL TOOL, NOT PART OF THE DIRECTOR'S RHYTHM. It skips the entrance's fairness rules (out
/// of sight, minimum distance) because whoever places the spot decides what the player sees. The Director
/// does the staging (<see cref="NemesisDirector.PeekAtAsync"/>); this only decides when.
/// </summary>
[RequireComponent(typeof(Collider))]
[AddComponentMenu("WIRED/Nemesis/Peek Trigger")]
public class NemesisPeekTrigger : MonoBehaviour
{
    [Tooltip("Where the Nemesis stands for the peek. It is snapped to the NavMesh and moved to the nearest " +
             "wall within the Director's wall search, so put it beside one. Only the position matters: it " +
             "turns to face the player.")]
    [SerializeField] private Transform peekSpot;

    [Tooltip("Seconds it holds the pose.")]
    [SerializeField, Min(0.5f)] private float holdSeconds = 4f;

    [Tooltip("Seconds after one peek before the volume plays another.")]
    [SerializeField, Min(0f)] private float cooldown = 6f;

    [Tooltip("Send the Nemesis back to sleep when the peek is over, so it does not go on to chase the player " +
             "and the next pass starts from the same place. Off: it carries on as the FSM decides.")]
    [SerializeField] private bool sleepAfter = true;

    private bool busy;
    private float armedAt;

    private void Reset()
    {
        Collider volume = GetComponent<Collider>();
        if (volume != null) volume.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (busy || Time.time < armedAt) return;
        if (other.GetComponentInParent<PlayerStateManager>() == null) return;

        if (peekSpot == null)
        {
            Debug.LogWarning($"[{nameof(NemesisPeekTrigger)}] '{name}' has no Peek Spot.", this);
            return;
        }

        PlayAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid PlayAsync(CancellationToken token)
    {
        busy = true;

        try
        {
            NemesisStateManager nemesis = FindAnyObjectByType<NemesisStateManager>(FindObjectsInactive.Include);
            if (nemesis == null)
            {
                Debug.LogWarning($"[{nameof(NemesisPeekTrigger)}] No Nemesis in the scene.", this);
                return;
            }

            // Asleep (or waiting for a safe spawn): wake it where it stands. The peek warps it to the spot.
            if (!nemesis.IsActive) nemesis.ActivateInPlace();
            if (!nemesis.IsActive) return;

            bool staged = await NemesisDirector.PeekAtAsync(peekSpot.position, holdSeconds);
            if (!staged)
            {
                Debug.LogWarning($"[{nameof(NemesisPeekTrigger)}] '{name}' could not stage the peek: it needs a " +
                                 "Director in the scene and a spot on the NavMesh beside a wall.", this);
            }

            if (sleepAfter && nemesis.IsActive) nemesis.Sleep();
        }
        finally
        {
            busy = false;
            armedAt = Time.time + cooldown;
        }
    }
}
