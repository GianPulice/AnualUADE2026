using UnityEngine;

/// <summary>
/// A test key that opens the skill check anywhere, without the Ventilation Hub — its one job. F6 by
/// default: F4, F8, F9 and F10 are taken by the escape sequence, the module explosion, the Nemesis
/// debug HUD and the Nemesis test console.
///
/// It does not complete anything: the result is only logged, so no puzzle or module resolves from a
/// test run. Misses and perfects do move the active module's timer, as they would in the Hub; with no
/// module active they change nothing.
///
/// Editor and development builds only, so it cannot ship as a way to open the check.
/// </summary>
public class SkillCheckTestKey : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private SkillCheckController controller;

    [Tooltip("Sequence to play. Empty = the controller's default.")]
    [SerializeField] private SO_SkillCheckData data;

    [Tooltip("F6 is free; F4, F8, F9 and F10 are taken by other debug keys.")]
    [SerializeField] private KeyCode key = KeyCode.F6;

    // Legacy input, like the other debug keys: the project runs both input backends.
    private void Update()
    {
        if (controller == null || !Input.GetKeyDown(key)) return;

        // Not over the inventory, the pause menu or an open check: nothing to test from there.
        if (PauseManager.IsGameplayInputBlocked) return;

        controller.Open(data, completed =>
            Debug.Log($"[SkillCheckTestKey] Sequence {(completed ? "completed" : "failed or cancelled")}."));
    }
#endif
}
