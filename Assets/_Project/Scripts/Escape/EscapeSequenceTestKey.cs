using UnityEngine;

/// <summary>
/// A test key that plays the escape sequence from the top, without having to solve the three-core
/// puzzle first. Its one job. F4 by default: F8, F9 and F10 are taken by the module explosion,
/// the Nemesis debug HUD and the Nemesis test console.
///
/// Editor and development builds only, so it cannot ship as a way to start the sequence.
/// Pressing it again mid-chase plays it again from the hub; pressing it during a cinematic does
/// nothing (F skips that), and during a capture it waits for the capture to end. Like every gameplay
/// key it is ignored under a menu — the win / result screens included: use their own buttons.
/// </summary>
public class EscapeSequenceTestKey : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private EscapeSequenceDirector director;

    [Tooltip("Tecla que dispara la secuencia. F4 está libre; F8, F9 y F10 ya las usan otros debug.")]
    [SerializeField] private KeyCode key = KeyCode.F4;

    // Legacy input, like the other debug keys: the project runs both input backends.
    private void Update()
    {
        if (director == null || !Input.GetKeyDown(key)) return;
        if (PauseManager.IsGameplayInputBlocked) return;

        director.StartForTest();
    }
#endif
}
