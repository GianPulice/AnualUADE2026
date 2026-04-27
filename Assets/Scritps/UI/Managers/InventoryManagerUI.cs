using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlador principal del sistema de inventario UI
///
/// Responsabilidades :
///   - Manejar input (Tab abre, ESC/Tab cierra, pila de capas)
///   - Controlar Time.timeScale al abrir/cerrar
///   - Activar/desactivar el cursor del mouse
///   - Orquestar comunicación entre Model y Views mediante InventoryEvents
///   - Gestionar timers de módulos con unscaledDeltaTime
///   - Mantener la pila de capas activas (inventario → diálogo → ESC)
///
/// NO manipula UI directamente.
/// NO contiene lógica de negocio (eso es InventoryManager).
/// </summary>

public class InventoryManagerUI : Singleton<InventoryManagerUI>
{
    // -- Configuración -------------------

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Módulos del dispositivo")]
    [SerializeField] private List<ModuleData> modules = new List<ModuleData>();

    [Header("Views")]
    [SerializeField] private InventoryView inventoryView;
    [SerializeField] private ItemDetailView itemDetailView;
    [SerializeField] private ModuleHUDView moduleHUDView;
    [SerializeField] private DiscardDialogView discardDialogView;

    // -- Estado interno -------------------

    private bool isInventoryOpen = false;
    private bool isDiscardOpen = false;

    private SO_InventoryItem selectedItem = null;
    private SO_InventoryItem pendingDiscard = null;
    private SO_InventoryItem currentSelectedItem;

    // -- Unity -------------------

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
        HandleInput();
        TickModuleTimers();
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ------------------ Inicialización ------------------

    private void InitializeViews()
    {
        inventoryView?.SetVisible(false);
        itemDetailView?.ShowEmpty();
        discardDialogView?.Hide();
        moduleHUDView?.Initialize(modules);
    }

    private void SubscribeToEvents()
    {
        InventoryEvents.OnItemAdded += HandleItemAdded;
        InventoryEvents.OnItemRemoved += HandleItemRemoved;
        InventoryEvents.OnItemSelected += HandleItemSelected;
        InventoryEvents.OnDiscardRequested += HandleDiscardRequested;
        InventoryEvents.OnDiscardConfirmed += HandleDiscardConfirmed;
        InventoryEvents.OnDiscardCancelled += HandleDiscardCancelled;
    }

    private void UnsubscribeFromEvents()
    {
        InventoryEvents.OnItemAdded -= HandleItemAdded;
        InventoryEvents.OnItemRemoved -= HandleItemRemoved;
        InventoryEvents.OnItemSelected -= HandleItemSelected;
        InventoryEvents.OnDiscardRequested -= HandleDiscardRequested;
        InventoryEvents.OnDiscardConfirmed -= HandleDiscardConfirmed;
        InventoryEvents.OnDiscardCancelled -= HandleDiscardCancelled;
    }

    // ------------------ Input ------------------ Despues se quita cuando se integre con el sistema de input

    private void HandleInput()
    {
        // ESC: capa superior primero
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleCancelInput();
        }

