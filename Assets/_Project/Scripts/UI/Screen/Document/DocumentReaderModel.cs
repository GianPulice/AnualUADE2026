using UnityEngine;

/// <summary>
/// MODEL of the note reader. Holds the three things the sheet can show — title, body, optional
/// image — and nothing about where they came from.
///
/// It is deliberately NOT an <c>SO_DocumentData</c> field any more: notes in the level are
/// <see cref="SO_InventoryItem"/> assets with <see cref="ItemContentType.Text"/> (that is what a
/// pickup hands to the inventory), while <see cref="SO_DocumentData"/> is the older read-in-place
/// asset. Both collapse to the same three values here, so the View never learns which one opened it.
/// </summary>
public class DocumentReaderModel : BaseScreenModel
{
    public string Title  { get; private set; }
    public string Body   { get; private set; }
    public Sprite Image  { get; private set; }

    public bool HasDocument => !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Body);

    public override void Initialize()
    {
        Clear();
        IsInitialized = true;
    }

    public void SetDocument(SO_DocumentData document)
    {
        if (document == null) { Clear(); return; }
        Set(document.Title, document.Content, document.Image);
    }

    /// <summary>
    /// A picked-up note. The item's icon is deliberately NOT used as the sheet image: it is a
    /// small inventory glyph and blowing it up to page width looks like a mistake.
    /// </summary>
    public void SetDocument(SO_InventoryItem item)
    {
        if (item == null) { Clear(); return; }
        Set(item.ItemName, item.TextContent, null);
    }

    private void Set(string title, string body, Sprite image)
    {
        Title = title;
        Body  = body;
        Image = image;
        NotifyDataChanged();
    }

    private void Clear()
    {
        Title = null;
        Body  = null;
        Image = null;
    }
}
