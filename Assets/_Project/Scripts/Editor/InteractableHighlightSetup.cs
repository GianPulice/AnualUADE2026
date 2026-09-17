using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the crosshair highlight onto every interactable family, from the Father prefab down, and
/// prepares the materials under them so the highlight can reach them.
///
/// Why from the Fathers: a variant inherits the component together with its profile, so a new
/// socket or valve made from its Father lights up with no extra step, in the same colour as the
/// rest. Puzzle props that were never made from a Father (the electric panel, the three push boxes)
/// get it on their own prefab.
///
/// Every step is skipped when already done, so it is safe to re-run:
///   1. Creates the two shared profiles (SO_Highlight_Items / SO_Highlight_Interactables) if they
///      are missing. Existing ones are never overwritten — that is where the colour gets tuned.
///   2. Adds ItemProximityHighlight to each root below and gives it its profile. A highlight that
///      already has a profile keeps it.
///   3. Switches Emission on for the URP/Lit materials under those prefabs and every variant of
///      them, when their emission colour is black: nothing changes on screen, it only stops URP
///      compiling the emission out. Materials with a colour set but emission off, or embedded in an
///      FBX, are listed instead of touched.
///
/// Set Up Item Glints does the same for <see cref="ItemGlint"/>, the far-away glint on pickups, from
/// the same idea: on the Father, so every variant has it.
/// </summary>
public static class InteractableHighlightSetup
{
    public const string ProfileFolder    = "Assets/_Project/ScriptableObjects/Highlight";
    public const string ItemsProfilePath = ProfileFolder + "/SO_Highlight_Items.asset";
    public const string PropsProfilePath = ProfileFolder + "/SO_Highlight_Interactables.asset";
    public const string GlintProfilePath = ProfileFolder + "/SO_Glint_Items.asset";
    private const string GlintMaterialPath = "Assets/_Project/Art/Materials/Items/mat_item_glint.mat";
    private const string GlintShaderName   = "WIRED/Items/Item Glint";
    private const string PrefabFolder      = "Assets/_Project/Prefabs";
    private const string CategoryConfigPath = "Assets/_Project/ScriptableObjects/CategoryConfig/ItemCategory.asset";
    private const string LogTag = "[InteractableHighlightSetup]";

    private static readonly string[] ItemRoots =
    {
        "Assets/_Project/Prefabs/InventoryItemFather/InventoryItem.prefab",
    };

    private static readonly string[] PropRoots =
    {
        "Assets/_Project/Prefabs/SocketFather/Socket.prefab",
        "Assets/_Project/Prefabs/ValveFather/Valve.prefab",
        "Assets/_Project/Prefabs/Puzzle1/Sequences/PanelElectrico.prefab",
        "Assets/_Project/Prefabs/Puzzle1/Containers/Box_A.prefab",
        "Assets/_Project/Prefabs/Puzzle1/Containers/Box_B.prefab",
        "Assets/_Project/Prefabs/Puzzle1/Containers/Box_C.prefab",
    };

    /// <summary>Carries its highlight on a child already (RideButton/Visual); only needs a profile.</summary>
    private const string MontacargasPath = "Assets/_Project/Prefabs/MontacargasRoot.prefab";

    [MenuItem("Tools/Interactables/Set Up Highlights")]
    private static void SetUp()
    {
        var log = new StringBuilder();

        SO_HighlightProfile items = LoadOrCreateProfile(ItemsProfilePath, true, log);
        SO_HighlightProfile props = LoadOrCreateProfile(PropsProfilePath, false, log);

        foreach (string path in ItemRoots) SetUpRoot(path, items, log);
        foreach (string path in PropRoots) SetUpRoot(path, props, log);
        AssignToExisting(MontacargasPath, props, log);

        EnableEmission(ItemRoots.Concat(PropRoots).Append(MontacargasPath).ToArray(), log);

        AssetDatabase.SaveAssets();
        Debug.Log($"{LogTag} Done.\n{log}");
    }

    private static SO_HighlightProfile LoadOrCreateProfile(string path, bool forItems, StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SO_HighlightProfile>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(ProfileFolder))
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Highlight");

