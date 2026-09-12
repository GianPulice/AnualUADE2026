#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Writes a first <see cref="SO_UIStyleProfile"/> for a prefab by walking the prefab itself, so the
/// paths are the real ones and nothing is typed by hand. It is a DRAFT: review it before applying.
///
/// The rules, by widget:
///   full-screen translucent image  → Dim, no frame
///   image that holds other nodes   → SurfacePanel + animated surface, Raised
///   button                         → SurfaceRaised / label TextPrimary, Raised + press
///     destructive or exit button   → AccentBgDeep / label Accent
///     Settings tab                 → SurfaceTabs; its active indicator Accent (thin) or AccentBgSubtle
///   Settings tab rail              → SurfaceTabs, no frame (the tabs sit flush on it)
///   scroll view, input field       → SurfaceScreen, Sunken
///   slider                         → track SurfaceScreen Sunken, fill Accent, handle BorderStrong Raised
///   toggle                         → box SurfaceScreen Sunken, check Accent
///   dropdown                       → SurfaceRaised Raised; list SurfaceScreen Sunken
///   scrollbar                      → SurfaceRaised, handle BorderStrong Raised
///   title text (by name or size)   → Oswald, TextPrimary — never a "// header", which stays mono
///   other text, uGUI Text included → TextPrimary / TextSecondary / TextMuted by how light it is now
///
/// Widget chrome (button backgrounds, tracks, knobs, boxes) goes in flatFills: the frame replaces
/// its sprite. The channel-change transition goes on the node that holds all of a screen's controls
/// and texts, never on the view itself.
///
/// Whatever is already styled — a UIThemeApplier, a BevelFrame, the surface material, a signal
/// transition, a CRT presenter — is read back as it is instead of being guessed again.
///
/// Left out and listed in the report: icons (custom sprites), images no rule recognises, masks,
/// nested prefabs (they get their own profile), and nodes whose path would be ambiguous.
///
/// Never overwrites: if the profile asset exists, drafting stops.
/// </summary>
public static class UIStyleProfileDrafter
{
    [MenuItem("Tools/UI/Style/Draft Profile From Prefab", priority = 10)]
    private static void DraftSelected()
    {
        SO_UIStyleProfile profile = CreateDraftAsset((GameObject)Selection.activeObject);
        if (profile == null) return;

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
    }

