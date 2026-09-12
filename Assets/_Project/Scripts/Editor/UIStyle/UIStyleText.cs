#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Text step: titles in Oswald, everything else in ShareTechMono, all with a black outline through
/// one shared TMP material preset per font — the text half of "a black border on everything"; frames
/// are the other half.
///
/// One preset per font, not per-label settings: TMP outline lives in the material, so a preset is the
/// only way to tune them all at once. Each preset is created on first use and never overwritten
/// after that — once it exists, its numbers belong to whoever tunes it in the Inspector.
///
/// FaceDilate is half the outline width on purpose: a TMP outline is centred on the glyph edge, so
/// without it the ring would eat half its width out of the letters and thin them. With it the face
/// keeps its size and the whole ring sits outside.
///
/// Texts inside a nested prefab are left to that prefab's profile. A font change is listed by name in
/// the report: the new font has other metrics, so that label's layout is worth a look.
/// </summary>
public static class UIStyleText
{
    public const string BodyFontPath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF.asset";
    public const string TitleFontPath = "Assets/_Project/Art/Fonts/Oswald/static/Oswald-Regular SDF.asset";

    private const string BodyPresetPath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF - Outline.mat";
    private const string TitlePresetPath = "Assets/_Project/Art/Fonts/Oswald/static/Oswald-Regular SDF - Outline.mat";

    private const string LegacyFontPath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular.ttf";
    private static readonly Vector2 LegacyOutline = new Vector2(1.5f, 1.5f);

    private const float OutlineWidth = 0.2f;
    private const float FaceDilate = OutlineWidth * 0.5f;

    public static void Apply(UIStyleContext ctx)
    {
        TMP_FontAsset body = UIStyleTools.LoadRequired<TMP_FontAsset>(BodyFontPath);
        TMP_FontAsset title = UIStyleTools.LoadRequired<TMP_FontAsset>(TitleFontPath);
        if (body == null || title == null) return;

        // Both, every run: cheap, and the title preset then exists before the first profile needs it.
        Material bodyPreset = GetOrCreatePreset(body, BodyPresetPath);
        Material titlePreset = GetOrCreatePreset(title, TitlePresetPath);

        if (!ctx.Profile.restyleTexts) return;

        HashSet<Transform> titles = new HashSet<Transform>(ctx.Profile.titleTexts.Select(ctx.Find));
        HashSet<Transform> untouched = new HashSet<Transform>(ctx.Profile.untouchedTexts.Select(ctx.Find));

        int changed = 0, nested = 0;
        foreach (TMP_Text text in ctx.Root.GetComponentsInChildren<TMP_Text>(true))
        {
            Transform node = text.transform;
            if (untouched.Contains(node)) continue;

            bool isTitle = titles.Contains(node);
            if (!isTitle && UIStyleTools.IsInNestedPrefab(node, ctx.Root))
            {
                nested++;
                continue;
            }

            TMP_FontAsset font = isTitle ? title : body;
            Material preset = isTitle ? titlePreset : bodyPreset;
            if (text.font == font && text.fontSharedMaterial == preset) continue;

            if (text.font != font)
            {
                string from = text.font != null ? text.font.name : "none";
                ctx.Report.AppendLine($"  FONT '{UIStyleTools.PathOf(node, ctx.Root.transform)}': {from} → {font.name}");
                // Before the material: assigning a font resets the material to the font's own.
                text.font = font;
            }

            text.fontSharedMaterial = preset;
            changed++;
        }

        ctx.Report.AppendLine($"  texts: {changed} changed" + (nested > 0 ? $", {nested} in nested prefabs left to their own profile" : ""));

        RestyleLegacyTexts(ctx, untouched);
    }

    /// <summary>
    /// uGUI Text cannot use a TMP font or preset, so it gets the same face from the TTF and a black
    /// Outline effect — close enough to read as one family. Converting it to TMP would be a
    /// different job: it removes a component other scripts may reference.
    /// </summary>
    private static void RestyleLegacyTexts(UIStyleContext ctx, HashSet<Transform> untouched)
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(LegacyFontPath);
        int changed = 0;

        foreach (Text text in ctx.Root.GetComponentsInChildren<Text>(true))
        {
            Transform node = text.transform;
            if (untouched.Contains(node) || UIStyleTools.IsInNestedPrefab(node, ctx.Root)) continue;

            bool touched = false;
            if (font != null && text.font != font)
            {
                ctx.Report.AppendLine($"  LEGACY TEXT '{UIStyleTools.PathOf(node, ctx.Root.transform)}': {(text.font != null ? text.font.name : "none")} → {font.name}");
                text.font = font;
                touched = true;
            }

            Outline outline = node.GetComponent<Outline>();
            if (outline == null)
            {
                outline = node.gameObject.AddComponent<Outline>();
                touched = true;
            }
            outline.effectColor = Color.black;
            outline.effectDistance = LegacyOutline;

            if (touched) changed++;
        }

        if (changed > 0) ctx.Report.AppendLine($"  legacy texts: {changed} changed");
    }

    private static Material GetOrCreatePreset(TMP_FontAsset font, string path)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // Copied from the font's own material, so it carries the atlas and the SDF settings; TMP
        // recognises a preset by that shared atlas.
        Material preset = new Material(font.material) { name = Path.GetFileNameWithoutExtension(path) };
        preset.SetFloat("_OutlineWidth", OutlineWidth);
        preset.SetColor("_OutlineColor", Color.black);
        preset.SetFloat("_FaceDilate", FaceDilate);
        preset.EnableKeyword("OUTLINE_ON"); // needed by the Mobile SDF shaders; harmless on the full one

        AssetDatabase.CreateAsset(preset, path);
        Debug.Log($"[UIStyle] Created outline preset {path}");
        return preset;
    }
}
#endif
