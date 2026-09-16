#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Builds the Win95 window the interaction prompt lives in, and the style profile that keeps it in
/// the theme.
///
/// The prompt was the last UI in the project still on its first look: a bare TMP line over a grey
/// translucent box, with no frame, no themed font and no profile. This turns it into the same kind
/// of window as everything else — title bar, bevelled frame, key cap, icon well — which
/// <see cref="InteractionPromptView"/> then dresses differently per message kind.
///
/// ONE-SHOT TOOL. Once the prefab is committed, the prefab is the source of truth and this file
/// should be deleted: docs/TODO-UI.md already records how the SequencePanel setup script rotted
/// against later refactors. It is idempotent in the meantime — it finds its own earlier work and
/// reconfigures it, so running twice changes nothing.
/// </summary>
public static class InteractionPromptWindowBuilder
{
    private const string PrefabPath  = "Assets/_Project/Prefabs/UI/Inventory/InteractionCanvas.prefab";
    private const string ProfilePath = "Assets/_Project/ScriptableObjects/UI/Style/UIStyle_InteractionCanvas.asset";

    // The window is a fixed size: every slot position below is a constant against it, which is what
    // lets the view move the message by plain insets instead of a layout group.
    private const float WindowWidth    = 560f;
    private const float WindowHeight   = 96f;
    private const float TitleBarHeight = 24f;
    private const float KeyCapX        = 12f;
    private const float KeyCapWidth    = 36f;
    private const float KeyCapHeight   = 32f;
    private const float IconWellX      = 56f;
    private const float IconWellSize   = 40f;
    private const float MessageInset   = 60f;   // textInsetBase + keySlotWidth on the view
    private const float MessageRight   = 16f;

    // Kept from the prefab as authored: the prompt sits on the left, above centre. Only the X gets a
    // margin, because a bevelled window flush against the screen edge reads as half off-screen.
    private const float PromptX = 24f;

