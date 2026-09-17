using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for the module explosion: generates the particle textures and materials,
/// builds Prefabs/VFX/ModuleExplosion, creates the SO_ModuleExplosionConfig and puts a
/// ModuleExplosionSequence on the Player prefab.
///
/// Idempotent. Existing assets are left alone (the prefab is meant to be tuned by hand after the
/// first build); use "Rebuild VFX Prefab" to regenerate the prefab from scratch.
/// </summary>
public static class ModuleExplosionSetup
{
    private const string TexDir    = "Assets/_Project/Art/Textures/VFX";
    private const string MatDir    = "Assets/_Project/Art/Materials/VFX";
    private const string PrefabDir = "Assets/_Project/Prefabs/VFX";
    private const string PrefabPath = PrefabDir + "/ModuleExplosion.prefab";
    private const string ConfigPath = "Assets/_Project/ScriptableObjects/Modules/SO_ModuleExplosionConfig.asset";
    private const string SfxPath    = "Assets/_Project/Audio/SFX/Modules/sfx_modulo_explosion_contenida.mp3";
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
    private const string ShaderName = "WIRED/VFX/Particle Unlit";

    // White and electric cyan. No red: that colour is reserved for danger lights.
    private static readonly Color Cyan = new Color(0.55f, 0.95f, 1f, 1f);

    [MenuItem("Tools/VFX/Module Explosion/Setup (build missing + wire)")]
    public static void SetupAll() => Run(rebuildPrefab: false);

    [MenuItem("Tools/VFX/Module Explosion/Rebuild VFX Prefab")]
    public static void RebuildPrefab() => Run(rebuildPrefab: true);

    private static void Run(bool rebuildPrefab)
    {
        EnsureFolder(TexDir);
        EnsureFolder(MatDir);
        EnsureFolder(PrefabDir);

        Texture2D dot   = EnsureTexture("vfx_dot.png",   GenerateDot);
        Texture2D smoke = EnsureTexture("vfx_smoke.png", GenerateSmoke);
        EnsureTexture("vfx_glint.png", GenerateGlint);   // Used by the item glint, built here too.

        Material add   = EnsureMaterial("mat_vfx_additive", dot,   additive: true);
        Material smk   = EnsureMaterial("mat_vfx_smoke",    smoke, additive: false);
        Material solid = EnsureMaterial("mat_vfx_debris",   Texture2D.whiteTexture, additive: false);

        ExplosionVFX prefab = AssetDatabase.LoadAssetAtPath<ExplosionVFX>(PrefabPath);
        if (prefab == null || rebuildPrefab) prefab = BuildPrefab(add, smk, solid);

        SO_ModuleExplosionConfig config = EnsureConfig(prefab);
        WirePlayer(config);

        AssetDatabase.SaveAssets();
        Debug.Log("[ModuleExplosionSetup] Done.");
    }

    // ── Prefab ───────────────────────────────────────────────────────────────────────────

