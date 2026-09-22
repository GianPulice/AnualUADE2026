using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A shot of the escape that outlives a Timeline or has none: the centre door while the player walks
/// out, the Nemesis leaving its door in the reveal, and the gate slamming shut at the end. One job:
/// be the live camera while
/// <see cref="EscapeSequenceDirector"/> says so, optionally keeping an eye on a target (the Nemesis)
/// and taking a hit (the gate). WHEN it is live is the director's; where it stands is the scene's —
/// move it in the Scene view.
///
/// A <see cref="CinemachineCamera"/> with no position or rotation behaviours, so its own transform
/// is the shot. It goes live by outranking the player's camera (<see cref="livePriority"/>) and
/// hands back by returning to the priority it has in the scene (-100: never live on its own). The
/// director makes every cut a hard cut while a cinematic plays.
///
/// The door's and the reveal's are fixed (their follow at 0); the reveal's is the old opening's
/// plane 2A. Framed over the player's shoulder instead, the open safe door kept getting between the
/// shot and the Nemesis.
/// </summary>
[RequireComponent(typeof(CinemachineCamera))]
public class EscapeShotCamera : MonoBehaviour
{
    [Tooltip("Prioridad mientras el plano está al aire. Tiene que ganarle a la cámara del jugador.")]
    [SerializeField] private int livePriority = 1000;

    [Header("Seguir al Nemesis")]
    [Tooltip("Cuánto gira hacia el Nemesis mientras se mueve. 0 = el encuadre queda fijo como está; " +
             "1 = lo sigue del todo.")]
    [SerializeField, Range(0f, 1f)] private float follow = 0.35f;

    [Tooltip("Altura (m) del punto del Nemesis al que mira: el pecho, no los pies.")]
    [SerializeField] private float targetHeight = 1.5f;

    [Tooltip("Segundos que tarda en alcanzarlo cuando se mueve. Más alto = más suave, más lento.")]
    [SerializeField, Min(0.01f)] private float followSmoothing = 0.25f;

    private CinemachineCamera cam;
    private int restPriority;
    private bool live;

    private Transform target;
    private Quaternion restRotation;
    private Vector3 basePosition;

    private float shakeAmplitude;
    private float shakeSeconds;
    private float shakeLeft;

    public bool IsLive => live;

    private void Awake()
    {
        cam = GetComponent<CinemachineCamera>();
        restPriority = cam.Priority;
    }

    private void OnDisable() => Release();

    /// <summary>Makes this the live camera, following <paramref name="lookTarget"/> (optional) from
    /// the framing it has in the scene.</summary>
    public void GoLive(Transform lookTarget)
    {
        if (cam == null) cam = GetComponent<CinemachineCamera>();

        target = lookTarget;
        restRotation = transform.rotation;
        basePosition = transform.position;
        shakeLeft = 0f;

        cam.Priority = livePriority;
        live = true;
    }

    /// <summary>Hands the screen back to whoever outranks it once it steps down.</summary>
    public void Release()
    {
        if (!live) return;
        live = false;
        target = null;
        shakeLeft = 0f;

        transform.SetPositionAndRotation(basePosition, restRotation);
        if (cam != null) cam.Priority = restPriority;
    }

    /// <summary>A hit: the camera shakes and settles over <paramref name="seconds"/>.</summary>
    public void Shake(float amplitude, float seconds)
    {
        if (!live || amplitude <= 0f) return;

        shakeAmplitude = amplitude;
        shakeSeconds = Mathf.Max(0.05f, seconds);
        shakeLeft = shakeSeconds;
    }

    // Update and not LateUpdate: CinemachineBrain reads the camera in its own LateUpdate.
    private void Update()
    {
        if (!live) return;

        float dt = Time.deltaTime;

        if (target != null && follow > 0f)
        {
            Vector3 aim = target.position + Vector3.up * targetHeight - basePosition;
            if (aim.sqrMagnitude > 0.0001f)
            {
                Quaternion wanted = Quaternion.Slerp(restRotation, Quaternion.LookRotation(aim), follow);
                transform.rotation = Quaternion.Slerp(transform.rotation, wanted,
                                                      1f - Mathf.Exp(-dt / followSmoothing));
            }
        }

        Vector3 offset = Vector3.zero;
        if (shakeLeft > 0f)
        {
            shakeLeft -= dt;

            // Perlin and not Random: a shake, not a jitter. Dies out linearly.
            float strength = 2f * shakeAmplitude * Mathf.Clamp01(shakeLeft / shakeSeconds);
            float t = Time.time * 28f;
            offset = transform.rotation * new Vector3(Mathf.PerlinNoise(t, 0.37f) - 0.5f,
                                                      Mathf.PerlinNoise(0.71f, t) - 0.5f, 0f) * strength;
        }

        transform.position = basePosition + offset;
    }
}
