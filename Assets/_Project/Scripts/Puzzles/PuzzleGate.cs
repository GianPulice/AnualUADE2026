using System.Collections;
using UnityEngine;

/// <summary>
/// Portón que se abre — traslada un Transform hacia arriba — cuando un puzzle dado se marca como
/// completado en <see cref="PuzzleStateManager"/>. Un mismo prefab se reutiliza por escena
/// cambiando sólo el <c>puzzleId</c> en el inspector.
/// </summary>
public class PuzzleGate : MonoBehaviour
{
    [SerializeField, PuzzleId] private string puzzleId;

    private const float OpenHeight = 3f;
    private const float OpenDuration = 7f;

    // Id del SO_SoundData registrado en el AudioManager (mismo nombre del asset). Si se renombra
    // el asset hay que actualizar esta constante — un typo compila igual y el AudioManager loguea
    // "no hay sonido con id X" sin reproducir nada.
    private const string OpenSoundId = "sfx_porton_abriendose";

    private Transform door;
    private AudioSource audioSource;
    private AnimationCurve openCurve;
    private Vector3 closedLocalPosition;
    private Coroutine openRoutine;

    private void Awake()
    {
        // Convención: la puerta que se mueve es el hijo llamado "Door" del prefab. Si no existe,
        // el propio transform funciona como fallback para configuraciones planas.
        Transform child = transform.Find("Door");
        door = child != null ? child : transform;

        // AudioSource local al Door: el sonido sale literalmente del GameObject visible del
        // portón, así no depende de que la posición world sea la esperada — y además acompaña al
        // mesh mientras sube. El AudioManager lo configura por id con la data del SO al reproducir.
        audioSource = door.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = door.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        closedLocalPosition = door.localPosition;
    }

    private void Start()
    {
        // Puerta ya abierta por checkpoint o re-entrada: sin animación, directo al final.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleId))
        {
            door.localPosition = closedLocalPosition + Vector3.up * OpenHeight;
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

    private void HandlePuzzleCompleted(string completedId)
    {
        if (completedId != puzzleId) return;
        if (openRoutine != null) return;

        openRoutine = StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        // Reproduce sobre el AudioSource local del Door: el sonido sale del GameObject visible
        // del portón sin importar qué posición tenga el root del prefab, y sigue a la puerta
        // mientras sube. El AudioManager configura el clip, mixer group y distancias del SO.
        if (AudioManager.Exists)
            AudioManager.Instance.PlayOneShotOn(OpenSoundId, audioSource);

        Vector3 from = door.localPosition;
        Vector3 to = closedLocalPosition + Vector3.up * OpenHeight;

        float elapsed = 0f;
        while (elapsed < OpenDuration)
        {
            elapsed += Time.deltaTime;
            float t = openCurve.Evaluate(Mathf.Clamp01(elapsed / OpenDuration));
            door.localPosition = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }

        door.localPosition = to;
        openRoutine = null;
    }
}
