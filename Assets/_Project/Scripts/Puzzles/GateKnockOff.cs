using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Moves a gate's leaf when something beats on it from the far side: it jumps in its frame at each
/// blow (<see cref="Shudder"/>), leaning a little further out every time, and the last blow knocks
/// it down (<see cref="KnockOffAsync"/>). One job: the leaf's motion.
/// Dents in the metal are <see cref="MeshDent"/>'s; the blows, the dust and the sound are
/// <see cref="NemesisGateEntrance"/>'s.
///
/// The fall is Unity's physics, simulated ONCE in the editor (the bake button on this inspector,
/// Editor/GateFallBaker) against the colliders round the gate and recorded in
/// <see cref="fallTrack"/>: it plays back the same every time, and nothing in play has a rigidbody.
/// Move the gate or the props where it lands → bake again.
///
/// SETUP: <see cref="landingFloor"/> sits on the floor at the foot of the gate, on the side it falls
/// to, its blue arrow (forward) pointing that way. Its height is the floor the leaf lands on.
/// </summary>
public class GateKnockOff : MonoBehaviour
{
    [Tooltip("La hoja que se mueve: el hijo \"Door\" del portón.")]
    [SerializeField] private Transform door;

    [Tooltip("Sobre el piso, al pie del portón, del lado hacia el que cae, con la flecha azul " +
             "(forward) apuntando para ese lado. Su altura es el piso donde queda la hoja.")]
    [SerializeField] private Transform landingFloor;

    [Tooltip("Colisionadores de la hoja: se apagan cuando sale volando, para que no trabe a nadie. " +
             "Vacío = todos los que tenga la hoja.")]
    [SerializeField] private Collider[] doorColliders = Array.Empty<Collider>();

    [Tooltip("Pared invisible en la abertura: se prende cuando la hoja sale volando. Detrás del " +
             "portón no hay piso.")]
    [SerializeField] private GameObject openingBlocker;

    [Tooltip("La caída horneada con física (botón \"Hornear caída\" de abajo). Vacío = la hoja no cae.")]
    [SerializeField] private SO_GateFallTrack fallTrack;

    [Header("Golpe (sacudón)")]
    [Tooltip("Segundos que tarda la hoja en asentarse después de un golpe.")]
    [SerializeField, Min(0.05f)] private float shudderSeconds = 0.35f;

    [Header("Caída")]
    [Tooltip("Segundos en los que se borra la diferencia entre cómo quedó la hoja después de los " +
             "golpes (inclinada) y cómo arranca la caída horneada (derecha).")]
    [SerializeField, Min(0f)] private float blendSeconds = 0.15f;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool restCaptured;
    private bool knockedOff;
    private CancellationTokenSource motionCts;

    /// <summary>The leaf is down (or on its way down): nothing moves it again.</summary>
    public bool IsKnockedOff => knockedOff;

    /// <summary>The leaf (for the bake).</summary>
    public Transform Door => door;

    /// <summary>The invisible wall in the opening (for the bake: it is not there while it falls).</summary>
    public GameObject OpeningBlocker => openingBlocker;

    /// <summary>Height of the floor the leaf falls onto (the landing mark's).</summary>
    public float FloorY => landingFloor != null ? landingFloor.position.y : door != null ? door.position.y : 0f;

    /// <summary>Where the bake writes.</summary>
    public SO_GateFallTrack FallTrack => fallTrack;

    /// <summary>Unit direction, flat, the leaf is pushed and falls to.</summary>
    public Vector3 Outward
    {
        get
        {
            Vector3 forward = landingFloor != null ? landingFloor.forward : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// How far above the floor the top of the fallen leaf is, straight above or below
    /// <paramref name="point"/> (0 where the leaf is not, or while it still stands): what something
    /// walking over it has to be raised by so its feet do not go into the sheet. Measured on the leaf
    /// as it lies now — flat, or propped on something.
    /// </summary>
    public float StepHeightAt(Vector3 point)
    {
        if (!knockedOff || door == null || leafBox.size == Vector3.zero) return 0f;

        // A ray straight down through the leaf's box, in the leaf's own space.
        const float reach = 6f;
        Vector3 origin = door.InverseTransformPoint(new Vector3(point.x, point.y + reach * 0.5f, point.z));
        Vector3 along = door.InverseTransformVector(Vector3.down * reach);

        float enter = 0f, exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = along[axis];
            float min = leafBox.min[axis], max = leafBox.max[axis];
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < min || o > max) return 0f;
                continue;
            }

            float t0 = (min - o) / d, t1 = (max - o) / d;
            if (t0 > t1) (t0, t1) = (t1, t0);
            enter = Mathf.Max(enter, t0);
            exit = Mathf.Min(exit, t1);
            if (enter > exit) return 0f;
        }

