#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Builds the hiding HUD into HUDCanvas: the per-type overlay (<see cref="HidingOverlayView"/>) under
/// the rest of the HUD, and the breath meter window (<see cref="BreathHoldMeterView"/>) in the
/// bottom-left corner.
///
/// ONE-SHOT TOOL, like <see cref="ModuleTimerHUDBuilder"/>: once the prefab is committed the prefab
/// is the source of truth. Idempotent in the meantime — it finds its own nodes and reconfigures them.
/// </summary>
public static class HidingHUDBuilder
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Canvas/HUDCanvas.prefab";

    private const string OverlayName = "HidingOverlay";
    private const string MeterName   = "BreathMeter";

    // Canvas units at the 1920x1080 reference. Bottom-left: the subtitles own the bottom centre and
    // the wake-up skip prompt the bottom right.
    private const float Margin       = 24f;
    private const float WindowWidth  = 300f;
    private const float WindowHeight = 72f;
    private const float Padding      = 12f;
    private const int   PipCount     = 10;
    private const float PipWidth     = 24f;
    private const float PipHeight    = 16f;

    [MenuItem("Tools/UI/Hiding HUD/Build")]
    public static void Build()
    {
        SO_UIThemeConfig theme = UIStyleTools.LoadRequired<SO_UIThemeConfig>(UIStyleTools.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();
        bool saved = UIStyleTools.EditPrefab(PrefabPath, report, root => BuildInto(root, theme, report));

        if (saved) Debug.Log($"[HidingHUD] {PrefabPath}\n{report}");
        else Debug.LogError($"[HidingHUD] NOT SAVED\n{report}");
    }

    private static bool BuildInto(GameObject canvasRoot, SO_UIThemeConfig theme, StringBuilder report)
    {
        BuildOverlay(canvasRoot.transform, report);
        BuildMeter(canvasRoot.transform, theme, report);
        return true;
    }

    private static void BuildOverlay(Transform canvas, StringBuilder report)
    {
        RectTransform overlay = UIBuildKit.Ensure(OverlayName, canvas);
        UIStyleTools.Stretch(overlay);
        UIStyleTools.GetOrAdd<HidingOverlayView>(overlay.gameObject);

        // Under the vignettes and everything after them: the proximity vignette still has to read
        // over the locker's slats.
        Transform vignette = canvas.Find("ProximittyVignette");
        if (vignette != null) overlay.SetSiblingIndex(vignette.GetSiblingIndex());

        report.AppendLine($"  built '{OverlayName}' at sibling {overlay.GetSiblingIndex()}");
    }

    private static void BuildMeter(Transform canvas, SO_UIThemeConfig theme, StringBuilder report)
    {
        RectTransform root = UIBuildKit.Ensure(MeterName, canvas);
        UIStyleTools.Stretch(root);

        // Same layer as the module timer: under the wake-up lids and the alerts.
        Transform lids = canvas.Find("WakeUpCinematic");
        if (lids != null && root.GetSiblingIndex() > lids.GetSiblingIndex())
            root.SetSiblingIndex(lids.GetSiblingIndex());

        CanvasGroup rootGroup = UIStyleTools.GetOrAdd<CanvasGroup>(root.gameObject);
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;
        rootGroup.alpha = 1f;

        ModalVisibilityGate gate = UIStyleTools.GetOrAdd<ModalVisibilityGate>(root.gameObject);
        SerializedObject gateSo = new SerializedObject(gate);
        UIBuildKit.Wire(gateSo, "canvasGroup", rootGroup, report);
        gateSo.ApplyModifiedPropertiesWithoutUndo();

        // -- window
        RectTransform window = UIBuildKit.Ensure("Window", root);
        UIBuildKit.Point(window, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin),
                         new Vector2(WindowWidth, WindowHeight));
        Image surface = UIBuildKit.Fill(window, theme, UIThemeRole.SurfacePanel);
        surface.material = UIBuildKit.LoadAsset<Material>(UIStyleTools.SurfaceMaterialPath, report);

        CanvasGroup windowGroup = UIStyleTools.GetOrAdd<CanvasGroup>(window.gameObject);
        windowGroup.interactable = false;
        windowGroup.blocksRaycasts = false;
        windowGroup.alpha = 0f;

        RectTransform labelRect = UIBuildKit.Ensure("Label", window);
        UIBuildKit.Point(labelRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -8f), new Vector2(110f, 22f));
        UIBuildKit.Label(labelRect, "BREATH", 16f, TextAlignmentOptions.MidlineLeft, theme, UIThemeRole.TextPrimary, title: true);

        RectTransform statusRect = UIBuildKit.Ensure("StatusText", window);
        UIBuildKit.Point(statusRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Padding, -8f), new Vector2(170f, 22f));
        TextMeshProUGUI statusText = UIBuildKit.Label(statusRect, "[F] HOLD BREATH", 13f, TextAlignmentOptions.MidlineRight, theme, null);
        statusText.color = theme.TextSecondary;

        float rowWidth = WindowWidth - Padding * 2f;
        float step = (rowWidth - PipWidth) / (PipCount - 1);

        RectTransform pipsRect = UIBuildKit.Ensure("Pips", window);
        UIBuildKit.Point(pipsRect, Vector2.zero, Vector2.zero, new Vector2(Padding, Padding), new Vector2(rowWidth, PipHeight));

        Graphic[] pips = new Graphic[PipCount];
        for (int i = 0; i < PipCount; i++)
        {
            RectTransform pipRect = UIBuildKit.Ensure("Pip" + i, pipsRect);
            UIBuildKit.Point(pipRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * step, 0f),
                             new Vector2(PipWidth, PipHeight));
            pips[i] = UIBuildKit.Plain(pipRect, theme.TextSecondary);
            UIBuildKit.Frame(pipRect, theme, Style.Sunken);
        }

        UIBuildKit.Frame(window, theme, Style.Raised);

        // -- behaviour
        BreathHoldMeterView view = UIStyleTools.GetOrAdd<BreathHoldMeterView>(root.gameObject);
        SerializedObject viewSo = new SerializedObject(view);
        UIBuildKit.Wire(viewSo, "theme", theme, report);
        UIBuildKit.Wire(viewSo, "windowGroup", windowGroup, report);
        UIBuildKit.Wire(viewSo, "statusText", statusText, report);
        UIBuildKit.WireArray(viewSo, "pips", pips, report);
        viewSo.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine($"  built '{MeterName}': window {WindowWidth}x{WindowHeight} at ({Margin}, {Margin}), {PipCount} pips");
    }
}
#endif
