using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum SettingsTab { Brightness = 0, Controls = 1, Screen = 2, Volume = 3 }

/// <summary>
/// Tab selector of the Settings screen. Associates each button with a panel GameObject:
/// clicking a tab enables its panel and disables the others. It does the same with the
/// buttons' "active" visuals (optional, via _activeIndicators).
/// </summary>
public class SettingsTabSelector : MonoBehaviour
{
    [Serializable]
    public struct TabEntry
    {
        public Button button;
        public GameObject panel;
        [Tooltip("Optional: GameObject enabled only while this tab is selected (e.g. the wireframe's red bar).")]
        public GameObject activeIndicator;
    }

    [SerializeField] private List<TabEntry> _tabs = new List<TabEntry>();
    [SerializeField] private SettingsTab _initialTab = SettingsTab.Volume;

    public event Action<SettingsTab> OnTabChanged;

    public SettingsTab CurrentTab { get; private set; }

    private void Awake()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            int captured = i;
            if (_tabs[i].button != null)
                _tabs[i].button.onClick.AddListener(() => SelectTab((SettingsTab)captured));
        }
    }

    private void OnEnable()
    {
        SelectTab(_initialTab);
    }

    private void OnDestroy()
    {
        foreach (TabEntry tab in _tabs)
        {
            if (tab.button != null) tab.button.onClick.RemoveAllListeners();
        }
    }

    public void SelectTab(SettingsTab tab)
    {
        CurrentTab = tab;

        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == (int)tab;
            if (_tabs[i].panel != null)           _tabs[i].panel.SetActive(active);
            if (_tabs[i].activeIndicator != null) _tabs[i].activeIndicator.SetActive(active);
        }

        OnTabChanged?.Invoke(tab);
    }
}