    [MenuItem("Tools/UI/Interaction Prompt/Rebuild Window")]
    public static void Rebuild()
    {
        SO_UIThemeConfig theme = UIStyleTools.LoadRequired<SO_UIThemeConfig>(UIStyleTools.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();

        bool saved = UIStyleTools.EditPrefab(PrefabPath, report, root => BuildWindow(root, theme, report));
        if (!saved)
        {
            Debug.LogError($"[InteractionPrompt] NOT SAVED\n{report}");
            return;
        }

        Debug.Log($"[InteractionPrompt] {PrefabPath}\n{report}");

        SO_UIStyleProfile profile = EnsureProfile();
        if (profile != null) UIStyleTools.Apply(new[] { profile });
    }

    // -- Prefab -------------------

    private static bool BuildWindow(GameObject root, SO_UIThemeConfig theme, StringBuilder report)
    {
        Transform promptRoot = root.transform.Find("PromptRoot");
        if (promptRoot == null)
        {
            report.AppendLine("  MISSING 'PromptRoot' — nothing was changed.");
            return false;
        }

        TextMeshProUGUI message = FindByName<TextMeshProUGUI>(root.transform, "InteractionMessageText");
        if (message == null)
        {
            report.AppendLine("  MISSING 'InteractionMessageText' — nothing was changed.");
            return false;
        }

        RectTransform promptRect = (RectTransform)promptRoot;
        promptRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
        promptRect.anchoredPosition = new Vector2(PromptX, promptRect.anchoredPosition.y);

        // -- window + title bar
        RectTransform window = Ensure("Window", promptRoot);
        UIStyleTools.Stretch(window);
        Paint(window, theme.SurfacePanel);

        RectTransform titleBar = Ensure("TitleBar", window);
        Top(titleBar, TitleBarHeight);
        Paint(titleBar, theme.SurfaceFooter);

        RectTransform titleText = Ensure("TitleText", titleBar);
        UIStyleTools.Stretch(titleText);
        titleText.offsetMin = new Vector2(8f, 0f);
        titleText.offsetMax = new Vector2(-56f, 0f);
        TextMeshProUGUI titleLabel = Label(titleText, @"C:\WIRED\INTERACT.EXE", 14f,
                                           TextAlignmentOptions.MidlineLeft, theme.TextSecondary);

        // Decorative only: the two caps of a Win95 title bar. They are not Buttons, so the frames
        // step will not try to give them press feedback.
        RectTransform minimise = TitleCap("TitleButtonMin", titleBar, -30f, theme);
        RectTransform close    = TitleCap("TitleButtonClose", titleBar, -8f, theme);

        // -- body
        RectTransform body = Ensure("Body", window);
        UIStyleTools.Stretch(body);
        body.offsetMax = new Vector2(0f, -TitleBarHeight);

        RectTransform keyCap = Ensure("KeyCap", body);
        LeftMiddle(keyCap, KeyCapX, KeyCapWidth, KeyCapHeight);
        Paint(keyCap, theme.SurfaceRaised);

        RectTransform keyLabel = Ensure("KeyLabel", keyCap);
        UIStyleTools.Stretch(keyLabel);
        Label(keyLabel, "E", 18f, TextAlignmentOptions.Center, theme.TextPrimary);

        RectTransform iconWell = Ensure("IconWell", body);
        LeftMiddle(iconWell, IconWellX, IconWellSize, IconWellSize);
        Paint(iconWell, theme.SurfaceScreen);

        RectTransform iconRect = Ensure("IconImage", iconWell);
        UIStyleTools.Stretch(iconRect);
        iconRect.offsetMin = new Vector2(5f, 5f);
        iconRect.offsetMax = new Vector2(-5f, -5f);
        Image iconImage = Paint(iconRect, Color.white);
        iconImage.preserveAspect = true;

        RectTransform glyphRect = Ensure("GlyphLabel", iconWell);
        UIStyleTools.Stretch(glyphRect);
        TextMeshProUGUI glyphLabel = Label(glyphRect, "!", 22f, TextAlignmentOptions.Center, theme.TextPrimary);

        // -- the message itself
        message.transform.SetParent(body, false);
        RectTransform messageRect = message.rectTransform;
        UIStyleTools.Stretch(messageRect);
        messageRect.offsetMin = new Vector2(MessageInset, 6f);
        messageRect.offsetMax = new Vector2(-MessageRight, -6f);
        message.alignment = TextAlignmentOptions.MidlineLeft;
        message.enableAutoSizing = false;
        message.fontSize = 26f;
        message.overflowMode = TextOverflowModes.Ellipsis;
        message.raycastTarget = false;
        message.richText = true;

        TMPTypewriterReveal typewriter = UIStyleTools.GetOrAdd<TMPTypewriterReveal>(message.gameObject);

        // The old flat background, now replaced by the window. Removed last: the message used to
        // live inside it.
        Transform legacyBackground = promptRoot.Find("Image");
        if (legacyBackground != null)
        {
            Object.DestroyImmediate(legacyBackground.gameObject);
            report.AppendLine("  removed the old flat background 'Image'");
        }

        // -- wiring
        InteractionPromptView view = root.GetComponentInChildren<InteractionPromptView>(true);
        if (view == null)
        {
            report.AppendLine("  MISSING InteractionPromptView — nothing was changed.");
            return false;
        }

        SerializedObject serialized = new SerializedObject(view);
        Wire(serialized, "theme", theme, report);
        Wire(serialized, "titleBar", titleBar.GetComponent<Image>(), report);
        Wire(serialized, "titleText", titleLabel, report);
        Wire(serialized, "keyCapRoot", keyCap.gameObject, report);
        Wire(serialized, "iconWell", iconWell.gameObject, report);
        Wire(serialized, "iconImage", iconImage, report);
        Wire(serialized, "glyphLabel", glyphLabel, report);
        Wire(serialized, "promptText", message, report);
        Wire(serialized, "typewriter", typewriter, report);
        Wire(serialized, "slide", promptRoot.GetComponent<UISlideTransition>(), report);

        // Written out rather than left to the field initialisers: those only run when the component
        // is first created, and this one already exists in the prefab — a newly added field would
        // otherwise come back deserialized with the class defaults instead of this look.
        WriteVariant(serialized, "commonVariant", @"C:\WIRED\INTERACT.EXE",
                     UIThemeRole.SurfaceFooter, UIThemeRole.TextSecondary,
                     showKey: true, blinkCursor: true, SlideDirection.FromBottom, report);
        WriteVariant(serialized, "itemVariant", @"C:\WIRED\ITEM.DAT",
                     UIThemeRole.SurfaceFooter, UIThemeRole.TextSecondary,
                     showKey: true, blinkCursor: true, SlideDirection.FromBottom, report);
        WriteVariant(serialized, "globalVariant", @"C:\WIRED\SYSTEM.MSG",
                     UIThemeRole.BevelLight, UIThemeRole.SurfaceScreen,
                     showKey: false, blinkCursor: false, SlideDirection.FromLeft, report);

        serialized.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine($"  window rebuilt: {minimise.name}, {close.name}, key cap, icon well, message");
        return true;
    }

    // -- Profile -------------------

    /// <summary>
    /// Creates the profile if it is missing and (re)writes its entries. TitleBar and TitleText are
    /// deliberately absent from the theme list: the view paints them per message kind, and a
    /// UIThemeApplier would repaint them on enable.
    /// </summary>
    private static SO_UIStyleProfile EnsureProfile()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[InteractionPrompt] No prefab at {PrefabPath}.");
            return null;
        }

