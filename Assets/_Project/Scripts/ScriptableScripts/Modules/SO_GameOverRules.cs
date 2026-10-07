using UnityEngine;

/// <summary>
/// Decides which module explosion ends the run. Read by <see cref="ModuleManager"/>, which reports
/// the GameOver, and exposed through <see cref="GameResultManager.ExplosionEndsRun"/> so the
/// explosion sequence and the Architect know whether they are looking at a penalty or a defeat.
///
/// Explosions End Run off = no explosion ever ends the run: every module only applies its penalty
/// and the game goes on, even with all of them exploded.
/// Explosions End Run on, then:
///   Use Fatal Module off = GameOver only when every module has exploded.
///   Use Fatal Module on  = one chosen module is the fatal one: its explosion ends the run, the
///                          others are penalties.
/// </summary>
[CreateAssetMenu(fileName = "SO_GameOverRules", menuName = "Scriptable Objects/Modules/Game Over Rules")]
public class SO_GameOverRules : ScriptableObject
{
    [Tooltip("Off = no module explosion ever ends the run: the penalty applies and the game goes on, " +
             "even when every module has exploded. On = the two options below decide which " +
             "explosion is a GameOver.")]
    [SerializeField] private bool explosionsEndRun = true;

    [Tooltip("Off = GameOver only when every module has exploded. " +
             "On = the explosion of the Fatal Module ends the run on its own.")]
    [SerializeField] private bool useFatalModule = true;

    [Tooltip("Module whose explosion ends the run (only used when Use Fatal Module is on). " +
             "Empty = any explosion ends the run. The others still apply their penalty, and if " +
             "every module ends up exploded the run ends anyway.")]
    [SerializeField] private ModuleData fatalModule;

    public bool ExplosionsEndRun => explosionsEndRun;
    public bool UseFatalModule => useFatalModule;
    public ModuleData FatalModule => fatalModule;

    /// <param name="exploded">The module that just exploded.</param>
    /// <param name="explodedCount">Exploded modules, including this one.</param>
    public bool EndsRun(ModuleData exploded, int explodedCount, int totalModules)
    {
        if (!explosionsEndRun) return false;
        if (totalModules > 0 && explodedCount >= totalModules) return true;
        if (!useFatalModule) return false;
        return fatalModule == null || fatalModule == exploded;
    }
}
