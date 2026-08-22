using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_Movement"/>. Three things the raw fields cannot tell you:
///
/// <list type="bullet">
/// <item><b>Alturas</b> — the standing and crouched capsules drawn to scale against a gap height
/// you type in, so "does the player fit under that container" stops being mental arithmetic.</item>
/// <item><b>Velocidad real</b> — the multipliers resolved into m/s, including where they land once
/// the legs module (M1) has exploded. A 0.45 multiplier means nothing on its own.</item>
/// <item><b>Escena</b> — buttons that push either stance onto the player loaded in the scene, so
/// the capsule can be eyeballed against the real geometry without entering Play.</item>
/// </list>
///
/// The gap height lives in EditorPrefs and not on the asset: it is a measuring stick for whoever
/// is tuning, not data the game ships with.
///
/// Labels in Spanish to match the other custom inspectors in this project.
/// </summary>
[CustomEditor(typeof(SO_Movement))]
public class SO_MovementEditor : Editor
{
    private const string GapPrefKey = "WIRED.SO_MovementEditor.GapHeight";
    private const float DefaultGapHeight = 1f;

    // Only used to draw the silhouette when there is no player loaded to read the real one from.
    private const float FallbackCapsuleRadius = 0.3f;

    private static readonly Color ApplyColor   = new Color(0.3f, 0.75f, 0.45f);
    private static readonly Color PreviewColor = new Color(0.45f, 0.6f, 0.85f);

    private ModuleData legsModule;

    private void OnEnable() => legsModule = FindLegsModule();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_Movement movement = (SO_Movement)target;

        PlayerDiagramGUI.SectionHeader("Alturas");
        DrawStanceDiagram(movement);

        PlayerDiagramGUI.SectionHeader("Velocidad real");
        DrawSpeedBars(movement);

