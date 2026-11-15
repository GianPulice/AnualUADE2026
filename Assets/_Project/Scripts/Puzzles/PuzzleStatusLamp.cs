using UnityEngine;

/// <summary>
/// A status lamp for one puzzle: shows <see cref="lockedMaterial"/> (and <see cref="lockedLightColor"/>)
/// until the puzzle is completed in <see cref="PuzzleStateManager"/>, then switches for good. Zona 2's
/// exit uses it as the pressure gauge beside the bulkhead: red while the system is unstable, green
/// once the hub's stabilization is done. Nothing else: it does not open anything, the gate does.
/// </summary>
public class PuzzleStatusLamp : MonoBehaviour
{
    [SerializeField, PuzzleId] private string puzzleId;

    [Tooltip("The renderer whose material changes.")]
    [SerializeField] private Renderer lampRenderer;
    [SerializeField] private Material lockedMaterial;
    [SerializeField] private Material completedMaterial;

    [Tooltip("Optional. Its colour follows the lamp.")]
    [SerializeField] private Light lampLight;
    [SerializeField] private Color lockedLightColor = new Color(1f, 0.15f, 0.1f);
    [SerializeField] private Color completedLightColor = new Color(0.2f, 1f, 0.3f);

    private void OnEnable()
    {
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDisable()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void Start()
    {
        // Start, not OnEnable: a checkpoint restore fills PuzzleStateManager before the level's Start.
        Apply(PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleId));
    }

    private void HandlePuzzleCompleted(string completedId)
    {
        if (completedId == puzzleId) Apply(true);
    }

    private void Apply(bool completed)
    {
        Material material = completed ? completedMaterial : lockedMaterial;
        if (lampRenderer != null && material != null) lampRenderer.sharedMaterial = material;
        if (lampLight != null) lampLight.color = completed ? completedLightColor : lockedLightColor;
    }
}
