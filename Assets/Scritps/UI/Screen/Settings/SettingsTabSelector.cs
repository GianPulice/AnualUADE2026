using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum SettingsTab { Brightness = 0, Controls = 1, Screen = 2, Volume = 3 }

/// <summary>
/// Tab selector of the Settings screen. Associates each button with a panel GameObject:
/// clicking a tab enables its panel and disables the others. It does the same with the
/// buttons' "active" visuals (optional, via _activeIndicators).
///
/// The last selected tab is remembered across sessions. That key lives here rather than in
/// SettingsModel on purpose: the tab is navigation state, not a preference. Routing it through
/// the model would drag it into the snapshot/revert cycle, and then leaving Settings with Back
/// would revert the tab as well as the sliders -- which is the bug this was added to fix.
/// </summary>
public class SettingsTabSelector : MonoBehaviour
{
    private const string KEY_LAST_TAB = "Settings_LastTab";

    [Serializable]
    public struct TabEntry
    {
        public Button button;
        public GameObject panel;
        [Tooltip("Optional: GameObject enabled only while this tab is selected (e.g. the wireframe's red bar).")]
        public GameObject activeIndicator;
    }

    [SerializeField] private List<TabEntry> _tabs = new List<TabEntry>();

    [Tooltip("Tab opened the very first time, before the player has ever switched tabs. " +
             "After that the last used tab wins -- see rememberLastTab.")]
    [SerializeField] private SettingsTab _initialTab = SettingsTab.Volume;

    [Tooltip("Reopen Settings on whichever tab was last used. Turn off to always open on Initial Tab.")]
    [SerializeField] private bool _rememberLastTab = true;

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
        SelectTab(ResolveStartingTab());
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

        Remember(tab);
        OnTabChanged?.Invoke(tab);
    }

    /// <summary>
    /// The stored tab, or <see cref="_initialTab"/> when there is nothing stored, remembering is
    /// off, or the stored index no longer names a tab that exists. That last case is the one
    /// worth guarding: dropping a tab from _tabs would otherwise leave a stale index in
    /// PlayerPrefs that opens Settings on no panel at all.
    /// </summary>
    private SettingsTab ResolveStartingTab()
    {
        if (!_rememberLastTab) return _initialTab;

        int stored = PlayerPrefs.GetInt(KEY_LAST_TAB, (int)_initialTab);
        if (stored < 0 || stored >= _tabs.Count) return _initialTab;

        return (SettingsTab)stored;
    }

    /// <summary>
    /// Persists immediately rather than at Apply: switching tabs and closing with Back has to
    /// keep the tab. Only writes on an actual change, so PlayerPrefs.Save's disk hit does not
    /// happen on the SelectTab that OnEnable itself issues.
    /// </summary>
    private void Remember(SettingsTab tab)
    {
        if (!_rememberLastTab) return;
        if (PlayerPrefs.GetInt(KEY_LAST_TAB, -1) == (int)tab) return;

        PlayerPrefs.SetInt(KEY_LAST_TAB, (int)tab);
        PlayerPrefs.Save();
    }
}
