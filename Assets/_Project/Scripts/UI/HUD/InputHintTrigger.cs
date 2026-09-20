using UnityEngine;

/// <summary>
/// Shows an input hint once per run. Two ways to fire, pick one or both:
///   - Show On Start: when the level starts, as soon as the player has control (after the
///     Architect's wake-up line, which locks movement). For "USE [WASD] TO MOVE".
///   - A trigger Collider on this object: when the player walks in. For hints tied to a place
///     ("USE [SHIFT] TO RUN" at the first long corridor).
///
/// <see cref="ShowNow"/> is public for UnityEvents (a puzzle finished, an item picked up).
///
/// The key shown and what closes the hint come from the hint's Input System action; see
/// <see cref="InputHint"/>. The trigger volume is drawn in the Scene view so it can be placed.
/// </summary>
public class InputHintTrigger : MonoBehaviour
{
    [SerializeField] private InputHint hint = new InputHint();

    [Header("Show on start")]
    [SerializeField] private bool showOnStart;

    [Tooltip("Seconds after the player gets control before it shows.")]
    [SerializeField, Min(0f)] private float startDelay = 1f;

    private bool fired;
    private float startTimer;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Update()
    {
        if (!showOnStart || fired) return;

        // Waits for control: not during the wake-up lock, a capture, a menu or the pause.
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || player.IsDisabled || PauseManager.IsGameplayInputBlocked)
        {
            startTimer = 0f;
            return;
        }

        startTimer += Time.deltaTime;
        if (startTimer >= startDelay) ShowNow();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (fired || !other.CompareTag("Player")) return;
        ShowNow();
    }

    public void ShowNow()
    {
        if (fired) return;

        // Only spent once the HUD actually took it: with no HUD loaded yet, try again next time.
        if (InputHintEvents.Show(hint) || InputHintEvents.HasShown(hint.Id)) fired = true;
    }

#if UNITY_EDITOR
    private static readonly Color GizmoFill = new Color(0.55f, 0.85f, 1f, 0.12f);
    private static readonly Color GizmoWire = new Color(0.55f, 0.85f, 1f, 0.9f);

    private void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = GizmoFill;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = GizmoWire;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;

        string what = string.IsNullOrWhiteSpace(hint.inputAction) ? hint.keys : hint.inputAction;
        UnityEditor.Handles.Label(transform.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f),
                                  $"HINT {what} → {hint.action}");
    }
#endif
}
