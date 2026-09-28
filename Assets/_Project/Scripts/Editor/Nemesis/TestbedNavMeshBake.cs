using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// The NavMesh bake the testbed builders share (NemesisTestbedBugLabBuilder, NemesisTestbedDropLabBuilder):
/// NavMeshSurface's own build, collected from ONE scene's roots only, and installed the way the Bake
/// button installs it.
///
/// WHY NOT THE BAKE BUTTON. In the editor, a surface that collects "All" gathers from the whole main
/// stage, which is every loaded scene: with Zona1 open, the Bake button (and
/// NavMeshAssetManager.StartBakingSurfaces, which is the same thing) bakes Zona1's floors, walls and
/// modifier volumes into the testbed's NavMesh, on top of it, at the same coordinates. So the sources
/// are collected root by root from this scene only, with the surface's own settings, and built
/// synchronously — which also means the scene is only saved once the NavMesh it references exists.
///
/// Moved out of the Bug Lab builder when the Drop Lab needed the same bake: two copies of it would
/// drift, and whichever builder ran last would decide what the testbed's NavMesh is.
/// </summary>
internal static class TestbedNavMeshBake
{
    /// <summary>
    /// Same settings, same markups, same agent/obstacle filtering, same modifier volumes and the same
    /// bounds rule as the package — the one difference is that other loaded scenes are not part of it.
    /// </summary>
    public static NavMeshData BakeSceneOnly(Scene scene, NavMeshSurface surface, StringBuilder log)
    {
        int mask = surface.layerMask.value;
        int agentType = surface.agentTypeID;

        // No public getter in this version of the package.
        SerializedProperty linksProperty = new SerializedObject(surface).FindProperty("m_GenerateLinks");
        bool generateLinks = linksProperty != null && linksProperty.boolValue;

        var markups = new List<NavMeshBuildMarkup>();
        foreach (NavMeshModifier modifier in NavMeshModifier.activeModifiers)
        {
            if (modifier == null || modifier.gameObject.scene != scene) continue;
            if ((mask & (1 << modifier.gameObject.layer)) == 0 || !modifier.AffectsAgentType(agentType)) continue;

            markups.Add(new NavMeshBuildMarkup
            {
                root = modifier.transform,
                overrideArea = modifier.overrideArea,
                area = modifier.area,
                ignoreFromBuild = modifier.ignoreFromBuild,
                applyToChildren = modifier.applyToChildren,
                overrideGenerateLinks = modifier.overrideGenerateLinks,
                generateLinks = modifier.generateLinks,
            });
        }

        var sources = new List<NavMeshBuildSource>();
        var fromRoot = new List<NavMeshBuildSource>();
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (!rootObject.activeInHierarchy) continue;

            // Clears its results list on every call, hence one list per root.
            UnityEditor.AI.NavMeshEditorHelpers.CollectSourcesInStage(
                rootObject.transform, mask, surface.useGeometry, surface.defaultArea, generateLinks,
                markups, false, scene, fromRoot);
            sources.AddRange(fromRoot);
        }

        if (surface.ignoreNavMeshAgent)
            sources.RemoveAll(source => source.component != null && source.component.GetComponent<NavMeshAgent>() != null);
        if (surface.ignoreNavMeshObstacle)
            sources.RemoveAll(source => source.component != null && source.component.GetComponent<NavMeshObstacle>() != null);

        // After the filters, like the package: a volume on a GameObject that also carries an obstacle
        // still counts. That is precisely what protects Zona1's obstacle pillars.
        int volumes = 0;
        foreach (NavMeshModifierVolume volume in NavMeshModifierVolume.activeModifiers)
        {
            if (volume == null || volume.gameObject.scene != scene) continue;
            if ((mask & (1 << volume.gameObject.layer)) == 0 || !volume.AffectsAgentType(agentType)) continue;

            Vector3 scale = volume.transform.lossyScale;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(volume.transform.TransformPoint(volume.center), volume.transform.rotation, Vector3.one),
                size = new Vector3(volume.size.x * Mathf.Abs(scale.x), volume.size.y * Mathf.Abs(scale.y),
                                   volume.size.z * Mathf.Abs(scale.z)),
                area = volume.area,
            });
            volumes++;
        }

        Bounds bounds = SourceBounds(surface, sources, log);
        NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
            surface.GetBuildSettings(), sources, bounds, surface.transform.position, surface.transform.rotation);

        log.AppendLine($"Baked {surface.name} from {scene.name} alone: {sources.Count} sources " +
                       $"({volumes} modifier volumes), layers {mask}.");
        return data;
    }

    /// <summary>
    /// What the Bake button does with the result: the old asset — when it is this scene's own, in the
    /// folder named after it — is deleted and the new data is saved in its place.
    /// </summary>
    public static void Install(Scene scene, NavMeshSurface surface, NavMeshData data, StringBuilder log)
    {
        string folder = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets",
                                     Path.GetFileNameWithoutExtension(scene.path)).Replace('\\', '/');
        EnsureFolder(folder);

        NavMeshData old = surface.navMeshData;
        string oldPath = old != null ? AssetDatabase.GetAssetPath(old) : string.Empty;
        bool ownsOld = !string.IsNullOrEmpty(oldPath) && oldPath.StartsWith(folder + "/", StringComparison.Ordinal);
        string path = ownsOld ? oldPath : AssetDatabase.GenerateUniqueAssetPath($"{folder}/NavMesh-{surface.name}.asset");

        surface.RemoveData();
        var serialized = new SerializedObject(surface);
        serialized.FindProperty("m_NavMeshData").objectReferenceValue = data;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (surface.isActiveAndEnabled) surface.AddData();

        if (ownsOld) AssetDatabase.DeleteAsset(oldPath);
        AssetDatabase.CreateAsset(data, path);
        log.AppendLine($"NavMesh saved to {path}.");
    }

    /// <summary>The package's CalculateWorldBounds, which is private: every source in the surface's
    /// unscaled local space, grown by 0.1 m so coplanar sources are not clipped.</summary>
    private static Bounds SourceBounds(NavMeshSurface surface, List<NavMeshBuildSource> sources, StringBuilder log)
    {
        Matrix4x4 worldToLocal = Matrix4x4.TRS(surface.transform.position, surface.transform.rotation, Vector3.one).inverse;
        var result = new Bounds();
        bool warned = false;

        foreach (NavMeshBuildSource source in sources)
        {
            switch (source.shape)
            {
                case NavMeshBuildSourceShape.Mesh:
                    if (source.sourceObject is Mesh mesh)
                        result.Encapsulate(TransformBounds(worldToLocal * source.transform, mesh.bounds));
                    break;
                case NavMeshBuildSourceShape.Box:
                case NavMeshBuildSourceShape.Sphere:
                case NavMeshBuildSourceShape.Capsule:
                case NavMeshBuildSourceShape.ModifierBox:
                    result.Encapsulate(TransformBounds(worldToLocal * source.transform, new Bounds(Vector3.zero, source.size)));
                    break;
                default:
                    // Terrain: the testbed has none, and the package needs the terrain module to size it.
                    if (!warned) log.AppendLine($"Warning: a {source.shape} source was left out of the bake bounds.");
                    warned = true;
                    break;
            }
        }

        result.Expand(0.1f);
        return result;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 c = bounds.center, e = bounds.extents;
        var result = new Bounds(matrix.MultiplyPoint3x4(c - e), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            result.Encapsulate(matrix.MultiplyPoint3x4(corner));
        }
        return result;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
