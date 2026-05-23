using System.Collections.Generic;

public class SaveSlotsModel : BaseScreenModel
{
    private IReadOnlyList<SO_SaveSlotData> _slots;

    public IReadOnlyList<SO_SaveSlotData> Slots => _slots;

    public void SetDatabase(SO_SaveSlotDatabase database)
    {
        _slots = database != null ? database.Slots : null;
        NotifyDataChanged();
    }

    public override void Initialize()
    {
        IsInitialized = true;
    }
}
