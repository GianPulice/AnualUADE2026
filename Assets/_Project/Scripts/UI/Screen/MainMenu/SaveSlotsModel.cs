using System;
using System.Collections.Generic;

public class SaveSlotsModel : BaseScreenModel
{
    private IReadOnlyList<SO_SaveSlotData> _slots;

    public IReadOnlyList<SO_SaveSlotData> Slots => _slots;

    /// <summary>
    /// The slot the menu marks as "last played": the one the player last started or loaded
    /// (<see cref="SaveGame.LastPlayedSlot"/>) while it still has a save, otherwise the newest save.
    /// <see cref="SaveGame.NoSlot"/> when every slot is empty.
    /// </summary>
    public int LastPlayedSlot { get; private set; } = SaveGame.NoSlot;

    public void SetDatabase(SO_SaveSlotDatabase database)
    {
        _slots = database != null ? database.Slots : null;
        LastPlayedSlot = FindLastPlayed(_slots, SaveGame.LastPlayedSlot);
        NotifyDataChanged();
    }

    public override void Initialize()
    {
        IsInitialized = true;
    }

    private static int FindLastPlayed(IReadOnlyList<SO_SaveSlotData> slots, int remembered)
    {
        if (slots == null) return SaveGame.NoSlot;

        // The slot played last, if its save is still there to continue. A run started in an empty
        // slot that never reached a save point, or was lost, or was won (resolved: nothing left to
        // continue), leaves nothing to point at: fall back below.
        foreach (SO_SaveSlotData slot in slots)
            if (slot != null && slot.IsLoadable && slot.SlotIndex == remembered) return remembered;

        int newest = SaveGame.NoSlot;
        DateTime newestUtc = DateTime.MinValue;
        foreach (SO_SaveSlotData slot in slots)
        {
            if (slot == null || !slot.IsLoadable || !slot.TryGetSavedUtc(out DateTime savedUtc)) continue;
            if (newest != SaveGame.NoSlot && savedUtc <= newestUtc) continue;

            newest = slot.SlotIndex;
            newestUtc = savedUtc;
        }
        return newest;
    }
}
