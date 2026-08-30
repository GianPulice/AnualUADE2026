using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weighted random selection, shared.
///
/// WHY THIS EXISTS
///
/// The same cumulative-weight roll was written by hand five times across the Nemesis and the
/// ambience systems, and every copy had rediscovered the same two edge cases independently:
/// what to do when every candidate weighs zero, and what to do when floating-point rounding lets
/// the roll fall past the last bucket. Five copies of a subtle loop is five chances for one of
/// them to quietly stop matching the others — and "clusters on" behaving differently from
/// "clusters off" for a reason nobody chose is exactly the kind of bug that never gets reported
/// because it just reads as the monster being weird.
///
/// WHY IT RETURNS AN INDEX AND NOT AN ITEM
///
/// The obvious signature is <c>T Roulette&lt;T&gt;(Dictionary&lt;T, float&gt;)</c>, and it is the
/// wrong one here. Every caller already holds two parallel buffers — the candidates and their
/// weights — precisely so the selection allocates nothing on a path that runs on every waypoint
/// arrival, every replan and every spawn. Handing those callers a Dictionary-based API would mean
/// building a dictionary per call to immediately throw it away. Returning an index lets them keep
/// the buffers they already reuse.
/// </summary>
public static class RouletteSelection
{
    /// <summary>Uniform float in [min, max]. Sugar over Random.value so a caller reading a
    /// designer's min/max pair does not have to spell the interpolation out.</summary>
    public static float GetRandom(float min, float max)
    {
        return min + (UnityEngine.Random.value * (max - min));
    }

    /// <summary>
    /// Index of a weighted-random pick among <paramref name="weights"/>, or -1 when the list is
    /// empty.
    ///
    /// Negative weights are clamped to zero rather than trusted: a negative entry would make the
    /// running total go backwards and hand the roll to whichever bucket happened to be next, which
    /// is not "unlikely", it is wrong.
    ///
    /// Every candidate weighing zero is a legitimate case and not an error — a designer can switch
    /// every route in a zone off — and the answer is uniform among them, because the Nemesis still
    /// has to go somewhere.
    /// </summary>
    public static int Roulette(IReadOnlyList<float> weights)
    {
        if (weights == null || weights.Count == 0) return -1;

        float total = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            total += Mathf.Max(0f, weights[i]);
        }

        if (total <= 0f) return UnityEngine.Random.Range(0, weights.Count);

        float roll = UnityEngine.Random.value * total;
        float cumulative = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            float weight = Mathf.Max(0f, weights[i]);

            // A zero-weight entry can never win while anything else has weight, and skipping it
            // here is not a formality: Random.value can return exactly 0, and then the very first
            // bucket satisfies "roll <= cumulative" with cumulative still at 0 - handing the draw
            // to a candidate the caller had deliberately weighted out. Rare enough to never show
            // up in testing and wrong every time it happens: a route the designer switched off
            // getting patrolled, or the pursuit taking a detour it had already rejected.
            if (weight <= 0f) continue;

            cumulative += weight;
            if (roll <= cumulative) return i;
        }

        // Only reachable through float rounding at the very edge of the range: Random.value can
        // return exactly 1, and summing the weights a second time above does not necessarily
        // reproduce the identical total. Falling back to the last bucket that actually carries
        // weight keeps this total rather than returning -1 on a list that provably had weight in
        // it - and, unlike falling back to the last INDEX, cannot land on a zero either.
        for (int i = weights.Count - 1; i >= 0; i--)
        {
            if (weights[i] > 0f) return i;
        }

        return -1;
    }

    /// <summary>
    /// Fisher-Yates, in place. Returns the same list so it can be used inline.
    /// </summary>
    /// <param name="onSwap">Called for every pair about to be exchanged, for a caller keeping a
    /// parallel structure in step. Almost always null.</param>
    public static List<T> Shuffle<T>(List<T> list, Action<T, T> onSwap = null)
    {
        if (list == null) return null;

        for (int i = 0; i < list.Count; i++)
        {
            int r = UnityEngine.Random.Range(i, list.Count);
            onSwap?.Invoke(list[i], list[r]);

            (list[i], list[r]) = (list[r], list[i]);
        }

        return list;
    }

    /// <summary>Array overload of <see cref="Shuffle{T}(List{T}, Action{T, T})"/>.</summary>
    public static T[] Shuffle<T>(T[] list, Action<T, T> onSwap = null)
    {
        if (list == null) return null;

        for (int i = 0; i < list.Length; i++)
        {
            int r = UnityEngine.Random.Range(i, list.Length);
            onSwap?.Invoke(list[i], list[r]);

            (list[i], list[r]) = (list[r], list[i]);
        }

        return list;
    }
}
