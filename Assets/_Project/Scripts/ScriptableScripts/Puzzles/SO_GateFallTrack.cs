using UnityEngine;

/// <summary>
/// A gate leaf's fall, simulated once with Unity's physics in the editor and played back the same
/// every time (<see cref="GateKnockOff"/>). The top half is how the fall is simulated; the bottom
/// half is what the simulation recorded, written by the bake button on <see cref="GateKnockOff"/>'s
/// inspector (Editor/GateFallBaker).
///
/// Recorded in WORLD space for the scene it was baked in: if the gate or the props round it move,
/// bake it again.
/// </summary>
[CreateAssetMenu(fileName = "SO_GateFallTrack", menuName = "Scriptable Objects/Puzzles/SO_GateFallTrack")]
public class SO_GateFallTrack : ScriptableObject
{
    [Header("Simulación (para hornear)")]
    [Tooltip("Kilos de la hoja. Cambia cómo la empujan y cuánto rebota contra lo que pega.")]
    [SerializeField, Min(1f)] private float mass = 180f;

    [Tooltip("El último golpe (N·s): cuánto empuja la hoja hacia afuera.")]
    [SerializeField, Min(0f)] private float impulse = 260f;

    [Tooltip("Dónde pega ese golpe, a lo alto de la hoja: 0 = abajo, 1 = arriba. Más arriba la " +
             "vence; más abajo la arranca deslizando.")]
    [SerializeField, Range(0f, 1f)] private float impulseHeight = 0.75f;

    [Tooltip("Dónde pega a lo ancho: 0 = centro, ±1 = un borde. Fuera del centro la hoja gira al caer.")]
    [SerializeField, Range(-1f, 1f)] private float impulseSide = 0f;

    [Tooltip("Fricción de la hoja contra el piso y los props (0..1).")]
    [SerializeField, Range(0f, 1f)] private float friction = 0.6f;

    [Tooltip("Rebote de la hoja (0..1). La chapa pesada rebota poco.")]
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.15f;

    [Tooltip("Metros que se le recortan a la hoja a cada costado para simularla: es más ancha que el " +
             "vestíbulo (los bordes entran 3 cm en las paredes) y entera se queda trabada.")]
    [SerializeField, Min(0f)] private float sideTrim = 0.1f;

    [Tooltip("Metros que se le recortan arriba para simularla: la hoja mide 3,54 m y el techo del " +
             "vestíbulo está a 3,45 m; entera choca el techo al vencerse y cae para atrás.")]
    [SerializeField, Min(0f)] private float topTrim = 0.14f;

    [Tooltip("Segundos máximos de simulación. Corta antes si la hoja se queda quieta.")]
    [SerializeField, Min(0.5f)] private float maxSeconds = 4f;

    [Tooltip("Muestras por segundo que se graban (la física corre a 4 pasos por muestra).")]
    [SerializeField, Range(30, 120)] private int samplesPerSecond = 60;

    [Header("Resultado (lo escribe el horneado)")]
    [SerializeField] private float sampleInterval;
    [SerializeField] private float impactTime;
    [SerializeField] private Vector3[] positions = System.Array.Empty<Vector3>();
    [SerializeField] private Quaternion[] rotations = System.Array.Empty<Quaternion>();

    public float Mass => mass;
    public float Impulse => impulse;
    public float ImpulseHeight => impulseHeight;
    public float ImpulseSide => impulseSide;
    public float Friction => friction;
    public float Bounciness => bounciness;
    public float SideTrim => sideTrim;
    public float TopTrim => topTrim;
    public float MaxSeconds => maxSeconds;
    public int SamplesPerSecond => samplesPerSecond;

    /// <summary>The track has been baked.</summary>
    public bool HasData => positions != null && positions.Length > 1 && rotations != null &&
                           rotations.Length == positions.Length && sampleInterval > 0f;

    /// <summary>Seconds from the push to the leaf lying still.</summary>
    public float Duration => HasData ? (positions.Length - 1) * sampleInterval : 0f;

    /// <summary>Seconds from the push to the leaf's first hard hit (the floor, or a prop on the way).</summary>
    public float ImpactTime => Mathf.Min(impactTime, Duration);

    /// <summary>The leaf's pose (world) <paramref name="time"/> seconds into the fall.</summary>
    public void Sample(float time, out Vector3 position, out Quaternion rotation)
    {
        float f = Mathf.Clamp(time / sampleInterval, 0f, positions.Length - 1);
        int i = Mathf.Min((int)f, positions.Length - 2);
        float t = f - i;
        position = Vector3.LerpUnclamped(positions[i], positions[i + 1], t);
        rotation = Quaternion.SlerpUnclamped(rotations[i], rotations[i + 1], t);
    }

    /// <summary>Where the leaf ends up.</summary>
    public void FinalPose(out Vector3 position, out Quaternion rotation)
    {
        position = positions[positions.Length - 1];
        rotation = rotations[rotations.Length - 1];
    }

#if UNITY_EDITOR
    /// <summary>Editor only: what a bake recorded.</summary>
    public void Store(float interval, float impact, Vector3[] recordedPositions, Quaternion[] recordedRotations)
    {
        sampleInterval = interval;
        impactTime = impact;
        positions = recordedPositions;
        rotations = recordedRotations;
    }
#endif
}
