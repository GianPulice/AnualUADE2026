using UnityEngine;

/// <summary>
/// Ids of the UI sounds (SO_SoundData in ScriptableObjects/Audio/UI, registered on the AudioManager
/// in the Data scene) and the one way the UI plays them: 2D, on the UI bus, audible while paused.
///
/// Where each one plays:
///   Hover / Click          — every Button, Toggle, Slider and Dropdown of a canvas with
///                            <see cref="UICanvasSounds"/> (menus, pause, settings, results,
///                            document reader, inventory).
///   ItemSelect             — the player clicks an item in the inventory list (not the auto-select
///                            on open).
///   ItemDiscard            — the player confirms discarding an item.
///   PauseOpen / PauseClose — the pause menu opens / closes.
///   SaveSlotHover          — hovering a save slot card's action button.
///   SaveConfirm            — clicking a save slot (new game / load), one of three variants.
///
/// Clips are reused on purpose so the whole UI sounds the same. Settings: sliders tick with Hover,
/// open / close reuse PauseOpen / PauseClose, Apply reuses SaveConfirm and Reset reuses ItemDiscard.
/// </summary>
public static class UISounds
{
    public const string Hover = "sfx_ui_hover";
    public const string Click = "sfx_ui_click";

    public const string ItemSelect = "sfx_ui_item_select";
    public const string ItemDiscard = "sfx_ui_item_discard";

    public const string PauseOpen = "sfx_ui_pause_01";
    public const string PauseClose = "sfx_ui_pause_02";

    public const string SaveSlotHover = "sfx_save_slot_hover";

    public static readonly string[] SaveConfirm =
    {
        "sfx_save_confirmacion",
        "sfx_save_confirmacion_alt_01",
        "sfx_save_confirmacion_alt_02",
    };

    public static void Play(string id)
    {
        if (string.IsNullOrEmpty(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlayUI(id);
    }

    public static void PlayRandom(string[] ids)
    {
        if (ids == null || ids.Length == 0) return;
        Play(ids[Random.Range(0, ids.Length)]);
    }
}
