using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Line = SO_ArchitectLineBank.Line;

/// <summary>
/// One-click setup for the Architect voice system:
///   - creates SO_ArchitectLineBank filled with the final text of architect_voice_system_spec v1.2;
///   - adds to HUDCanvas.prefab the controller, the subtitle (bottom centre) and the general alert
///     (top centre).
///
/// Idempotent: an existing bank is never overwritten (only lines it lacks are added), and nodes
/// already on the HUD are left as they are.
/// </summary>
public static class ArchitectVoiceSetup
{
    private const string BankPath = "Assets/_Project/ScriptableObjects/Architect/SO_ArchitectLineBank.asset";
    private const string HudPath = "Assets/_Project/Prefabs/UI/Canvas/HUDCanvas.prefab";
    private const string FontPath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF.asset";
    private const string FontOutlinePath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF - Outline.mat";

    private const string ControllerName = "ArchitectVoice";
    private const string SubtitleName = "ArchitectSubtitle";
    private const string AlertName = "HUDAlert";

    private static readonly Color SubtitleGrey = new Color32(0xAA, 0xAA, 0xAA, 0xFF);   // Spec §1.3.

    [MenuItem("Tools/Architect/Setup (line bank + HUD)")]
    public static void Setup()
    {
        SO_ArchitectLineBank bank = EnsureBank();
        WireHud(bank);
        AssetDatabase.SaveAssets();
        Debug.Log("[ArchitectVoiceSetup] Done.");
    }

    // ── Wake-up cinematic ────────────────────────────────────────────────────────────────

    private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";
    private const string CinematicName = "WakeUpCinematic";
    private const string PlayerCameraName = "FreeLook Camera";

    /// <summary>
    /// Adds the wake-up cinematic: the eyes overlay on HUDCanvas (below the subtitle), the camera
    /// pan on the Player's FreeLook Camera, the crosshair hidden until control comes back (LevelUI),
    /// and the page break of ARC_01a in the bank.
    /// Idempotent, like <see cref="Setup"/>.
    /// </summary>
    [MenuItem("Tools/Architect/Setup Wake-Up Cinematic")]
    public static void SetupWakeUpCinematic()
    {
        AddWakeUpPageBreak();
        WireCinematicHud();
        WireWakeUpConfigAndSkipPrompt();
        WireCameraPan();
        WireCrosshair();
        AssetDatabase.SaveAssets();
        Debug.Log("[ArchitectVoiceSetup] Wake-up cinematic done.");
    }

    private static void AddWakeUpPageBreak()
    {
        SO_ArchitectLineBank bank = AssetDatabase.LoadAssetAtPath<SO_ArchitectLineBank>(BankPath);
        Line line = bank != null ? bank.Find(ArchitectLineID.WakeUpMoment1) : null;
        if (line == null || line.variants.Length == 0) return;

        string text = line.variants[0];
        if (text.IndexOf(ArchitectLinePages.Separator) >= 0) return;

        int firstSentence = text.IndexOf(". ", StringComparison.Ordinal);
        if (firstSentence < 0) return;

        line.variants[0] = text.Substring(0, firstSentence + 1) + " " + ArchitectLinePages.Separator +
                           text.Substring(firstSentence + 1);
        EditorUtility.SetDirty(bank);
    }

    private static void WireCinematicHud()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            if (root.transform.Find(CinematicName) != null) return;

            RectTransform overlay = NewRect(CinematicName, root.transform);
            Stretch(overlay);
            CanvasGroup overlayGroup = AddGroup(overlay.gameObject);
            overlayGroup.alpha = 0f;

            // Under the subtitle, over everything else: the first sentence is read on this black.
            Transform subtitle = root.transform.Find(SubtitleName);
            if (subtitle != null) overlay.SetSiblingIndex(subtitle.GetSiblingIndex());

            // The lids overlap a few pixels past the middle, so there is no seam while closed.
            RectTransform top = NewBlack("TopLid", overlay);
            top.anchorMin = new Vector2(0f, 0.5f);
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(0f, -4f);
            top.offsetMax = Vector2.zero;

