using UnityEngine;

[CreateAssetMenu(fileName = "SO_SoundData", menuName = "Scriptable Objects/Audio/SO_SoundData")]
public class SO_SoundData : ScriptableObject
{
    public enum SoundCategory { SFX, Music }

    [SerializeField] private string id;
    [SerializeField] private SoundCategory category = SoundCategory.SFX;
    [SerializeField] private AudioClip clip;
    [SerializeField] private bool loop = false;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public SoundCategory Category => category;
    public AudioClip Clip => clip;
    public bool Loop => loop;
}
