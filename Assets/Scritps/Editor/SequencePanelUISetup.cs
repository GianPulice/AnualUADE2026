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
/// Utilidad de editor: arma la UI del panel de secuencia en la escena LevelUI.
/// Idempotente — si ya existe, la borra y la rehace.
/// Menu: Tools / Puzzle UI / Setup Sequence Panel UI
/// </summary>
public static class SequencePanelUISetup
{
    private const string LevelUIScenePath = "Assets/Scenes/UI/LevelUI.unity";

    // ── Paleta ──────────────────────────────────────────────────────────────
    private static readonly Color ColorBackdrop      = new Color(0f, 0f, 0f, 0.80f);
    private static readonly Color ColorPanelBorder   = new Color(0.28f, 0.62f, 0.98f, 0.90f);
    private static readonly Color ColorPanelFill     = new Color(0.07f, 0.09f, 0.14f, 0.98f);
    private static readonly Color ColorAccent        = new Color(0.36f, 0.72f, 1f,    1f);
    private static readonly Color ColorText          = new Color(0.94f, 0.96f, 1f,    1f);
    private static readonly Color ColorTextMuted     = new Color(0.66f, 0.72f, 0.85f, 1f);
    private static readonly Color ColorSequenceOk    = new Color(0.50f, 0.95f, 0.60f, 1f);
    private static readonly Color ColorButtonFill    = new Color(0.11f, 0.14f, 0.22f, 1f);
    private static readonly Color ColorButtonOutline = new Color(0.28f, 0.62f, 0.98f, 0.55f);
    private static readonly Color ColorButtonHover   = new Color(0.75f, 0.85f, 1f,    1f);
    private static readonly Color ColorButtonPressed = new Color(0.50f, 0.70f, 0.90f, 1f);
    private static readonly Color ColorClose         = new Color(0.32f, 0.10f, 0.10f, 1f);
    private static readonly Color ColorCloseHover    = new Color(0.55f, 0.18f, 0.18f, 1f);
    private static readonly Color ColorDividerStrong = new Color(0.36f, 0.72f, 1f,    0.45f);
    private static readonly Color ColorDividerSoft   = new Color(0.36f, 0.72f, 1f,    0.15f);

    // ── Dimensiones (en el sistema de referencia 1920x1080) ─────────────────
    private const float PanelWidth        = 900f;
    private const float PanelHeight       = 820f;
    private const int   PadX              = 56;
    private const int   PadYTop           = 40;
    private const int   PadYBottom        = 40;
    private const int   VSpacing          = 22;
    private const float HeaderHeight      = 64f;
    private const float StatusHeight      = 40f;
    private const float SeqDisplayHeight  = 52f;
    private const float GridHeight        = 360f;
    private const float ButtonCell        = 150f;
    private const float ButtonSpacing     = 20f;

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

        // ── Panel (marco exterior) ─────────────────────────────────────────
        GameObject panelGO = New("Panel", canvasGO.transform, uiLayer, typeof(Image));
        panelGO.GetComponent<Image>().color = ColorPanelBorder;
        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRT.anchoredPosition = Vector2.zero;

        // Panel interior (2px offset del marco → simula borde)
        GameObject innerGO = New("PanelInner", panelGO.transform, uiLayer, typeof(Image), typeof(VerticalLayoutGroup));
        innerGO.GetComponent<Image>().color = ColorPanelFill;
        RectTransform innerRT = innerGO.GetComponent<RectTransform>();
        innerRT.anchorMin = Vector2.zero; innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(2, 2);
        innerRT.offsetMax = new Vector2(-2, -2);
        VerticalLayoutGroup vlg = innerGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding             = new RectOffset(PadX, PadX, PadYTop, PadYBottom);
        vlg.spacing             = VSpacing;
        vlg.childAlignment      = TextAnchor.UpperCenter;
        vlg.childControlWidth   = true;  vlg.childControlHeight   = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        // ── Header: Title + Close (anclado top-right) ──────────────────────
        GameObject headerGO = New("Header", innerGO.transform, uiLayer, typeof(LayoutElement));
        LayoutElement headerLE = headerGO.GetComponent<LayoutElement>();
        headerLE.minHeight = HeaderHeight; headerLE.preferredHeight = HeaderHeight; headerLE.flexibleHeight = 0f;

