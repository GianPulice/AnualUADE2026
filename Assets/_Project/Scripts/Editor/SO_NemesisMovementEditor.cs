using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_NemesisMovement"/>. The four state speeds mean nothing on their own
/// — "chase speed 4" only matters relative to how fast the player can move away from it, and that
/// second half of the comparison lives on a completely different asset
/// (<see cref="SO_Movement"/>). This puts both on one set of bars, at one scale, so "does it catch
/// me" stops being mental arithmetic across two inspectors.
///
/// Same shape as <see cref="SO_MovementEditor"/>'s "Velocidad real" section, mirrored: that one
/// shows the player healthy vs. penalised: this one shows the Nemesis's four states against the
/// player's three, healthy AND penalised. The penalised row is the one that matters — a chase
/// speed that loses to a healthy sprint but wins against a legs-penalised one is the entire point
/// of the M1 module, and the only way to see that at a glance is having both bars in the same
/// picture.
///
/// Asset-level lookups only (<see cref="AssetDatabase.FindAssets"/> for the player's SO_Movement
/// and the Legs ModuleData), same as <see cref="SO_MovementEditor.FindLegsModule"/> — no scene
/// dependency, so this works with nothing loaded.
///
/// Labels in Spanish to match the other custom inspectors in this project.
/// </summary>
[CustomEditor(typeof(SO_NemesisMovement))]
public class SO_NemesisMovementEditor : Editor
{
    private SO_Movement playerMovement;
    private ModuleData legsModule;

    private void OnEnable()
    {
        playerMovement = FindPlayerMovement();
        legsModule = FindLegsModule();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_NemesisMovement movement = (SO_NemesisMovement)target;

        PlayerDiagramGUI.SectionHeader("Velocidad: Nemesis vs. jugador");
        DrawComparison(movement);
    }

    private void DrawComparison(SO_NemesisMovement nemesis)
    {
        if (playerMovement == null)
        {
            EditorGUILayout.HelpBox(
                "No se encontró ningún SO_Movement en el proyecto — sin eso no hay velocidad de " +
                "jugador contra la cual comparar.", MessageType.Warning);
            return;
        }

        float playerSprint = playerMovement.MoveSpeed * playerMovement.SprintSpeedMultiplier;
        float playerWalk = playerMovement.MoveSpeed;
        float playerCrouch = playerMovement.MoveSpeed * playerMovement.CrouchSpeedMultiplier;

        float legsFactor = legsModule != null ? Mathf.Clamp01(legsModule.CojeraMultiplier) : 1f;
        bool showPenalty = legsModule != null && legsFactor < 1f;

        // One shared scale across every bar in every group, healthy or not: a Nemesis bar and a
        // player bar are only comparable if neither has been silently renormalised to its own
        // group's max. Whatever the current fastest thing in the picture is sets the ruler.
        float scaleMax = Mathf.Max(nemesis.ChaseSpeed, nemesis.PatrolSpeed, nemesis.InvestigationSpeed,
                                   nemesis.SearchSpeed, playerSprint);

        const float RowHeight = 18f;
        float height = 18f + RowHeight * 4f              // Nemesis
                      + 12f + 15f + RowHeight * 3f        // Player, healthy
                      + (showPenalty ? 12f + 15f + RowHeight * 3f : 0f);

        Rect canvas = PlayerDiagramGUI.Canvas(height);
        float y = canvas.y;

        y = DrawGroup(canvas, y, "Nemesis", PlayerDiagramGUI.Bad, scaleMax, RowHeight,
                     ("patrulla", nemesis.PatrolSpeed),
                     ("investigación", nemesis.InvestigationSpeed),
                     ("persecución", nemesis.ChaseSpeed),
                     ("búsqueda", nemesis.SearchSpeed));

        y += 12f;
        PlayerDiagramGUI.HLine(canvas.x, canvas.xMax, y, PlayerDiagramGUI.Floor);
        y += 6f;

        y = DrawGroup(canvas, y, "jugador (sano)", PlayerDiagramGUI.Standing, scaleMax, RowHeight,
                     ("correr", playerSprint), ("caminar", playerWalk), ("agachado", playerCrouch));

        if (showPenalty)
        {
            y += 12f;
            PlayerDiagramGUI.HLine(canvas.x, canvas.xMax, y, PlayerDiagramGUI.Floor);
            y += 6f;

            DrawGroup(canvas, y, $"jugador (M1 explotado, ×{legsFactor:0.##})", PlayerDiagramGUI.Crouched,
                     scaleMax, RowHeight,
                     ("correr", playerSprint * legsFactor), ("caminar", playerWalk * legsFactor),
                     ("agachado", playerCrouch * legsFactor));
        }
        else if (legsModule == null)
        {
            EditorGUILayout.LabelField(
                "No se encontró ningún ModuleData con penalidad Legs, así que no se dibuja la fila penalizada.",
                EditorStyles.wordWrappedMiniLabel);
        }

        DrawVerdicts(nemesis.ChaseSpeed, playerSprint, showPenalty ? playerSprint * legsFactor : (float?)null);
    }

    private static float DrawGroup(Rect canvas, float y, string title, Color color, float scaleMax,
                                   float rowHeight, params (string label, float value)[] rows)
    {
        PlayerDiagramGUI.Text(new Rect(canvas.x, y, canvas.width, 14f), title, PlayerDiagramGUI.Muted);
        y += 15f;

        foreach ((string label, float value) in rows)
        {
            PlayerDiagramGUI.Bar(new Rect(canvas.x, y, canvas.width, rowHeight), label, value,
                                 scaleMax, color, $"{value:0.00} m/s");
            y += rowHeight;
        }

        return y;
    }

    /// <summary>
    /// The one comparison the whole section exists for: can the player outrun a chase, healthy and
    /// (if a legs penalty applies) hobbled. "Realmente peligroso" is a direct callback to how this
    /// case was originally described — a chase speed that only beats a healthy sprint is not
    /// dangerous, it is expected; one that also beats the PENALISED sprint is the situation that
    /// actually needs a second look.
    /// </summary>
    private static void DrawVerdicts(float chaseSpeed, float healthySprint, float? penalisedSprint)
    {
        EditorGUILayout.Space(6);

        bool escapesHealthy = chaseSpeed < healthySprint;
        PlayerDiagramGUI.Verdict(escapesHealthy,
            escapesHealthy
                ? $"Corriendo sano ({healthySprint:0.00} m/s) le ganás a la persecución ({chaseSpeed:0.00} m/s)"
                : $"La persecución ({chaseSpeed:0.00} m/s) te alcanza aunque corras sano ({healthySprint:0.00} m/s) — ni corriendo escapás");

        if (penalisedSprint == null) return;

        bool escapesPenalised = chaseSpeed < penalisedSprint.Value;
        PlayerDiagramGUI.Verdict(escapesPenalised,
            escapesPenalised
                ? $"Con M1 explotado seguís ganándole corriendo ({penalisedSprint.Value:0.00} vs {chaseSpeed:0.00} m/s)"
                : $"Con M1 explotado la persecución te alcanza incluso corriendo ({penalisedSprint.Value:0.00} vs {chaseSpeed:0.00} m/s) — el caso realmente peligroso");
    }

    // Lookups =================================================================================

    private static SO_Movement FindPlayerMovement()
    {
        string[] guids = AssetDatabase.FindAssets("t:SO_Movement");
        if (guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<SO_Movement>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>Same lookup as <see cref="SO_MovementEditor"/>'s private one, duplicated rather
    /// than shared: it is ten lines, and the two editors are not otherwise coupled.</summary>
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
