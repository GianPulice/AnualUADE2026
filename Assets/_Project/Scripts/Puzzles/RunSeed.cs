using System;
using UnityEngine;

/// <summary>
/// The random seed of the current run. A puzzle whose solution changes from one run to the next
/// (the SP1 code, which basket each SP2 box goes in, the SP3 valves) derives it from this seed and
/// its own puzzle id, through <see cref="RandomFor"/>.
///
/// Nothing stores the solutions: whoever asks derives the same values again from the same seed.
/// That is what keeps a solution fixed for the whole run — through checkpoints, captures and scene
/// reloads — and keeps the clue notes, the puzzle and its visuals agreeing with each other.
///
/// A run starts at <see cref="GameSession.BeginNewSession"/> (New Game, Retry): the seed is dropped
/// there and a new one is drawn the first time a puzzle asks. Playing a level straight from the
/// editor draws one on first use too.
/// </summary>
public static class RunSeed
{
    private static int? seed;

    public static int Value
    {
        get
        {
            if (!seed.HasValue)
            {
                seed = Guid.NewGuid().GetHashCode();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[{nameof(RunSeed)}] New run seed: {seed.Value}.");
#endif
            }
            return seed.Value;
        }
    }

    /// <summary>
    /// A generator for one puzzle of this run. The same key gives the same sequence of numbers for
    /// the whole run, and different keys give unrelated ones.
    /// </summary>
    public static System.Random RandomFor(string key) => new System.Random(unchecked(Value ^ StableHash(key)));

    // FNV-1a. string.GetHashCode is free to change between runtimes; this one never does.
    private static int StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private static void DropSeed() => seed = null;

    /// <summary>Static state survives leaving Play mode when domain reload is disabled.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => seed = null;

    /// <summary>
    /// AfterAssembliesLoaded, NOT SubsystemRegistration: GameSession clears OnNewSessionStarting in
    /// SubsystemRegistration, and Unity does not order methods of the same load type (see
    /// <see cref="GameResultManager"/>).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void HookSessionReset()
    {
        GameSession.OnNewSessionStarting -= DropSeed;
        GameSession.OnNewSessionStarting += DropSeed;
    }
}
