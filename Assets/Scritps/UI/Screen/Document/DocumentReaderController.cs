using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Controller del lector de documentos (notas, pizarras, cartas).
///
/// A diferencia de los otros controllers (Pause, Settings, SequencePanel, Inventory),
/// el DocumentReader **NO se registra en el UIStateManager** y **NO pausa el juego**.
/// Sigue el spec §5: "NO pausar el juego durante la inspeccion. El jugador puede moverse.
/// Si el jugador sale del rango mientras lee: cerrar la UI automaticamente."
///
/// Consecuencias:
///   - <c>Time.timeScale</c> queda en 1: el InteractionManager sigue raycasteando.
///   - El player puede moverse y rotar la camara.
///   - Si el target del raycast cambia (player se alejo o apunto a otra cosa), la UI cierra sola.
///   - ESC abre la pausa por encima (queda visible el documento debajo, hasta despausar).
///     El documento NO se cierra con ESC por diseño — solo con "salir del rango" o nuevo prompt.
/// </summary>
public class DocumentReaderController : BaseScreenController<DocumentReaderView, DocumentReaderModel>
{
    public static DocumentReaderController Instance { get; private set; }

    private bool isOpen;
    private bool isTransitioning;
    private IInteractable openingTarget;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        Instance = this;

        model = new DocumentReaderModel();
        model.Initialize();

        if (view != null) view.gameObject.SetActive(false);

        InteractionEvents.OnTargetChanged += HandleTargetChanged;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void Open(SO_DocumentData data)
    {
        if (data == null || isOpen || isTransitioning) return;

        model.SetDocument(data);
        view.Populate(data);

        // Guardamos qué interactable abrió este documento para que el auto-close
        // sepa cuándo realmente "salimos" de su rango.
        openingTarget = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        OpenSafe().Forget();
    }

    // ── Hooks BaseScreenController ───────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        isOpen = true;
        // Sin Push al UIStateManager — el juego sigue corriendo (timeScale=1) y el
        // player puede moverse libremente (spec §5).
    }

    protected override void OnBeforeClose()
    {
        isOpen = false;
        openingTarget = null;
    }

    // ── Auto-close por cambio de target ──────────────────────────────────────

    private void HandleTargetChanged(IInteractable newTarget)
    {
        if (!isOpen) return;

        // Si el InteractionManager dejo de apuntar al note que abrió este documento,
        // (sea porque el player se alejó, sea porque ahora mira otra cosa), cerramos.
        if (!ReferenceEquals(newTarget, openingTarget))
            CloseSafe().Forget();
    }

    // ── Helpers async ────────────────────────────────────────────────────────

    private async UniTaskVoid OpenSafe()
    {
        isTransitioning = true;
        await Open();
        isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        await Close();
        isTransitioning = false;
    }
}
