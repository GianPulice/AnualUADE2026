using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One checkbox per family of Scene-view gizmos, for the scene it lives in. Everything starts on;
/// untick what is in the way. "Todos" / "Ninguno" in the inspector flip the whole list at once.
///
/// WHY IT EXISTS. With the Nemesis rings, the pressure zones, the tutorial hints and ~300 colliders
/// all drawing together, the Scene view is unreadable and moving the camera drops frames — most of
/// it the Handles.Label calls, which lay out GUI text on every single repaint.
///
/// HOW IT HIDES THEM. Through UnityEditor.GizmoUtility: the same per-type switch as the Scene view's
/// Gizmos dropdown. With it off Unity never calls that type's OnDrawGizmos, so a hidden family costs
/// nothing — a bool checked inside each OnDrawGizmos would still pay for the call on every repaint —
/// and none of the ~30 scripts that draw had to change. It reaches Unity's own gizmos too
/// (colliders, cameras, lights), which no script-side check could.
///
/// SCOPED TO THE SCENE. That switch is editor-wide: left alone, a family hidden here would stay
/// hidden in every other scene with no checkbox anywhere to bring it back. So the manager writes
/// down what IT hid and turns exactly that back on when it goes away (scene closed, component or
/// object disabled, deleted, domain reload). Something hidden by hand from the dropdown never makes
/// the list and is left as it was. The list lives in EditorPrefs, so a crash with the scene open
/// cannot strand it either: GizmoManagerEditor restores it as soon as a scene without a manager is
/// open.
///
/// Editor only. The GameObject is tagged EditorOnly and the component does nothing in a build.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GizmoManager : MonoBehaviour
{
    [Header("Nemesis")]
    [Tooltip("NemesisGizmos: conos de visión, anillos de oído, alcance de captura y búsqueda.")]
    [SerializeField] private bool nemesisRanges = true;

    [Tooltip("NemesisRoute: las polilíneas de patrulla y sus waypoints.")]
    [SerializeField] private bool nemesisRoutes = true;

    [Tooltip("NemesisPressureZone: los cilindros de las zonas de presión y su texto " +
             "(\"ala oeste · r 9 m · 4 waypoints\").")]
    [SerializeField] private bool nemesisPressureZones = true;

    [Tooltip("Debug que aparece al seleccionar: NemesisDirector, NemesisController, " +
             "NemesisElevatorLink, NemesisDoorUser.")]
    [SerializeField] private bool nemesisDebug = true;

    [Header("Nivel y gameplay")]
    [Tooltip("InputHintTrigger: las cajas de los hints del tutorial (\"HINT Player/Sprint → run\").")]
    [SerializeField] private bool inputHints = true;

    [Tooltip("Checkpoint.")]
    [SerializeField] private bool checkpoints = true;

    [Tooltip("WinTrigger: la zona de victoria.")]
    [SerializeField] private bool winTrigger = true;

    [Tooltip("InteractionRangeGizmo: el alcance de interacción del jugador.")]
    [SerializeField] private bool interactionRange = true;

    [Tooltip("Secuencia de escape: EscapeRevealTrigger, EscapeGuideDoor, EscapeFogCycle, " +
             "EscapeCorridorLock, EscapeCorridorFlicker.")]
    [SerializeField] private bool escapeSequence = true;

    [Tooltip("Ascensor: ElevatorCabinNavMesh, ElevatorCallPanel.")]
    [SerializeField] private bool elevator = true;

    [Tooltip("Escondites, items y señuelos: HidingSpot, ItemGlint, DecoyNoiseSource.")]
    [SerializeField] private bool hidingItemsDecoys = true;

    [Header("Audio y render")]
    [Tooltip("Ambiente: AmbienceZone, AmbienceEmitter, AmbiencePlacementResolver.")]
    [SerializeField] private bool ambience = true;

    [Tooltip("Pasos: FootstepSurface, FootstepEmitter.")]
    [SerializeField] private bool footsteps = true;

    [Tooltip("Luz y niebla: LightZone, FogBeacon, FogLightBypass, FogLightBypassPlayerFade, " +
             "VisionRangeController.")]
    [SerializeField] private bool lightAndFog = true;

    [Header("Unity")]
    [Tooltip("Box, Sphere, Capsule y Mesh Collider, y CharacterController. Son las cajas verdes: " +
             "hay unos 300 en esta escena.")]
    [SerializeField] private bool colliders = true;

    [Tooltip("Camera y CinemachineCamera: los frustums (las líneas blancas largas) y sus íconos.")]
    [SerializeField] private bool cameras = true;

    [Tooltip("Light, ReflectionProbe y LightProbeGroup, con sus íconos.")]
    [SerializeField] private bool lights = true;

    [Tooltip("AudioSource, AudioReverbZone y AudioListener, con sus íconos.")]
    [SerializeField] private bool audioSources = true;

    [Tooltip("Componentes de NavMesh: Surface, Link, Modifier, ModifierVolume, Obstacle y Agent. " +
             "La malla del NavMesh en sí se apaga desde el overlay AI Navigation del Scene view " +
             "(Show NavMesh).")]
    [SerializeField] private bool navMesh = true;

    /// <summary>
    /// Every checkbox with the component types it hides. Adding a family is a field above plus one
    /// line here; a script that starts drawing gizmos goes into the family it belongs to.
    /// </summary>
    private IEnumerable<(bool show, Type[] types)> Families()
    {
        yield return (nemesisRanges, new[] { typeof(NemesisGizmos) });
        yield return (nemesisRoutes, new[] { typeof(NemesisRoute) });
        yield return (nemesisPressureZones, new[] { typeof(NemesisPressureZone) });
        yield return (nemesisDebug, new[]
        {
            typeof(NemesisDirector), typeof(NemesisController), typeof(NemesisElevatorLink),
            typeof(NemesisDoorUser),
        });

        yield return (inputHints, new[] { typeof(InputHintTrigger) });
        yield return (checkpoints, new[] { typeof(Checkpoint) });
        yield return (winTrigger, new[] { typeof(WinTrigger) });
        yield return (interactionRange, new[] { typeof(InteractionRangeGizmo) });
        yield return (escapeSequence, new[]
        {
            typeof(EscapeRevealTrigger), typeof(EscapeGuideDoor), typeof(EscapeFogCycle),
            typeof(EscapeCorridorLock), typeof(EscapeCorridorFlicker),
        });
        yield return (elevator, new[] { typeof(ElevatorCabinNavMesh), typeof(ElevatorCallPanel) });
        yield return (hidingItemsDecoys, new[] { typeof(HidingSpot), typeof(ItemGlint), typeof(DecoyNoiseSource) });

        yield return (ambience, new[]
        {
            typeof(AmbienceZone), typeof(AmbienceEmitter), typeof(AmbiencePlacementResolver),
        });
        yield return (footsteps, new[] { typeof(FootstepSurface), typeof(FootstepEmitter) });
        yield return (lightAndFog, new[]
        {
            typeof(LightZone), typeof(FogBeacon), typeof(FogLightBypass),
            typeof(FogLightBypassPlayerFade), typeof(VisionRangeController),
        });

        yield return (colliders, new[]
        {
            typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider), typeof(MeshCollider),
            typeof(CharacterController),
        });
        yield return (cameras, new[] { typeof(Camera), typeof(CinemachineCamera) });
        yield return (lights, new[] { typeof(Light), typeof(ReflectionProbe), typeof(LightProbeGroup) });
        yield return (audioSources, new[] { typeof(AudioSource), typeof(AudioReverbZone), typeof(AudioListener) });
        yield return (navMesh, new[]
        {
            typeof(NavMeshSurface), typeof(NavMeshLink), typeof(NavMeshModifier),
            typeof(NavMeshModifierVolume), typeof(NavMeshObstacle), typeof(NavMeshAgent),
        });
    }

