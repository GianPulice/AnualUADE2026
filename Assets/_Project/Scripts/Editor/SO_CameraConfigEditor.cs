using UnityEngine;
#if UNITY_EDITOR
using Unity.Cinemachine;
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_CameraConfig"/>. Crouch Pivot Drop is a number whose only real
/// meaning is geometric — where the camera ends up relative to the crouched character's head —
/// so this draws that geometry from the side instead of asking you to picture it.
///
/// The diagram reads the real rig out of the loaded scene when there is one (pivot height, orbit
/// distance, capsule heights) and falls back to the prefab's authored values when there is not,
/// saying so rather than quietly drawing a fiction.
///
/// Preview state lives in <see cref="SessionState"/> and not EditorPrefs: it is throwaway, and it
/// should not survive closing Unity with a scene left mid-preview.
///
/// Labels in Spanish to match the other custom inspectors in this project.
/// </summary>
[CustomEditor(typeof(SO_CameraConfig))]
public class SO_CameraConfigEditor : Editor
{
    private const string PreviewingKey = "WIRED.SO_CameraConfigEditor.Previewing";
    private const string StandingYKey  = "WIRED.SO_CameraConfigEditor.StandingPivotY";

    private const float FallbackStandingPivotY = 1.6f;
    private const float FallbackStandingHeight = 1.8f;
    private const float FallbackCrouchHeight   = 0.9f;
    private const float FallbackCapsuleRadius  = 0.3f;
    private const float FallbackOrbitRadius    = 2f;
    private const float FallbackOrbitHeight    = 1f;

    private static readonly Color ApplyColor   = new Color(0.3f, 0.75f, 0.45f);
    private static readonly Color PreviewColor = new Color(0.45f, 0.6f, 0.85f);

    /// <summary>Everything the diagram needs, resolved from the scene or from fallbacks.</summary>
    private struct Rig
    {
        public Transform Pivot;
        public float StandingPivotY;
        public float StandingHeight;
        public float CrouchHeight;
        public float CapsuleRadius;
        public float OrbitRadius;
        public float OrbitHeight;
        public bool FromScene;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_CameraConfig config = (SO_CameraConfig)target;
        Rig rig = ReadRig();

        PlayerDiagramGUI.SectionHeader("Encuadre lateral");
        DrawDiagram(config, rig);

        PlayerDiagramGUI.SectionHeader("Escena");
        DrawSceneButtons(config, rig);
    }

    // Diagrama ===============================================================================

    private static void DrawDiagram(SO_CameraConfig config, Rig rig)
    {
        float crouchPivotY = Mathf.Max(0f, rig.StandingPivotY - config.CrouchPivotDrop);

        Rect canvas = PlayerDiagramGUI.Canvas(214f);
        float groundY = canvas.yMax - 14f;
        float usableH = groundY - canvas.y - 18f;

        // Scale has to satisfy both axes: the tallest thing on screen is either the standing
        // capsule or the camera itself, and horizontally the whole orbit distance has to fit.
        float topMetres = Mathf.Max(rig.StandingHeight, rig.StandingPivotY + rig.OrbitHeight) * 1.12f;
        float pxV = usableH / Mathf.Max(0.01f, topMetres);
        float pxH = canvas.width * 0.55f / Mathf.Max(0.01f, rig.OrbitRadius * 1.15f);
        float px = Mathf.Min(pxV, pxH);

        float playerX = canvas.xMax - canvas.width * 0.28f;

        PlayerDiagramGUI.HLine(canvas.x, canvas.xMax, groundY, PlayerDiagramGUI.Floor, 2f);
        PlayerDiagramGUI.Text(new Rect(canvas.xMax - 40f, groundY + 1f, 40f, 13f), "piso",
                              PlayerDiagramGUI.Muted, TextAnchor.MiddleRight);

        float halfWidth = Mathf.Max(6f, rig.CapsuleRadius * px);

        // Standing is outline-only: this diagram is about the crouched pose, standing is the
        // reference the drop is measured from.
        Rect standingBody = new Rect(playerX - halfWidth, groundY - rig.StandingHeight * px,
                                     halfWidth * 2f, rig.StandingHeight * px);
        PlayerDiagramGUI.Outline(standingBody, new Color(PlayerDiagramGUI.Standing.r,
                                                         PlayerDiagramGUI.Standing.g,
                                                         PlayerDiagramGUI.Standing.b, 0.55f));

        Rect crouchBody = new Rect(playerX - halfWidth, groundY - rig.CrouchHeight * px,
                                   halfWidth * 2f, rig.CrouchHeight * px);
        PlayerDiagramGUI.Box(crouchBody, new Color(PlayerDiagramGUI.Crouched.r,
                                                   PlayerDiagramGUI.Crouched.g,
                                                   PlayerDiagramGUI.Crouched.b, 0.30f));
        PlayerDiagramGUI.Outline(crouchBody, PlayerDiagramGUI.Crouched, 1.5f);
        PlayerDiagramGUI.HLine(crouchBody.xMin - 6f, crouchBody.xMax + 6f, crouchBody.yMin,
                               PlayerDiagramGUI.Crouched, 2f);

        float standingPivotScreenY = groundY - rig.StandingPivotY * px;
        float crouchPivotScreenY = groundY - crouchPivotY * px;

        // Camera sits behind the player at the neutral (centre) orbit. The real rig rides a
        // 3-orbit spline driven by the look axis, so this is the resting position, not a promise.
        Vector2 cameraPos = new Vector2(playerX - rig.OrbitRadius * px,
                                        crouchPivotScreenY - rig.OrbitHeight * px);
        Vector2 aimPos = new Vector2(playerX, crouchPivotScreenY);
        DrawFovCone(cameraPos, aimPos, config.Fov);

        PlayerDiagramGUI.Box(new Rect(cameraPos.x - 6f, cameraPos.y - 4.5f, 12f, 9f),
                             PlayerDiagramGUI.Accent);
        PlayerDiagramGUI.Text(new Rect(cameraPos.x - 70f, cameraPos.y - 20f, 140f, 14f),
                              $"camara  FOV {config.Fov:0}°", PlayerDiagramGUI.Accent,
                              TextAnchor.MiddleCenter);

        DrawPivotMarker(playerX, standingPivotScreenY, PlayerDiagramGUI.Standing,
                        $"pivote {rig.StandingPivotY:0.00}  parado", canvas);
        DrawPivotMarker(playerX, crouchPivotScreenY, PlayerDiagramGUI.Crouched,
                        $"pivote {crouchPivotY:0.00}  agachado", canvas);

        PlayerDiagramGUI.VMeasure(playerX + halfWidth + 16f, standingPivotScreenY,
                                  crouchPivotScreenY, PlayerDiagramGUI.Accent,
                                  $"drop {config.CrouchPivotDrop:0.00}", 90f);

        DrawDiagramVerdicts(config, rig, crouchPivotY);
    }

