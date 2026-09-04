using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DocumentReaderView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI contentText;
    [SerializeField] private Image documentImage;

    public void Populate(SO_DocumentData data)
    {
        if (data == null) return;

        titleText.text   = data.Title;
        contentText.text = data.Content;

        if (documentImage != null)
        {
            bool hasImage = data.Image != null;
            documentImage.gameObject.SetActive(hasImage);
            if (hasImage) documentImage.sprite = data.Image;
        }
    }
}
