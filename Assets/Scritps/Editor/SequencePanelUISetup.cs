using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor utility: builds the sequence panel UI in the LevelUI scene.
/// Menu: Tools / Puzzle UI / Setup Sequence Panel UI
///
/// Look: industrial security keypad — dark metal chassis, amber LCD display, square
/// keys with relief and a status LED. It deliberately does NOT use the diagonal sweep
/// (ButtonHoverSweepEffect) of the rest of the UI: that is menu language, not physical
/// keypad language.
///
/// ── WARNING ──────────────────────────────────────────────────────────────────
/// This script is DESTRUCTIVE: it deletes the existing canvas and rebuilds it from scratch.
/// Once the panel has been turned into a prefab and hand-tweaked, do NOT run it again or
/// that work is lost. It is meant to regenerate the base, not to iterate.
/// </summary>
public static class SequencePanelUISetup
{
    private const string LevelUIScenePath = "Assets/Scenes/UI/LevelUI.unity";

    // The same two fonts the rest of the game's canvases use.
    private const string FontMonoPath  = "Assets/Font/Share_Tech_Mono/ShareTechMono-Regular SDF.asset";
    private const string FontTitlePath = "Assets/Font/Oswald/static/Oswald-Regular SDF.asset";

    // ── Palette: dark metal + amber ─────────────────────────────────────────
    private static readonly Color ColorBackdrop      = new Color(0f,     0f,     0f,     0.85f);
    private static readonly Color ColorChassis       = new Color(0.13f,  0.13f,  0.14f,  1f);     // outer bezel
    private static readonly Color ColorPlate         = new Color(0.085f, 0.085f, 0.095f, 0.99f);  // inner plate
    private static readonly Color ColorAmber         = new Color(1f,     0.65f,  0.10f,  1f);
    private static readonly Color ColorAmberDim      = new Color(0.55f,  0.33f,  0.05f,  1f);
    private static readonly Color ColorText          = new Color(0.86f,  0.85f,  0.82f,  1f);
    private static readonly Color ColorTextMuted     = new Color(0.50f,  0.49f,  0.46f,  1f);
    private static readonly Color ColorLcdBackground = new Color(0.03f,  0.035f, 0.03f,  1f);
    private static readonly Color ColorLcdText       = new Color(1f,     0.72f,  0.20f,  1f);
    private static readonly Color ColorKeyFill       = new Color(0.18f,  0.18f,  0.19f,  1f);
    private static readonly Color ColorKeyBevel      = new Color(0.03f,  0.03f,  0.04f,  0.9f);
    private static readonly Color ColorKeyHoverTint  = new Color(1f,     0.88f,  0.65f,  1f);
    private static readonly Color ColorKeyPressTint  = new Color(0.65f,  0.45f,  0.18f,  1f);
    private static readonly Color ColorClose         = new Color(0.30f,  0.10f,  0.09f,  1f);
    private static readonly Color ColorCloseHover    = new Color(0.62f,  0.22f,  0.20f,  1f);
    private static readonly Color ColorDividerStrong = new Color(1f,     0.65f,  0.10f,  0.40f);

    // ── Dimensions (1920x1080 reference system) ─────────────────────────────
    private const float PanelWidth       = 520f;
    private const float PanelHeight      = 850f;
    private const int   BevelThickness   = 3;
    private const int   PadX             = 46;
    private const int   PadYTop          = 34;
    private const int   PadYBottom       = 34;
    private const int   VSpacing         = 20;
    private const float HeaderHeight     = 52f;
    private const float LcdHeight        = 78f;
    private const float StatusHeight     = 32f;
    private const float LedSize          = 14f;
    private const float ButtonCell       = 120f;
    private const float ButtonSpacing    = 14f;
    private const int   GridColumns      = 3;   // A keypad is 3 columns wide, not 4.
    private const int   KeypadRows       = 4;   // 1-9 in a square + the row holding the 0.