    private static ExplosionVFX BuildPrefab(Material additive, Material smokeMat, Material debrisMat)
    {
        GameObject root = new GameObject("ModuleExplosion");
        ExplosionVFX vfx = root.AddComponent<ExplosionVFX>();

        // Flash: a couple of big white pops that grow and die almost at once.
        ParticleSystem flash = CreateSystem(root, "Flash", additive);
        {
            var main = flash.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Cyan);
            SetBurst(flash, 3);
            var sol = flash.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.3f));
            FadeOut(flash);
            var shape = flash.shape;
            shape.enabled = false;
        }

        // Sparks: fast stretched streaks thrown outwards, pulled down by gravity.
        ParticleSystem sparks = CreateSystem(root, "Sparks", additive);
        {
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Cyan);
            main.gravityModifier = 1.2f;
            SetBurst(sparks, 45);
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            FadeOut(sparks);
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.5f;
            renderer.velocityScale = 0.04f;
        }

        // Smoke: a slow dark cloud that swells and thins out after the flash.
        ParticleSystem smk = CreateSystem(root, "Smoke", smokeMat);
        {
            var main = smk.main;
            main.startDelay = 0.04f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.14f, 0.15f, 0.17f, 0.75f), new Color(0.25f, 0.27f, 0.3f, 0.6f));
            main.gravityModifier = -0.05f;
            SetBurst(smk, 10);
            var shape = smk.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var sol = smk.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.5f));
            FadeOut(smk);
        }

        // Debris: small solid chips of the device casing, heavy, no glow.
        ParticleSystem debris = CreateSystem(root, "Debris", debrisMat);
        {
            var main = debris.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.2f, 0.2f, 0.22f, 1f), new Color(0.45f, 0.45f, 0.48f, 1f));
            main.gravityModifier = 2.5f;
            SetBurst(debris, 14);
            var shape = debris.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            FadeOut(debris);
        }

        // Light: a short cyan-white pop on the surroundings. Driven by ExplosionVFX.
        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.75f, 0.95f, 1f);
        light.range = 4f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.enabled = false;

        SerializedObject so = new SerializedObject(vfx);
        so.FindProperty("flashLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved.GetComponent<ExplosionVFX>();
    }

    private static ParticleSystem CreateSystem(GameObject root, string name, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.playOnAwake = false;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    private static void SetBurst(ParticleSystem ps, int count)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    private static void FadeOut(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    // ── Config, Player ─────────────────────────────────────────────────────

    private static SO_ModuleExplosionConfig EnsureConfig(ExplosionVFX prefab)
    {
        SO_ModuleExplosionConfig config = AssetDatabase.LoadAssetAtPath<SO_ModuleExplosionConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SO_ModuleExplosionConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }

        SerializedObject so = new SerializedObject(config);
        SerializedProperty vfx = so.FindProperty("vfxPrefab");
        if (vfx.objectReferenceValue == null) vfx.objectReferenceValue = prefab;

        SerializedProperty sfx = so.FindProperty("sfxClip");
        if (sfx.objectReferenceValue == null)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxPath);
            if (clip == null) Debug.LogWarning($"[ModuleExplosionSetup] SFX not found at {SfxPath}.");
            sfx.objectReferenceValue = clip;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        return config;
    }

    private static void WirePlayer(SO_ModuleExplosionConfig config)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerStateManager player = contents.GetComponentInChildren<PlayerStateManager>(true);
            GameObject host = player != null ? player.gameObject : contents;

            ModuleExplosionSequence sequence = host.GetComponent<ModuleExplosionSequence>();
            if (sequence == null) sequence = host.AddComponent<ModuleExplosionSequence>();

            SerializedObject so = new SerializedObject(sequence);
            SerializedProperty prop = so.FindProperty("config");
            if (prop.objectReferenceValue == null) prop.objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ── Textures & materials ─────────────────────────────────────────────────────────────

    private delegate Color32[] PixelGenerator(int size);

    private static Texture2D EnsureTexture(string file, PixelGenerator generator)
    {
        string path = $"{TexDir}/{file}";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 32;   // Small and point-filtered: reads as PSX, not as a soft modern sprite.
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(generator(size));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color32[] GenerateDot(int size)
    {
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d);
            a = a * a;
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        return px;
    }

    private static Color32[] GenerateSmoke(int size)
    {
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float noise = Mathf.PerlinNoise(x * 0.23f + 7.1f, y * 0.23f + 3.3f);
            float a = Mathf.Clamp01((1f - d) * 1.4f) * Mathf.Lerp(0.45f, 1f, noise);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
        }
        return px;
    }

    /// <summary>Four-point star with a small core: the "something to pick up" sparkle.</summary>
    private static Color32[] GenerateGlint(int size)
    {
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Abs(x - c) / c;
            float dy = Mathf.Abs(y - c) / c;
            float cross = Mathf.Max(Mathf.Clamp01(1f - dx) * Mathf.Clamp01(1f - dy * 9f),
                                    Mathf.Clamp01(1f - dy) * Mathf.Clamp01(1f - dx * 9f));
            float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 3f);
            float a = Mathf.Clamp01(cross * cross + core);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        return px;
    }

    private static Material EnsureMaterial(string name, Texture2D texture, bool additive)
    {
        string path = $"{MatDir}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[ModuleExplosionSetup] Shader '{ShaderName}' not found.");
            return null;
        }

        mat = new Material(shader);
        mat.SetTexture("_MainTex", texture == Texture2D.whiteTexture ? null : texture);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