        // Tab: toggle del inventario (solo si no hay diálogo abierto)
        if (Input.GetKeyDown(toggleKey) && !isDiscardOpen)
        {
            if (isInventoryOpen) CloseInventory();
            else OpenInventory();
        }
    }

    // -- Apertura / Cierre --------------------

    /// <summary>
    /// Abre el inventario:
    ///   - Time.timeScale = 0 (pausa el gameplay)
    ///   - Cursor habilitado y visible
    ///   - Lista refresheada
    ///   - Primer ítem seleccionado automáticamente si hay ítems
    /// </summary>
    public void OpenInventory()
    {
        if (isInventoryOpen) return;

        isInventoryOpen = true;

        Time.timeScale = 0f;

        // Activar cursor para interactuar con la UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Mostrar y poblar la vista
        inventoryView?.SetVisible(true);
        RefreshItemList();

        //AutoSelectFirstItem();

        InventoryEvents.InventoryToggled(true);
    }

    /// <summary>
    /// Cierra el inventario:
    ///   - Time.timeScale = 1 (reanuda gameplay)
    ///   - Cursor deshabilitado (vuelve al estado de juego)
    ///   - Detener audio de grabación si estaba reproduciéndose
    /// </summary>
    public void CloseInventory()
    {
        if (!isInventoryOpen) return;

        isInventoryOpen = false;
        selectedItem = null;

        // Reanudar gameplay
        Time.timeScale = 1f;

        // Desactivar cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Ocultar vista y limpiar detalle
        inventoryView?.SetVisible(false);
        itemDetailView?.ShowEmpty();

        // Detener audio de grabación
        //  itemDetailView?.StopAudio();

        InventoryEvents.InventoryToggled(false);
    }

    // ── Selección de ítem ─────────────────────────────────────────────────────

    /// <summary>
    /// Llamado por ItemSlotView cuando el jugador clickea un ítem.
    /// Notifica a las Views a través del evento.
    /// </summary>
    public void SelectItem(SO_InventoryItem item)
    {
        if (item == null) return;
        selectedItem = item;
        currentSelectedItem = item;
        InventoryEvents.ItemSelected(item);
    }

    private void AutoSelectFirstItem()
    {
        IReadOnlyList<SO_InventoryItem> allItems = InventoryManager.Instance.GetAllItems();

        if (allItems.Count > 0)
        {
            SelectItem(allItems[0]);
        }
        else
        {
            // Lista vacía: mostrar estado vacío en el panel de detalle
            itemDetailView?.ShowEmpty();
        }
    }

    // ── Descarte ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Llamado por ItemDetailView al presionar el botón de descarte.
    /// NO elimina el ítem — solo solicita la confirmación.
    /// </summary>
    public void RequestDiscard(SO_InventoryItem item)
    {
        if (item == null) return;

        pendingDiscard = item;
        isDiscardOpen = true;

        InventoryEvents.DiscardRequested(item);
    }

    /// <summary>Llamado por DiscardDialogView al confirmar.</summary>
    public void ConfirmDiscard()
    {
        if (pendingDiscard == null) return;

        SO_InventoryItem toDiscard = pendingDiscard;
        pendingDiscard = null;
        isDiscardOpen = false;

        InventoryManager.Instance.DiscardItem(toDiscard);
        InventoryEvents.DiscardConfirmed(toDiscard);
    }

    /// <summary>Llamado por DiscardDialogView al cancelar o por ESC.</summary>
    public void CancelDiscard()
    {
        pendingDiscard = null;
        isDiscardOpen = false;
        InventoryEvents.DiscardCancelled();
    }

    // -- Handlers de eventos --------------------

    private void HandleItemAdded(SO_InventoryItem item)
    {
        RefreshItemList();
    }

    private void HandleItemRemoved(SO_InventoryItem item)
    {
        RefreshItemList();

        // Si el ítem removido era el seleccionado, limpiar el panel de detalle
        if (selectedItem == item)
        {
            selectedItem = null;
            itemDetailView?.ShowEmpty();
            AutoSelectFirstItem();
        }
    }

    private void HandleItemSelected(SO_InventoryItem item)
    {
        itemDetailView?.ShowDetail(item);
    }

    private void HandleDiscardRequested(SO_InventoryItem item)
    {
        discardDialogView?.Show(item);
    }

    private void HandleDiscardConfirmed(SO_InventoryItem item)
    {
        discardDialogView?.Hide();
    }

    private void HandleDiscardCancelled()
    {
        discardDialogView?.Hide();
    }

    private void HandleCancelInput()
    {
        //Agregar un if mas para cuando este la de Audio
        if (isDiscardOpen) // (Usa la variable booleana que controle tu modal)
        {
            CancelDiscard();
            return;
        }
        if (currentSelectedItem != null)
        {
            ClearSelection();
            return;
        }
        CloseInventory();
    }

    private void ClearSelection()
    {
        currentSelectedItem = null;

        InventoryEvents.ItemSelected(null);

        inventoryView.HighlightItem(null);
    }

    // -- Módulos (unscaledDeltaTime) --------------------

        /// <summary>
        /// Actualiza los timers de módulos con unscaledDeltaTime.
        /// Esto asegura que el countdown siga corriendo aunque Time.timeScale == 0.
        /// </summary>
    private void TickModuleTimers()
    {
        foreach (ModuleData module in modules)
        {
            if (module.Status != ModuleStatus.Active || !module.IsTimerRunning) continue;

            module.TimeRemaining -= Time.unscaledDeltaTime;

            if (module.TimeRemaining <= 0f)
            {
                module.TimeRemaining = 0f;
                module.IsTimerRunning = false;
                module.Status = ModuleStatus.Exploded;

                InventoryEvents.ModuleExploded(module);
                InventoryEvents.ModuleStateChanged(module);
            }
            else
            {
                InventoryEvents.ModuleTimerTick(module);
            }
        }
    }

    // -- API pública de módulos --------------------

    // Zona rara, despues hay que chequear cuando el desarrollo de los modulos este mas avanzado.
    // Por ahora se deja esta API pública para facilitar la integración con el sistema de módulos, pero
    // idealmente el InventoryManagerUI no debería exponer métodos específicos de módulos,
    // sino solo manejar su estado interno y comunicarlo a través de eventos.

    /// <summary>Inicia o reinicia el timer de un módulo.</summary>
    public void StartModuleTimer(string moduleId)
    {
        ModuleData module = GetModule(moduleId);
        if (module == null) return;

        module.TimeRemaining = module.TimerDuration;
        module.IsTimerRunning = true;
        module.Status = ModuleStatus.Active;

        InventoryEvents.ModuleStateChanged(module);
    }

    /// <summary>Marca un módulo como resuelto (puzzle completado).</summary>
    public void ResolveModule(string moduleId)
    {
        ModuleData module = GetModule(moduleId);
        if (module == null) return;

        module.IsTimerRunning = false;
        module.Status = ModuleStatus.Resolved;

        InventoryEvents.ModuleStateChanged(module);
    }

    public ModuleData GetModule(string moduleId) =>
        modules.Find(m => m.ModuleID == moduleId);

    public List<ModuleData> GetAllModules() => modules;

    public int GetExplodedCount() =>
        modules.FindAll(m => m.Status == ModuleStatus.Exploded).Count;

    public ModuleData GetActiveModule() =>
        modules.Find(m => m.Status == ModuleStatus.Active && m.IsTimerRunning);

    // -- Helpers --------------------

    private void RefreshItemList()
    {
        if (!isInventoryOpen) return;
        inventoryView?.RefreshList(InventoryManager.Instance);
    }

    // -- Accessors de estado --------------------

    public bool IsInventoryOpen => isInventoryOpen;
    public SO_InventoryItem SelectedItem => selectedItem;
}
