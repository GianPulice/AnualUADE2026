using UnityEngine;

/// <summary>
/// A level the run can be saved at. One asset per level that starts with a save (Zona 2 today; not
/// the first level, which is what New Game already is).
///
/// Referenced by the level's entry point (<see cref="Zone2EntryTeleport"/> for Zona 2): reaching it
/// through the real path empties the inventory and writes the run into the active slot
/// (<see cref="SaveGame.ReachLevel"/>); loading that slot puts the player back on that entry point.
/// </summary>
[CreateAssetMenu(fileName = "SO_SaveLevel", menuName = "Scriptable Objects/SaveSlots/Save Level")]
public class SO_SaveLevel : ScriptableObject
{
    [Tooltip("Unique id written into the save file. Do not change it once builds with saves exist: a " +
             "save whose id no entry point answers to loads the scene but leaves the player at its " +
             "start.")]
    [SerializeField] private string levelId = "zone_2";

    [Tooltip("What the save slot card shows as the location. Player-facing text (English).")]
    [SerializeField] private string displayName = "Zone 2";

    public string LevelId => levelId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? levelId : displayName;

    public bool IsValid => !string.IsNullOrWhiteSpace(levelId);
}
