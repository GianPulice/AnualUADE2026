using UnityEngine;

/// <summary>Tuning for <see cref="RadioDecoy"/>: one use, sounds until the Nemesis breaks it.</summary>
[CreateAssetMenu(fileName = "SO_RadioDecoy", menuName = "Scriptable Objects/Decoys/Radio Decoy")]
public class SO_RadioDecoy : ScriptableObject
{
    [Header("Interacción")]
    [SerializeField] private string interactText = "Encender radio";

    [Header("Ruido")]
    [Tooltip("Metros a los que el Nemesis la escucha sin nada en el medio (la \"distancia media X\"). " +
             "Paredes y pisos la achican igual que al ruido del jugador. No la limita el ListenRange " +
             "del Nemesis.")]
    [SerializeField, Min(0f)] private float hearingDistance = 12f;

    [Tooltip("Segundos que suena antes de apagarse sola si nadie la rompe. 0 = hasta que la rompan.")]
    [SerializeField, Min(0f)] private float maxPlayTime = 0f;

    [Header("Rotura")]
    [Tooltip("Metros horizontales a la radio a los que el Nemesis arranca a romperla. Tiene que " +
             "cubrir el stopping distance del Nemesis más la distancia del investigatePoint a la radio.")]
    [SerializeField, Min(0.1f)] private float breakReach = 2.5f;

    [Tooltip("Segundos desde que arranca el corte enojado hasta el golpe.")]
    [SerializeField, Min(0f)] private float breakWindup = 1.2f;

    [Tooltip("Segundos que se queda quieto después del golpe.")]
    [SerializeField, Min(0f)] private float breakRecovery = 1f;

    [Header("Audio")]
    [SerializeField, SoundId] private string turnOnSoundId = "";
    [Tooltip("Loop que suena mientras está prendida. Se reproduce en el AudioSource de la radio.")]
    [SerializeField, SoundId] private string loopSoundId = "";
    [SerializeField, SoundId] private string breakSoundId = "";

    public string InteractText => interactText;
    public float HearingDistance => hearingDistance;
    public float MaxPlayTime => maxPlayTime;
    public float BreakReach => breakReach;
    public float BreakWindup => breakWindup;
    public float BreakRecovery => breakRecovery;
    public string TurnOnSoundId => turnOnSoundId;
    public string LoopSoundId => loopSoundId;
    public string BreakSoundId => breakSoundId;
}