        float topY = point.y + reach * 0.5f - reach * enter;
        float floorY = landingFloor != null ? landingFloor.position.y : point.y;
        return Mathf.Max(0f, topY - floorY);
    }

    /// <summary>
    /// A blow from behind: the leaf jumps out by <paramref name="push"/> metres and rings back into
    /// its frame, leaning out <paramref name="leanDegrees"/> more than it did, for good.
    /// </summary>
    public void Shudder(float push, float leanDegrees)
    {
        if (door == null || knockedOff) return;
        CaptureRest();

        CancelMotion();
        restRotation = Quaternion.AngleAxis(leanDegrees, Vector3.Cross(Vector3.up, Outward)) * restRotation;

        motionCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        ShudderAsync(push, motionCts.Token).Forget();
    }

    /// <summary>
    /// Knocks the leaf down: plays the baked fall from wherever the blows left it. Completes on the
    /// leaf's first hard hit (the moment for the dust, the sound and the shake); what comes after it
    /// (bounces, sliding to rest) plays on its own.
    /// </summary>
    public async UniTask KnockOffAsync(CancellationToken token)
    {
        if (door == null || knockedOff) return;
        CaptureRest();
        CancelMotion();
        BeginKnockOff();

        if (fallTrack == null || !fallTrack.HasData)
        {
            Debug.LogWarning($"[{nameof(GateKnockOff)}] '{name}': no baked fall (Fall Track): the leaf stays up.", this);
            return;
        }

        motionCts = CancellationTokenSource.CreateLinkedTokenSource(token, this.GetCancellationTokenOnDestroy());
        CancellationToken motionToken = motionCts.Token;

        // Where the blows left the leaf against where the bake starts it: faded out at the start.
        fallTrack.Sample(0f, out Vector3 bakedStart, out Quaternion bakedStartRotation);
        Vector3 offset = door.position - bakedStart;
        Quaternion turn = door.rotation * Quaternion.Inverse(bakedStartRotation);

        float elapsed = 0f;
        float impact = fallTrack.ImpactTime;
        while (elapsed < impact)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, motionToken);
            elapsed += Time.deltaTime;
            ShowFall(elapsed, offset, turn);
        }

        PlayRestAsync(elapsed, offset, turn, motionToken).Forget();
    }

    /// <summary>The leaf down where it ends up, at once: what a skip needs.</summary>
    public void SnapKnockedOff()
    {
        if (door == null) return;
        CaptureRest();
        CancelMotion();
        if (!knockedOff) BeginKnockOff();

        if (fallTrack == null || !fallTrack.HasData) return;
        fallTrack.FinalPose(out Vector3 position, out Quaternion rotation);
        door.SetPositionAndRotation(position, rotation);
    }

    private async UniTaskVoid PlayRestAsync(float elapsed, Vector3 offset, Quaternion turn, CancellationToken token)
    {
        try
        {
            float duration = fallTrack.Duration;
            while (elapsed < duration)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
                ShowFall(elapsed, offset, turn);
            }
        }
        catch (OperationCanceledException)
        {
            // A skip snaps it to the end.
        }
    }

    private void ShowFall(float time, Vector3 offset, Quaternion turn)
    {
        fallTrack.Sample(time, out Vector3 position, out Quaternion rotation);

        float fade = blendSeconds > 0f ? 1f - Mathf.Clamp01(time / blendSeconds) : 0f;
        if (fade > 0f)
        {
            position += offset * fade;
            rotation = Quaternion.Slerp(Quaternion.identity, turn, fade) * rotation;
        }

        door.SetPositionAndRotation(position, rotation);
    }

    // At load, while the leaf stands shut and undented (a skip may dent it before it first moves).
    private void Awake() => CaptureRest();

    // Where the baked fall leaves the leaf.
    private void OnDrawGizmosSelected()
    {
        if (door == null || fallTrack == null || !fallTrack.HasData) return;
        MeshFilter mesh = door.GetComponent<MeshFilter>();
        if (mesh == null || mesh.sharedMesh == null) return;

        fallTrack.FinalPose(out Vector3 position, out Quaternion rotation);
        Bounds local = mesh.sharedMesh.bounds;
        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(position, rotation, door.lossyScale);
        Gizmos.DrawWireCube(local.center, local.size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDestroy() => CancelMotion();

    private void BeginKnockOff()
    {
        knockedOff = true;
        foreach (Collider col in Colliders())
            if (col != null) col.enabled = false;
        if (openingBlocker != null) openingBlocker.SetActive(true);
    }

    private async UniTaskVoid ShudderAsync(float push, CancellationToken token)
    {
        Vector3 outward = Outward;
        float elapsed = 0f;

        try
        {
            while (elapsed < shudderSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;

                // Out at once, back, a smaller bounce, still: a heavy sheet ringing in its frame.
                float s = Mathf.Clamp01(elapsed / shudderSeconds);
                float offset = push * Mathf.Sin(s * Mathf.PI * 2.5f) * (1f - s) * (1f - s);
                door.SetPositionAndRotation(restPosition + outward * offset, restRotation);
            }

            door.SetPositionAndRotation(restPosition, restRotation);
        }
        catch (OperationCanceledException)
        {
            // A new blow or the knock-off took over: it starts from wherever this left the leaf.
        }
    }

    private void CaptureRest()
    {
        if (restCaptured || door == null) return;
        restCaptured = true;
        restPosition = door.position;
        restRotation = door.rotation;

        // The leaf's box before any dent: what it is stood on (the dents' bulge ends up underneath).
        MeshFilter mesh = door.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null) leafBox = mesh.sharedMesh.bounds;
    }

    private Bounds leafBox;

    private Collider[] Colliders() =>
        doorColliders != null && doorColliders.Length > 0
            ? doorColliders
            : door != null ? door.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();

    private void CancelMotion()
    {
        motionCts?.Cancel();
        motionCts?.Dispose();
        motionCts = null;
    }
}
