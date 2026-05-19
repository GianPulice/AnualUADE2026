using UnityEngine;

[CreateAssetMenu(fileName = "SO_Document", menuName = "Scriptable Objects/Documents/Document Data")]
public class SO_DocumentData : ScriptableObject
{
    [SerializeField] private string title;
    [TextArea(5, 20)]
    [SerializeField] private string content;
    [SerializeField] private Sprite image;

    public string Title   => title;
    public string Content => content;
    public Sprite Image   => image;
}
