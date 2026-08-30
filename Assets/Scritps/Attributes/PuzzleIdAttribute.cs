using UnityEngine;

/// <summary>
/// Marks a string field as holding a puzzle id, so the inspector offers the ids that actually
/// exist in the project instead of a blank text box.
///
/// Puzzle ids are matched by string against <see cref="PuzzleStateManager"/>, and a typo does not
/// fail — it produces a gate that never opens, a route that never unlocks, or a Nemesis that never
/// wakes up, with nothing in the console to say why. Every one of those has to be found by
/// playing. This turns the field into a list of the ids the puzzle assets declare.
///
/// It stays a plain string on purpose. The five puzzle SOs (SO_PuzzleData, SO_ValvePuzzleData,
/// SO_HubPuzzleData, SO_SequencePuzzleData, SO_ContainerPuzzleData) share no base class, so a
/// typed asset reference could only ever accept one of them — and giving them one would change
/// the serialized type of five assets to buy an inspector convenience. Nothing about runtime
/// changes here; only what the inspector draws.
///
/// Lives in the runtime assembly because the fields it decorates do. The drawer that reads it is
/// in Editor/PuzzleIdDrawer.cs.
/// </summary>
public class PuzzleIdAttribute : PropertyAttribute
{
}
