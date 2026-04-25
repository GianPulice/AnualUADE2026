using System.Collections.Generic;
using UnityEngine;

public class InventoryManagerUI : Singleton<InventoryManagerUI> 
{
    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Módulos")]
    [SerializeField] private List<ModuleData> modules = new List<ModuleData>();

    [Header("Referencias a Views")]
    [SerializeField] private InventoryView inventoryView;
    [SerializeField] private ItemDetailView itemDetailView;
    [SerializeField] private ModuleView moduleView;

    // ------------------ Estado interno ------------------

    private bool isInventoryOpen = false;
    private SO_InventoryItem selectedItem = null;

    //------------------ Unity ------------------

    void Awake()
    {
        CreateSingleton(true);
    }

    void Start()
    {
        SubscribeToEvents();
        InitializeViews();
    }

    void Update()
    {
        HandleToggleInput();
        TickModuleTimers();
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ------------------ Inicialización ------------------

    private void InitializeViews()
    {
        // El inventario empieza cerrado
        inventoryView?.SetVisible(false);

        // Cargar la lista inicial de ítems
        RefreshItemList();

        // Inicializar vista de módulos
        moduleView?.Initialize(modules);

        // Limpiar detalle
        itemDetailView?.ClearDetail();
    }

    private void SubscribeToEvents()
    {
        InventoryEvents.OnItemAdded += HandleItemAdded;
        InventoryEvents.OnItemRemoved += HandleItemRemoved;
        InventoryEvents.OnItemSelected += HandleItemSelected;
        InventoryEvents.OnItemUsed += HandleItemUsed;
        InventoryEvents.OnItemDiscarded += HandleItemDiscarded;
    }

    private void UnsubscribeFromEvents()
    {
        InventoryEvents.OnItemAdded -= HandleItemAdded;
        InventoryEvents.OnItemRemoved -= HandleItemRemoved;
        InventoryEvents.OnItemSelected -= HandleItemSelected;
        InventoryEvents.OnItemUsed -= HandleItemUsed;
        InventoryEvents.OnItemDiscarded -= HandleItemDiscarded;
    }

    // ------------------ Input ------------------ Despues se quita cuando se integre con el sistema de input

    private void HandleToggleInput()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;
        inventoryView?.SetVisible(isInventoryOpen);

        if (isInventoryOpen)
        {
            RefreshItemList();
        }
        else
        {
            selectedItem = null;
            itemDetailView?.ClearDetail();
        }

        InventoryEvents.InventoryToggled(isInventoryOpen);
    }

    // -- Acciones de ítems (llamadas desde ItemSlotView via botones) -------

    public void SelectItem(SO_InventoryItem item)
    {
        if (item == null) return;
        selectedItem = item;
        InventoryEvents.ItemSelected(item);
    }
    public void UseSelectedItem()
    {
        if (selectedItem == null) return;

        InventoryManager.Instance.UseItem(selectedItem);
        InventoryEvents.ItemUsed(selectedItem);
    }

    public void DiscardSelectedItem()
    {
        if (selectedItem == null) return;

        SO_InventoryItem toDiscard = selectedItem;
        selectedItem = null;

        InventoryManager.Instance.RemoveItem(toDiscard);
        InventoryEvents.ItemDiscarded(toDiscard);
        InventoryEvents.ItemRemoved(toDiscard);
    }

    // ------------------ Event Handlers ------------------
    private void HandleItemAdded(SO_InventoryItem item)
    {
        RefreshItemList();

        if (InventoryManager.Instance.ReturnTrueIfInventoryIsFull())
        {
            InventoryEvents.InventoryFull();
        }
    }

    private void HandleItemRemoved(SO_InventoryItem item)
    {
        RefreshItemList();

        if (selectedItem == item)
        {
            selectedItem = null;
            itemDetailView?.ClearDetail();
        }
    }

    private void HandleItemSelected(SO_InventoryItem item)
    {
        itemDetailView?.ShowDetail(item);
    }

    private void HandleItemUsed(SO_InventoryItem item)
    {
        RefreshItemList();
    }

    private void HandleItemDiscarded(SO_InventoryItem item)
    {
        // El refresh ya lo hace HandleItemRemoved que se dispara junto
    }

    // ------------------ Módulos ------------------

    private void TickModuleTimers()
    {
        foreach (ModuleData module in modules)
        {
            if (!module.isTimerRunning) continue;

            module.timeRemaining -= Time.deltaTime;

            if (module.timeRemaining <= 0f)
            {
                module.timeRemaining = 0f;
                module.isTimerRunning = false;
                module.status = ModuleStatus.Failure;
                module.failuresCount++;

                InventoryEvents.ModuleFailure(module);
                InventoryEvents.ModuleStatusChanged(module);
            }
            else
            {
                InventoryEvents.ModuleTimerTick(module);
            }
        }
    }

    /// <summary>Activa el timer de un módulo por su ID.</summary>
    public void StartModuleTimer(string moduleId)
    {
        ModuleData module = GetModule(moduleId);
        if (module == null) return;

        module.timeRemaining = module.timerDuration;
        module.isTimerRunning = true;
        module.status = ModuleStatus.Active;

        InventoryEvents.ModuleStatusChanged(module);
    }

    /// <summary>Resetea los fallos de un módulo.</summary>
    public void ResetModuleFailures(string moduleId)
    {
        ModuleData module = GetModule(moduleId);
        if (module == null) return;

        module.failuresCount = 0;
        module.status = ModuleStatus.Active;

        InventoryEvents.ModuleStatusChanged(module);
    }

    public ModuleData GetModule(string moduleId)
    {
        return modules.Find(m => m.moduleID == moduleId);
    }

    public List<ModuleData> GetAllModules() => modules;

    // -- Helpers ------------------

    private void RefreshItemList()
    {
        if (!isInventoryOpen) return;
        inventoryView?.RefreshList(InventoryManager.Instance);
    }
}
