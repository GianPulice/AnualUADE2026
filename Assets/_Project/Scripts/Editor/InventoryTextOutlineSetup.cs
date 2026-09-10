#if UNITY_EDITOR
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives every inventory label a black outline, through one shared TMP material preset of
/// ShareTechMono — the text half of "a black border on everything"; frames are the other half, see
/// <see cref="InventoryFrameSetup"/>.
///
/// One preset, not per-label settings: TMP outline lives in the material, so a preset is the only
/// way to tune them all at once. The preset is created on the first run and never overwritten after
/// that — once it exists, its numbers belong to whoever tunes it in the Inspector.
///
/// FaceDilate is half the outline width on purpose: a TMP outline is centred on the glyph edge, so
/// without it the ring would eat half its width out of the letters and thin them. With it the face
/// keeps its size and the whole ring sits outside.
///
/// USAGE: Tools / UI / Inventory / Outline Texts. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventoryTextOutlineSetup
{
    private const string MaterialPath = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF - Outline.mat";
    private const float OutlineWidth = 0.2f;
    private const float FaceDilate = OutlineWidth * 0.5f;

    private static readonly string[] Prefabs =
    {
        InventoryRedesign.CanvasPrefab,
        InventoryRedesign.SlotPrefab,
        InventoryRedesign.GroupLabelPrefab,
        InventoryRedesign.ParameterPrefab,
        InventoryRedesign.ModuleRowPrefab,
    };

    [MenuItem("Tools/UI/Inventory/Outline Texts", priority = 13)]
    public static void Apply()
    {
        TMP_FontAsset font = InventoryRedesign.LoadRequired<TMP_FontAsset>(InventoryRedesign.FontPath);
        if (font == null) return;

        Material preset = GetOrCreatePreset(font);
        StringBuilder report = new StringBuilder();
        int outlined = 0;

        foreach (string prefab in Prefabs)
        {
            report.AppendLine(prefab);

            InventoryRedesign.EditPrefab(prefab, root =>
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    // The preset shares ShareTechMono's atlas; on any other font it would render garbage.
                    if (text.font != font)
                    {
                        report.AppendLine($"  SKIPPED '{text.name}' — font is {(text.font != null ? text.font.name : "none")}.");
                        continue;
                    }

                    if (text.fontSharedMaterial == preset) continue;

                    text.fontSharedMaterial = preset;
                    outlined++;
                }
            });
        }

        Debug.Log($"[InventoryTextOutlineSetup] {outlined} label(s) switched to '{preset.name}'.\n\n{report}");
    }

    private static Material GetOrCreatePreset(TMP_FontAsset font)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        // Copied from the font's own material, so it carries the atlas and the SDF settings; TMP
        // recognises a preset by that shared atlas.
        Material preset = new Material(font.material) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
        preset.SetFloat("_OutlineWidth", OutlineWidth);
        preset.SetColor("_OutlineColor", Color.black);
        preset.SetFloat("_FaceDilate", FaceDilate);
        preset.EnableKeyword("OUTLINE_ON"); // needed by the Mobile SDF shaders; harmless on the full one

        AssetDatabase.CreateAsset(preset, MaterialPath);
        return preset;
    }
}
#endif
