using UnityEngine;

/// <summary>
/// Settings > Screen > Graphics (Low / Medium / High). One job: turn the saved level into the
/// texture and mesh detail the game renders with.
///
///   Low     textures at a quarter of their resolution, LODs drop early, no anisotropic filtering.
///   Medium  textures at half resolution, default LOD distance, anisotropic where a texture asks.
///   High    full-resolution textures, LODs kept longer, anisotropic everywhere.
///
/// Textures only shrink if they have mipmaps: a texture imported without them (pixel-art UI, the
/// UV warning sign) always shows at full size, which is what those want.
///
/// Static and self-starting, like <see cref="GameInput"/>'s modes: applied once after the first
/// scene loads and again whenever <see cref="SettingsModel.OnSettingsApplied"/> fires, so no
/// scene needs a component placed. In the editor the original QualitySettings values are put back
/// when Play ends: QualitySettings changed in Play mode would otherwise stay changed in the
/// project.
/// </summary>
public static class GraphicsQualityApplier
{
    /// <summary>PlayerPrefs key, written by <see cref="SettingsModel"/>. 0 Low, 1 Medium, 2 High.</summary>
    public const string QualityKey = "Settings_GraphicsQuality";
    private const int DefaultQuality = 2;

    private static readonly int[] TextureMipLimit = { 2, 1, 0 };
    private static readonly float[] LodBias = { 0.6f, 1f, 2f };
    private static readonly AnisotropicFiltering[] Anisotropic =
        { AnisotropicFiltering.Disable, AnisotropicFiltering.Enable, AnisotropicFiltering.ForceEnable };

    private static bool captured;
    private static int originalMipLimit;
    private static float originalLodBias;
    private static AnisotropicFiltering originalAniso;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        // Domain reload may be off: unsubscribe first so a second Play does not apply twice.
        SettingsModel.OnSettingsApplied -= Apply;
        SettingsModel.OnSettingsApplied += Apply;
        Application.quitting -= Restore;
        Application.quitting += Restore;
        Apply();
    }

    /// <summary>Applies the saved level.</summary>
    public static void Apply()
    {
        int q = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, DefaultQuality), 0, 2);
        if (!captured)
        {
            originalMipLimit = QualitySettings.globalTextureMipmapLimit;
            originalLodBias = QualitySettings.lodBias;
            originalAniso = QualitySettings.anisotropicFiltering;
            captured = true;
        }
        QualitySettings.globalTextureMipmapLimit = TextureMipLimit[q];
        QualitySettings.lodBias = LodBias[q];
        QualitySettings.anisotropicFiltering = Anisotropic[q];
    }

    // Application.quitting also fires when the editor leaves Play mode.
    private static void Restore()
    {
        if (!captured || !Application.isEditor) return;
        QualitySettings.globalTextureMipmapLimit = originalMipLimit;
        QualitySettings.lodBias = originalLodBias;
        QualitySettings.anisotropicFiltering = originalAniso;
        captured = false;
    }
}
