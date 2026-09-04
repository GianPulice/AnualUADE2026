using UnityEngine;

public class BallPuzzleItem : MonoBehaviour
{
    [SerializeField] private SO_ContainerData ballData;

    public string BallId => ballData != null ? ballData.ContainerId : string.Empty;
    public string LinkedPuzzleId => ballData != null ? ballData.LinkedPuzzleId : string.Empty;

    public bool IsConfigured => ballData != null;
}
