using UnityEngine;

/// <summary>
/// A hub light that shows how unstable the ventilation system is (Central Puzzle 2, spec §3:
/// "la iluminación del Hub se estabiliza"). It flickers while the hub puzzle is unsolved, harder
/// during the stabilization checks, settles a step with every passed check, and holds steady once
/// the puzzle is completed. Every number is in the <see cref="SO_StabilizationPuzzleData"/>.
///
/// Put it on any Light of the hub, or on a parent: it drives every Light at or below it. It only
/// ever lowers the intensity from the one each light was authored with, so a level designer lights
/// the room as usual and the flicker eats into that.
///
/// State is read live — the puzzle from PuzzleStateManager, the progress from the
/// <see cref="StabilizationCheckController"/> — so a checkpoint rollback is followed for free.
/// </summary>
public class StabilizationFlickerLight : MonoBehaviour
{
    [SerializeField] private SO_StabilizationPuzzleData puzzleData;

    [Tooltip("Smoothing of the instability between steps, in seconds.")]
    [SerializeField, Min(0.01f)] private float settleSeconds = 0.6f;

    private Light[] lights = new Light[0];
    private float[] baseIntensity = new float[0];
    private float agitation;
    private float seed;
    private float dropoutUntil;

    private void Awake()
    {
        if (puzzleData == null)
            Debug.LogError($"[{nameof(StabilizationFlickerLight)}] No SO_StabilizationPuzzleData on '{name}'.", this);

        lights = GetComponentsInChildren<Light>(true);
        baseIntensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) baseIntensity[i] = lights[i].intensity;

        // Each light flickers on its own pattern, never in lockstep with the others.
        seed = Random.Range(0f, 1000f);
    }

    private void OnDisable()
    {
        for (int i = 0; i < lights.Length; i++)
            if (lights[i] != null) lights[i].intensity = baseIntensity[i];
    }

    private void Update()
    {
        if (puzzleData == null || lights.Length == 0) return;

        agitation = Mathf.MoveTowards(agitation, TargetAgitation(), Time.deltaTime / settleSeconds);

        float level = 1f;
        if (agitation > 0.001f)
        {
            // Dropouts: rare short blackouts, more frequent the worse the system is.
            if (Time.time >= dropoutUntil && Random.value < puzzleData.LightsDropoutRate * agitation * Time.deltaTime)
                dropoutUntil = Time.time + Random.Range(0.05f, 0.18f);

            float noise = Mathf.PerlinNoise(seed, Time.time * puzzleData.LightsFlickerSpeed);
            level = 1f - puzzleData.LightsFlickerDepth * agitation * noise;
            if (Time.time < dropoutUntil) level *= 1f - agitation;
        }

        for (int i = 0; i < lights.Length; i++)
            if (lights[i] != null) lights[i].intensity = baseIntensity[i] * level;
    }

    private float TargetAgitation()
    {
        if (IsCompleted(puzzleData.PuzzleId)) return 0f;

        StabilizationCheckController checks = StabilizationCheckController.Instance;
        if (checks != null && checks.IsOpen && checks.ActiveData == puzzleData)
            return puzzleData.LightsLeft(checks.PassedChecks);

        return puzzleData.LightsIdleAgitation;
    }

    private static bool IsCompleted(string id) =>
        !string.IsNullOrWhiteSpace(id) && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(id);
}
