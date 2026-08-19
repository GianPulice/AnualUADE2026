using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_PostProcessToggle"/>: one big button switching every post process
/// at once, plus smaller ones for each piece individually.
///
/// The big button changes its label and colour from the real state of the materials and of the
/// renderer features, not from a bool stored on the asset. That matters because both can be edited
/// elsewhere (from the material inspector, from the PC_Renderer inspector, or from code in Play
/// Mode), and a button reading "Activar" when the effect is already active is worse than no button
/// at all.
///
/// Everything goes through Undo: switching post processing off is exactly the kind of change made
/// to look at one thing and reverted straight away with Ctrl+Z.
///
/// The button labels are in Spanish to match SO_ItemCategoryConfigEditor, the project's other
/// custom inspector button.
/// </summary>
[CustomEditor(typeof(SO_PostProcessToggle))]
public class SO_PostProcessToggleEditor : Editor
{
    private static readonly Color OffColor = new Color(0.85f, 0.35f, 0.3f);
    private static readonly Color OnColor = new Color(0.3f, 0.75f, 0.45f);

    private readonly List<Object> targetBuffer = new List<Object>();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_PostProcessToggle toggle = (SO_PostProcessToggle)target;

        EditorGUILayout.Space(16);
        DrawMasterButton(toggle);

        EditorGUILayout.Space(8);
        DrawIndividualButtons(toggle);

        EditorGUILayout.Space(8);
        DrawStatus(toggle);
    }

    private void DrawMasterButton(SO_PostProcessToggle toggle)
    {
        bool anyEnabled = toggle.IsAnyEnabled;

        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = anyEnabled ? OffColor : OnColor;

        string label = anyEnabled ? "Desactivar Post Process" : "Activar Post Process";

        if (GUILayout.Button(label, GUILayout.Height(48)))
        {
            Apply(toggle, () => toggle.SetAllEnabled(!anyEnabled), label);
        }

        GUI.backgroundColor = previous;
    }

    private void DrawIndividualButtons(SO_PostProcessToggle toggle)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSingleButton(toggle.IsPs1Enabled, "PSX", () => toggle.TogglePs1(), toggle);
            DrawSingleButton(toggle.IsVisionFogEnabled, "Vision Fog", () => toggle.ToggleVisionFog(), toggle);
        }

        // On a row of its own because the label does not fit beside the other two, and because it
        // is the button with the wider blast radius: it takes the passes out of PC_Renderer for
        // every camera using that renderer, not just for the view being looked at.
        DrawSingleButton(toggle.AreRendererFeaturesEnabled, "pasadas del PC_Renderer",
                         () => toggle.ToggleRendererFeatures(), toggle);
    }

    private void DrawSingleButton(bool isEnabled, string name, System.Action action,
                                  SO_PostProcessToggle toggle)
    {
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = isEnabled ? OffColor : OnColor;

        string label = isEnabled ? $"Desactivar {name}" : $"Activar {name}";

        if (GUILayout.Button(label, GUILayout.Height(30))) Apply(toggle, action, label);

        GUI.backgroundColor = previous;
    }

    private static void DrawStatus(SO_PostProcessToggle toggle)
    {
        string ps1 = toggle.IsPs1Enabled ? "ON" : "OFF";
        string fog = toggle.IsVisionFogEnabled ? "ON" : "OFF";
        string passes = toggle.AreRendererFeaturesEnabled ? "ON" : "OFF";

        EditorGUILayout.HelpBox($"PSX: {ps1}   |   Vision Fog: {fog}   |   PC_Renderer: {passes}\n\n" +
                                "The buttons write straight into the materials and into " +
                                "PC_Renderer.asset, so the change persists and will show up in " +
                                "git. Ctrl+Z reverts it.",
                                MessageType.None);

        // The one combination that looks like a bug: the material inspector says the effect is on,
        // the screen says otherwise. Naming it here saves the search through the shader.
        if (!toggle.AreRendererFeaturesEnabled && (toggle.IsPs1Enabled || toggle.IsVisionFogEnabled))
        {
            EditorGUILayout.HelpBox("The renderer features are off, so nothing is drawn even " +
                                    "though the materials still have their effect enabled. Turn " +
                                    "the passes back on to see them.",
                                    MessageType.Warning);
        }
    }

    /// <summary>
    /// Runs the action with Undo, marking every touched asset dirty.
    ///
    /// Undo is registered on the MATERIALS and the RENDERER FEATURES, not on the ScriptableObject:
    /// the asset does not change, their serialised fields do. Registering the wrong object makes
    /// Ctrl+Z look like it works while reverting nothing.
    ///
    /// The renderer features are sub-assets of PC_Renderer.asset, so marking them dirty is what
    /// gets the flag written into that file on SaveAssets.
    /// </summary>
    private void Apply(SO_PostProcessToggle toggle, System.Action action, string undoLabel)
    {
        toggle.CollectTargets(targetBuffer);

        if (targetBuffer.Count == 0)
        {
            Debug.LogWarning("[SO_PostProcessToggle] Nothing is assigned on this asset. Drag " +
                             "PS1Effect.mat, the VisionFog*.mat files, and the PSXEffect and " +
                             "Vision Fog features of PC_Renderer.asset into the fields above.",
                             toggle);
            return;
        }

        Undo.RecordObjects(targetBuffer.ToArray(), undoLabel);

        action();

        for (int i = 0; i < targetBuffer.Count; i++) EditorUtility.SetDirty(targetBuffer[i]);

        AssetDatabase.SaveAssets();

        // The material inspector does not repaint on its own when written to from outside, and
        // without this its checkbox keeps showing the old value until it is reselected.
        SceneView.RepaintAll();
    }
}
#endif
