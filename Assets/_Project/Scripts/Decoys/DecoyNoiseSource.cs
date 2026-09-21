using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The part of a decoy the Nemesis hears. It does nothing on its own: the decoy that owns it
/// (<see cref="RadioDecoy"/>, <see cref="FireAlarmDecoy"/>, <see cref="ChainDecoy"/>) decides
/// when it sounds and how far, and <see cref="FieldOfListening"/> reads the static registry.
///
/// Not a trigger sphere on the DetectableAudio layer, the way the player and the Director make
/// noise, because a decoy needs to say two things a sphere cannot: how far it is heard in plain
/// metres, and "heard from anywhere". See <see cref="FieldOfListening"/>.ListenDecoys.
///
/// What it deliberately does not do: play audio, count uses or time anything. Those are the
/// owning decoy's job.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Decoys/Decoy Noise Source")]
public class DecoyNoiseSource : MonoBehaviour
{
    [Tooltip("Donde va a investigar el Nemesis. Vacío = este transform.\n\n" +
             "Ponelo en el piso, del lado desde el que tiene que llegar: la radio en una mesa o " +
             "la alarma en una pared no están sobre el NavMesh, y el punto se proyecta al NavMesh " +
             "más cercano, que puede quedar del otro lado de la pared.")]
    [SerializeField] private Transform investigatePoint;

    private static readonly List<DecoyNoiseSource> active = new List<DecoyNoiseSource>();

    /// <summary>Every decoy currently sounding, in no particular order.</summary>
    public static IReadOnlyList<DecoyNoiseSource> Active => active;

    public bool IsEmitting { get; private set; }

    /// <summary>Metres at which the Nemesis hears it with nothing in the way. Walls and floors
    /// shrink it the same way they shrink the player's noise.</summary>
    public float HearingDistance { get; private set; }

    /// <summary>Heard from any distance, through anything.</summary>
    public bool AudibleEverywhere { get; private set; }

    /// <summary>Where the Nemesis goes: <see cref="investigatePoint"/> snapped to the NavMesh,
    /// worked out once when the decoy starts sounding — decoys do not move.</summary>
    public Vector3 NoisePosition { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();

    public void StartEmitting(float hearingDistance, bool audibleEverywhere)
    {
        HearingDistance = Mathf.Max(0f, hearingDistance);
        AudibleEverywhere = audibleEverywhere;

        Vector3 point = investigatePoint != null ? investigatePoint.position : transform.position;
        NoisePosition = NemesisNav.TrySnapToNavMesh(point, out Vector3 snapped) ? snapped : point;

        if (IsEmitting) return;
        IsEmitting = true;
        active.Add(this);
    }

    public void StopEmitting()
    {
        if (!IsEmitting) return;
        IsEmitting = false;
        active.Remove(this);
    }

    private void OnDisable() => StopEmitting();

    private void OnDrawGizmosSelected()
    {
        Vector3 point = investigatePoint != null ? investigatePoint.position : transform.position;
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point, 0.3f);
        Gizmos.DrawLine(transform.position, point);

        if (Application.isPlaying && IsEmitting && !AudibleEverywhere)
            Gizmos.DrawWireSphere(NoisePosition, HearingDistance);
    }
}
