using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EDITOR ONLY. Puts the run in the state its <see cref="SO_LevelStartState"/> describes when Play
/// is pressed straight in this level, so a later level can be tested on its own. One per level
/// scene; drop the prefab and give it the level's asset.
///
/// It only acts on an untouched run — no module ever started and no puzzle completed. Coming from
/// the previous level the run is not untouched, so the state the player really earned is kept. In
/// a build the component does nothing at all.
///
/// Runs in Awake: the managers live in the Data scene, loaded before any level, and every Start of
/// the level (the player's penalty catch-up among them) then sees the restored state.
/// </summary>
public class LevelStartState : MonoBehaviour
{
    [SerializeField] private SO_LevelStartState startState;

#if UNITY_EDITOR
    private void Awake()
    {
        if (startState == null)
        {
            Debug.LogWarning($"[{nameof(LevelStartState)}] No SO_LevelStartState on '{name}'.", this);
            return;
        }

        if (!ModuleManager.Exists || !PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(LevelStartState)}] The Data scene managers are not loaded — " +
                             "nothing to set up. Press Play through the Bootstrap.", this);
            return;
        }

        if (!IsUntouchedRun())
        {
            Debug.Log($"[{nameof(LevelStartState)}] Arrived from a previous level: keeping the real " +
                      "module and puzzle state.", this);
            return;
        }

        ApplyModules();
        ApplyPuzzles();
    }

    private static bool IsUntouchedRun()
    {
        if (PuzzleStateManager.Instance.CompletedPuzzleCount > 0) return false;

        foreach (ModuleRuntime runtime in ModuleManager.Instance.GetAllModules())
            if (runtime != null && runtime.HasBeenActivated) return false;

        return true;
    }

    private void ApplyModules()
    {
        IReadOnlyList<ModuleRuntime> modules = ModuleManager.Instance.GetAllModules();
        int resolvedUpTo = IndexOf(modules, startState.ResolvedUpTo, "Resolved Up To");
        int explodedUpTo = IndexOf(modules, startState.ExplodedUpTo, "Exploded Up To");

        for (int i = 0; i < modules.Count; i++)
        {
            if (i <= explodedUpTo)
                ModuleManager.Instance.RestoreModuleState(modules[i].ModuleID, ModuleStatus.Exploded);
            else if (i <= resolvedUpTo)
                ModuleManager.Instance.RestoreModuleState(modules[i].ModuleID, ModuleStatus.Resolved);
        }

        Debug.Log($"[{nameof(LevelStartState)}] Editor start state '{startState.name}' applied " +
                  $"(resolved up to #{resolvedUpTo + 1}, exploded up to #{explodedUpTo + 1}).", this);
    }

    private void ApplyPuzzles()
    {
        // After the modules on purpose: completing a module's puzzle tries to resolve it, and a
        // module already restored as Exploded must stay Exploded (ResolveModule refuses it).
        foreach (string puzzleId in startState.CompletedPuzzleIds)
            PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleId);
    }

    /// <summary>Position of the module in SO_ModulesConfig, -1 when it is empty or not in it.</summary>
    private int IndexOf(IReadOnlyList<ModuleRuntime> modules, ModuleData data, string field)
    {
        if (data == null) return -1;

        for (int i = 0; i < modules.Count; i++)
            if (modules[i].Data == data) return i;

        Debug.LogWarning($"[{nameof(LevelStartState)}] '{data.name}' ({field}) is not in the " +
                         "modules config — ignored.", this);
        return -1;
    }
#endif
}
