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
///                          others are penalties. Fatal Requires Exploded narrows it further:
///                          the fatal module only ends the run if those modules had already
///                          exploded (M2 after M1); otherwise it is a penalty too.
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

    [Tooltip("Modules that must ALREADY be exploded for the Fatal Module's explosion to end the run " +
             "(only used when Use Fatal Module is on). If any of them is not exploded, the Fatal " +
             "Module's explosion is just a penalty. Empty = the Fatal Module always ends the run.")]
    [SerializeField] private ModuleData[] fatalRequiresExploded = System.Array.Empty<ModuleData>();

    [Tooltip("Which screen a run-ending explosion leads to. Lose = the retry screen, the save is kept. " +
             "GameOver = no retry, the save is deleted.")]
    [SerializeField] private GameState explosionResult = GameState.Lose;

    public bool ExplosionsEndRun => explosionsEndRun;
    public bool UseFatalModule => useFatalModule;
    public ModuleData FatalModule => fatalModule;
    public GameState ExplosionResult => explosionResult;

    public ModuleData[] FatalRequiresExploded => fatalRequiresExploded;

    /// <param name="exploded">The module that just exploded.</param>
    /// <param name="explodedCount">Exploded modules, including this one.</param>
    /// <param name="isExploded">Whether a given module is already in the Exploded status.</param>
    public bool EndsRun(ModuleData exploded, int explodedCount, int totalModules,
                        System.Func<ModuleData, bool> isExploded)
    {
        if (!explosionsEndRun) return false;
        if (totalModules > 0 && explodedCount >= totalModules) return true;
        if (!useFatalModule) return false;
        if (fatalModule != null && fatalModule != exploded) return false;

        if (fatalRequiresExploded == null || isExploded == null) return true;
        foreach (ModuleData required in fatalRequiresExploded)
            if (required != null && !isExploded(required)) return false;

        return true;
    }
}
