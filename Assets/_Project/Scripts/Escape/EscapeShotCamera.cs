using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A shot of the escape: the doors slamming behind the player and the pan down the corridor, the
/// Nemesis's eyes, its charge, the gate falling and the Nemesis left in the dust. One job: be the
/// live camera while <see cref="EscapeSequenceDirector"/> says so, optionally keeping an eye on a
/// target (the Nemesis), panning to a point (<see cref="BeginPan"/>) and taking a hit (the gate).
/// WHEN it is live is the director's; where it stands is the scene's — move it in the Scene view.
///
/// A <see cref="CinemachineCamera"/> with no position or rotation behaviours, so its own transform
/// is the shot. It goes live by outranking the player's camera (<see cref="livePriority"/>) and
/// hands back by returning to the priority it has in the scene (-100: never live on its own). The
/// director makes every cut a hard cut while a cinematic plays. Stepping down puts the transform
/// back where the scene has it, pan and all, so the next run starts from the same framing.
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

    private bool panning;
    private float panFromYaw;
    private float panYawDelta;
    private float panFromPitch;
    private float panToPitch;
    private float panSeconds;
    private float panElapsed;
    private AnimationCurve panCurve;

    public bool IsLive => live;

    /// <summary>A <see cref="BeginPan"/> is still turning the shot.</summary>
    public bool IsPanning => live && panning;

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
        panning = false;

        cam.Priority = livePriority;
        live = true;
    }

    /// <summary>
    /// Turns the live shot, from the framing it has now, until it looks at <paramref name="lookAt"/>
    /// — in <paramref name="seconds"/>, on <paramref name="curve"/> (0..1 over 0..1; null = ease in
    /// and out). A pan: the camera stays where it stands. It stops following its target.
    ///
    /// <paramref name="turnSign"/> says which way round: +1 turns right (yaw growing), -1 left, 0 the
    /// short way. A half turn has no short way to speak of, and the long way would sweep the wall
    /// instead of the corridor, so the scene decides.
    /// </summary>
    public void BeginPan(Vector3 lookAt, float seconds, AnimationCurve curve, int turnSign)
    {
        if (!live) return;

        Vector3 aim = lookAt - basePosition;
        if (aim.sqrMagnitude < 0.0001f) return;

        target = null;

        Vector3 from = transform.rotation.eulerAngles;
        Vector3 to = Quaternion.LookRotation(aim).eulerAngles;

        panFromYaw = from.y;
        panYawDelta = Mathf.DeltaAngle(from.y, to.y);
        if (turnSign > 0 && panYawDelta < 0f) panYawDelta += 360f;
        else if (turnSign < 0 && panYawDelta > 0f) panYawDelta -= 360f;

        panFromPitch = Mathf.DeltaAngle(0f, from.x);
        panToPitch = Mathf.DeltaAngle(0f, to.x);

        panSeconds = Mathf.Max(0.01f, seconds);
        panElapsed = 0f;
        panCurve = curve;
        panning = true;
    }

    /// <summary>Lands a pan in progress on its last frame at once (a skip).</summary>
    public void FinishPan()
    {
        if (!panning) return;
        panElapsed = panSeconds;
        ApplyPan();
    }

    /// <summary>Hands the screen back to whoever outranks it once it steps down.</summary>
    public void Release()
    {
        if (!live) return;
        live = false;
        target = null;
        shakeLeft = 0f;
        panning = false;

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

        if (panning)
        {
            panElapsed += dt;
            ApplyPan();
        }
        else if (target != null && follow > 0f)
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

    private void ApplyPan()
    {
        float t = Mathf.Clamp01(panElapsed / panSeconds);
        float k = panCurve != null && panCurve.length > 0 ? panCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

        transform.rotation = Quaternion.Euler(Mathf.LerpUnclamped(panFromPitch, panToPitch, k),
                                              panFromYaw + panYawDelta * k, 0f);

        if (t >= 1f) panning = false;
    }
}
