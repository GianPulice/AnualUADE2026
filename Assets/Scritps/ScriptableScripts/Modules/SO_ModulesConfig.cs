using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ordered list of modules for a run. Designers drop the three <see cref="ModuleData"/> assets
/// (M1, M2, M3) in the order they should activate. The manager reads this once and builds one
/// <see cref="ModuleRuntime"/> per entry.
///
/// Kept as a separate asset (instead of a field on ModuleManager) so designers can swap whole
/// configurations without touching the scene or the manager prefab — e.g. a "hard mode" config
/// with tighter timers.
/// </summary>
[CreateAssetMenu(fileName = "SO_ModulesConfig", menuName = "Scriptable Objects/Modules/Modules Config")]
public class SO_ModulesConfig : ScriptableObject
{
    [Tooltip("Modules in activation order. First entry is M1, second M2, third M3. " +
             "The manager enforces that a module cannot activate until the previous one is Resolved.")]
    [SerializeField] private List<ModuleData> modules = new List<ModuleData>();

    public IReadOnlyList<ModuleData> Modules => modules;
    public int Count => modules.Count;
}
