#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Builds the module timer window into HUDCanvas: the active module's MM:SS inside a draining block
/// ring, its name, one pip per module and the time-jump popup — the top-left HUD from the "UI y
/// timer" sketch. See <see cref="ModuleTimerHUDView"/> for the behaviour.
///
/// ONE-SHOT TOOL, like <see cref="InteractionPromptWindowBuilder"/>: once the prefab is committed the
/// prefab is the source of truth and this file (with <see cref="UIBuildKit"/>) should be deleted.
/// Idempotent in the meantime — it finds its own nodes and reconfigures them.
/// </summary>
public static class ModuleTimerHUDBuilder
{
    private const string PrefabPath     = "Assets/_Project/Prefabs/UI/Canvas/HUDCanvas.prefab";
    private const string NormalTickPath = "Assets/_Project/Audio/SFX/Modules/sfx_modulo_tick_normal.mp3";
    private const string UrgentTickPath = "Assets/_Project/Audio/SFX/Modules/sfx_modulo_tick_urgente.mp3";

    private const string RootName = "ModuleTimerHUD";

    // Canvas units at the 1920x1080 reference. Anchored to the top-left corner at a fixed margin, so
    // it holds at every 16:9 resolution; the interaction prompt (left, above centre) starts ~135 px
    // below its bottom edge, and the alert / input hint are centred.
    private const float Margin         = 24f;
    private const float WindowWidth    = 300f;
    private const float WindowHeight   = 124f;
    private const float TitleBarHeight = 24f;

    private const float RingSize       = 88f;
    private const float RingX          = 10f;
    private const float RingThickness  = 8f;
    private const int   RingBlocks     = 30;   // one block = 1/30 of the timer (6 s of M2)
    private const float RingBlockGap   = 3f;

    private const float InfoX          = 110f; // left edge of the text column, inside the body
    private const float PipSize        = 18f;
    private const float PipStep        = 28f;
    private const int   PipCount       = 3;

    [MenuItem("Tools/UI/Module Timer HUD/Build")]
    public static void Build()
    {
        SO_UIThemeConfig theme = UIStyleTools.LoadRequired<SO_UIThemeConfig>(UIStyleTools.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();
        bool saved = UIStyleTools.EditPrefab(PrefabPath, report, root => BuildInto(root, theme, report));

        if (saved) Debug.Log($"[ModuleTimerHUD] {PrefabPath}\n{report}");
        else Debug.LogError($"[ModuleTimerHUD] NOT SAVED\n{report}");
    }

    private static bool BuildInto(GameObject canvasRoot, SO_UIThemeConfig theme, StringBuilder report)
    {
        // -- root: gate + view + beeper, stretched over the canvas so the window anchors to its corner
        RectTransform root = UIBuildKit.Ensure(RootName, canvasRoot.transform);
        UIStyleTools.Stretch(root);

        // Under the wake-up eyelids, the subtitles and the alerts (drawn later = on top), over the
        // vignettes: the lids must cover it and a system alert must never hide behind it.
        Transform lids = canvasRoot.transform.Find("WakeUpCinematic");
        if (lids != null && root.GetSiblingIndex() > lids.GetSiblingIndex())
            root.SetSiblingIndex(lids.GetSiblingIndex());

        CanvasGroup rootGroup = UIStyleTools.GetOrAdd<CanvasGroup>(root.gameObject);
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;
        rootGroup.alpha = 1f;

        ModalVisibilityGate gate = UIStyleTools.GetOrAdd<ModalVisibilityGate>(root.gameObject);
        SerializedObject gateSo = new SerializedObject(gate);
        UIBuildKit.Wire(gateSo, "canvasGroup", rootGroup, report);
        SerializedProperty ignored = gateSo.FindProperty("ignoredModalIds");
        ignored.arraySize = 1;
        // The skill check applies its penalties to this very timer: it has to stay readable over it.
        ignored.GetArrayElementAtIndex(0).stringValue = "SkillCheck";
        gateSo.ApplyModifiedPropertiesWithoutUndo();

        // -- window
        RectTransform window = UIBuildKit.Ensure("Window", root);
        UIBuildKit.Point(window, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         new Vector2(Margin, -Margin), new Vector2(WindowWidth, WindowHeight));
        Image surface = UIBuildKit.Fill(window, theme, UIThemeRole.SurfacePanel);
        // The animated grid of the sketch: same surface as the inventory's windows.
        surface.material = UIBuildKit.LoadAsset<Material>(UIStyleTools.SurfaceMaterialPath, report);

        CanvasGroup windowGroup = UIStyleTools.GetOrAdd<CanvasGroup>(window.gameObject);
        windowGroup.interactable = false;
        windowGroup.blocksRaycasts = false;

        UISlideTransition slide = UIStyleTools.GetOrAdd<UISlideTransition>(window.gameObject);
        SerializedObject slideSo = new SerializedObject(slide);
        UIBuildKit.Wire(slideSo, "canvasGroup", windowGroup, report);
        slideSo.FindProperty("slideDistance").floatValue = WindowWidth + Margin + 16f;
        slideSo.FindProperty("startHidden").boolValue = true;
        slideSo.FindProperty("initialHiddenDirection").enumValueIndex = (int)SlideDirection.FromLeft;
        slideSo.FindProperty("ignoreTimeScale").boolValue = true;
        slideSo.FindProperty("fadeWithSlide").boolValue = true;
        slideSo.FindProperty("autoHide").boolValue = false;
        slideSo.FindProperty("feedbackStyle").boolValue = false;
        slideSo.ApplyModifiedPropertiesWithoutUndo();

        UIBuildKit.TitleBar(window, theme, @"C:\WIRED\MODULE.SYS", TitleBarHeight, 14f);

        // -- body
        RectTransform body = UIBuildKit.Ensure("Body", window);
        UIBuildKit.Inset(body, 0f, 0f, 0f, TitleBarHeight);

        // Ring, with the time inside it. The pulse scales this node, not the window: the slide
        // cancels every tween on the window's own object.
        RectTransform ringRoot = UIBuildKit.Ensure("RingRoot", body);
        UIBuildKit.Point(ringRoot, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                         new Vector2(RingX + RingSize * 0.5f, 0f), new Vector2(RingSize, RingSize));

        RectTransform trackRect = UIBuildKit.Ensure("RingTrack", ringRoot);
        UIStyleTools.Stretch(trackRect);
        UIRingArc track = UIBuildKit.Ring(trackRect, RingThickness, RingBlocks, RingBlockGap);
        UIStyleTheme.Paint(track, theme, UIThemeRole.TextDisabled, false);

        RectTransform ringRect = UIBuildKit.Ensure("Ring", ringRoot);
        UIStyleTools.Stretch(ringRect);
        UIRingArc ring = UIBuildKit.Ring(ringRect, RingThickness, RingBlocks, RingBlockGap);
        ring.color = theme.TextSecondary; // runtime-driven: no applier

        RectTransform timerRect = UIBuildKit.Ensure("TimerText", ringRoot);
        UIStyleTools.Stretch(timerRect);
        TextMeshProUGUI timerText = UIBuildKit.Label(timerRect, "00:00", 20f, TextAlignmentOptions.Center, theme, null);

        // Text column
        RectTransform labelRect = UIBuildKit.Ensure("ModuleLabel", body);
        UIBuildKit.Point(labelRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InfoX, -12f), new Vector2(180f, 24f));
        TextMeshProUGUI moduleLabel = UIBuildKit.Label(labelRect, "M2 // CHEST", 18f, TextAlignmentOptions.MidlineLeft,
                                                       theme, UIThemeRole.TextPrimary);

        RectTransform statusRect = UIBuildKit.Ensure("StatusText", body);
        UIBuildKit.Point(statusRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InfoX, -38f), new Vector2(120f, 20f));
        TextMeshProUGUI statusText = UIBuildKit.Label(statusRect, "T-MINUS", 14f, TextAlignmentOptions.MidlineLeft, theme, null);
        statusText.color = theme.TextMuted;

