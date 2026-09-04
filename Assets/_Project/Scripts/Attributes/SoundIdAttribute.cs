using UnityEngine;

/// <summary>
/// Marks a string field as holding a sound id, so the inspector offers the ids that actually exist
/// in the project instead of a blank text box. Same idea as <see cref="PuzzleIdAttribute"/>, and
/// for the same reason.
///
/// Sound ids are matched by string in <see cref="AudioManager"/>'s dictionary, and a typo does not
/// fail — the lookup misses and nothing plays, with nothing in the console to say why. That failure
/// is especially easy to ship here because most of these fields are legitimately optional ("leave
/// empty for silence"), so a wrong id and a deliberately blank one look identical from the outside.
///
/// It stays a plain string rather than becoming a direct <c>SO_SoundData</c> reference on purpose:
/// AudioManager resolves by id at runtime against the list it was given, so a hard asset reference
/// on every interactable would pull sound assets into scenes and prefabs that only ever need to
/// name one. Nothing about runtime changes here; only what the inspector draws.
///
/// Lives in the runtime assembly because the fields it decorates do. The drawer that reads it is in
/// Editor/SoundIdDrawer.cs.
/// </summary>
public class SoundIdAttribute : PropertyAttribute
{
}
