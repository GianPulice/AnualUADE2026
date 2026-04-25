using System.Collections.Generic;
using UnityEngine;

public class ModuleView : MonoBehaviour
{
    [Header("Módulo activo (panel circular izquierdo)")]
    [SerializeField] private ActiveModuleDisplay activeModuleDisplay;

    [Header("Filas de módulos")]
    [SerializeField] private Transform moduleRowContainer;
    [SerializeField] private ModuleRowView moduleRowPrefab;

    [Header("Contador de fallos")]
    [SerializeField] private FailuresCounterView failuresCounter;

    private Dictionary<string, ModuleRowView> moduleRowMap = new Dictionary<string, ModuleRowView>();

    //------------------ Unity ------------------

    void OnEnable()
    {
        InventoryEvents.OnModuleTimerTick     += HandleTimerTick;
        InventoryEvents.OnModuleStatusChanged += HandleStatusChanged;
        InventoryEvents.OnModuleFailure       += HandleModuleFailure;
    }

    void OnDisable()
    {
        InventoryEvents.OnModuleTimerTick     -= HandleTimerTick;
        InventoryEvents.OnModuleStatusChanged -= HandleStatusChanged;
        InventoryEvents.OnModuleFailure       -= HandleModuleFailure;
    }

    // ------------------ Initialize Modules ------------------

    public void Initialize(List<ModuleData> modules)
    {
        moduleRowMap.Clear();

        foreach (ModuleData module in modules)
        {
            ModuleRowView row = Instantiate(moduleRowPrefab, moduleRowContainer);
            row.Setup(module);
            moduleRowMap[module.moduleID] = row;
        }

        // Mostrar el módulo activo (el primero por defecto)
        if (modules.Count > 0)
        {
            activeModuleDisplay?.UpdateDisplay(modules[0]);
        }

        RefreshFailuresCount(modules);
    }

    // -- Handlers de eventos ------------------

    private void HandleTimerTick(ModuleData module)
    {
        if (moduleRowMap.TryGetValue(module.moduleID, out ModuleRowView row))
        {
            row.UpdateTimer(module);
        }

        // Actualizar display circular del módulo activo si corresponde
        activeModuleDisplay?.UpdateDisplay(module);
    }

    private void HandleStatusChanged(ModuleData module)
    {
        if (moduleRowMap.TryGetValue(module.moduleID, out ModuleRowView row))
        {
            row.UpdateStatus(module);
        }

        RefreshFailuresCount(InventoryManagerUI.Instance.GetAllModules());
    }

    private void HandleModuleFailure(ModuleData module)
    {
        if (moduleRowMap.TryGetValue(module.moduleID, out ModuleRowView row))
        {
            row.PlayFailureAnimation();
        }
    }

    // -- Helpers ------------------

    private void RefreshFailuresCount(List<ModuleData> modules)
    {
        int total = 0;
        foreach (ModuleData m in modules)
        {
            total += m.failuresCount;
        }
        //Agregar cambio de color relacionado al failureCount 
        failuresCounter?.SetCount(total);
    }
}
