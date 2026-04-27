using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VIEW del HUD del dispositivo dentro del inventario (spec §3).
///
/// Responsabilidades (SRP):
///   - Mostrar el timer del módulo activo (bloque izquierdo)
///   - Mostrar las barras de estado de M1/M2/M3 (bloque central)
///   - Mostrar los pips de fallos restantes (bloque derecho)
///   - Suscribirse a eventos de módulo para actualizarse en tiempo real
///   - NO manipula lógica de negocio ni de módulos
///
/// Los timers usan unscaledDeltaTime en el Controller, por lo que
/// los eventos de tick llegan correctamente aunque Time.timeScale == 0.
/// </summary>
public class ModuleHUDView : MonoBehaviour
{
    // ── Sub-views ─────────────────────────────────────────────────────────────

    [Header("Bloque izquierdo — Timer del módulo activo")]
    [SerializeField] private ActiveModuleTimerView activeTimerView;

    [Header("Bloque central — Estado de módulos")]
    [SerializeField] private Transform moduleRowContainer;
    [SerializeField] private ModuleRowView moduleRowPrefab;

    [Header("Bloque derecho — Fallos restantes")]
    [SerializeField] private FailuresPipsView failuresPipsView;

    // ── Estado ────────────────────────────────────────────────────────────────

    private Dictionary<string, ModuleRowView> rowMap = new Dictionary<string, ModuleRowView>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        InventoryEvents.OnModuleTimerTick += HandleTimerTick;
        InventoryEvents.OnModuleStateChanged += HandleStateChanged;
        InventoryEvents.OnModuleExploded += HandleModuleExploded;
    }

    void OnDisable()
    {
        InventoryEvents.OnModuleTimerTick -= HandleTimerTick;
        InventoryEvents.OnModuleStateChanged -= HandleStateChanged;
        InventoryEvents.OnModuleExploded -= HandleModuleExploded;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Inicializa las filas de módulo. Llamado por el Controller en Start.</summary>
    public void Initialize(List<ModuleData> modules)
    {
        rowMap.Clear();

        foreach (ModuleData module in modules)
        {
            ModuleRowView row = Instantiate(moduleRowPrefab, moduleRowContainer);
            row.Setup(module);
            rowMap[module.ModuleID] = row;
        }

        // Estado inicial del timer y los pips
        RefreshActiveTimer(modules);
        RefreshFailuresPips(modules);
    }

    // ── Handlers de eventos ───────────────────────────────────────────────────

    private void HandleTimerTick(ModuleData module)
    {
        // Actualizar fila de este módulo
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateProgress(module);

        // Si es el módulo activo, actualizar el bloque izquierdo también
        if (module.Status == ModuleStatus.Active)
            activeTimerView?.UpdateTimer(module);
    }

    private void HandleStateChanged(ModuleData module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips(InventoryManagerUI.Instance.GetAllModules());

        // Si cambia el módulo activo, actualizar el bloque de timer
        RefreshActiveTimer(InventoryManagerUI.Instance.GetAllModules());
    }

    private void HandleModuleExploded(ModuleData module)
    {
        if (rowMap.TryGetValue(module.ModuleID, out ModuleRowView row))
            row.UpdateStatus(module);

        RefreshFailuresPips(InventoryManagerUI.Instance.GetAllModules());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshActiveTimer(List<ModuleData> modules)
    {
        ModuleData active = InventoryManagerUI.Instance.GetActiveModule();
        activeTimerView?.UpdateTimer(active); // null = sin módulo activo → muestra "--:--"
    }

    private void RefreshFailuresPips(List<ModuleData> modules)
    {
        int explodedCount = InventoryManagerUI.Instance.GetExplodedCount();
        failuresPipsView?.SetFailures(explodedCount, modules.Count);
    }
}