        SO_UIStyleProfile profile = AssetDatabase.LoadAssetAtPath<SO_UIStyleProfile>(ProfilePath);
        bool created = profile == null;
        if (created) profile = ScriptableObject.CreateInstance<SO_UIStyleProfile>();

        const string window   = "PromptRoot/Window";
        const string titleBar = window + "/TitleBar";
        const string body     = window + "/Body";
        const string keyCap   = body + "/KeyCap";
        const string iconWell = body + "/IconWell";

        profile.prefab = prefab;
        profile.revertNestedStyleOverrides = false;

        profile.theme = new List<SO_UIStyleProfile.ThemeEntry>
        {
            Theme(window, UIThemeRole.SurfacePanel),
            Theme(keyCap, UIThemeRole.SurfaceRaised),
            Theme(keyCap + "/KeyLabel", UIThemeRole.TextPrimary),
            Theme(iconWell, UIThemeRole.SurfaceScreen),
            Theme(iconWell + "/GlyphLabel", UIThemeRole.TextPrimary),
            Theme(body + "/InteractionMessageText", UIThemeRole.TextPrimary),
            Theme(titleBar + "/TitleButtonMin", UIThemeRole.SurfaceRaised),
            Theme(titleBar + "/TitleButtonClose", UIThemeRole.SurfaceRaised),
        };

        profile.flatFills = new List<string>
        {
            window, titleBar, keyCap, iconWell,
            titleBar + "/TitleButtonMin", titleBar + "/TitleButtonClose",
        };

        profile.frames = new List<SO_UIStyleProfile.FrameEntry>
        {
            Frame(window, Style.Raised),
            Frame(keyCap, Style.Raised),
            Frame(iconWell, Style.Sunken),
            Frame(titleBar + "/TitleButtonMin", Style.Raised),
            Frame(titleBar + "/TitleButtonClose", Style.Raised),
        };

        // The well holds an icon, not a laid-out label, so it needs no padding of its own.
        profile.minWellPadding = 0;

        // Everything monospaced: a terminal has one face. titleTexts stays empty on purpose — it is
        // what would put Oswald on the title bar.
        profile.restyleTexts = true;
        profile.titleTexts = new List<string>();
        profile.untouchedTexts = new List<string>();

        profile.surfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(UIStyleTools.SurfaceMaterialPath);
        profile.animatedSurfaces = new List<string> { window };