    private static void DrawPivotMarker(float x, float y, Color colour, string label, Rect canvas)
    {
        PlayerDiagramGUI.DashedHLine(canvas.x, x, y, new Color(colour.r, colour.g, colour.b, 0.5f));
        PlayerDiagramGUI.Box(new Rect(x - 4f, y - 4f, 8f, 8f), colour);
        PlayerDiagramGUI.Text(new Rect(canvas.x, y - 15f, 190f, 14f), label, colour);
    }

    private static void DrawFovCone(Vector2 from, Vector2 to, float fovDegrees)
    {
        Vector2 dir = to - from;
        if (dir.sqrMagnitude < 0.001f) return;

        float length = dir.magnitude * 1.5f;
        dir.Normalize();

        Color cone = new Color(PlayerDiagramGUI.Accent.r, PlayerDiagramGUI.Accent.g,
                               PlayerDiagramGUI.Accent.b, 0.45f);
        float half = fovDegrees * 0.5f;

        PlayerDiagramGUI.Line(from, from + Rotate(dir, half) * length, cone);
        PlayerDiagramGUI.Line(from, from + Rotate(dir, -half) * length, cone);
        PlayerDiagramGUI.Line(from, to, new Color(cone.r, cone.g, cone.b, 0.28f), 1f);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private static void DrawDiagramVerdicts(SO_CameraConfig config, Rig rig, float crouchPivotY)
    {
        if (!rig.FromScene)
        {
            EditorGUILayout.HelpBox(
                "No hay ningun player en las escenas cargadas: el diagrama usa los valores por " +
                "defecto de la prefab (pivote 1.60, capsulas 1.80 / 0.90). Carga la escena del " +
                "player para verlo con los numeros reales.",
                MessageType.Info);
        }

        float clearance = crouchPivotY - rig.CrouchHeight;
        if (clearance >= 0f)
        {
            PlayerDiagramGUI.Verdict(true,
                $"Pivote {clearance:0.00} m por encima de la cabeza agachada");
        }
        else
        {
            PlayerDiagramGUI.Verdict(false,
                $"Pivote {-clearance:0.00} m POR DEBAJO de la cabeza agachada: la camara mira " +
                $"al pecho y el personaje tapa el encuadre");
        }

        float standingClearance = rig.StandingPivotY - rig.StandingHeight;
        EditorGUILayout.LabelField(
            $"De pie el pivote esta {Mathf.Abs(standingClearance):0.00} m " +
            (standingClearance >= 0f ? "por encima" : "por debajo") +
            " de la cabeza. Un drop que mantenga esa misma relacion seria " +
            $"{rig.StandingPivotY - (rig.CrouchHeight + standingClearance):0.00}.",
            EditorStyles.wordWrappedMiniLabel);
    }

    // Escena =================================================================================

    private static void DrawSceneButtons(SO_CameraConfig config, Rig rig)
    {
        if (rig.Pivot == null)
        {
            EditorGUILayout.HelpBox(
                "No hay ningun player con Tracking Target de Cinemachine en las escenas cargadas.\n\n" +
                "Abri (o carga en aditivo) la escena que lo contiene y aparecen los botones.",
                MessageType.Info);
            return;
        }

        bool previewing = SessionState.GetBool(PreviewingKey, false);

        // In Play mode PlayerCameraController drives the pivot every frame, so previewing here
        // would be overwritten instantly. That is also the better workflow: in Play the slider
        // above is already live.
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(previewing))
            {
                GUI.backgroundColor = PreviewColor;
                if (GUILayout.Button("Previsualizar agachado", GUILayout.Height(26)))
                    StartPreview(rig.Pivot, config.CrouchPivotDrop);
            }

            using (new EditorGUI.DisabledScope(!previewing))
            {
                GUI.backgroundColor = ApplyColor;
                if (GUILayout.Button("Volver a parado", GUILayout.Height(26)))
                    EndPreview(rig.Pivot);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField(
            $"Pivote en escena: {rig.Pivot.localPosition.y:0.00} m " +
            (previewing ? "(previsualizando agachado)" : "(parado)"),
            EditorStyles.miniLabel);

        if (Application.isPlaying) return;

        if (previewing)
        {
            EditorGUILayout.HelpBox(
                "Estas previsualizando: el pivote de la escena esta bajado. Volve a \"parado\" " +
                "antes de guardar o de entrar a Play — PlayerCameraController toma la altura " +
                "actual como la de pie al arrancar, y quedaria calibrado mal.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "El boton escribe en la escena y la ensucia. Pasa por Undo (Ctrl+Z).",
                MessageType.Info);
        }
    }

    private static void StartPreview(Transform pivot, float drop)
    {
        SessionState.SetFloat(StandingYKey, pivot.localPosition.y);
        SessionState.SetBool(PreviewingKey, true);

        Undo.RecordObject(pivot, "Previsualizar agachado");
        Vector3 local = pivot.localPosition;
        local.y -= drop;
        pivot.localPosition = local;
    }

    private static void EndPreview(Transform pivot)
    {
        // Restores the captured height rather than adding the drop back: the drop may well have
        // been dragged in the inspector while previewing, and re-adding the new one would leave
        // the pivot somewhere it never was.
        float standing = SessionState.GetFloat(StandingYKey, FallbackStandingPivotY);

        Undo.RecordObject(pivot, "Volver a parado");
        Vector3 local = pivot.localPosition;
        local.y = standing;
        pivot.localPosition = local;

        SessionState.SetBool(PreviewingKey, false);
    }

    // Lookups ================================================================================

    private static Rig ReadRig()
    {
        Rig rig = new Rig
        {
            StandingPivotY = FallbackStandingPivotY,
            StandingHeight = FallbackStandingHeight,
            CrouchHeight   = FallbackCrouchHeight,
            CapsuleRadius  = FallbackCapsuleRadius,
            OrbitRadius    = FallbackOrbitRadius,
            OrbitHeight    = FallbackOrbitHeight,
        };

        PlayerStateManager player = PlayerDiagramGUI.FindLoadedPlayer();
        if (player == null) return rig;

        rig.FromScene = true;

        if (player.Movement != null)
        {
            rig.StandingHeight = player.Movement.StandingHeight;
            rig.CrouchHeight = player.Movement.CrouchHeight;
        }

        CapsuleCollider capsule = player.CapsuleColl != null
            ? player.CapsuleColl : player.GetComponent<CapsuleCollider>();
        if (capsule != null) rig.CapsuleRadius = capsule.radius;

        CinemachineCamera cam = player.GetComponentInChildren<CinemachineCamera>(true);
        if (cam == null) return rig;

        rig.Pivot = cam.Follow;
        if (rig.Pivot != null)
        {
            // While previewing, the live localPosition is the crouched one — the standing height
            // is the value captured when the preview started.
            rig.StandingPivotY = SessionState.GetBool(PreviewingKey, false)
                ? SessionState.GetFloat(StandingYKey, FallbackStandingPivotY)
                : rig.Pivot.localPosition.y;
        }

        CinemachineOrbitalFollow orbital = cam.GetComponent<CinemachineOrbitalFollow>();
        if (orbital != null)
        {
            rig.OrbitRadius = Mathf.Max(0.1f, orbital.Orbits.Center.Radius);
            rig.OrbitHeight = orbital.Orbits.Center.Height;
        }

        return rig;
    }
}
#endif