        GameObject titleGO = New("TitleText", headerGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(titleGO.GetComponent<RectTransform>());
        TextMeshProUGUI titleText = titleGO.GetComponent<TextMeshProUGUI>();
        titleText.text            = "PANEL ELECTRICO";
        titleText.fontSize        = 44;
        titleText.fontStyle       = FontStyles.Bold;
        titleText.alignment       = TextAlignmentOptions.Center;
        titleText.color           = ColorText;
        titleText.characterSpacing = 12f;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode     = TextOverflowModes.Ellipsis;

        GameObject closeGO = New("CloseButton", headerGO.transform, uiLayer, typeof(Image), typeof(Button));
        RectTransform closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1f, 0.5f);
        closeRT.anchorMax = new Vector2(1f, 0.5f);
        closeRT.pivot     = new Vector2(1f, 0.5f);
        closeRT.sizeDelta = new Vector2(44, 44);
        closeRT.anchoredPosition = new Vector2(0, 0);
        closeGO.GetComponent<Image>().color = ColorClose;
        Button closeBtn = closeGO.GetComponent<Button>();
        ColorBlock ccb = closeBtn.colors;
        ccb.normalColor      = Color.white;
        ccb.highlightedColor = ColorCloseHover;
        ccb.pressedColor     = new Color(0.4f, 0.12f, 0.12f, 1f);
        ccb.selectedColor    = Color.white;
        closeBtn.colors = ccb;

