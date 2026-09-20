using UnityEngine;

/// <summary>
/// Marks a string field as holding an Input System action path ("Player/Sprint"), so the inspector
/// offers the actions of the project-wide asset (Project Settings > Input System) instead of a blank
/// text box. A typo would not fail loudly: the lookup returns null and the hint just loses its key.
///
/// A string rather than an InputActionReference so a hint can be authored in code or by hand in a
/// scene file the same way. The drawer that reads it is in Editor/InputActionPathDrawer.cs.
/// </summary>
public class InputActionPathAttribute : PropertyAttribute
{
}
