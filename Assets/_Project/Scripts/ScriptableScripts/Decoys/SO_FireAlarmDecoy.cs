using UnityEngine;

/// <summary>Tuning for <see cref="FireAlarmDecoy"/>: one use, arms, rings from anywhere, stops.</summary>
[CreateAssetMenu(fileName = "SO_FireAlarmDecoy", menuName = "Scriptable Objects/Decoys/Fire Alarm Decoy")]
public class SO_FireAlarmDecoy : ScriptableObject
{
    [Header("Interacción")]
    [SerializeField] private string interactText = "Activar alarma";

    [Header("Tiempos")]
    [Tooltip("Segundos desde que la tocás hasta que empieza a sonar y a tirar agua.")]
    [SerializeField, Min(0f)] private float armDelay = 10f;

    [Tooltip("Segundos que suena. Mientras suena el Nemesis la escucha desde cualquier lado.")]
    [SerializeField, Min(0.2f)] private float ringDuration = 30f;

    [Header("Audio")]
    [SerializeField, SoundId] private string pressSoundId = "";
    [Tooltip("Loop de la sirena. Se reproduce en el AudioSource de la alarma.")]
    [SerializeField, SoundId] private string ringLoopSoundId = "";
    [SerializeField, SoundId] private string stopSoundId = "";

    public string InteractText => interactText;
    public float ArmDelay => armDelay;
    public float RingDuration => ringDuration;
    public string PressSoundId => pressSoundId;
    public string RingLoopSoundId => ringLoopSoundId;
    public string StopSoundId => stopSoundId;
}
