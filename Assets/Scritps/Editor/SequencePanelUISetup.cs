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

        // ---- Canvas root ----
        GameObject canvasGO = new GameObject("SequencePanelCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGO.layer = uiLayer;
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        CanvasGroup cg = canvasGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
        SceneManager.MoveGameObjectToScene(canvasGO, levelUI);

        // ---- Background ----
        GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.layer = uiLayer;
        bgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        bgGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // ---- Panel central ----
        GameObject panelGO = new GameObject("Panel",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panelGO.layer = uiLayer;
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(760, 720);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.16f, 0.98f);
        VerticalLayoutGroup vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 36, 36);
        vlg.spacing = 18;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        // ---- Close button (absoluto, top-right del panel) ----
        GameObject closeGO = new GameObject("CloseButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        closeGO.layer = uiLayer;
        closeGO.transform.SetParent(panelGO.transform, false);
        RectTransform closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1f, 1f);
        closeRT.anchorMax = new Vector2(1f, 1f);
        closeRT.pivot = new Vector2(1f, 1f);
        closeRT.sizeDelta = new Vector2(36, 36);
        closeRT.anchoredPosition = new Vector2(-12, -12);
        closeGO.GetComponent<Image>().color = new Color(0.32f, 0.10f, 0.10f, 1f);
        Button closeBtn = closeGO.GetComponent<Button>();
        ColorBlock closeColors = closeBtn.colors;
        closeColors.highlightedColor = new Color(0.55f, 0.18f, 0.18f, 1f);
        closeColors.pressedColor = new Color(0.4f, 0.12f, 0.12f, 1f);
        closeBtn.colors = closeColors;
        LayoutElement closeIgnore = closeGO.AddComponent<LayoutElement>();
        closeIgnore.ignoreLayout = true;
        GameObject closeLabelGO = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        closeLabelGO.layer = uiLayer;
        closeLabelGO.transform.SetParent(closeGO.transform, false);
        RectTransform clRT = closeLabelGO.GetComponent<RectTransform>();
        clRT.anchorMin = Vector2.zero; clRT.anchorMax = Vector2.one;
        clRT.offsetMin = Vector2.zero; clRT.offsetMax = Vector2.zero;
        TextMeshProUGUI closeLabel = closeLabelGO.GetComponent<TextMeshProUGUI>();
        closeLabel.text = "X"; closeLabel.fontSize = 20; closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.color = Color.white;

        // ---- Title (centrado, fila propia) ----
        GameObject titleGO = new GameObject("TitleText",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        titleGO.layer = uiLayer;
        titleGO.transform.SetParent(panelGO.transform, false);
        TextMeshProUGUI titleText = titleGO.GetComponent<TextMeshProUGUI>();
        titleText.text = "PANEL ELECTRICO";
        titleText.fontSize = 38;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(0.92f, 0.92f, 1f, 1f);
        titleText.characterSpacing = 8f;
        LayoutElement titleLE = titleGO.GetComponent<LayoutElement>();
        titleLE.minHeight = 50; titleLE.preferredHeight = 50;

        // ---- Divisor (linea fina debajo del titulo) ----
        GameObject divGO = new GameObject("Divider",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        divGO.layer = uiLayer;
        divGO.transform.SetParent(panelGO.transform, false);
        divGO.GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.6f, 0.5f);
        LayoutElement divLE = divGO.GetComponent<LayoutElement>();
        divLE.minHeight = 1; divLE.preferredHeight = 1;

        // ---- Status ----
        GameObject statusGO = new GameObject("StatusText",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        statusGO.layer = uiLayer;
        statusGO.transform.SetParent(panelGO.transform, false);
        LayoutElement statusLE = statusGO.GetComponent<LayoutElement>();
        statusLE.minHeight = 32; statusLE.preferredHeight = 32;
        TextMeshProUGUI statusText = statusGO.GetComponent<TextMeshProUGUI>();
        statusText.text = "Ingrese la secuencia";
        statusText.fontSize = 20;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = new Color(0.78f, 0.82f, 0.95f, 1f);

        // ---- Grid container ----
        GameObject gridGO = new GameObject("ButtonsContainer",
            typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        gridGO.layer = uiLayer;
        gridGO.transform.SetParent(panelGO.transform, false);
        LayoutElement gridLE = gridGO.GetComponent<LayoutElement>();
        gridLE.minHeight = 420; gridLE.preferredHeight = 420;
        GridLayoutGroup grid = gridGO.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(140, 140);
        grid.spacing = new Vector2(16, 16);
        grid.padding = new RectOffset(20, 20, 20, 20);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        RectTransform gridRT = gridGO.GetComponent<RectTransform>();

        // ---- Button template (inactivo, fuera del grid) ----
        GameObject btnTemplateGO = new GameObject("ButtonTemplate",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnTemplateGO.layer = uiLayer;
        btnTemplateGO.transform.SetParent(canvasGO.transform, false);
        btnTemplateGO.SetActive(false);
        Image btnImg = btnTemplateGO.GetComponent<Image>();
        btnImg.color = new Color(0.13f, 0.14f, 0.20f, 1f);
        Button btnTemplate = btnTemplateGO.GetComponent<Button>();
        ColorBlock btnColors = btnTemplate.colors;
        btnColors.normalColor = new Color(1f, 1f, 1f, 1f);
        btnColors.highlightedColor = new Color(0.75f, 0.85f, 1f, 1f);
        btnColors.pressedColor = new Color(0.6f, 0.75f, 0.9f, 1f);
        btnColors.colorMultiplier = 1f;
        btnTemplate.colors = btnColors;
        GameObject btnLabelGO = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        btnLabelGO.layer = uiLayer;
        btnLabelGO.transform.SetParent(btnTemplateGO.transform, false);
        RectTransform btnLabelRT = btnLabelGO.GetComponent<RectTransform>();
        btnLabelRT.anchorMin = Vector2.zero; btnLabelRT.anchorMax = Vector2.one;
        btnLabelRT.offsetMin = Vector2.zero; btnLabelRT.offsetMax = Vector2.zero;
        TextMeshProUGUI btnLabel = btnLabelGO.GetComponent<TextMeshProUGUI>();
        btnLabel.text = "1"; btnLabel.fontSize = 48; btnLabel.fontStyle = FontStyles.Bold;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color = new Color(0.92f, 0.92f, 0.95f, 1f);

        // ---- Sequence display ----
        GameObject seqGO = new GameObject("SequenceDisplayText",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        seqGO.layer = uiLayer;
        seqGO.transform.SetParent(panelGO.transform, false);
        LayoutElement seqLE = seqGO.GetComponent<LayoutElement>();
        seqLE.minHeight = 40; seqLE.preferredHeight = 40;
        TextMeshProUGUI seqText = seqGO.GetComponent<TextMeshProUGUI>();
        seqText.text = "Ingresada: —";
        seqText.fontSize = 22;
        seqText.fontStyle = FontStyles.Bold;
        seqText.alignment = TextAlignmentOptions.Center;
        seqText.color = new Color(0.45f, 0.9f, 0.55f, 1f);
        seqText.characterSpacing = 4f;

        // ---- View + refs ----
        SequencePanelView view = canvasGO.AddComponent<SequencePanelView>();
        BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(SequencePanelView).GetField("canvasGroup", bf).SetValue(view, cg);
        typeof(SequencePanelView).GetField("buttonsParent", bf).SetValue(view, gridRT);
        typeof(SequencePanelView).GetField("buttonPrefab", bf).SetValue(view, btnTemplateGO.GetComponent<Button>());
        typeof(SequencePanelView).GetField("titleText", bf).SetValue(view, titleText);
        typeof(SequencePanelView).GetField("sequenceDisplayText", bf).SetValue(view, seqText);
        typeof(SequencePanelView).GetField("statusText", bf).SetValue(view, statusText);
        typeof(SequencePanelView).GetField("closeButton", bf).SetValue(view, closeBtn);

        // ---- Controller GameObject ----
        GameObject ctrlGO = new GameObject("SequencePanelUIController");
        SceneManager.MoveGameObjectToScene(ctrlGO, levelUI);
        SequencePanelUIController ctrl = ctrlGO.AddComponent<SequencePanelUIController>();
        typeof(SequencePanelUIController).GetField("view", bf).SetValue(ctrl, view);

        canvasGO.SetActive(false);

        EditorSceneManager.MarkSceneDirty(levelUI);
        EditorSceneManager.SaveScene(levelUI);

        Debug.Log("[SequencePanelUISetup] UI armada y guardada en LevelUI.");
    }

    private static void DestroyIfExists(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) Object.DestroyImmediate(existing);
    }
}
