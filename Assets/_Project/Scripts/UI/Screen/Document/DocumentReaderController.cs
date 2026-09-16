using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CONTROLLER of the note reader. Two ways in, one sheet:
///
///   Open(<see cref="SO_InventoryItem"/>) — READING MODE. What a pickup calls the moment a note
///     lands in the inventory. The game is frozen (<see cref="PausesGame"/>) and the sheet stays
///     up until the player dismisses it; the note object itself is already gone from the level,
///     so there is nothing to walk away from.
///
///   Open(<see cref="SO_DocumentData"/>) — READ IN PLACE. The older behaviour kept for
///     <see cref="NoteInteractable"/>: the world keeps running behind the sheet and it closes by
///     itself as soon as the crosshair leaves the thing that opened it.
///
/// Both close through <see cref="RequestClose"/>, which is also what the X, the dim and ESC end up
/// calling — see <see cref="DocumentReaderView.OnCloseRequested"/>.
/// </summary>
public class DocumentReaderController : BaseScreenController<DocumentReaderView, DocumentReaderModel>, IModalUI
{
    public static DocumentReaderController Instance { get; private set; }

    [Header("Audio")]
    [Tooltip("Played when the sheet opens. Leave empty for silence — the pickup sound already fired.")]
    [SoundId]
    [SerializeField] private string openSoundId = string.Empty;

    private bool isOpen;
    private bool isTransitioning;
    private IInteractable openingTarget;
    private bool trackTarget;

    /// <summary>Reading mode: freeze the game and ignore what the crosshair is doing.</summary>
    private bool pausesWhileOpen;

    public bool IsOpen => isOpen;

    // ── IModalUI ─────────────────────────────────────────────────────────────
    public string ModalId        => "DocumentReader";
    public bool   ConsumesEscape => true;

    /// <summary>
    /// Only in reading mode. The pause canvas sorts at 1 and this one at 60, so a pause menu
    /// opened on top of the sheet would render UNDER it — the player would be looking at a note
    /// with an invisible menu swallowing their input. The game is already frozen here anyway, so
    /// there is nothing pause would add. Read in place, the world is live and pause must work.
    /// </summary>
    public bool BlocksPause => isOpen && pausesWhileOpen;

    public bool PausesGame  => pausesWhileOpen;
    public void RequestClose() => CloseSafe().Forget();

    private void Awake()
    {
        Instance = this;

        model = new DocumentReaderModel();
        model.Initialize();

        if (view != null)
        {
            view.gameObject.SetActive(false);
            view.OnCloseRequested += RequestClose;
        }

        InteractionEvents.OnTargetChanged += HandleTargetChanged;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
        if (view != null) view.OnCloseRequested -= RequestClose;

        // Without this the static keeps pointing at a destroyed controller after a scene change,
        // and the next pickup opens a reader that is not in any scene any more.
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the note the player has just picked up. Called by <see cref="PickupInteractable"/>
    /// right after the item reaches the inventory, so the first read is free and the inventory
    /// copy is the re-read.
    /// </summary>
    public void Open(SO_InventoryItem item)
    {
        if (item == null) return;

        model.SetDocument(item);
        OpenCurrent(pauses: true, track: false);
    }

    /// <summary>Opens a document read off a world object, with the world still running.</summary>
    public void Open(SO_DocumentData data)
    {
        if (data == null) return;

        model.SetDocument(data);
        OpenCurrent(pauses: false, track: true);
    }

    private void OpenCurrent(bool pauses, bool track)
    {
        if (isOpen || isTransitioning) return;

        pausesWhileOpen = pauses;

        view.Populate(model.Title, model.Body, model.Image);

        // Which interactable opened this, so the auto-close knows when we have really left its
        // range. Only in read-in-place mode: a picked-up note is destroyed on the spot, and
        // InteractionManager clears its target the instant a modal goes up, which would slam the
        // sheet shut on the frame it appeared.
        trackTarget = track;
        openingTarget = track && InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        if (AudioManager.Exists && !string.IsNullOrWhiteSpace(openSoundId))
            AudioManager.Instance.PlaySFX(openSoundId);

        OpenSafe().Forget();
    }

    // ── BaseScreenController hooks ───────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        isOpen = true;
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        isOpen = false;
        openingTarget = null;
        trackTarget = false;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

        // Cleared only after the Pop: UIStateManager re-reads PausesGame while unstacking, and a
        // false here before that would leave Time.timeScale at 0 with nothing left to unfreeze it.
        pausesWhileOpen = false;
    }

    // ── Auto-close on target change (read in place only) ─────────────────────

    private void HandleTargetChanged(IInteractable newTarget)
    {
        if (!isOpen || !trackTarget) return;

        // The InteractionManager stopped pointing at the note that opened this document — the
        // player walked away, or is now looking at something else.
        if (!ReferenceEquals(newTarget, openingTarget))
            CloseSafe().Forget();
    }

    // ── Async helpers ────────────────────────────────────────────────────────

    private async UniTaskVoid OpenSafe()
    {
        isTransitioning = true;
        await Open();
        isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        if (isTransitioning || !isOpen) return;
        isTransitioning = true;
        await Close();
        isTransitioning = false;
    }
}
