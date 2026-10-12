using UnityEngine;

/// <summary>
/// EDITOR ONLY. The state a level starts in when Play is pressed straight in it, as if the levels
/// before it had been played: which modules are already over (resolved or exploded) and which
/// puzzles already completed. Lets a designer balance Puzzle 2 with M1 resolved, with M1 exploded,
/// or with M1 and M2 exploded, without playing Puzzle 1 first.
///
/// Read by <see cref="LevelStartState"/>, and only when the run is untouched (no module ever
/// started, no puzzle completed): arriving from the previous level, the real state wins. Never in
/// a build.
///
/// Modules count in the order of SO_ModulesConfig. Every module up to <see cref="ResolvedUpTo"/>
/// starts Resolved, every module up to <see cref="ExplodedUpTo"/> starts Exploded (that one wins
/// where both reach), and the rest are untouched.
/// </summary>
[CreateAssetMenu(fileName = "SO_LevelStartState", menuName = "Scriptable Objects/Levels/Level Start State (Editor)")]
public class SO_LevelStartState : ScriptableObject
{
    [Header("Modules (in SO_ModulesConfig order)")]
    [Tooltip("The modules the previous levels own: this one and every module before it start " +
             "RESOLVED. For Puzzle 2 that is M1. Empty = none.")]
    [SerializeField] private ModuleData resolvedUpTo;

    [Tooltip("This one and every module before it start EXPLODED instead (their penalties apply, " +
             "no explosion is shown and no GameOver is checked). Overrides Resolved Up To. " +
             "Empty = no module exploded.")]
    [SerializeField] private ModuleData explodedUpTo;

    [Header("Puzzles")]
    [Tooltip("Puzzles the previous levels completed. Doors, routes and the Nemesis escalation " +
             "that depend on them behave as if they had been solved.")]
    [SerializeField, PuzzleId] private string[] completedPuzzleIds = new string[0];

    public ModuleData ResolvedUpTo => resolvedUpTo;
    public ModuleData ExplodedUpTo => explodedUpTo;
    public string[] CompletedPuzzleIds => completedPuzzleIds;
}
