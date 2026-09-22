using UnityEngine;

/// <summary>Tuning for <see cref="ChainDecoy"/>: infinite uses, a short medium-range rattle.</summary>
[CreateAssetMenu(fileName = "SO_ChainDecoy", menuName = "Scriptable Objects/Decoys/Chain Decoy")]
public class SO_ChainDecoy : ScriptableObject
{
    [Header("Interacción")]
    [SerializeField] private string interactText = "Mover cadenas";

    [Tooltip("Segundos desde un uso hasta que se puede volver a usar.")]
    [SerializeField, Min(0f)] private float cooldown = 2f;

    [Header("Ruido")]
    [Tooltip("Metros a los que el Nemesis escucha el ruido (\"ruido medio\"). Paredes y pisos lo " +
             "achican igual que al ruido del jugador.")]
    [SerializeField, Min(0f)] private float hearingDistance = 8f;

    [Tooltip("Segundos que dura el ruido audible para el Nemesis. Mínimo 0.2: escucha cada 0.1 s " +
             "y un ruido más corto se le puede escapar entero.")]
    [SerializeField, Min(0.2f)] private float noiseDuration = 1.5f;

    [Header("Audio")]
    [SerializeField, SoundId] private string rattleSoundId = "";

    public string InteractText => interactText;
    public float Cooldown => cooldown;
    public float HearingDistance => hearingDistance;
    public float NoiseDuration => noiseDuration;
    public string RattleSoundId => rattleSoundId;
}
