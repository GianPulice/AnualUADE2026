public class DocumentReaderModel : BaseScreenModel
{
    public SO_DocumentData CurrentDocument { get; private set; }

    public override void Initialize()
    {
        CurrentDocument = null;
        IsInitialized = true;
    }

    public void SetDocument(SO_DocumentData document)
    {
        CurrentDocument = document;
        NotifyDataChanged();
    }
}