        profile.signalPanels = new List<SO_UIStyleProfile.SignalPanelEntry>();
        profile.crt = new SO_UIStyleProfile.CRTSettings { enabled = false };

        if (created)
        {
            UIStyleTools.EnsureFolder(UIStyleTools.ProfileFolder);
            AssetDatabase.CreateAsset(profile, ProfilePath);
            Debug.Log($"[InteractionPrompt] Created {ProfilePath}");
        }
        else
        {
            EditorUtility.SetDirty(profile);
        }

        AssetDatabase.SaveAssets();
        return profile;
    }

    private static SO_UIStyleProfile.ThemeEntry Theme(string path, UIThemeRole role) =>
        new SO_UIStyleProfile.ThemeEntry { path = path, role = role, preserveAlpha = false };

    private static SO_UIStyleProfile.FrameEntry Frame(string path, Style style) =>
        new SO_UIStyleProfile.FrameEntry { path = path, style = style, pressable = false };

    // -- Helpers -------------------

    private static RectTransform Ensure(string name, Transform parent)
    {
        Transform existing = parent.Find(name);
        return existing != null ? (RectTransform)existing : UIStyleTools.CreateUIChild(name, parent);
    }

    /// <summary>A bar pinned to the top edge, full width.</summary>
    private static void Top(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    /// <summary>A fixed-size slot against the left edge, vertically centred.</summary>
    private static void LeftMiddle(RectTransform rt, float x, float width, float height)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.localScale = Vector3.one;
    }

    private static RectTransform TitleCap(string name, Transform titleBar, float x, SO_UIThemeConfig theme)
    {
        RectTransform rt = Ensure(name, titleBar);
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(18f, 14f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.localScale = Vector3.one;
        Paint(rt, theme.SurfaceRaised);
        return rt;
    }

    private static Image Paint(RectTransform rt, Color color)
    {
        Image image = UIStyleTools.GetOrAdd<Image>(rt.gameObject);
        image.color = color;
        image.sprite = null;
        // The prompt is a HUD overlay on the highest canvas in the project: nothing in it should
        // ever eat a click meant for the UI underneath.
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Label(RectTransform rt, string text, float size,
                                         TextAlignmentOptions alignment, Color color)
    {
        TextMeshProUGUI label = UIStyleTools.GetOrAdd<TextMeshProUGUI>(rt.gameObject);
        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = color;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        return label;
    }

    private static T FindByName<T>(Transform root, string name) where T : Component
    {
        foreach (T candidate in root.GetComponentsInChildren<T>(true))
            if (candidate.name == name) return candidate;
        return null;
    }

    /// <summary>Fills one of the view's three PromptVariant blocks.</summary>
    private static void WriteVariant(SerializedObject serialized, string field, string title,
                                     UIThemeRole titleBarRole, UIThemeRole titleTextRole,
                                     bool showKey, bool blinkCursor, SlideDirection enter,
                                     StringBuilder report)
    {
        SerializedProperty variant = serialized.FindProperty(field);
        if (variant == null)
        {
            report.AppendLine($"  NO FIELD '{field}' on InteractionPromptView — left at its defaults.");
            return;
        }

        variant.FindPropertyRelative("title").stringValue = title;
        variant.FindPropertyRelative("titleBarRole").enumValueIndex = (int)titleBarRole;
        variant.FindPropertyRelative("titleTextRole").enumValueIndex = (int)titleTextRole;
        variant.FindPropertyRelative("showKey").boolValue = showKey;
        variant.FindPropertyRelative("blinkCursor").boolValue = blinkCursor;
        variant.FindPropertyRelative("enterDirection").enumValueIndex = (int)enter;
    }

    private static void Wire(SerializedObject serialized, string field, Object value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            report.AppendLine($"  NO FIELD '{field}' on InteractionPromptView — left unwired.");
            return;
        }
        property.objectReferenceValue = value;
    }
}
#endif