        PlayerDiagramGUI.SectionHeader("Escena");
        DrawSceneButtons(movement);
    }

    // Alturas ================================================================================

    private void DrawStanceDiagram(SO_Movement movement)
    {
        float gap = EditorPrefs.GetFloat(GapPrefKey, DefaultGapHeight);

        CapsuleCollider capsule = ResolveCapsule(PlayerDiagramGUI.FindLoadedPlayer());
        float radius = capsule != null ? capsule.radius : FallbackCapsuleRadius;

        Rect canvas = PlayerDiagramGUI.Canvas(196f);
        float groundY = canvas.yMax - 14f;
        float usable = groundY - canvas.y - 18f;
        float tallest = Mathf.Max(movement.StandingHeight, gap);
        float pxPerMetre = usable / Mathf.Max(0.01f, tallest * 1.1f);

        PlayerDiagramGUI.HLine(canvas.x, canvas.xMax, groundY, PlayerDiagramGUI.Floor, 2f);
        PlayerDiagramGUI.Text(new Rect(canvas.xMax - 40f, groundY + 1f, 40f, 13f), "piso",
                              PlayerDiagramGUI.Muted, TextAnchor.MiddleRight);

        float halfWidth = Mathf.Max(7f, radius * pxPerMetre);
        DrawStance(canvas.x + canvas.width * 0.24f, groundY, movement.StandingHeight, pxPerMetre,
                   halfWidth, PlayerDiagramGUI.Standing, "parado");
        DrawStance(canvas.x + canvas.width * 0.50f, groundY, movement.CrouchHeight, pxPerMetre,
                   halfWidth, PlayerDiagramGUI.Crouched, "agachado");

        // The gap crosses both silhouettes rather than sitting beside them: the whole question is
        // which of the two pokes through it.
        float gapY = groundY - gap * pxPerMetre;
        PlayerDiagramGUI.DashedHLine(canvas.x, canvas.xMax, gapY, PlayerDiagramGUI.Accent);
        PlayerDiagramGUI.Text(new Rect(canvas.xMax - 170f, gapY - 15f, 170f, 14f),
                              $"hueco {gap:0.00} m", PlayerDiagramGUI.Accent,
                              TextAnchor.MiddleRight, FontStyle.Bold);

        EditorGUI.BeginChangeCheck();
        float newGap = EditorGUILayout.Slider(
            new GUIContent("Hueco a superar (m)",
                           "Altura libre del hueco que estas probando (container, ducto, reja). " +
                           "Se guarda en EditorPrefs, no en el asset: es una regla de medir, no " +
                           "un dato del juego."),
            gap, 0.3f, 3f);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetFloat(GapPrefKey, newGap);

        DrawHeightVerdicts(movement, newGap);
    }

    private static void DrawStance(float centreX, float groundY, float height, float pxPerMetre,
                                   float halfWidth, Color colour, string caption)
    {
        float pixels = height * pxPerMetre;
        Rect body = new Rect(centreX - halfWidth, groundY - pixels, halfWidth * 2f, pixels);

        PlayerDiagramGUI.Box(body, new Color(colour.r, colour.g, colour.b, 0.28f));
        PlayerDiagramGUI.Outline(body, colour, 1.5f);

        // The head line sticks out past the body so it lines up against the gap line without the
        // eye having to bridge two separate edges.
        PlayerDiagramGUI.HLine(body.xMin - 6f, body.xMax + 6f, body.yMin, colour, 2f);

        PlayerDiagramGUI.Text(new Rect(centreX - 50f, body.yMin - 16f, 100f, 14f),
                              $"{height:0.00} m", colour, TextAnchor.MiddleCenter, FontStyle.Bold);
        PlayerDiagramGUI.Text(new Rect(centreX - 50f, groundY + 1f, 100f, 13f), caption,
                              colour, TextAnchor.MiddleCenter);
    }

    private static void DrawHeightVerdicts(SO_Movement movement, float gap)
    {
        if (movement.CrouchHeight >= movement.StandingHeight)
        {
            EditorGUILayout.HelpBox(
                $"Crouch Height ({movement.CrouchHeight:0.00}) no es menor que Standing Height " +
                $"({movement.StandingHeight:0.00}). Agacharse no achica la capsula.",
                MessageType.Error);
            return;
        }

        float crouchClearance = gap - movement.CrouchHeight;
        PlayerDiagramGUI.Verdict(crouchClearance >= 0f,
            crouchClearance >= 0f
                ? $"Entra agachado, con {crouchClearance:0.00} m de aire"
                : $"NO entra agachado: la capsula pasa el hueco por {-crouchClearance:0.00} m");

        float standClearance = gap - movement.StandingHeight;
        EditorGUILayout.LabelField(
            standClearance >= 0f
                ? $"Parado tambien entra ({standClearance:0.00} m de aire): este hueco no obliga a agacharse."
                : $"Parado no entra por {-standClearance:0.00} m: el hueco obliga a agacharse, que es la idea.",
            EditorStyles.wordWrappedMiniLabel);
    }

    // Velocidad real =========================================================================

    private void DrawSpeedBars(SO_Movement movement)
    {
        float sprint = movement.MoveSpeed * movement.SprintSpeedMultiplier;
        float walk   = movement.MoveSpeed;
        float crouch = movement.MoveSpeed * movement.CrouchSpeedMultiplier;

        float legsFactor = legsModule != null ? Mathf.Clamp01(legsModule.CojeraMultiplier) : 1f;
        bool showPenalty = legsModule != null && legsFactor < 1f;

        const float RowHeight = 18f;
        Rect canvas = PlayerDiagramGUI.Canvas(showPenalty ? 156f : 82f);
        float y = canvas.y;

        // Both groups are scaled against the healthy sprint, so the penalised group visibly
        // shrinks instead of being renormalised back to full width.
        PlayerDiagramGUI.Text(new Rect(canvas.x, y, canvas.width, 14f), "sano",
                              PlayerDiagramGUI.Muted);
        y += 15f;
        y = DrawSpeedGroup(canvas, y, RowHeight, sprint, walk, crouch, sprint,
                           PlayerDiagramGUI.Standing);

        if (showPenalty)
        {
            y += 6f;
            PlayerDiagramGUI.HLine(canvas.x, canvas.xMax, y, PlayerDiagramGUI.Floor);
            y += 6f;
            PlayerDiagramGUI.Text(new Rect(canvas.x, y, canvas.width, 14f),
                                  $"con {legsModule.ModuleLogLabel} explotado (x{legsFactor:0.00})",
                                  PlayerDiagramGUI.Bad);
            y += 15f;
            DrawSpeedGroup(canvas, y, RowHeight, sprint * legsFactor, walk * legsFactor,
                           crouch * legsFactor, sprint, PlayerDiagramGUI.Bad);
        }
        else if (legsModule == null)
        {
            EditorGUILayout.LabelField(
                "No se encontro ningun ModuleData con penalidad Legs, asi que no se dibuja la fila penalizada.",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private static float DrawSpeedGroup(Rect canvas, float y, float rowHeight,
                                        float sprint, float walk, float crouch,
                                        float scaleMax, Color colour)
    {
        PlayerDiagramGUI.Bar(new Rect(canvas.x, y, canvas.width, rowHeight), "correr",
                             sprint, scaleMax, colour, $"{sprint:0.00} m/s");
        y += rowHeight;
        PlayerDiagramGUI.Bar(new Rect(canvas.x, y, canvas.width, rowHeight), "caminar",
                             walk, scaleMax, colour, $"{walk:0.00} m/s");
        y += rowHeight;
        PlayerDiagramGUI.Bar(new Rect(canvas.x, y, canvas.width, rowHeight), "agachado",
                             crouch, scaleMax, colour, $"{crouch:0.00} m/s");
        return y + rowHeight;
    }

    // Escena =================================================================================

    private void DrawSceneButtons(SO_Movement movement)
    {
        CapsuleCollider capsule = ResolveCapsule(PlayerDiagramGUI.FindLoadedPlayer());

        if (capsule == null)
        {
            // The player lives in a gameplay scene loaded additively, so this is the normal state
            // while working on any other scene on its own, not an error.
            EditorGUILayout.HelpBox(
                "No hay ningun player con CapsuleCollider en las escenas cargadas.\n\n" +
                "Abri (o carga en aditivo) la escena que lo contiene y aparecen los botones.",
                MessageType.Info);
            return;
        }

        // In Play mode PlayerCrouchState rewrites the capsule on every stance change, so a button
        // here would be undone within the frame. Nothing broken, just nothing worth offering.
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = ApplyColor;
            if (GUILayout.Button("Aplicar altura parado", GUILayout.Height(26)))
                ApplyHeight(capsule, movement.StandingHeight, "Aplicar altura parado");

            GUI.backgroundColor = PreviewColor;
            if (GUILayout.Button("Previsualizar agachado", GUILayout.Height(26)))
                ApplyHeight(capsule, movement.CrouchHeight, "Previsualizar agachado");

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        DrawCapsuleStatus(capsule, movement);
    }

    private static void DrawCapsuleStatus(CapsuleCollider capsule, SO_Movement movement)
    {
        float height = capsule.height;
        string stance;
        if (Mathf.Abs(height - movement.CrouchHeight) < 0.001f) stance = "agachado";
        else if (Mathf.Abs(height - movement.StandingHeight) < 0.001f) stance = "parado";
        else stance = "no coincide con ninguna de las dos alturas del SO";

        EditorGUILayout.LabelField($"Capsula en escena: {height:0.00} m ({stance})",
                                   EditorStyles.miniLabel);

        if (Application.isPlaying) return;

        EditorGUILayout.HelpBox(
            "Los botones escriben en la escena y la ensucian. Todo pasa por Undo (Ctrl+Z), pero " +
            "acordate de volver a \"parado\" antes de guardar si estabas previsualizando.",
            MessageType.Warning);
    }

    private static void ApplyHeight(CapsuleCollider capsule, float height, string undoLabel)
    {
        Undo.RecordObject(capsule, undoLabel);
        capsule.height = height;
        // Centre is always half the height, the same rule PlayerCrouchState follows, so the
        // capsule stays sitting on the floor instead of sinking into it.
        capsule.center = new Vector3(capsule.center.x, height * 0.5f, capsule.center.z);
    }

    // Lookups ================================================================================

    /// <summary>
    /// Serialized reference first, then the component. Outside Play mode PlayerStateManager has
    /// not run its Awake resolve pass yet, so the field can legitimately still be empty.
    /// </summary>
    private static CapsuleCollider ResolveCapsule(PlayerStateManager player)
    {
        if (player == null) return null;
        return player.CapsuleColl != null ? player.CapsuleColl : player.GetComponent<CapsuleCollider>();
    }

    private static ModuleData FindLegsModule()
    {
        string[] guids = AssetDatabase.FindAssets("t:ModuleData");
        for (int i = 0; i < guids.Length; i++)
        {
            ModuleData data = AssetDatabase.LoadAssetAtPath<ModuleData>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (data != null && data.Penalty == PenaltyType.Legs) return data;
        }
        return null;
    }
}
#endif