    [MenuItem("Tools/Puzzle UI/Setup Sequence Panel UI")]
    public static void Build()
    {
        Scene levelUI = SceneManager.GetSceneByName("LevelUI");
        if (!levelUI.IsValid() || !levelUI.isLoaded)
        {
            levelUI = EditorSceneManager.OpenScene(LevelUIScenePath, OpenSceneMode.Single);
        }
        SceneManager.SetActiveScene(levelUI);

        DestroyIfExists("SequencePanelCanvas");
        DestroyIfExists("SequencePanelUIController");

        int uiLayer = LayerMask.NameToLayer("UI");

        TMP_FontAsset fontMono  = LoadFont(FontMonoPath);
        TMP_FontAsset fontTitle = LoadFont(FontTitlePath);

        // ── Canvas root ────────────────────────────────────────────────────
        GameObject canvasGO = New("SequencePanelCanvas", null, uiLayer,
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        CanvasGroup cg = canvasGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
        SceneManager.MoveGameObjectToScene(canvasGO, levelUI);

        // ── Backdrop full-screen ───────────────────────────────────────────
        GameObject bgGO = New("Backdrop", canvasGO.transform, uiLayer, typeof(Image));
        Stretch(bgGO.GetComponent<RectTransform>());
        bgGO.GetComponent<Image>().color = ColorBackdrop;

        // ── Chassis (outer bezel) ──────────────────────────────────────────
        GameObject chassisGO = New("Chassis", canvasGO.transform, uiLayer, typeof(Image), typeof(Shadow));
        chassisGO.GetComponent<Image>().color = ColorChassis;
        Shadow chassisShadow = chassisGO.GetComponent<Shadow>();
        chassisShadow.effectColor    = new Color(0f, 0f, 0f, 0.65f);
        chassisShadow.effectDistance = new Vector2(6, -6);
        RectTransform chassisRT = chassisGO.GetComponent<RectTransform>();
        chassisRT.anchorMin = new Vector2(0.5f, 0.5f);
        chassisRT.anchorMax = new Vector2(0.5f, 0.5f);
        chassisRT.pivot     = new Vector2(0.5f, 0.5f);
        chassisRT.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        chassisRT.anchoredPosition = Vector2.zero;

        // Inner plate: the offset against the chassis is what draws the bevel.
        GameObject plateGO = New("Plate", chassisGO.transform, uiLayer, typeof(Image), typeof(VerticalLayoutGroup));
        plateGO.GetComponent<Image>().color = ColorPlate;
        RectTransform plateRT = plateGO.GetComponent<RectTransform>();
        plateRT.anchorMin = Vector2.zero; plateRT.anchorMax = Vector2.one;
        plateRT.offsetMin = new Vector2(BevelThickness, BevelThickness);
        plateRT.offsetMax = new Vector2(-BevelThickness, -BevelThickness);
        VerticalLayoutGroup vlg = plateGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(PadX, PadX, PadYTop, PadYBottom);
        vlg.spacing               = VSpacing;
        vlg.childAlignment        = TextAnchor.UpperCenter;
        vlg.childControlWidth     = true;  vlg.childControlHeight     = true;
        vlg.childForceExpandWidth = true;  vlg.childForceExpandHeight = false;

        // ── Header: title + close button ───────────────────────────────────
        GameObject headerGO = New("Header", plateGO.transform, uiLayer, typeof(LayoutElement));
        FixHeight(headerGO.GetComponent<LayoutElement>(), HeaderHeight);

        GameObject titleGO = New("TitleText", headerGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(titleGO.GetComponent<RectTransform>());
        TextMeshProUGUI titleText = titleGO.GetComponent<TextMeshProUGUI>();
        ApplyFont(titleText, fontTitle);
        titleText.text             = "ELECTRICAL PANEL";
        titleText.fontSize         = 30;
        titleText.fontStyle        = FontStyles.Bold;
        titleText.alignment        = TextAlignmentOptions.Center;
        titleText.color            = ColorText;
        titleText.characterSpacing = 10f;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode     = TextOverflowModes.Ellipsis;

        GameObject closeGO = New("CloseButton", headerGO.transform, uiLayer, typeof(Image), typeof(Button));
        RectTransform closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1f, 0.5f);
        closeRT.anchorMax = new Vector2(1f, 0.5f);
        closeRT.pivot     = new Vector2(1f, 0.5f);
        closeRT.sizeDelta = new Vector2(38, 38);
        closeRT.anchoredPosition = Vector2.zero;
        closeGO.GetComponent<Image>().color = ColorClose;
        Button closeBtn = closeGO.GetComponent<Button>();
        Tint(closeBtn, ColorCloseHover, new Color(0.45f, 0.14f, 0.13f, 1f));

        GameObject closeLabelGO = New("Label", closeGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(closeLabelGO.GetComponent<RectTransform>());
        TextMeshProUGUI closeLabel = closeLabelGO.GetComponent<TextMeshProUGUI>();
        ApplyFont(closeLabel, fontMono);
        closeLabel.text      = "X";
        closeLabel.fontSize  = 20;
        closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.color     = ColorText;

        MakeDivider("DividerTop", plateGO.transform, uiLayer, ColorDividerStrong, 2);

        // ── Display LCD ────────────────────────────────────────────────────
        GameObject lcdGO = New("Lcd", plateGO.transform, uiLayer, typeof(Image), typeof(LayoutElement));
        lcdGO.GetComponent<Image>().color = ColorLcdBackground;
        FixHeight(lcdGO.GetComponent<LayoutElement>(), LcdHeight);

        GameObject lcdBezelGO = New("LcdBezel", lcdGO.transform, uiLayer, typeof(Image));
        RectTransform lcdBezelRT = lcdBezelGO.GetComponent<RectTransform>();
        lcdBezelRT.anchorMin = Vector2.zero; lcdBezelRT.anchorMax = Vector2.one;
        lcdBezelRT.offsetMin = new Vector2(6, 6);
        lcdBezelRT.offsetMax = new Vector2(-6, -6);
        lcdBezelGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        GameObject seqGO = New("SequenceDisplayText", lcdGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(seqGO.GetComponent<RectTransform>());
        TextMeshProUGUI seqText = seqGO.GetComponent<TextMeshProUGUI>();
        ApplyFont(seqText, fontMono);
        seqText.text             = "> _";
        seqText.fontSize         = 40;
        seqText.alignment        = TextAlignmentOptions.Center;
        seqText.color            = ColorLcdText;
        seqText.characterSpacing = 10f;

        // ── Keypad ─────────────────────────────────────────────────────────
        // The wrapper centres the grid horizontally and absorbs the leftover height, so the
        // panel tolerates 6, 8, 9 or 12 keys without touching constants. The reserved height is
        // that of the keypad proper: 1-9 in a square plus the row where the 0 sits alone.
        GameObject gridWrapGO = New("GridWrapper", plateGO.transform, uiLayer,
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        LayoutElement wrapLE = gridWrapGO.GetComponent<LayoutElement>();
        wrapLE.minHeight = ButtonCell * KeypadRows + ButtonSpacing * (KeypadRows - 1);
        wrapLE.flexibleHeight = 1f;
        HorizontalLayoutGroup hlg = gridWrapGO.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.padding               = new RectOffset(0, 0, 0, 0);
        hlg.spacing               = 0;
        hlg.childControlWidth     = false; hlg.childControlHeight     = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        GameObject gridGO = New("ButtonsContainer", gridWrapGO.transform, uiLayer,
            typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        GridLayoutGroup grid = gridGO.GetComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(ButtonCell, ButtonCell);
        grid.spacing         = new Vector2(ButtonSpacing, ButtonSpacing);
        grid.padding         = new RectOffset(0, 0, 0, 0);
        grid.childAlignment  = TextAnchor.MiddleCenter;
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = GridColumns;
        // The fitter lets the grid grow on its own with however many keys the View
        // instantiates at runtime (the size used to be hardcoded for 4x2).
        ContentSizeFitter fitter = gridGO.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        RectTransform gridRT = gridGO.GetComponent<RectTransform>();

        // ── Status row: LED + text ─────────────────────────────────────────
        GameObject statusRowGO = New("StatusRow", plateGO.transform, uiLayer,
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        FixHeight(statusRowGO.GetComponent<LayoutElement>(), StatusHeight);
        HorizontalLayoutGroup statusHlg = statusRowGO.GetComponent<HorizontalLayoutGroup>();
        statusHlg.childAlignment        = TextAnchor.MiddleCenter;
        statusHlg.spacing               = 12;
        statusHlg.childControlWidth     = false; statusHlg.childControlHeight     = false;
        statusHlg.childForceExpandWidth = false; statusHlg.childForceExpandHeight = false;

        GameObject ledGO = New("StatusLed", statusRowGO.transform, uiLayer, typeof(Image), typeof(LayoutElement));
        Image ledImage = ledGO.GetComponent<Image>();
        ledImage.color = ColorAmberDim;
        LayoutElement ledLE = ledGO.GetComponent<LayoutElement>();
        ledLE.minWidth  = LedSize; ledLE.preferredWidth  = LedSize;
        ledLE.minHeight = LedSize; ledLE.preferredHeight = LedSize;
        ledGO.GetComponent<RectTransform>().sizeDelta = new Vector2(LedSize, LedSize);

        GameObject statusGO = New("StatusText", statusRowGO.transform, uiLayer,
            typeof(TextMeshProUGUI), typeof(LayoutElement));
        LayoutElement statusLE = statusGO.GetComponent<LayoutElement>();
        statusLE.minWidth = 320f; statusLE.preferredWidth = 320f;
        statusLE.minHeight = StatusHeight; statusLE.preferredHeight = StatusHeight;
        TextMeshProUGUI statusText = statusGO.GetComponent<TextMeshProUGUI>();
        ApplyFont(statusText, fontMono);
        statusText.text             = "ENTER THE SEQUENCE";
        statusText.fontSize         = 16;
        statusText.alignment        = TextAlignmentOptions.Left;
        statusText.color            = ColorTextMuted;
        statusText.characterSpacing = 4f;

        // ── Key template (inactive, outside the layout) ────────────────────
        GameObject btnTemplateGO = New("ButtonTemplate", canvasGO.transform, uiLayer,
            typeof(Image), typeof(Button), typeof(Shadow));
        btnTemplateGO.SetActive(false);
        btnTemplateGO.GetComponent<Image>().color = ColorKeyFill;
        // Bottom-right shadow: physical key relief. Replaces the blue Outline.
        Shadow keyBevel = btnTemplateGO.GetComponent<Shadow>();
        keyBevel.effectColor    = ColorKeyBevel;
        keyBevel.effectDistance = new Vector2(3, -3);
        Tint(btnTemplateGO.GetComponent<Button>(), ColorKeyHoverTint, ColorKeyPressTint);

        GameObject btnLabelGO = New("Label", btnTemplateGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(btnLabelGO.GetComponent<RectTransform>());
        TextMeshProUGUI btnLabel = btnLabelGO.GetComponent<TextMeshProUGUI>();
        ApplyFont(btnLabel, fontMono);
        btnLabel.text      = "1";
        btnLabel.fontSize  = 54;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color     = ColorText;

        // ── View + refs ───────────────────────────────────────────────────
        SequencePanelView view = canvasGO.AddComponent<SequencePanelView>();
        SetPrivateField(view, "canvasGroup",         cg);
        SetPrivateField(view, "buttonsParent",       gridRT);
        SetPrivateField(view, "buttonPrefab",        btnTemplateGO.GetComponent<Button>());
        SetPrivateField(view, "titleText",           titleText);
        SetPrivateField(view, "sequenceDisplayText", seqText);
        SetPrivateField(view, "statusText",          statusText);
        SetPrivateField(view, "statusLed",           ledImage);
        SetPrivateField(view, "closeButton",         closeBtn);

        // ── Controller ────────────────────────────────────────────────────
        GameObject ctrlGO = new GameObject("SequencePanelUIController");
        SceneManager.MoveGameObjectToScene(ctrlGO, levelUI);
        SequencePanelUIController ctrl = ctrlGO.AddComponent<SequencePanelUIController>();
        SetPrivateField(ctrl, "view", view);

        canvasGO.SetActive(false);

        EditorSceneManager.MarkSceneDirty(levelUI);
        EditorSceneManager.SaveScene(levelUI);

        Debug.Log("[SequencePanelUISetup] Keypad built and saved in LevelUI. " +
                  "Turn it into a prefab before tweaking it — running this again destroys it.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TMP_FontAsset LoadFont(string path)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
            Debug.LogWarning($"[SequencePanelUISetup] Font '{path}' was not found. " +
                             "The text will fall back to TMP's default (LiberationSans).");
        return font;
    }

    private static void ApplyFont(TextMeshProUGUI text, TMP_FontAsset font)
    {
        if (font != null) text.font = font;
    }

    /// <summary>ColorBlock with a white normal: the tint multiplies the Image's color.</summary>
    private static void Tint(Button button, Color highlighted, Color pressed)
    {
        ColorBlock cb = button.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = highlighted;
        cb.pressedColor     = pressed;
        cb.selectedColor    = Color.white;
        cb.colorMultiplier  = 1f;
        button.colors = cb;
    }

    private static void FixHeight(LayoutElement le, float height)
    {
        le.minHeight = height; le.preferredHeight = height; le.flexibleHeight = 0f;
    }

    private static GameObject New(string name, Transform parent, int layer, params Type[] components)
    {
        var list = new List<Type> { typeof(RectTransform) };
        foreach (Type t in components)
            if (t != typeof(RectTransform)) list.Add(t);

        GameObject go = new GameObject(name, list.ToArray());
        go.layer = layer;
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void MakeDivider(string name, Transform parent, int layer, Color color, float height)
    {
        GameObject divGO = New(name, parent, layer, typeof(Image), typeof(LayoutElement));
        divGO.GetComponent<Image>().color = color;
        FixHeight(divGO.GetComponent<LayoutElement>(), height);
    }

    private static void DestroyIfExists(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
    }

    /// <summary>
    /// Assigns a private/protected field by reflection, also searching in base classes.
    /// Needed because <c>canvasGroup</c> (in <c>BaseScreenView</c>) and <c>view</c>
    /// (in <c>BaseScreenController</c>) are protected and declared in the base.
    /// </summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        Type type = target.GetType();
        FieldInfo field = null;
        while (type != null && field == null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        if (field == null)
        {
            Debug.LogError($"[SequencePanelUISetup] Field '{fieldName}' was not found in {target.GetType().Name} nor in its bases.");
            return;
        }

        field.SetValue(target, value);
    }
}