        RectTransform pipsRect = UIBuildKit.Ensure("Pips", body);
        UIBuildKit.Point(pipsRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InfoX, -66f),
                         new Vector2(PipStep * (PipCount - 1) + PipSize, PipSize));

        Graphic[] pips = new Graphic[PipCount];
        for (int i = 0; i < PipCount; i++)
        {
            RectTransform pipRect = UIBuildKit.Ensure("Pip" + i, pipsRect);
            UIBuildKit.Point(pipRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * PipStep, 0f),
                             new Vector2(PipSize, PipSize));
            pips[i] = UIBuildKit.Plain(pipRect, theme.TextDisabled);
            UIBuildKit.Frame(pipRect, theme, Style.Sunken);
        }

        RectTransform deltaRect = UIBuildKit.Ensure("DeltaText", body);
        UIBuildKit.Point(deltaRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -36f), new Vector2(72f, 24f));
        TextMeshProUGUI deltaText = UIBuildKit.Label(deltaRect, "-5s", 20f, TextAlignmentOptions.MidlineRight, theme, null);
        deltaText.color = theme.Accent;

        // The window's frame goes on top of everything in it.
        UIBuildKit.Frame(window, theme, Style.Raised);

        // -- behaviour
        ModuleTimerBeeper beeper = UIStyleTools.GetOrAdd<ModuleTimerBeeper>(root.gameObject);
        SerializedObject beeperSo = new SerializedObject(beeper);
        UIBuildKit.Wire(beeperSo, "normalClip", UIBuildKit.LoadAsset<AudioClip>(NormalTickPath, report), report);
        UIBuildKit.Wire(beeperSo, "urgentClip", UIBuildKit.LoadAsset<AudioClip>(UrgentTickPath, report), report);
        beeperSo.ApplyModifiedPropertiesWithoutUndo();

        ModuleTimerHUDView view = UIStyleTools.GetOrAdd<ModuleTimerHUDView>(root.gameObject);
        SerializedObject viewSo = new SerializedObject(view);
        UIBuildKit.Wire(viewSo, "theme", theme, report);
        UIBuildKit.Wire(viewSo, "slide", slide, report);
        UIBuildKit.Wire(viewSo, "timerText", timerText, report);
        UIBuildKit.Wire(viewSo, "moduleLabel", moduleLabel, report);
        UIBuildKit.Wire(viewSo, "statusText", statusText, report);
        UIBuildKit.Wire(viewSo, "ring", ring, report);
        UIBuildKit.Wire(viewSo, "pulseTarget", ringRoot, report);
        UIBuildKit.WireArray(viewSo, "pips", pips, report);
        UIBuildKit.Wire(viewSo, "deltaText", deltaText, report);
        UIBuildKit.Wire(viewSo, "beeper", beeper, report);
        viewSo.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine($"  built '{RootName}': window {WindowWidth}x{WindowHeight} at ({Margin}, -{Margin}), " +
                          $"ring {RingBlocks} blocks, {PipCount} pips, beeper wired");
        return true;
    }
}
#endif
