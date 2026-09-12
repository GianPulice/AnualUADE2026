#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nested-overrides step: reverts the style overrides a prefab holds on the prefabs nested in it —
/// colour, font, material, sprite, theme role — so each nested prefab shows the look its own profile
/// gives it. A Back button that pins the plain font material would otherwise never get the outline.
///
/// Text, sizes and layout are per instance by nature and are kept. Runs first; theme entries of the
/// outer profile that point into a nested prefab write their override again right after, so the
/// intended overrides (a red Exit made of the generic button) come out the same every run.
///
/// Only when the profile asks (revertNestedStyleOverrides).
/// </summary>
public static class UIStyleNestedOverrides
{
    private static readonly HashSet<string> GraphicProperties = new HashSet<string>
    {
        "m_Color", "m_Sprite", "m_Material", "m_Type",
        "m_fontColor", "m_fontColor32", "m_fontAsset", "m_sharedMaterial",
        "m_enableVertexGradient", "m_fontColorGradient", "m_fontColorGradientPreset",
    };

    private static readonly HashSet<string> ApplierProperties = new HashSet<string> { "theme", "role", "preserveAlpha" };
    private static readonly HashSet<string> FrameProperties = new HashSet<string> { "theme", "style" };

    public static void Apply(UIStyleContext ctx)
    {
        if (!ctx.Profile.revertNestedStyleOverrides) return;

        int reverted = 0;
        foreach (Transform node in ctx.Root.GetComponentsInChildren<Transform>(true))
        {
            GameObject go = node.gameObject;
            if (go == ctx.Root || !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;

            foreach (ObjectOverride objectOverride in PrefabUtility.GetObjectOverrides(go, false))
            {
                Object target = objectOverride.instanceObject;
                HashSet<string> style = StylePropertiesOf(target);
                if (style == null) continue;

                SerializedObject data = new SerializedObject(target);
                List<string> paths = new List<string>();
                SerializedProperty it = data.GetIterator();
                for (bool enter = true; it.Next(enter); enter = false)
                    if (it.prefabOverride && style.Contains(it.name)) paths.Add(it.propertyPath);

                foreach (string path in paths)
                {
                    PrefabUtility.RevertPropertyOverride(data.FindProperty(path), InteractionMode.AutomatedAction);
                    reverted++;
                }
            }
        }

        if (reverted > 0) ctx.Report.AppendLine($"  nested prefabs: {reverted} style override(s) reverted");
    }

    private static HashSet<string> StylePropertiesOf(Object target)
    {
        switch (target)
        {
            case UIThemeApplier _: return ApplierProperties;
            case UIBevelFrame _: return FrameProperties;
            case Graphic _: return GraphicProperties;
            default: return null;
        }
    }
}
#endif