    [MenuItem("Tools/UI/Style/Draft Profile From Prefab", true)]
    private static bool IsPrefabSelected() =>
        Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);

    /// <summary>Drafts and saves UIStyle_&lt;Prefab&gt;.asset; null (and a warning) if it already exists.</summary>
    public static SO_UIStyleProfile CreateDraftAsset(GameObject prefab)
    {
        string assetPath = $"{UIStyleTools.ProfileFolder}/{ProfileName(prefab)}.asset";

        if (AssetDatabase.LoadAssetAtPath<SO_UIStyleProfile>(assetPath) != null)
        {
            Debug.LogWarning($"[UIStyleDrafter] {assetPath} already exists. Drafting never overwrites a profile — rename or delete it first.");
            return null;
        }

        StringBuilder report = new StringBuilder();
        SO_UIStyleProfile profile = Draft(prefab, report);

        UIStyleTools.EnsureFolder(UIStyleTools.ProfileFolder);
        AssetDatabase.CreateAsset(profile, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[UIStyleDrafter] Drafted {assetPath} — review it before applying.\n{report}", profile);
        return profile;
    }

    public static string ProfileName(GameObject prefab) =>
        "UIStyle_" + new string(prefab.name.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>A new, unsaved profile for <paramref name="prefab"/>; the prefab is only read.</summary>
    public static SO_UIStyleProfile Draft(GameObject prefab, StringBuilder report)
    {
        SO_UIStyleProfile profile = ScriptableObject.CreateInstance<SO_UIStyleProfile>();
        profile.prefab = prefab;
        profile.revertNestedStyleOverrides = true;
        profile.surfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(UIStyleTools.SurfaceMaterialPath);
        profile.crt.material = AssetDatabase.LoadAssetAtPath<Material>(UIStyleTools.CRTMaterialPath);

        GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
        try
        {
            new Pass(profile, root, report).Run();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return profile;
    }

    // -- The walk -------------------

    private sealed class Pass
    {
        private static readonly string[] Generated =
            { UIStyleTools.FrameName, UIStyleTools.StaticName, UIStyleTools.ScanBarName, "SweepBar" };

        private static readonly string[] DestructiveWords =
            { "quit", "exit", "salir", "delete", "borrar", "discard", "descartar", "reset", "restablecer", "abandon" };
        private static readonly string[] TitleWords = { "title", "titulo", "título", "header", "heading", "encabezado" };
        private static readonly string[] DividerWords = { "divider", "division", "separator", "separador", "line", "linea", "línea" };
        private static readonly HashSet<string> BuiltInSprites = new HashSet<string>
            { "UISprite", "Background", "InputFieldBackground", "Knob", "Checkmark", "DropdownArrow", "UIMask" };

        private const float TitleFontSize = 44f;
        private const float ThinSize = 6f;

        private readonly SO_UIStyleProfile profile;
        private readonly GameObject root;
        private readonly StringBuilder report;

        private readonly HashSet<Transform> claimed = new HashSet<Transform>();   // classified by a widget rule
        private readonly Dictionary<Transform, UIThemeRole> roles = new Dictionary<Transform, UIThemeRole>();
        private readonly HashSet<Transform> framed = new HashSet<Transform>();
        private readonly HashSet<Transform> surfaced = new HashSet<Transform>();
        private readonly HashSet<Transform> tabButtons = new HashSet<Transform>();

        private readonly List<string> destructive = new List<string>();
        private readonly List<string> inverted = new List<string>();
        private readonly List<string> legacy = new List<string>();
        private readonly List<string> icons = new List<string>();
        private readonly List<string> unclassified = new List<string>();
        private readonly List<string> skipped = new List<string>();

        private TMP_FontAsset titleFont;
        private int readBack;

        public Pass(SO_UIStyleProfile profile, GameObject root, StringBuilder report)
        {
            this.profile = profile;
            this.root = root;
            this.report = report;
        }

        public void Run()
        {
            titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIStyleText.TitleFontPath);

            ReadTabs();
            Walk(root.transform);
            PickSignalPanel();
            DraftCRT();
            Summarize();
        }

        private void Walk(Transform node)
        {
            if (node != root.transform)
            {
                if (Generated.Contains(node.name)) return;

                if (UIStyleTools.IsInNestedPrefab(node, root))
                {
                    string source = Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(node.gameObject));
                    skipped.Add($"'{PathOf(node)}' — nested {source}, styled by its own profile");
                    return;
                }

                if (node.name.Contains('/'))
                {
                    skipped.Add($"'{PathOf(node)}' — UNADDRESSABLE, the name has a '/'");
                    return;
                }

                if (node.parent.Cast<Transform>().Count(s => s.name == node.name) > 1)
                {
                    skipped.Add($"'{PathOf(node)}' — AMBIGUOUS, siblings share the name (rename one to style it)");
                    return;
                }
            }

            ReadExistingStyle(node);
            if (!claimed.Contains(node)) Classify(node);

            foreach (Transform child in node) Walk(child);
        }

        private void Classify(Transform node)
        {
            // A mask's graphic only cuts; it is not a surface anyone sees.
            if (node.GetComponent<Mask>() != null) return;

            if (node.TryGetComponent(out Button button)) DraftButton(node, button);
            else if (node.TryGetComponent(out TMP_Dropdown dropdown)) DraftDropdown(node, dropdown);
            else if (node.TryGetComponent(out Toggle toggle)) DraftToggle(node, toggle);
            else if (node.TryGetComponent(out Slider slider)) DraftSlider(node, slider);
            else if (node.TryGetComponent(out Scrollbar scrollbar)) DraftScrollbar(node, scrollbar);
            else if (node.TryGetComponent(out TMP_InputField input)) DraftInput(node, input);
            else if (node.TryGetComponent(out ScrollRect scroll)) DraftScroll(node, scroll);
            else if (node.TryGetComponent(out TMP_Text text)) DraftText(node, text);
            else if (node.TryGetComponent(out Text legacyText)) DraftLegacyText(node, legacyText);
            else if (node.TryGetComponent(out Image image)) DraftImage(node, image);
        }

        // -- Widgets -------------------

        private void DraftButton(Transform node, Button button)
        {
            claimed.Add(node);
            TMP_Text[] labels = OwnTexts(node);

            bool isTab = tabButtons.Contains(node);
            string words = node.name + " " + string.Join(" ", labels.Select(l => l.text));
            bool isDestructive = !isTab && Matches(words, DestructiveWords);
            if (isDestructive) destructive.Add(PathOf(node));

            Transform background = button.targetGraphic != null ? button.targetGraphic.transform : node;
            Theme(background, isTab ? UIThemeRole.SurfaceTabs : isDestructive ? UIThemeRole.AccentBgDeep : UIThemeRole.SurfaceRaised);
            Flat(background);
            claimed.Add(background);
            Frame(node, Style.Raised, pressable: true);

            foreach (TMP_Text label in labels)
            {
                Theme(label.transform, isDestructive ? UIThemeRole.Accent : isTab ? UIThemeRole.TextSecondary : UIThemeRole.TextPrimary);
                claimed.Add(label.transform);
            }
        }

        private void DraftToggle(Transform node, Toggle toggle)
        {
            claimed.Add(node);

            if (toggle.targetGraphic != null)
            {
                Transform box = toggle.targetGraphic.transform;
                Theme(box, UIThemeRole.SurfaceScreen);
                Flat(box);
                Frame(box, Style.Sunken);
                claimed.Add(box);
            }

            if (toggle.graphic != null)
            {
                Theme(toggle.graphic.transform, UIThemeRole.Accent);
                claimed.Add(toggle.graphic.transform);
            }
        }

        private void DraftSlider(Transform node, Slider slider)
        {
            claimed.Add(node);

            Transform track = node.Find("Background");
            if (track != null && track.GetComponent<Graphic>() != null)
            {
                Theme(track, UIThemeRole.SurfaceScreen);
                Flat(track);
                Frame(track, Style.Sunken);
                claimed.Add(track);
            }

            if (slider.fillRect != null)
            {
                Theme(slider.fillRect, UIThemeRole.Accent);
                Flat(slider.fillRect);
                claimed.Add(slider.fillRect);
            }

            if (slider.handleRect != null)
            {
                Theme(slider.handleRect, UIThemeRole.BorderStrong);
                Flat(slider.handleRect);
                Frame(slider.handleRect, Style.Raised);
                claimed.Add(slider.handleRect);
            }
        }

        private void DraftScrollbar(Transform node, Scrollbar scrollbar)
        {
            claimed.Add(node);
            Theme(node, UIThemeRole.SurfaceRaised);
            Flat(node);

            if (scrollbar.handleRect == null) return;
            Theme(scrollbar.handleRect, UIThemeRole.BorderStrong);
            Flat(scrollbar.handleRect);
            Frame(scrollbar.handleRect, Style.Raised);
            claimed.Add(scrollbar.handleRect);
        }

        private void DraftDropdown(Transform node, TMP_Dropdown dropdown)
        {
            claimed.Add(node);
            Theme(node, UIThemeRole.SurfaceRaised);
            Flat(node);
            Frame(node, Style.Raised);

            ClaimTheme(dropdown.captionText, UIThemeRole.TextPrimary);
            ClaimTheme(node.Find("Arrow"), UIThemeRole.TextPrimary);

            if (dropdown.template != null)
            {
                Theme(dropdown.template, UIThemeRole.SurfaceScreen);
                Flat(dropdown.template);
                Frame(dropdown.template, Style.Sunken);
                claimed.Add(dropdown.template);
            }

            // The template's item is a Toggle; claimed here so the toggle rule does not frame every row.
            if (dropdown.itemText == null) return;
            ClaimTheme(dropdown.itemText, UIThemeRole.TextPrimary);

            Toggle item = dropdown.itemText.GetComponentInParent<Toggle>(true);
            if (item == null) return;
            claimed.Add(item.transform);
            ClaimTheme(item.targetGraphic, UIThemeRole.SurfaceRaised);
            ClaimTheme(item.graphic, UIThemeRole.Accent);
        }

        private void DraftInput(Transform node, TMP_InputField input)
        {
            claimed.Add(node);
            Theme(node, UIThemeRole.SurfaceScreen);
            Flat(node);
            Frame(node, Style.Sunken);
            ClaimTheme(input.textComponent, UIThemeRole.TextPrimary);
            ClaimTheme(input.placeholder, UIThemeRole.TextMuted);
        }

        private void DraftScroll(Transform node, ScrollRect scroll)
        {
            claimed.Add(node);
            if (scroll.viewport != null) claimed.Add(scroll.viewport);   // a mask, not a surface

            if (node.GetComponent<Graphic>() == null) return;
            Theme(node, UIThemeRole.SurfaceScreen);
            Frame(node, Style.Sunken);
        }

        private void DraftText(Transform node, TMP_Text text)
        {
            // "// something" is the project's mono header, as in "// INVENTORY" — never an Oswald title.
            bool monoHeader = text.text != null && text.text.TrimStart().StartsWith("//");
            bool styledTitle = titleFont != null && text.font == titleFont &&
                               text.fontSharedMaterial != null && text.fontSharedMaterial.name.EndsWith("- Outline");
            bool isTitle = !monoHeader && (styledTitle || Matches(node.name, TitleWords) || text.fontSize >= TitleFontSize);

            if (isTitle) profile.titleTexts.Add(PathOf(node));
            Theme(node, isTitle ? UIThemeRole.TextPrimary : TextRole(text.color, node));
        }

        private void DraftLegacyText(Transform node, Text text)
        {
            legacy.Add(PathOf(node));
            Theme(node, TextRole(text.color, node));
        }

        private void DraftImage(Transform node, Image image)
        {
            RectTransform rt = (RectTransform)node;
            bool fullScreen = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one &&
                              rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
            bool holdsContent = node.Cast<Transform>().Any(child => !Generated.Contains(child.name));

            // An opaque full-screen image that holds the screen is its window, not a dim.
            if (fullScreen && (!holdsContent || image.color.a < 0.99f))
            {
                Theme(node, UIThemeRole.Dim);
                return;
            }

            if (holdsContent)
            {
                // A window only if it ends up SurfacePanel: an already-styled well or title bar that
                // holds an icon keeps its own role and gets neither the motion nor a window frame.
                Theme(node, UIThemeRole.SurfacePanel);
                if (roles[node] != UIThemeRole.SurfacePanel) return;

                Surface(node);
                if (!fullScreen) Frame(node, Style.Raised);
                return;
            }

            if (rt.rect.width <= ThinSize || rt.rect.height <= ThinSize || Matches(node.name, DividerWords))
            {
                Theme(node, UIThemeRole.Divider);
                return;
            }

            if (image.sprite != null && !BuiltInSprites.Contains(image.sprite.name))
            {
                icons.Add(PathOf(node));
                return;
            }

            if (!roles.ContainsKey(node)) unclassified.Add(PathOf(node));
        }

        // -- Settings tabs, signal, CRT -------------------

        private void ReadTabs()
        {
            foreach (SettingsTabSelector selector in root.GetComponentsInChildren<SettingsTabSelector>(true))
            {
                // The rail the tabs sit in: its own colour, no frame. The tabs sit flush on it, so a
                // frame would overlap them, and the padding a Sunken well gets would narrow them.
                if (selector.GetComponent<Image>() != null)
                {
                    Theme(selector.transform, UIThemeRole.SurfaceTabs);
                    claimed.Add(selector.transform);
                }

                SerializedProperty tabs = new SerializedObject(selector).FindProperty("_tabs");
                for (int i = 0; i < tabs.arraySize; i++)
                {
                    SerializedProperty tab = tabs.GetArrayElementAtIndex(i);

                    if (tab.FindPropertyRelative("button").objectReferenceValue is Button button)
                        tabButtons.Add(button.transform);

                    if (tab.FindPropertyRelative("panel").objectReferenceValue is GameObject panel &&
                        panel.GetComponent<BaseScreenView>() == null)
                        AddSignal(panel.transform, SO_UIStyleProfile.SignalTrigger.OnEnable);

                    if (tab.FindPropertyRelative("activeIndicator").objectReferenceValue is GameObject indicator &&
                        indicator.GetComponent<Graphic>() != null)
                    {
                        Rect rect = ((RectTransform)indicator.transform).rect;
                        bool thin = rect.width <= ThinSize || rect.height <= ThinSize;
                        Theme(indicator.transform, thin ? UIThemeRole.Accent : UIThemeRole.AccentBgSubtle);
                        claimed.Add(indicator.transform);
                    }
                }
            }
        }

        /// <summary>
        /// Screens only — they are the ones that get shown. The transition goes on the node that holds
        /// every control and text of the view, so the whole screen locks on at once; never on the view
        /// itself, whose fade owns that CanvasGroup. When only the view holds them all, the child
        /// that holds most of them.
        /// </summary>
        private void PickSignalPanel()
        {
            if (profile.signalPanels.Count > 0) return;

            BaseScreenView view = root.GetComponentInChildren<BaseScreenView>(true);
            if (view == null) return;

            Transform[] content = view.GetComponentsInChildren<Selectable>(true).Select(s => s.transform)
                .Concat(view.GetComponentsInChildren<TMP_Text>(true).Select(t => t.transform))
                .Where(t => !UnderGenerated(t, view.transform))
                .ToArray();
            if (content.Length == 0)
            {
                report.AppendLine("  signal: the view holds no controls or texts — add a panel by hand if wanted.");
                return;
            }

            Transform holder = content[0];
            while (holder != null && !content.All(t => t.IsChildOf(holder))) holder = holder.parent;

            if (holder == null || holder == view.transform || !holder.IsChildOf(view.transform) || holder.GetComponent<BaseScreenView>() != null)
                holder = view.transform.Cast<Transform>()
                    .Where(c => !Generated.Contains(c.name) && c.GetComponent<BaseScreenView>() == null)
                    .OrderByDescending(c => content.Count(t => t.IsChildOf(c)))
                    .FirstOrDefault();

            if (holder == null)
            {
                report.AppendLine("  signal: no node to carry the transition — add one by hand if wanted.");
                return;
            }

            // A screen being shown catches the signal edge to edge, even when what flickers is a column.
            RectTransform rt = (RectTransform)holder;
            bool fullScreen = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one &&
                              rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
            AddSignal(holder, SO_UIStyleProfile.SignalTrigger.OnEnable).fullScreenOverlays = !fullScreen;
        }

        private void DraftCRT()
        {
            if (root.TryGetComponent(out CanvasCRTPresenter presenter))
            {
                SerializedObject data = new SerializedObject(presenter);
                profile.crt.enabled = true;
                profile.crt.contentPath = data.FindProperty("content").objectReferenceValue is GameObject content ? PathOf(content.transform) : "";
                if (data.FindProperty("visibility")?.objectReferenceValue is CanvasGroup group)
                {
                    profile.crt.useVisibility = true;
                    profile.crt.visibilityPath = PathOf(group.transform);
                }
                if (data.FindProperty("screenMaterial").objectReferenceValue is Material material) profile.crt.material = material;
                return;
            }

            // Off until the phase decides; the visibility group is filled in for when it is turned on.
            BaseScreenView view = root.GetComponentInChildren<BaseScreenView>(true);
            if (view == null || view.GetComponent<CanvasGroup>() == null) return;
            profile.crt.useVisibility = true;
            profile.crt.visibilityPath = PathOf(view.transform);
        }

        // -- What is already there -------------------

        private void ReadExistingStyle(Transform node)
        {
            if (node.TryGetComponent(out UIThemeApplier _)) Theme(node, UIThemeRole.TextPrimary);   // Theme() reads the applier's own role

            Transform frame = node.Find(UIStyleTools.FrameName);
            if (frame != null && frame.TryGetComponent(out UIBevelFrame _))
                Frame(node, Style.Raised, node.GetComponent<UIBevelPressFeedback>() != null);   // Frame() reads the frame's own style

            if (profile.surfaceMaterial != null && node.TryGetComponent(out Image image) && image.material == profile.surfaceMaterial)
                Surface(node);

            if (node.TryGetComponent(out UISignalTransition _))
            {
                if (node.TryGetComponent(out ItemSelectionTransitionTrigger trigger))
                {
                    SO_UIStyleProfile.SignalPanelEntry entry = AddSignal(node, SO_UIStyleProfile.SignalTrigger.ItemSelection);
                    SerializedProperty list = new SerializedObject(trigger).FindProperty("typewriters");
                    for (int i = 0; i < list.arraySize; i++)
                        if (list.GetArrayElementAtIndex(i).objectReferenceValue is Component typewriter)
                            entry.typewriters.Add(UIStyleTools.PathOf(typewriter.transform, node));
                }
                else
                {
                    AddSignal(node, SO_UIStyleProfile.SignalTrigger.OnEnable);
                }
            }
        }

        // -- Entries -------------------

        private void Theme(Transform node, UIThemeRole role)
        {
            if (node == null || roles.ContainsKey(node) || node.GetComponent<Graphic>() == null) return;

            bool preserveAlpha = false;
            if (node.TryGetComponent(out UIThemeApplier applier))
            {
                SerializedObject data = new SerializedObject(applier);
                role = (UIThemeRole)data.FindProperty("role").enumValueIndex;
                preserveAlpha = data.FindProperty("preserveAlpha").boolValue;
                readBack++;
            }

            roles[node] = role;
            profile.theme.Add(new SO_UIStyleProfile.ThemeEntry { path = PathOf(node), role = role, preserveAlpha = preserveAlpha });
        }

        private void ClaimTheme(Component component, UIThemeRole role)
        {
            if (component == null) return;
            Theme(component.transform, role);
            claimed.Add(component.transform);
        }

        private void Frame(Transform node, Style style, bool pressable = false)
        {
            if (node == null || !framed.Add(node)) return;

            Transform existing = node.Find(UIStyleTools.FrameName);
            if (existing != null && existing.TryGetComponent(out UIBevelFrame frame)) style = frame.Style;

            profile.frames.Add(new SO_UIStyleProfile.FrameEntry
            {
                path = PathOf(node),
                style = style,
                pressable = pressable && node.GetComponent<Button>() != null,
            });
        }

        /// <summary>Chrome only: callers pass backgrounds, tracks, knobs and boxes, never an icon.</summary>
        private void Flat(Transform node)
        {
            if (node == null || !node.TryGetComponent(out Image image) || image.sprite == null) return;

            string path = PathOf(node);
            if (!profile.flatFills.Contains(path)) profile.flatFills.Add(path);
        }

        private void Surface(Transform node)
        {
            if (surfaced.Add(node)) profile.animatedSurfaces.Add(PathOf(node));
        }

        private SO_UIStyleProfile.SignalPanelEntry AddSignal(Transform node, SO_UIStyleProfile.SignalTrigger trigger)
        {
            string path = PathOf(node);
            SO_UIStyleProfile.SignalPanelEntry entry = profile.signalPanels.FirstOrDefault(e => e.path == path);
            if (entry != null) return entry;

            entry = new SO_UIStyleProfile.SignalPanelEntry { path = path, trigger = trigger };
            profile.signalPanels.Add(entry);
            return entry;
        }

        // -- Helpers -------------------

        private UIThemeRole TextRole(Color color, Transform node)
        {
            if (color.r - Mathf.Max(color.g, color.b) > 0.25f) return UIThemeRole.Accent;

            float light = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            if (light >= 0.78f) return UIThemeRole.TextPrimary;
            if (light >= 0.55f) return UIThemeRole.TextSecondary;
            if (light >= 0.35f) return UIThemeRole.TextMuted;

            // Dark text was written for a light surface, and the surfaces are black now.
            inverted.Add(PathOf(node));
            return UIThemeRole.TextPrimary;
        }

        private TMP_Text[] OwnTexts(Transform node) =>
            node.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => !UIStyleTools.IsInNestedPrefab(t.transform, root) && !UnderGenerated(t.transform, node))
                .ToArray();

        private static bool UnderGenerated(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (Generated.Contains(t.name)) return true;
            return false;
        }

        private static bool Matches(string text, string[] words)
        {
            string lower = text.ToLowerInvariant();
            return words.Any(lower.Contains);
        }

        private string PathOf(Transform node) => UIStyleTools.PathOf(node, root.transform);

        private void Summarize()
        {
            report.AppendLine($"  theme: {profile.theme.Count} ({readBack} read back from existing appliers)");
            report.AppendLine($"  flat fills: {profile.flatFills.Count}");
            report.AppendLine($"  frames: {profile.frames.Count}, well padding ≥ {profile.minWellPadding}");
            report.AppendLine($"  titles (Oswald): {profile.titleTexts.Count}" + List(profile.titleTexts));
            report.AppendLine($"  animated surfaces: {profile.animatedSurfaces.Count}" + List(profile.animatedSurfaces));
            report.AppendLine($"  signal panels: {profile.signalPanels.Count}" + List(profile.signalPanels.Select(s => $"{s.path} ({s.trigger}{(s.fullScreenOverlays ? ", full-screen static" : "")})")));
            report.AppendLine($"  crt: {(profile.crt.enabled ? "on" : "off — turn it on for modal screens")}" +
                              (profile.crt.useVisibility ? $", visibility '{profile.crt.visibilityPath}'" : ""));

            if (destructive.Count > 0) report.AppendLine("  destructive buttons (AccentBgDeep):" + List(destructive));
            if (inverted.Count > 0) report.AppendLine("  INVERTED — dark texts made TextPrimary:" + List(inverted));
            if (legacy.Count > 0) report.AppendLine("  LEGACY uGUI Text — ShareTechMono TTF + Outline when applied:" + List(legacy));
            if (icons.Count > 0) report.AppendLine("  icons, left as they are:" + List(icons));
            if (unclassified.Count > 0) report.AppendLine("  UNCLASSIFIED — no rule matched, decide by hand:" + List(unclassified));
            if (skipped.Count > 0) report.AppendLine("  skipped:" + List(skipped));
        }

        private static string List(IEnumerable<string> items)
        {
            string[] all = items.ToArray();
            return all.Length == 0 ? "" : "\n      " + string.Join("\n      ", all);
        }
    }
}
#endif