        var profile = ScriptableObject.CreateInstance<SO_HighlightProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var so = new SerializedObject(profile);
        if (forItems)
        {
            // Spec §2.1: a faint category tint at rest; tint up and a little emission when looked at.
            so.FindProperty("farTint").floatValue      = 0.15f;
            so.FindProperty("farEmission").floatValue  = 0f;
            so.FindProperty("nearTint").floatValue     = 0.4f;
            so.FindProperty("nearEmission").floatValue = 0.2f;
            so.FindProperty("categoryConfig").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<SO_ItemCategoryConfig>(CategoryConfigPath);
        }
        else
        {
            // Spec §6, props without a category: no tint in either state, only emission answers,
            // and nothing at rest, so a prop looks exactly as authored until it is looked at.
            so.FindProperty("farTint").floatValue      = 0f;
            so.FindProperty("farEmission").floatValue  = 0f;
            so.FindProperty("nearTint").floatValue     = 0f;
            so.FindProperty("nearEmission").floatValue = 0.15f;
            // #E0E0E0, the UI's "selected" white — see SO_HighlightProfile.emissionColor.
            so.FindProperty("emissionColor").colorValue = new Color(0.878f, 0.878f, 0.878f);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);

        log.AppendLine($"Created {path}");
        return profile;
    }

    private static void SetUpRoot(string path, SO_HighlightProfile profile, StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var highlight = root.GetComponent<ItemProximityHighlight>();
            bool added = highlight == null;
            if (added) highlight = root.AddComponent<ItemProximityHighlight>();

            bool assigned = AssignIfEmpty(highlight, profile);
            if (!added && !assigned)
            {
                log.AppendLine($"Already set up: {path}");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine($"{(added ? "Added highlight" : "Assigned profile")} ({profile.name}): {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignToExisting(string path, SO_HighlightProfile profile, StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            foreach (ItemProximityHighlight highlight in root.GetComponentsInChildren<ItemProximityHighlight>(true))
                changed |= AssignIfEmpty(highlight, profile);

            if (!changed) return;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine($"Assigned profile ({profile.name}): {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Fills the component's <c>profile</c> field when it is empty. A set one is kept.</summary>
    private static bool AssignIfEmpty(Component component, ScriptableObject profile)
    {
        var so = new SerializedObject(component);
        SerializedProperty property = so.FindProperty("profile");
        if (property.objectReferenceValue != null) return false;

        property.objectReferenceValue = profile;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ── Item glints ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts <see cref="ItemGlint"/> on every pickup, with the same re-runnable steps as the highlight:
    ///   1. Creates mat_item_glint and SO_Glint_Items if they are missing. Existing ones are never
    ///      overwritten — the material is where the star's shape is tuned, the profile its timing.
    ///   2. Adds the glint to every pickup prefab under Prefabs/ that is not a variant of another
    ///      pickup prefab, so Fathers get it and their variants inherit it.
    ///   3. Adds it to the pickups placed straight in the open scenes, the ones that come from no
    ///      prefab. Those scenes are left dirty, to be saved by hand.
    /// </summary>
    [MenuItem("Tools/Interactables/Set Up Item Glints")]
    private static void SetUpGlints()
    {
        var log = new StringBuilder();

        Material material = LoadOrCreateGlintMaterial(log);
        if (material == null) return;

        SO_GlintProfile profile = LoadOrCreateGlintProfile(material, log);

        foreach (string path in FindPickupPrefabRoots()) AddGlintToPrefab(path, profile, log);
        AddGlintInOpenScenes(profile, log);

        AssetDatabase.SaveAssets();
        Debug.Log($"{LogTag} Glints done.\n{log}");
    }

    private static Material LoadOrCreateGlintMaterial(StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(GlintMaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find(GlintShaderName);
        if (shader == null)
        {
            Debug.LogError($"{LogTag} Shader '{GlintShaderName}' not found (Art/Materials/Items/ItemGlint.shader). " +
                           "Check the Console for shader compile errors, then run this again.");
            return null;
        }

        var material = new Material(shader) { name = "mat_item_glint" };
        AssetDatabase.CreateAsset(material, GlintMaterialPath);
        log.AppendLine($"Created {GlintMaterialPath}");
        return material;
    }

    private static SO_GlintProfile LoadOrCreateGlintProfile(Material material, StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SO_GlintProfile>(GlintProfilePath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(ProfileFolder))
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Highlight");

        // Every other value keeps the defaults written in SO_GlintProfile.
        var profile = ScriptableObject.CreateInstance<SO_GlintProfile>();
        AssetDatabase.CreateAsset(profile, GlintProfilePath);

        var so = new SerializedObject(profile);
        so.FindProperty("material").objectReferenceValue = material;
        so.FindProperty("categoryConfig").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<SO_ItemCategoryConfig>(CategoryConfigPath);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);

        log.AppendLine($"Created {GlintProfilePath}");
        return profile;
    }

    /// <summary>
    /// Prefabs with a <see cref="PickupInteractable"/> on their root, minus the variants of another
    /// one: those inherit the glint from it (InventoryItemFather, NoteFather).
    /// </summary>
    private static List<string> FindPickupPrefabRoots()
    {
        var pickups = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && asset.GetComponent<PickupInteractable>() != null) pickups.Add(path);
        }

        var pickupSet = new HashSet<string>(pickups);
        return pickups.Where(path => !DerivesFrom(path, pickupSet)).ToList();
    }

    private static void AddGlintToPrefab(string path, SO_GlintProfile profile, StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var glint = root.GetComponent<ItemGlint>();
            bool added = glint == null;
            if (added) glint = root.AddComponent<ItemGlint>();

            bool assigned = AssignIfEmpty(glint, profile);
            if (!added && !assigned)
            {
                log.AppendLine($"Already glints: {path}");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine($"{(added ? "Added glint" : "Assigned glint profile")}: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Pickups built in the scene rather than placed from a prefab. A pickup that comes from a prefab
    /// is left to the prefab pass above, and reported if it still has none.
    /// </summary>
    private static void AddGlintInOpenScenes(SO_GlintProfile profile, StringBuilder log)
    {
        PickupInteractable[] pickups = Object.FindObjectsByType<PickupInteractable>(FindObjectsInactive.Include);

        foreach (PickupInteractable pickup in pickups)
        {
            var glint = pickup.GetComponent<ItemGlint>();
            if (glint != null)
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(glint) && AssignIfEmpty(glint, profile))
                {
                    EditorSceneManager.MarkSceneDirty(pickup.gameObject.scene);
                    log.AppendLine($"Assigned glint profile in scene '{pickup.gameObject.scene.name}': {pickup.name}");
                }
                continue;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(pickup))
            {
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(pickup);
                log.AppendLine($"  NOT touched, comes from '{source}', which has no glint (outside " +
                               $"{PrefabFolder}?): {pickup.name}");
                continue;
            }

            glint = Undo.AddComponent<ItemGlint>(pickup.gameObject);
            AssignIfEmpty(glint, profile);
            EditorSceneManager.MarkSceneDirty(pickup.gameObject.scene);
            log.AppendLine($"Added glint in scene '{pickup.gameObject.scene.name}' (save the scene): {pickup.name}");
        }
    }

    /// <summary>
    /// Switches Emission on, black, for every URP/Lit material the highlights on
    /// <paramref name="roots"/> and on every variant of them would drive.
    /// </summary>
    private static void EnableEmission(string[] roots, StringBuilder log)
    {
        var rootSet = new HashSet<string>(roots);
        var prefabs = new List<string>(roots);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!rootSet.Contains(path) && DerivesFrom(path, rootSet)) prefabs.Add(path);
        }

        var materials = new HashSet<Material>();
        foreach (string path in prefabs)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            // Where the highlight sits: the ride button keeps it on its Visual child, everything
            // else on the root — which is also the fallback for a variant not yet reimported after
            // its Father changed. Judged by the same rule the component uses at runtime.
            Transform[] owners = asset.GetComponentsInChildren<ItemProximityHighlight>(true)
                                      .Select(h => h.transform).ToArray();
            if (owners.Length == 0) owners = new[] { asset.transform };

            foreach (Transform owner in owners)
            foreach (Renderer renderer in ItemProximityHighlight.GatherRenderers(owner))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null) materials.Add(material);
            }
        }

        log.AppendLine($"Materials under {prefabs.Count} prefab(s):");
        foreach (Material material in materials.OrderBy(m => m.name))
        {
            if (ItemProximityHighlight.GetSupport(material) != ItemProximityHighlight.SlotSupport.EmissionKeywordOff)
                continue;

            // Only the project's own .mat files. A package material (URP's default Lit, which some
            // placeholder cubes still use) is shared with the whole editor and read-only, and one
            // embedded in an FBX has to be extracted before it can be edited at all.
            string materialPath = AssetDatabase.GetAssetPath(material);
            if (!materialPath.StartsWith("Assets/") || !materialPath.EndsWith(".mat"))
            {
                log.AppendLine($"  NOT touched, lives in '{materialPath}': {material.name}");
                continue;
            }

            if (material.GetColor("_EmissionColor").maxColorComponent > 0f)
            {
                log.AppendLine($"  NOT touched, has an emission colour with Emission off — switching it " +
                               $"on would light it up: {material.name}");
                continue;
            }

            // BakedEmissive is what URP's inspector reads back to decide the keyword
            // (BaseShaderGUI: GI flags & AnyEmissive), so the keyword survives the next time someone
            // opens the material. EmissiveIsBlack keeps the lightmapper treating it as not emissive.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive |
                                               MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            log.AppendLine($"  Emission on (black): {material.name}  ({materialPath})");
        }
    }

    private static bool DerivesFrom(string path, HashSet<string> roots)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        for (int depth = 0; asset != null && depth < 8; depth++)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            if (source == null) return false;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (roots.Contains(sourcePath)) return true;

            asset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        }

        return false;
    }
}