        GameObject closeLabelGO = New("Label", closeGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(closeLabelGO.GetComponent<RectTransform>());
        TextMeshProUGUI closeLabel = closeLabelGO.GetComponent<TextMeshProUGUI>();
        closeLabel.text      = "X";
        closeLabel.fontSize  = 22;
        closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.color     = Color.white;

        // ── Divisor superior ───────────────────────────────────────────────
        MakeDivider("DividerTop", innerGO.transform, uiLayer, ColorDividerStrong, 2);

        // ── Status ─────────────────────────────────────────────────────────
        GameObject statusGO = New("StatusText", innerGO.transform, uiLayer, typeof(TextMeshProUGUI), typeof(LayoutElement));
        LayoutElement statusLE = statusGO.GetComponent<LayoutElement>();
        statusLE.minHeight = StatusHeight; statusLE.preferredHeight = StatusHeight; statusLE.flexibleHeight = 0f;
        TextMeshProUGUI statusText = statusGO.GetComponent<TextMeshProUGUI>();
        statusText.text      = "Ingrese la secuencia";
        statusText.fontSize  = 22;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color     = ColorTextMuted;

        // ── Grid (contenedor centrado) ─────────────────────────────────────
        // Wrapper con HorizontalLayoutGroup para centrar el grid dentro del ancho total.
        GameObject gridWrapGO = New("GridWrapper", innerGO.transform, uiLayer,
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        LayoutElement wrapLE = gridWrapGO.GetComponent<LayoutElement>();
        wrapLE.minHeight = GridHeight; wrapLE.preferredHeight = GridHeight; wrapLE.flexibleHeight = 0f;
        HorizontalLayoutGroup hlg = gridWrapGO.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment       = TextAnchor.MiddleCenter;
        hlg.padding              = new RectOffset(0, 0, 0, 0);
        hlg.spacing              = 0;
        hlg.childControlWidth    = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        float gridW = ButtonCell * 4 + ButtonSpacing * 3;
        float gridH = ButtonCell * 2 + ButtonSpacing * 1;

        GameObject gridGO = New("ButtonsContainer", gridWrapGO.transform, uiLayer,
            typeof(GridLayoutGroup), typeof(LayoutElement));
        LayoutElement gridLE = gridGO.GetComponent<LayoutElement>();
        gridLE.minWidth  = gridW; gridLE.preferredWidth  = gridW;
        gridLE.minHeight = gridH; gridLE.preferredHeight = gridH;
        gridLE.flexibleWidth = 0f; gridLE.flexibleHeight = 0f;
        GridLayoutGroup grid = gridGO.GetComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(ButtonCell, ButtonCell);
        grid.spacing         = new Vector2(ButtonSpacing, ButtonSpacing);
        grid.padding         = new RectOffset(0, 0, 0, 0);
        grid.childAlignment  = TextAnchor.MiddleCenter;
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        RectTransform gridRT = gridGO.GetComponent<RectTransform>();
        gridRT.sizeDelta = new Vector2(gridW, gridH);

        // ── Button Template (inactivo, fuera del layout) ──────────────────
        GameObject btnTemplateGO = New("ButtonTemplate", canvasGO.transform, uiLayer, typeof(Image), typeof(Button), typeof(Outline));
        btnTemplateGO.SetActive(false);
        Image btnImg = btnTemplateGO.GetComponent<Image>();
        btnImg.color = ColorButtonFill;
        Outline btnOutline = btnTemplateGO.GetComponent<Outline>();
        btnOutline.effectColor    = ColorButtonOutline;
        btnOutline.effectDistance = new Vector2(2, -2);
        Button btnTemplate = btnTemplateGO.GetComponent<Button>();
        ColorBlock bcb = btnTemplate.colors;
        bcb.normalColor      = Color.white;
        bcb.highlightedColor = ColorButtonHover;
        bcb.pressedColor     = ColorButtonPressed;
        bcb.selectedColor    = Color.white;
        bcb.colorMultiplier  = 1f;
        btnTemplate.colors = bcb;

        GameObject btnLabelGO = New("Label", btnTemplateGO.transform, uiLayer, typeof(TextMeshProUGUI));
        Stretch(btnLabelGO.GetComponent<RectTransform>());
        TextMeshProUGUI btnLabel = btnLabelGO.GetComponent<TextMeshProUGUI>();
        btnLabel.text      = "1";
        btnLabel.fontSize  = 60;
        btnLabel.fontStyle = FontStyles.Bold;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color     = ColorText;

        // ── Divisor inferior ──────────────────────────────────────────────
        MakeDivider("DividerBottom", innerGO.transform, uiLayer, ColorDividerSoft, 1);

        // ── Sequence display ──────────────────────────────────────────────
        GameObject seqGO = New("SequenceDisplayText", innerGO.transform, uiLayer, typeof(TextMeshProUGUI), typeof(LayoutElement));
        LayoutElement seqLE = seqGO.GetComponent<LayoutElement>();
        seqLE.minHeight = SeqDisplayHeight; seqLE.preferredHeight = SeqDisplayHeight; seqLE.flexibleHeight = 0f;
        TextMeshProUGUI seqText = seqGO.GetComponent<TextMeshProUGUI>();
        seqText.text            = "Ingresada: —";
        seqText.fontSize        = 26;
        seqText.fontStyle       = FontStyles.Bold;
        seqText.alignment       = TextAlignmentOptions.Center;
        seqText.color           = ColorSequenceOk;
        seqText.characterSpacing = 6f;

        // ── View + refs ───────────────────────────────────────────────────
        SequencePanelView view = canvasGO.AddComponent<SequencePanelView>();
        SetPrivateField(view, "canvasGroup",         cg);
        SetPrivateField(view, "buttonsParent",       gridRT);
        SetPrivateField(view, "buttonPrefab",        btnTemplateGO.GetComponent<Button>());
        SetPrivateField(view, "titleText",           titleText);
        SetPrivateField(view, "sequenceDisplayText", seqText);
        SetPrivateField(view, "statusText",          statusText);
        SetPrivateField(view, "closeButton",         closeBtn);

        // ── Controller ────────────────────────────────────────────────────
        GameObject ctrlGO = new GameObject("SequencePanelUIController");
        SceneManager.MoveGameObjectToScene(ctrlGO, levelUI);
        SequencePanelUIController ctrl = ctrlGO.AddComponent<SequencePanelUIController>();
        SetPrivateField(ctrl, "view", view);

        canvasGO.SetActive(false);

        EditorSceneManager.MarkSceneDirty(levelUI);
        EditorSceneManager.SaveScene(levelUI);

        Debug.Log("[SequencePanelUISetup] UI armada y guardada en LevelUI.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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
        LayoutElement le = divGO.GetComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height; le.flexibleHeight = 0f;
    }

    private static void DestroyIfExists(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
    }

    /// <summary>
    /// Asigna un campo privado/protegido por reflection, buscando también en clases base.
    /// Necesario porque <c>canvasGroup</c> (en <c>BaseScreenView</c>) y <c>view</c>
    /// (en <c>BaseScreenController</c>) son protected y declarados en la base.
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
            Debug.LogError($"[SequencePanelUISetup] No se encontró el campo '{fieldName}' en {target.GetType().Name} ni en sus bases.");
            return;
        }

        field.SetValue(target, value);
    }
}
