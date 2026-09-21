using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Portón que se abre — sube el hijo "Door" y a la vez lleva su escala Z a openScaleZ — cuando un puzzle dado se marca como
/// completado en <see cref="PuzzleStateManager"/>. Un mismo prefab se reutiliza por escena
/// cambiando sólo el <c>puzzleId</c> en el inspector.
///
/// Con <c>opensOnPuzzleCompleted</c> apagado el puzzle no lo abre: lo abre otro sistema con
/// <see cref="Open"/>, cuando le sirva (el portón del escape lo abre la secuencia durante la
/// persecución). La duración es siempre openDuration: el sonido está cortado a ella.
/// </summary>
public class PuzzleGate : MonoBehaviour
{
    [SerializeField, PuzzleId] private string puzzleId;

    [Tooltip("Se abre solo cuando se completa su puzzle. Apagado = lo abre otro sistema con Open " +
             "(el portón del escape: lo va abriendo la secuencia mientras dura la persecución).")]
    [SerializeField] private bool opensOnPuzzleCompleted = true;

    // Con el portón abierto el hijo "Door" sube openHeight y su escala Z llega a openScaleZ, las
    // dos cosas a la vez sobre la misma curva y en openDuration segundos.
    [Header("Apertura")]
    [Tooltip("Cuánto sube el hijo \"Door\" (en su espacio local) con el portón abierto.")]
    [SerializeField] private float openHeight = 3f;
    [Tooltip("Escala Z final del hijo \"Door\" con el portón abierto. X e Y no se tocan.")]
    [SerializeField] private float openScaleZ = 4f;
    [Tooltip("Segundos que tarda en abrir. Posición, escala y el sonido de apertura van juntos: " +
             "si se cambia, hay que cambiar el sonido.")]
    [SerializeField, Min(0.01f)] private float openDuration = 7f;

    // Id del SO_SoundData registrado en el AudioManager (mismo nombre del asset). Si se renombra
    // el asset hay que actualizar esta constante — un typo compila igual y el AudioManager loguea
    // "no hay sonido con id X" sin reproducir nada.
    private const string OpenSoundId = "sfx_porton_abriendose";

    private Transform door;
    private AnimationCurve openCurve;
    private Vector3 closedLocalPosition;
    private Vector3 closedLocalScale;
    private CancellationTokenSource openCts;

    private void Awake()
    {
        // Convención: la puerta que se mueve es el hijo llamado "Door" del prefab. Si no existe,
        // el propio transform funciona como fallback para configuraciones planas.
        Transform child = transform.Find("Door");
        door = child != null ? child : transform;

        openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        closedLocalPosition = door.localPosition;
        closedLocalScale = door.localScale;
    }

    private void Start()
    {
        // Puerta ya abierta por checkpoint o re-entrada: sin animación, directo al final.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleId))
        {
            door.localPosition = OpenPosition();
            door.localScale = OpenScale();
        }
    }

    private void OnEnable()
    {
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDisable()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void OnDestroy()
    {
        openCts?.Cancel();
        openCts?.Dispose();
        openCts = null;
    }

    /// <summary>Seconds the opening takes, sound included. Whoever times the gate from outside
    /// schedules around this; it is not theirs to change, the sound is cut to it.</summary>
    public float OpenDuration => openDuration;

    private void HandlePuzzleCompleted(string completedId)
    {
        if (!opensOnPuzzleCompleted || completedId != puzzleId) return;
        Open();
    }

    /// <summary>
    /// Plays the opening: the sound and the door together, over <see cref="OpenDuration"/>. Once:
    /// a gate that is opening or open ignores it.
    /// </summary>
    public void Open()
    {
        if (door == null || openCts != null) return;

        openCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        OpenAsync(openDuration, openCts.Token).Forget();
    }

    // Scaled time on purpose: a gameplay wait, it freezes with the pause menu like the rest of the
    // level. The only cancel is the gate going away.
    private async UniTaskVoid OpenAsync(float seconds, CancellationToken token)
    {
        // Sonido 3D anclado en la posición del portón — mismo patrón que DoorInteractable.
        // Requiere que el root del prefab y el hijo Door estén alineados con el mesh visible.
        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX(OpenSoundId, transform.position);

        // Posición y escala comparten el mismo t: arrancan y terminan juntas.
        Vector3 fromPosition = door.localPosition;
        Vector3 toPosition = OpenPosition();
        Vector3 fromScale = door.localScale;
        Vector3 toScale = OpenScale();

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;
            float t = openCurve.Evaluate(Mathf.Clamp01(elapsed / seconds));
            door.localPosition = Vector3.LerpUnclamped(fromPosition, toPosition, t);
            door.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
        }

        door.localPosition = toPosition;
        door.localScale = toScale;
    }

    private Vector3 OpenPosition() => closedLocalPosition + Vector3.up * openHeight;

    private Vector3 OpenScale() =>
        new Vector3(closedLocalScale.x, closedLocalScale.y, openScaleZ);
}
