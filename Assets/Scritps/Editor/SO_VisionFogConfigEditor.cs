using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Inspector for <see cref="SO_VisionFogConfig"/>: buttons that wire the preset into the
/// <see cref="VisionRangeController"/> of the loaded scenes, instead of having to find the
/// controller by hand and drag the asset into its Default Config field.
///
/// Two different things, deliberately kept as separate buttons:
/// <list type="bullet">
/// <item><b>Aplicar como Default</b> — the real change. Writes the controller's
/// <c>defaultConfig</c> field, so it persists into the scene file and shows up in git.</item>
/// <item><b>Previsualizar</b> — writes the shader globals directly and touches nothing else.
/// For comparing presets in the Scene view without dirtying a scene. It is throwaway state:
/// the moment Play starts, the controller's LateUpdate overwrites it.</item>
/// </list>
///
/// The button reads the controller's real current value rather than a flag on the asset — same
/// reasoning as SO_PostProcessToggleEditor: a button offering to apply something already applied
/// is worse than no button.
///
/// Labels in Spanish to match the project's other custom inspectors.
/// </summary>
[CustomEditor(typeof(SO_VisionFogConfig))]
public class SO_VisionFogConfigEditor : Editor
{
    private static readonly Color ApplyColor   = new Color(0.3f, 0.75f, 0.45f);
    private static readonly Color PreviewColor = new Color(0.45f, 0.6f, 0.85f);

    // IDs taken from VisionFogState.Ids rather than re-declared: two copies of the same string
    // is how a rename in the shader ends up half-applied.

    private readonly List<VisionRangeController> controllerBuffer = new List<VisionRangeController>();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_VisionFogConfig config = (SO_VisionFogConfig)target;

        // Before the controller check on purpose: the preview is pure maths on the preset, so it
        // works with no scene loaded at all. That is the case where it earns the most — tuning a
        // preset from the Project window without opening the level.
        EditorGUILayout.Space(12);
        VisionFogPreviewDrawer.Draw(config);

        CollectControllers();

        EditorGUILayout.Space(16);

        if (controllerBuffer.Count == 0)
        {
            // The controller lives in LevelUI.unity, which is loaded additively — so this is the
            // normal state while working on any other scene on its own, not an error.
            EditorGUILayout.HelpBox(
                "No hay ningún VisionRangeController en las escenas cargadas.\n\n" +
                "Abrí (o cargá en aditivo) la escena que lo contiene — normalmente " +
                "Scenes/UI/LevelUI.unity — y los botones aparecen.",
                MessageType.Info);
            return;
        }

        DrawApplyButton(config);

        EditorGUILayout.Space(8);
        DrawPreviewButtons(config);