            RectTransform bottom = NewBlack("BottomLid", overlay);
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = new Vector2(1f, 0.5f);
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0f, 4f);

            RectTransform dim = NewBlack("Dim", overlay);
            Stretch(dim);
            CanvasGroup dimGroup = AddGroup(dim.gameObject);

            WakeUpCinematicView view = overlay.gameObject.AddComponent<WakeUpCinematicView>();
            Set(view, "overlayGroup", overlayGroup);
            Set(view, "topLid", top);
            Set(view, "bottomLid", bottom);
            Set(view, "dimGroup", dimGroup);

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private const string WakeUpConfigPath = "Assets/_Project/ScriptableObjects/Architect/SO_WakeUpCinematicConfig.asset";
    private const string SkipPromptName = "WakeUpSkipPrompt";

    /// <summary>
    /// Creates SO_WakeUpCinematicConfig, assigns it to the controller if it has none, and adds the
    /// "[Press F to skip]" text at the bottom right of HUDCanvas, as the last child so it draws over
    /// the cinematic's black.
    /// </summary>
    private static void WireWakeUpConfigAndSkipPrompt()
    {
        EnsureFolder("Assets/_Project/ScriptableObjects/Architect");
        SO_WakeUpCinematicConfig config = AssetDatabase.LoadAssetAtPath<SO_WakeUpCinematicConfig>(WakeUpConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SO_WakeUpCinematicConfig>();
            AssetDatabase.CreateAsset(config, WakeUpConfigPath);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            bool changed = false;

            ArchitectVoiceController controller = root.GetComponentInChildren<ArchitectVoiceController>(true);
            if (controller == null)
            {
                Debug.LogWarning($"[ArchitectVoiceSetup] No {nameof(ArchitectVoiceController)} in {HudPath}. Run Tools ▸ Architect ▸ Setup first.");
            }
            else if (controller.WakeUpConfig == null)
            {
                Set(controller, "wakeUpConfig", config);
                changed = true;
            }

            if (root.transform.Find(SkipPromptName) == null)
            {
                BuildSkipPrompt(root.transform, config);
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildSkipPrompt(Transform parent, SO_WakeUpCinematicConfig config)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        SO_UIThemeConfig theme = AssetDatabase.LoadAssetAtPath<SO_UIThemeConfig>(UIStyleTools.ThemePath);

        // Root: bottom right, fixed size (a one-line box never scales in width), clear of the subtitle.
        RectTransform root = NewRect(SkipPromptName, parent);
        root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-40f, 40f);
        root.sizeDelta = new Vector2(320f, 44f);
        root.SetAsLastSibling();
        AddGroup(root.gameObject).alpha = 0f;

        // Just the text, loose on screen: no panel behind it.
        RectTransform labelRect = NewRect("Label", root);
        Stretch(labelRect);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(-16f, 0f);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(label, font, null, 24f, SubtitleGrey);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.alignment = TextAlignmentOptions.Center;
        label.text = config != null ? config.SkipPromptText : "[Press F to skip]";
        Theme(label, theme, UIThemeRole.TextSecondary);

        WakeUpSkipPromptView view = root.gameObject.AddComponent<WakeUpSkipPromptView>();
        Set(view, "label", label);
    }

    private static RectTransform NewBlack(string name, Transform parent)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return rect;
    }

    private const string LevelUIPath = "Assets/_Project/Scenes/UI/LevelUI.unity";
    private const string CrosshairName = "Crosshair";

    /// <summary>
    /// Hides the crosshair (LevelUI, canvas sorted at 1000, above the cinematic's black) until the
    /// player gets control back. Opens LevelUI additively if it is not loaded, and only closes it
    /// again if it was this tool that opened it.
    /// </summary>
    private static void WireCrosshair()
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneByPath(LevelUIPath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(LevelUIPath, OpenSceneMode.Additive);

        try
        {
            GameObject crosshair = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == CrosshairName) { crosshair = t.gameObject; break; }
                if (crosshair != null) break;
            }

            if (crosshair == null)
            {
                Debug.LogWarning($"[ArchitectVoiceSetup] No '{CrosshairName}' in {LevelUIPath}. Add {nameof(WakeUpCinematicHidden)} to it by hand.");
                return;
            }
            if (crosshair.GetComponent<WakeUpCinematicHidden>() != null) return;

            crosshair.AddComponent<WakeUpCinematicHidden>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void WireCameraPan()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Transform rig = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == PlayerCameraName) { rig = t; break; }

            if (rig == null)
            {
                Debug.LogWarning($"[ArchitectVoiceSetup] No '{PlayerCameraName}' in {PlayerPath}. Add {nameof(WakeUpCameraPan)} by hand.");
                return;
            }
            bool changed = false;
            if (rig.GetComponent<WakeUpCameraPan>() == null)
            {
                rig.gameObject.AddComponent<WakeUpCameraPan>();
                changed = true;
            }
            // The camera shot of the checkpoint stand-up after a capture.
            if (rig.GetComponent<CaptureStandUpCameraPan>() == null)
            {
                rig.gameObject.AddComponent<CaptureStandUpCameraPan>();
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── Line bank ────────────────────────────────────────────────────────────────────────

    private static SO_ArchitectLineBank EnsureBank()
    {
        EnsureFolder("Assets/_Project/ScriptableObjects/Architect");

        SO_ArchitectLineBank bank = AssetDatabase.LoadAssetAtPath<SO_ArchitectLineBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SO_ArchitectLineBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }

        int added = 0;
        foreach (Line line in SpecLines())
        {
            if (bank.Find(line.id) != null) continue;
            bank.EditableLines.Add(line);
            added++;
        }

        EditorUtility.SetDirty(bank);
        Debug.Log($"[ArchitectVoiceSetup] Line bank: {added} line(s) added.");
        return bank;
    }

    private static Line L(ArchitectLineID id, ArchitectLineCategory category, string alert, bool overMenus,
                          params string[] variants) =>
        new Line { id = id, category = category, variants = variants, clips = Array.Empty<AudioClip>(),
                   alert = alert, playsOverMenus = overMenus };

    /// <summary>
    /// Final recording script, spec §2-§5. Do not rewrite. The only addition is the subtitle page
    /// break ('|') in ARC_01a, which the wake-up cinematic syncs to and is never shown or read.
    /// </summary>
    private static IEnumerable<Line> SpecLines()
    {
        const ArchitectLineCategory M = ArchitectLineCategory.Mandatory;
        const ArchitectLineCategory C = ArchitectLineCategory.Context;
        const ArchitectLineCategory K = ArchitectLineCategory.Conditional;
        const ArchitectLineCategory R = ArchitectLineCategory.Random;

        yield return L(ArchitectLineID.WakeUpMoment1, M, "", false,
            "There's a device on your body. | Three modules. One timer at a time. When it hits zero, it goes off.");
        yield return L(ArchitectLineID.WakeUpMoment2, M, "", false,
            "The rules are simple: solve it, disarm it, get out.");
        yield return L(ArchitectLineID.NemesisReleased, M, "> CONTAINMENT BREACH", false,
            "You're not alone in the complex anymore. And there's more than time to worry about now.");
        yield return L(ArchitectLineID.GameEnd, M, "<color=#2EB847>> {0} DISARMED</color>", true,
            "You made it. You're the first.");

        yield return L(ArchitectLineID.ContextZone1, C, "", false,
            "Industrial sector. Three parts of the system to bring back online. Once all three are up, you can move on.");
        yield return L(ArchitectLineID.ContextZone2, C, "", false,
            "Down here you'll have to think a little harder before it goes off.");
        yield return L(ArchitectLineID.ContextCentral1, C, "<color=#2EB847>> M1 DISARMED</color>", false,
            "Module disarmed. The gate is open. The complex goes on.");
        yield return L(ArchitectLineID.ContextCentral2, C, "<color=#2EB847>> M2 DISARMED</color>", false,
            "Two modules down. One left.");
        yield return L(ArchitectLineID.ContextFirstNote, C, "", false,
            "Someone was here before you. They left something for whoever came next.");

        yield return L(ArchitectLineID.TimerCritical, K, "> {0} CRITICAL", false,
            "You're running out of time.", "Almost.", "Tick.");
        yield return L(ArchitectLineID.ExplodedLegs, K, "> {0} DETONATED", false,
            "The legs. Every step from now on will remind you.");
        yield return L(ArchitectLineID.ExplodedChest, K, "> {0} DETONATED", false,
            "The chest. Breathing will cost you more than it used to.");
        yield return L(ArchitectLineID.ExplodedHead, K, "> {0} DETONATED", false,
            "The head. What you see won't always be real from now on.");
        yield return L(ArchitectLineID.ResolvedInTime, K, "", false,
            "You made it in time.", "Good.");
        yield return L(ArchitectLineID.Captured, K, "", false,
            "Too close.",
            "That was a mistake.",
            "You moved when you shouldn't have.",
            "Predictable.",
            "Obvious from the moment you walked into that room.",
            "It doesn't miss.",
            "It always gets there.",
            "It's been doing this a long time.",
            "This has happened before.",
            "Plenty made that same call.",
            "That's usually how it ends.",
            "Curious. Not where I expected.",
            "Did you see it coming?",
            "This is part of the process.",
            "Interesting. Almost.");
        yield return L(ArchitectLineID.GameOver, K, "", true,
            "You got this far. That's already more than most.");

        yield return L(ArchitectLineID.Idle, R, "", false,
            // 5.1 On the Nemesis
            "It's close. Very close.",
            "It can hear you.",
            "Don't move.",
            "It already knows you're there.",
            "That sound you made was a mistake.",
            "It's two rooms away.",
            "Don't look at it. It won't help.",
            "Still.",
            "It doesn't like loud noises.",
            "It learns the routes. Change yours.",
            "It doesn't tolerate being ignored.",
            "If you lose sight of it, it doesn't lose sight of you.",
            "It doesn't run. It doesn't need to.",
            // 5.2 The complex and previous victims
            "You're not the first to stand there.",
            "Others got this far. None of them made it past that point.",
            "This place has a memory.",
            // 5.3 The player
            "You're taking longer than expected.",
            "Interesting. That wasn't the obvious route.",
            "I'm watching you.",
            "Notice you're breathing differently? That's normal.",
            // 5.4 The device
            "The device is working perfectly.",
            "That sound you hear is the timer. You'll get used to it.",
            "When it goes off, it doesn't hurt right away.");
    }

    // ── HUD ──────────────────────────────────────────────────────────────────────────────

    private static void WireHud(SO_ArchitectLineBank bank)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Material fontOutline = AssetDatabase.LoadAssetAtPath<Material>(FontOutlinePath);
        SO_UIThemeConfig theme = AssetDatabase.LoadAssetAtPath<SO_UIThemeConfig>(UIStyleTools.ThemePath);

        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            if (root.transform.Find(ControllerName) == null) BuildController(root.transform, bank);
            if (root.transform.Find(SubtitleName) == null) BuildSubtitle(root.transform, font, fontOutline);
            if (root.transform.Find(AlertName) == null) BuildAlert(root.transform, font, fontOutline, theme);

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildController(Transform parent, SO_ArchitectLineBank bank)
    {
        GameObject go = new GameObject(ControllerName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ArchitectVoiceController controller = go.AddComponent<ArchitectVoiceController>();
        Set(controller, "bank", bank);
    }

    private static void BuildSubtitle(Transform parent, TMP_FontAsset font, Material outline)
    {
        // Root: bottom centre, the menu gate.
        RectTransform root = NewRect(SubtitleName, parent);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 90f);
        root.sizeDelta = new Vector2(1500f, 150f);
        CanvasGroup gate = AddGroup(root.gameObject);

        // Text: the fade and the typing.
        RectTransform textRect = NewRect("Text", root);
        Stretch(textRect);
        CanvasGroup fade = AddGroup(textRect.gameObject);
        fade.alpha = 0f;

        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(text, font, outline, 34f, SubtitleGrey);
        text.alignment = TextAlignmentOptions.Bottom;
        text.text = string.Empty;

        TMPTypewriterReveal typewriter = textRect.gameObject.AddComponent<TMPTypewriterReveal>();
        Set(typewriter, "charactersPerSecond", 38f);
        Set(typewriter, "maxDuration", 6f);     // Dialogue keeps its pace; only very long lines speed up.
        Set(typewriter, "delay", 0.1f);

        ArchitectSubtitleView view = root.gameObject.AddComponent<ArchitectSubtitleView>();
        Set(view, "visibilityGroup", gate);
        Set(view, "fadeGroup", fade);
        Set(view, "label", text);
        Set(view, "typewriter", typewriter);
    }

    private static void BuildAlert(Transform parent, TMP_FontAsset font, Material outline, SO_UIThemeConfig theme)
    {
        // Root: top centre, hidden with the rest of the HUD while a menu is open.
        RectTransform root = NewRect(AlertName, parent);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -60f);
        root.sizeDelta = new Vector2(760f, 64f);
        CanvasGroup gate = AddGroup(root.gameObject);
        ModalVisibilityGate modalGate = root.gameObject.AddComponent<ModalVisibilityGate>();
        Set(modalGate, "canvasGroup", gate);

        // Panel: the part that drops in.
        RectTransform panelRect = NewRect("Panel", root);
        Stretch(panelRect);
        CanvasGroup panelGroup = AddGroup(panelRect.gameObject);
        Image panel = panelRect.gameObject.AddComponent<Image>();
        panel.raycastTarget = false;
        Theme(panel, theme, UIThemeRole.AccentBgDeep);

        UISlideTransition slide = panelRect.gameObject.AddComponent<UISlideTransition>();
        Set(slide, "canvasGroup", panelGroup);
        Set(slide, "startHidden", true);
        SetEnum(slide, "initialHiddenDirection", (int)SlideDirection.FromTop);
        Set(slide, "ignoreTimeScale", true);
        Set(slide, "fadeWithSlide", true);
        Set(slide, "autoHide", false);

        // Thin accent line under the text, so it reads as a system banner and not a loose label.
        RectTransform lineRect = NewRect("AccentLine", panelRect);
        lineRect.anchorMin = new Vector2(0f, 0f);
        lineRect.anchorMax = new Vector2(1f, 0f);
        lineRect.pivot = new Vector2(0.5f, 0f);
        lineRect.sizeDelta = new Vector2(0f, 3f);
        Image line = lineRect.gameObject.AddComponent<Image>();
        line.raycastTarget = false;
        Theme(line, theme, UIThemeRole.AccentBorder);

        RectTransform labelRect = NewRect("Label", panelRect);
        Stretch(labelRect);
        labelRect.offsetMin = new Vector2(24f, 0f);
        labelRect.offsetMax = new Vector2(-24f, 0f);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(label, font, outline, 30f, Color.white);
        label.alignment = TextAlignmentOptions.Center;
        label.text = string.Empty;
        Theme(label, theme, UIThemeRole.Accent);

        TMPTypewriterReveal typewriter = labelRect.gameObject.AddComponent<TMPTypewriterReveal>();

        HUDAlertView view = root.gameObject.AddComponent<HUDAlertView>();
        Set(view, "slide", slide);
        Set(view, "label", label);
        Set(view, "typewriter", typewriter);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static CanvasGroup AddGroup(GameObject go)
    {
        CanvasGroup group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    private static void StyleText(TextMeshProUGUI text, TMP_FontAsset font, Material outline, float size, Color color)
    {
        if (font != null) text.font = font;
        if (outline != null) text.fontSharedMaterial = outline;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
    }

    private static void Theme(Graphic graphic, SO_UIThemeConfig theme, UIThemeRole role)
    {
        if (theme == null) return;
        UIThemeApplier applier = graphic.gameObject.AddComponent<UIThemeApplier>();
        Set(applier, "theme", theme);
        SetEnum(applier, "role", (int)role);
        applier.Apply();
    }

    private static void Set(UnityEngine.Object target, string property, UnityEngine.Object value)
    {
        SerializedObject so = new SerializedObject(target);
        Require(so, property).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(UnityEngine.Object target, string property, float value)
    {
        SerializedObject so = new SerializedObject(target);
        Require(so, property).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(UnityEngine.Object target, string property, bool value)
    {
        SerializedObject so = new SerializedObject(target);
        Require(so, property).boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEnum(UnityEngine.Object target, string property, int index)
    {
        SerializedObject so = new SerializedObject(target);
        Require(so, property).enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SerializedProperty Require(SerializedObject so, string property)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
            throw new InvalidOperationException($"'{so.targetObject.GetType().Name}' has no serialized field '{property}'.");
        return prop;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
