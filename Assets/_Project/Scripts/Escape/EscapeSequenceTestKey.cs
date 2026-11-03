using UnityEngine;

/// <summary>
/// A test key that wins module 1 (<see cref="EscapeSequenceDirector.WinModuleForTest"/>): the
/// player is put in the hub and the three-core puzzle counts as solved, so M1 resolves and the
/// escape sequence plays from the top. F4 by default: F8, F9 and F10 are taken by the module
/// explosion, the Nemesis debug HUD and the Nemesis test console.
///
/// Editor and Development builds only, like the other debug keys: a standard build ignores it.
/// The body is compiled out, not the class, so the component stays valid in the scene.
/// Once the puzzle is solved it does nothing, and like every gameplay key it is ignored under a
/// menu — the win / result screens included: use their own buttons.
/// </summary>
public class EscapeSequenceTestKey : MonoBehaviour
{
    [SerializeField] private EscapeSequenceDirector director;

    [Tooltip("Tecla que gana el módulo 1. F4 está libre; F8, F9 y F10 ya las usan otros debug.")]
    [SerializeField] private KeyCode key = KeyCode.F4;

    // Legacy input, like the other debug keys: the project runs both input backends.
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (director == null || !Input.GetKeyDown(key)) return;
        if (PauseManager.IsGameplayInputBlocked) return;

        director.WinModuleForTest();
#endif
    }
}
