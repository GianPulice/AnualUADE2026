using UnityEngine;

/// <summary>
/// Leans the Nemesis's torso on top of the animation: forward with speed, back when it brakes and
/// forward when it takes off, into a turn, and hunched under whatever is too low for it
/// (<see cref="NemesisHeadroom"/> says how much). Its one job: posing the spine. Added to the model (the
/// Animator's object) by <see cref="NemesisStateManager"/>, so the prefab needs no edit.
///
/// Why it exists (10/11/2026): the model has one locomotion clip, a walk, and the Nemesis covers
/// 1–5.6 m/s with it. Played faster it keeps the feet planted (NemesisStateManager.TickLocomotionRate)
/// but the body stays upright like a walk at any speed, and every start, stop and turn reads as the
/// whole body being slid around. A few degrees of lean that follow the real motion are what make it
/// read as a heavy body carrying weight instead.
///
/// Measured from the ROOT's movement, not the NavMeshAgent's velocity: the agent is off on a lift,
/// a drop and under a cinematic's hand, and the lean has to follow the body through all of them.
///
/// Runs in LateUpdate after the Animator and BEFORE <see cref="NemesisArmWallGuard"/> (order 0) and
/// <see cref="NemesisPeekRig"/> (100): both measure and fix the arms, and they have to see the torso
/// where it ends up.
/// </summary>
[DefaultExecutionOrder(-50)]
public class NemesisLocomotionLean : MonoBehaviour
{
    [Tooltip("Grados hacia adelante a toda velocidad (Full Lean Speed).")]
    [SerializeField, Range(0f, 30f)] private float maxSpeedLean = 6f;

    [Tooltip("Metros por segundo a los que la inclinación por velocidad llega al máximo.")]
    [SerializeField, Min(0.1f)] private float fullLeanSpeed = 4.5f;

    [Tooltip("Grados por cada m/s² de aceleración: adelante al arrancar, atrás al frenar.")]
    [SerializeField, Range(0f, 5f)] private float accelerationLean = 0.8f;

    [Tooltip("Tope de la inclinación por aceleración, en grados, para los dos lados.")]
    [SerializeField, Range(0f, 20f)] private float maxAccelerationLean = 5f;

    [Tooltip("Grados de inclinación lateral hacia adentro de la curva por cada 100°/s de giro.")]
    [SerializeField, Range(0f, 10f)] private float turnBank = 2.5f;

    [Tooltip("Tope de la inclinación lateral, en grados.")]
    [SerializeField, Range(0f, 15f)] private float maxTurnBank = 4f;

    [Tooltip("Segundos de suavizado. Más largo = más pesado.")]
    [SerializeField, Min(0.01f)] private float smoothing = 0.2f;

    [Tooltip("Huesos que reparten la inclinación, de abajo hacia arriba. Los que no existan se saltean.")]
    [SerializeField] private string[] boneNames = { "spine.001", "spine.002", "spine.003" };

    private Transform body;
    private Transform[] bones;
    private Quaternion[] lastWritten;
    private Quaternion[] lastBase;

    private Vector3 lastPosition;
    private float lastYaw;
    private Vector3 smoothedVelocity;
    private float smoothedAcceleration;
    private float smoothedYawRate;
    private float pitch;
    private float roll;
    private bool primed;
    private NemesisHeadroom headroom;

    private void Awake()
    {
        NemesisStateManager owner = GetComponentInParent<NemesisStateManager>();
        body = owner != null ? owner.transform : transform.root;
        headroom = body.GetComponent<NemesisHeadroom>();

        var found = new System.Collections.Generic.List<Transform>();
        foreach (string boneName in boneNames)
        {
            Transform bone = FindDeep(transform, boneName);
            if (bone != null) found.Add(bone);
        }

        bones = found.ToArray();
        lastWritten = new Quaternion[bones.Length];
        lastBase = new Quaternion[bones.Length];

        if (bones.Length == 0)
        {
            Debug.LogWarning($"[{nameof(NemesisLocomotionLean)}] None of the spine bones " +
                             $"({string.Join(", ", boneNames)}) is under '{name}': no lean.", this);
            enabled = false;
        }
    }

    private void OnEnable() => primed = false;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 position = body.position;
        float yaw = body.eulerAngles.y;

        if (!primed)
        {
            lastPosition = position;
            lastYaw = yaw;
            smoothedVelocity = Vector3.zero;
            smoothedAcceleration = 0f;
            smoothedYawRate = 0f;
            pitch = roll = 0f;
            primed = true;
        }

        Vector3 step = position - lastPosition;
        step.y = 0f;
        lastPosition = position;

        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;

        // A warp is not a sprint: forget the motion rather than lean into a teleport.
        if (step.magnitude > 1f)
        {
            smoothedVelocity = Vector3.zero;
            smoothedAcceleration = 0f;
            yawRate = 0f;
            step = Vector3.zero;
        }

        float k = 1f - Mathf.Exp(-dt / smoothing);
        Vector3 velocity = step / dt;
        float previousForward = Vector3.Dot(smoothedVelocity, body.forward);
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, k);
        float forwardSpeed = Vector3.Dot(smoothedVelocity, body.forward);
        smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, (forwardSpeed - previousForward) / dt, k);
        smoothedYawRate = Mathf.Lerp(smoothedYawRate, yawRate, k);

        float speedLean = Mathf.Clamp01(Mathf.Max(0f, forwardSpeed) / fullLeanSpeed) * maxSpeedLean;
        float accelLean = Mathf.Clamp(smoothedAcceleration * accelerationLean, -maxAccelerationLean, maxAccelerationLean);
        float targetPitch = speedLean + accelLean;

        // Into the turn: a right turn (positive yaw rate) banks the torso right, which is a negative
        // roll about the body's forward axis. Only while moving: turning on the spot does not bank.
        float moving = Mathf.Clamp01(forwardSpeed / 1f);
        float targetRoll = -Mathf.Clamp(smoothedYawRate / 100f * turnBank, -maxTurnBank, maxTurnBank) * moving;

        pitch = Mathf.Lerp(pitch, targetPitch, k);
        roll = Mathf.Lerp(roll, targetRoll, k);

        Apply();
    }

    /// <summary>Spreads the lean over the bones, about the body's own axes, on top of the pose the
    /// Animator left. A bone the clip does not animate keeps whatever was written last frame, so its
    /// own base is remembered and the lean is not stacked frame after frame.</summary>
    private void Apply()
    {
        // The duck under a low frame or pipe rides on the same bones (NemesisHeadroom measures it).
        float duck = headroom != null ? headroom.DuckAngle : 0f;

        Quaternion lean = Quaternion.AngleAxis((pitch + duck) / bones.Length, body.right) *
                          Quaternion.AngleAxis(roll / bones.Length, body.forward);

        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            Quaternion current = bone.localRotation;
            Quaternion baseRotation = current == lastWritten[i] ? lastBase[i] : current;

            bone.localRotation = baseRotation;
            bone.rotation = lean * bone.rotation;

            lastBase[i] = baseRotation;
            lastWritten[i] = bone.localRotation;
        }
    }

    private static Transform FindDeep(Transform root, string boneName)
    {
        if (root.name == boneName) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, boneName);
            if (found != null) return found;
        }

        return null;
    }
}