#if UNITY_EDITOR
    private const char KeySeparator = '|';
    private const string GizmoChannel = "gizmo";
    private const string IconChannel = "icon";

    /// <summary>Per project: EditorPrefs is shared by every project on the machine.</summary>
    private static string PrefsKey => "WIRED.GizmoManager.Hidden:" + Application.dataPath;

    private void OnEnable() => ScheduleApply();

    private void OnValidate() => ScheduleApply();

    private void OnDisable() => RestoreHiddenGizmos();

    /// <summary>
    /// Applied on the next editor tick rather than on the spot: OnValidate runs mid-deserialisation,
    /// where touching editor state is not safe, and on scene load it fires together with OnEnable —
    /// one deferred call covers both.
    /// </summary>
    private void ScheduleApply()
    {
        UnityEditor.EditorApplication.delayCall -= Apply;
        UnityEditor.EditorApplication.delayCall += Apply;
    }

    private void Apply()
    {
        // Destroyed or switched off between the scheduling and now: OnDisable already restored.
        if (this == null || !isActiveAndEnabled) return;

        HashSet<string> hidden = LoadHidden();

        foreach ((bool show, Type[] types) in Families())
        {
            foreach (Type type in types)
                SetShown(type, show, hidden);
        }

        SaveHidden(hidden);
        UnityEditor.SceneView.RepaintAll();
    }

    /// <summary>
    /// Hides only what is currently showing, and records it; shows only what this manager recorded.
    /// That pair is what keeps a gizmo someone switched off by hand in the dropdown switched off.
    /// </summary>
    private static void SetShown(Type type, bool show, HashSet<string> hidden)
    {
        // Not registered: the type has never had a gizmo or an icon to draw.
        if (!UnityEditor.GizmoUtility.TryGetGizmoInfo(type, out UnityEditor.GizmoInfo info)) return;

        string gizmoKey = GizmoChannel + KeySeparator + type.AssemblyQualifiedName;
        string iconKey = IconChannel + KeySeparator + type.AssemblyQualifiedName;

        if (!show)
        {
            if (info.hasGizmo && info.gizmoEnabled)
            {
                UnityEditor.GizmoUtility.SetGizmoEnabled(type, false, false);
                hidden.Add(gizmoKey);
            }

            if (info.hasIcon && info.iconEnabled)
            {
                UnityEditor.GizmoUtility.SetIconEnabled(type, false);
                hidden.Add(iconKey);
            }

            return;
        }

        if (hidden.Remove(gizmoKey)) UnityEditor.GizmoUtility.SetGizmoEnabled(type, true, false);
        if (hidden.Remove(iconKey)) UnityEditor.GizmoUtility.SetIconEnabled(type, true);
    }

    /// <summary>
    /// Turns back on everything a GizmoManager hid and clears the record. Called from OnDisable, and
    /// by GizmoManagerEditor when a scene opens with no manager in it (the crash case).
    /// </summary>
    public static void RestoreHiddenGizmos()
    {
        HashSet<string> hidden = LoadHidden();
        if (hidden.Count == 0) return;

        foreach (string key in hidden)
        {
            int split = key.IndexOf(KeySeparator);
            if (split < 0) continue;

            // Renamed or deleted since it was hidden: nothing left to show.
            Type type = Type.GetType(key.Substring(split + 1));
            if (type == null) continue;

            if (key.StartsWith(GizmoChannel + KeySeparator)) UnityEditor.GizmoUtility.SetGizmoEnabled(type, true, false);
            else UnityEditor.GizmoUtility.SetIconEnabled(type, true);
        }

        UnityEditor.EditorPrefs.DeleteKey(PrefsKey);
        UnityEditor.SceneView.RepaintAll();
    }

    private static HashSet<string> LoadHidden()
    {
        string raw = UnityEditor.EditorPrefs.GetString(PrefsKey, string.Empty);
        return new HashSet<string>(raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void SaveHidden(HashSet<string> hidden)
    {
        if (hidden.Count == 0) UnityEditor.EditorPrefs.DeleteKey(PrefsKey);
        else UnityEditor.EditorPrefs.SetString(PrefsKey, string.Join("\n", hidden));
    }
#endif
}