        EditorGUILayout.Space(8);
        DrawStatus(config);
    }

    private void DrawApplyButton(SO_VisionFogConfig config)
    {
        bool alreadyDefault = IsDefaultEverywhere(config);

        using (new EditorGUI.DisabledScope(alreadyDefault))
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = ApplyColor;

            string label = alreadyDefault
                ? "Ya es el Default"
                : controllerBuffer.Count > 1
                    ? $"Aplicar como Default ({controllerBuffer.Count} controllers)"
                    : "Aplicar como Default";

            if (GUILayout.Button(label, GUILayout.Height(48))) ApplyAsDefault(config);

            GUI.backgroundColor = previous;
        }
    }

    private void DrawPreviewButtons(SO_VisionFogConfig config)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = PreviewColor;

            if (GUILayout.Button("Previsualizar", GUILayout.Height(30))) ApplyPreview(config);

            GUI.backgroundColor = previous;

            if (GUILayout.Button("Limpiar preview", GUILayout.Height(30))) ClearPreview();
        }
    }

    private void DrawStatus(SO_VisionFogConfig config)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            for (int i = 0; i < controllerBuffer.Count; i++)
            {
                VisionRangeController controller = controllerBuffer[i];
                if (controller == null) continue;

                SO_VisionFogConfig current = GetDefaultConfig(controller);
                string currentName = current != null ? current.name : "<vacío>";
                string mark = current == config ? "●" : "○";

                EditorGUILayout.LabelField(
                    $"{mark} {controller.gameObject.scene.name} → {currentName}");
            }
        }

        if (Application.isPlaying)
        {
            // Worth saying out loud: the change is visible immediately because the controller
            // re-reads defaultConfig every LateUpdate, but Unity throws away Play Mode edits.
            EditorGUILayout.HelpBox(
                "En Play Mode el cambio se ve al instante, pero se pierde al salir. " +
                "Volvé a aplicarlo en Edit Mode para que quede guardado.",
                MessageType.Warning);
        }

        // The one combination that reads as "the button did nothing": the preset is wired in, but
        // its own values switch the effect off. Naming it here saves a hunt through the shader.
        if (config.visionEnd <= config.visionStart + 0.001f)
        {
            EditorGUILayout.HelpBox(
                "visionEnd <= visionStart: el shader hace early-out y no dibuja niebla. " +
                "Subí visionEnd por encima de visionStart.",
                MessageType.Warning);
        }
        else if (config.darkness <= 0.001f && config.inscatterStrength <= 0.001f)
        {
            // The two halves of the model are both off, so the pass runs and returns the scene
            // untouched. Easy to hit while tuning, and indistinguishable from "the shader broke".
            EditorGUILayout.HelpBox(
                "darkness = 0 e inscatterStrength = 0: las dos mitades del modelo están " +
                "apagadas, así que el pass corre y devuelve la escena tal cual. Subí darkness " +
                "para oscurecer, o inscatterStrength para que se vea el fogColor.",
                MessageType.Warning);
        }
        else if (config.fogColor.maxColorComponent > 0.01f && config.inscatterStrength <= 0.001f)
        {
            // The v1 mental model ("fogColor is what the screen becomes") survives the migration
            // and this is where it bites: the colour is set, and nothing shows it.
            EditorGUILayout.HelpBox(
                "fogColor tiene color pero inscatterStrength = 0, así que no se ve: la " +
                "extinción sola va al negro. Para \"oscuridad con un dejo de color\" subí " +
                "inscatterStrength de a poco (0.03–0.1); 1 es niebla estilo Silent Hill.",
                MessageType.Info);
        }
        else if (config.lightPreservation > 0.001f && config.maxLightPreservation <= 0.001f)
        {
            EditorGUILayout.HelpBox(
                "lightPreservation > 0 pero maxLightPreservation = 0: el techo anula el efecto " +
                "entero y ninguna luz perfora la niebla.",
                MessageType.Warning);
        }
        else if (config.playerLightRange <= 0f)
        {
            EditorGUILayout.HelpBox(
                "playerLightRange = 0: las luces del módulo no perforan la niebla, así que " +
                "playerLightColor no tiñe ni inyecta nada.",
                MessageType.Info);
        }
    }

    /// <summary>
    /// Writes <c>defaultConfig</c> on every controller found.
    ///
    /// Through SerializedObject rather than the public <c>SetDefaultConfig</c>: the field is
    /// <c>[SerializeField] private</c>, and only this path writes it into the scene file, registers
    /// the Undo entry against the controller (not against this asset, which does not change) and
    /// flags the value as a prefab override where that applies.
    ///
    /// No explicit runtime call is needed in Play Mode — the controller re-reads the field every
    /// LateUpdate, so the write shows on screen on the next frame.
    /// </summary>
    private void ApplyAsDefault(SO_VisionFogConfig config)
    {
        for (int i = 0; i < controllerBuffer.Count; i++)
        {
            VisionRangeController controller = controllerBuffer[i];
            if (controller == null) continue;

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty prop = so.FindProperty("defaultConfig");
            if (prop == null)
            {
                Debug.LogWarning("[SO_VisionFogConfig] VisionRangeController no tiene el campo " +
                                 "'defaultConfig' — ¿lo renombraron?", controller);
                continue;
            }

            prop.objectReferenceValue = config;
            so.ApplyModifiedProperties(); // registra el Undo por su cuenta

            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        SceneView.RepaintAll();
    }

    /// <summary>
    /// Pushes the preset's values straight to the shader globals, bypassing the stack and the
    /// LateUpdate lerp. Nothing is written to disk — this is for eyeballing presets side by side.
    /// </summary>
    private void ApplyPreview(SO_VisionFogConfig config)
    {
        for (int i = 0; i < controllerBuffer.Count; i++)
        {
            VisionRangeController controller = controllerBuffer[i];
            if (controller == null) continue;

            controller.ApplyPreviewBlend(VisionFogState.FromConfig(config));
        }

        SceneView.RepaintAll();
    }

    /// <summary>
    /// Puts the globals back to the "no player" state the controller itself writes, which makes
    /// the shader early-out. Without this the last preview stays on screen in Edit Mode, since
    /// nothing else overwrites the globals until Play starts.
    /// </summary>
    private static void ClearPreview()
    {
        Shader.SetGlobalFloat(VisionFogState.Ids.VisionEnd, 0f);
        Shader.SetGlobalFloat(VisionFogState.Ids.PlayerLightRange, 0f);
        Shader.SetGlobalInt(VisionFogState.Ids.BypassCount, 0);
        SceneView.RepaintAll();
    }

    private void CollectControllers()
    {
        controllerBuffer.Clear();
        // Include inactive: the controller sitting on a disabled object is exactly the case where
        // the wiring is easiest to get wrong, so it should still be reachable from here.
        // No FindObjectsSortMode — that overload is obsolete as of Unity 6.4, and this one is
        // already unsorted.
        controllerBuffer.AddRange(FindObjectsByType<VisionRangeController>(
            FindObjectsInactive.Include));
    }

    private bool IsDefaultEverywhere(SO_VisionFogConfig config)
    {
        for (int i = 0; i < controllerBuffer.Count; i++)
        {
            VisionRangeController controller = controllerBuffer[i];
            if (controller == null) continue;
            if (GetDefaultConfig(controller) != config) return false;
        }
        return true;
    }

    private static SO_VisionFogConfig GetDefaultConfig(VisionRangeController controller)
    {
        SerializedObject so = new SerializedObject(controller);
        SerializedProperty prop = so.FindProperty("defaultConfig");
        return prop != null ? prop.objectReferenceValue as SO_VisionFogConfig : null;
    }
}
#endif
