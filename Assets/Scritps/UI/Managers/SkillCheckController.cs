using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

// Controlador del Skill-Check (Puzzle Central 2 — Hub de Ventilación).
// IModalUI: PausesGame=false → Time.timeScale=1 durante los checks.
// El player no puede moverse (IsAnyModalOpen=true) pero el Nemesis sí.
// Expone Open(SO_SkillCheckData) para que el puzzle lo invoque.
// Expone OnCompleted/OnFailed para que el puzzle reaccione al resultado.
public class SkillCheckController : BaseScreenController<SkillCheckView, SkillCheckModel>, IModalUI
{
    public static SkillCheckController Instance { get; private set; }
    public bool IsOpen { get; private set; }

    [Header("Configuración por defecto")]
    [SerializeField] private SO_SkillCheckData defaultData;

    // IModalUI
    public string ModalId       => "SkillCheck";
    public bool ConsumesEscape  => false;
    public bool BlocksPause     => false;
    public bool PausesGame      => false;

    public void RequestClose() => CloseSafe().Forget();

    private bool _isRunning;
    private SO_SkillCheckData _activeData;

    // El puzzle espera este callback para saber si el check fue completado.
    public System.Action OnCompleted;
    public System.Action OnFailed;

    private void Awake()
    {
        Instance = this;
        model = new SkillCheckModel();
        model.Initialize();
        view.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeModel();
    }

    // Llamado externamente por el puzzle controller.
    public void Open(SO_SkillCheckData data = null)
    {
        if (IsOpen) return;
        _activeData = data != null ? data : defaultData;
        if (_activeData == null)
        {
            Debug.LogError("[SkillCheckController] Sin SO_SkillCheckData asignado.");
            return;
        }

        IsOpen = true;
        model.Configure(_activeData);
        OpenSafe().Forget();
    }

    private async UniTaskVoid OpenSafe()
    {
        await base.Open();
    }

    protected override void OnBeforeOpen()
    {
        UIStateManager.Instance.Push(this);
        SubscribeModel();
        view.Initialize(
            model.SuccessZoneStart,
            model.SuccessZoneWidth,
            model.TotalChecks,
            _activeData.flashDuration);
        _isRunning = true;
    }

    protected override void OnBeforeClose()
    {
        _isRunning = false;
        UIStateManager.Instance.Pop(this);
        UnsubscribeModel();
    }

    private void Update()
    {
        if (!_isRunning) return;
        if (Time.timeScale == 0f) return;

        view.Tick(model.NeedleSpeed);

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            HandleEInput();
    }

    private void HandleEInput()
    {
        bool success = model.TryInput(view.NeedleAngle);

        if (success)
        {
            view.FlashSuccess();
            view.UpdateCounter(model.CurrentCheck, model.TotalChecks);
            view.UpdateSuccessZone(model.SuccessZoneStart, model.SuccessZoneWidth);
        }
        else
        {
            view.FlashFail();
            ApplyTimerPenalty();
        }
    }

    private void ApplyTimerPenalty()
    {
        if (InventoryManagerUI.Instance == null) return;
        ModuleData active = InventoryManagerUI.Instance.GetActiveModule();
        if (active == null || !active.IsTimerRunning) return;
        active.TimeRemaining = Mathf.Max(0f, active.TimeRemaining - _activeData.failTimePenalty);
    }

    private void HandleCheckSuccess() { }

    private void HandleCheckFailed() { }

    private void HandleAllComplete()
    {
        _isRunning = false;
        OnCompleted?.Invoke();
        OnCompleted = null;
        CloseSafe().Forget();
    }

    private async UniTaskVoid CloseSafe()
    {
        IsOpen = false;
        await base.Close();
    }

    private void SubscribeModel()
    {
        model.OnCheckSuccess       += HandleCheckSuccess;
        model.OnCheckFailed        += HandleCheckFailed;
        model.OnAllChecksComplete  += HandleAllComplete;
    }

    private void UnsubscribeModel()
    {
        model.OnCheckSuccess       -= HandleCheckSuccess;
        model.OnCheckFailed        -= HandleCheckFailed;
        model.OnAllChecksComplete  -= HandleAllComplete;
    }
}
