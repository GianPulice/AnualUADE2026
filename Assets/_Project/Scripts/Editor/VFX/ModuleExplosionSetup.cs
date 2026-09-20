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

    // Blood. Deliberately darker and duller than the danger red (#CC1A1A): gore is meant to read
    // as wet meat, not as the signal colour the LEDs and the UI use to mean "danger".
    private static readonly Color BloodDeep   = new Color(0.30f, 0.015f, 0.02f, 1f);
    private static readonly Color BloodBright = new Color(0.62f, 0.05f,  0.05f, 1f);

    private static readonly string[] GoreSystems = { "BloodSpray", "BloodMist", "BloodChunks" };

    [MenuItem("Tools/VFX/Module Explosion/Setup (build missing + wire)")]
    public static void SetupAll() => Run(rebuildPrefab: false);

    [MenuItem("Tools/VFX/Module Explosion/Rebuild VFX Prefab")]
    public static void RebuildPrefab() => Run(rebuildPrefab: true);

    /// <summary>
    /// Throws away the three blood systems and builds them again, leaving the rest of the prefab
    /// (flash, sparks, smoke, debris, light) exactly as it was tuned. This is the one to use after
    /// changing the gore numbers below: a full rebuild would also undo hand-tuning.
    /// </summary>
    [MenuItem("Tools/VFX/Module Explosion/Refresh Gore Layers")]
    public static void RefreshGore()
    {
        EnsureFolder(TexDir);
        EnsureFolder(MatDir);

        if (AssetDatabase.LoadAssetAtPath<ExplosionVFX>(PrefabPath) == null)
        {
            Debug.LogWarning($"[ModuleExplosionSetup] No prefab at {PrefabPath}. Run Setup first.");
            return;
        }

        EnsureGore(BloodMaterial(), SmokeMaterial(), replaceExisting: true);
        AssetDatabase.SaveAssets();
        Debug.Log("[ModuleExplosionSetup] Gore layers rebuilt.");
    }

    private static Material BloodMaterial() =>
        EnsureMaterial("mat_vfx_blood", EnsureTexture("vfx_blood.png", GenerateBloodBlob), additive: false);

    private static Material SmokeMaterial() =>
        EnsureMaterial("mat_vfx_smoke", EnsureTexture("vfx_smoke.png", GenerateSmoke), additive: false);

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
        Material gore  = BloodMaterial();

        ExplosionVFX prefab = AssetDatabase.LoadAssetAtPath<ExplosionVFX>(PrefabPath);
        if (prefab == null || rebuildPrefab) prefab = BuildPrefab(add, smk, solid, gore);
        else EnsureGore(gore, smk, replaceExisting: false);   // Gore on a prefab built before it existed.

        SO_ModuleExplosionConfig config = EnsureConfig(prefab);
        WirePlayer(config);

        AssetDatabase.SaveAssets();
        Debug.Log("[ModuleExplosionSetup] Done.");
    }

    // ── Prefab ───────────────────────────────────────────────────────────────────────────

    private static ExplosionVFX BuildPrefab(Material additive, Material smokeMat, Material debrisMat,
                                            Material bloodMat)
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

        BuildGore(root, bloodMat, smokeMat);

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

    // Gore âââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââ

    /// <summary>
    /// The blood layers, kept apart from <see cref="BuildPrefab"/> so they can be added to (or
    /// rebuilt on) a prefab that was already tuned by hand, without touching the rest of it.
    /// Systems that already exist are left alone; the caller deletes them first to force a rebuild.
    /// </summary>
    /// <returns>True when at least one system was created, i.e. the prefab needs saving.</returns>
    private static bool BuildGore(GameObject root, Material bloodMat, Material mistMat)
    {
        bool built = false;

        // Spray: the arterial burst. Stretched droplets thrown wide, pulled down hard, and they
        // stop where they land instead of bouncing â blood is not a rubber ball.
        if (root.transform.Find("BloodSpray") == null)
        {
            built = true;
            ParticleSystem spray = CreateSystem(root, "BloodSpray", bloodMat);
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(BloodDeep, BloodBright);
            main.gravityModifier = 2.4f;
            main.maxParticles = 600;   // Headroom for ExplosionVFX.intensity above 1.
            SetBurst(spray, 140);
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            FadeOut(spray);
            // Lower dampen than Chunks on purpose: this is a Stretch renderer, so its visible
            // LENGTH shrinks with speed (see velocityScale below). The old 0.7 dropped a droplet to
            // 30% speed in one collision event — a long streak snapping to a stub the instant it
            // touched the floor, which is what "hits the floor and cuts" was describing. Losing
            // speed more gradually over a couple of contact steps instead reads as a droplet
            // skidding to a stop, not a hard cut.
            Splat(spray, dampen: 0.4f);
            var renderer = spray.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.4f;
            // Halved with the same droplet-snap in mind: less of the visible length comes from
            // instantaneous speed, so the drop in speed on impact is less of a drop in shape too.
            renderer.velocityScale = 0.025f;
        }

        // Mist: the red haze the burst leaves hanging. Same texture as the smoke, tinted by its own
        // start colour; it SINKS where the smoke rises, so the two do not read as one cloud.
        if (root.transform.Find("BloodMist") == null)
        {
            built = true;
            ParticleSystem mist = CreateSystem(root, "BloodMist", mistMat);
            var main = mist.main;
            main.startDelay = 0.02f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.22f, 0.01f, 0.015f, 0.55f), new Color(0.42f, 0.04f, 0.04f, 0.4f));
            main.gravityModifier = 0.15f;
            main.maxParticles = 200;
            SetBurst(mist, 18);
            var shape = mist.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;
            var sol = mist.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.6f));
            FadeOut(mist);
        }

        // Chunks: heavy gibs. Slower and fewer than the spray, they outlive it and tumble to the
        // floor, so the shot still has something moving once the flash is gone.
        if (root.transform.Find("BloodChunks") == null)
        {
            built = true;
            ParticleSystem chunks = CreateSystem(root, "BloodChunks", bloodMat);
            var main = chunks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.25f, 0.02f, 0.02f, 1f), new Color(0.5f, 0.07f, 0.05f, 1f));
            main.gravityModifier = 3.2f;
            main.maxParticles = 150;
            SetBurst(chunks, 26);
            var shape = chunks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.07f;
            var rol = chunks.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            FadeOut(chunks);
            Splat(chunks, dampen: 0.85f);
        }

        return built;
    }

    /// <summary>
    /// Makes a system hit level geometry and stay put, instead of falling through it.
    ///
    /// <paramref name="radiusScale"/> inflates the collision sphere past the particle's visible
    /// size, so it stops a hair ABOVE the floor instead of exactly on it. At 1 (the old default)
    /// the pivot sits flush with the surface, and since neither this shader nor URP's stock one
    /// does soft-particle depth fading, a particle resting exactly at that height gets hard
    /// depth-tested against the floor mesh from grazing camera angles — the geometric half of the
    /// reported "collides with the floor and cuts".
    /// </summary>
    private static void Splat(ParticleSystem ps, float dampen, float radiusScale = 1.6f)
    {
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        // High = a real raycast per particle. Medium and Low read the depth buffer, which in URP is
        // only there when a feature asks for it, so the blood would fall through the floor at random.
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.dampen = dampen;
        collision.bounce = 0.05f;
        collision.lifetimeLoss = 0f;
        collision.sendCollisionMessages = false;
        collision.radiusScale = radiusScale;
    }

    /// <summary>
    /// Adds the gore layers to the saved prefab. With replaceExisting the three are deleted first,
    /// so the numbers above win over whatever is in the asset.
    /// </summary>
    private static void EnsureGore(Material bloodMat, Material mistMat, bool replaceExisting)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (replaceExisting)
            {
                foreach (string name in GoreSystems)
                {
                    Transform existing = contents.transform.Find(name);
                    if (existing != null) Object.DestroyImmediate(existing.gameObject);
                }
            }

            if (!BuildGore(contents, bloodMat, mistMat)) return;
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // Systems ââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââââ

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

    /// <summary>
    /// A droplet: a round core with a noise-warped edge, so the spray does not read as a hundred
    /// copies of the same perfect circle. Only the alpha matters â the colour comes from the
    /// system's start colour.
    /// </summary>
    private static Color32[] GenerateBloodBlob(int size)
    {
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);

            // The noise pushes the edge in and out; the centre stays solid, so it reads as thick
            // fluid rather than as the soft additive dot the sparks use.
            float wobble = Mathf.PerlinNoise(x * 0.18f + 11.7f, y * 0.18f + 4.9f) * 0.35f;
            float a = Mathf.Clamp01((1f - d + wobble - 0.18f) * 2.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
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
