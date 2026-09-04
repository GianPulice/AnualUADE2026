using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paints the Graphic of this GameObject with a role from <see cref="SO_UIThemeConfig"/>.
///
/// Works on any Graphic, so it covers both Image and TextMeshProUGUI (TMP_Text derives from
/// MaskableGraphic). The point is that no prefab holds a literal color any more: changing a
/// token in the asset repaints every node that references it, in the editor and at runtime.
///
/// Runs with [ExecuteAlways] so the palette can be tuned without entering Play mode.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Graphic))]
[AddComponentMenu("WIRED/UI/UI Theme Applier")]
public class UIThemeApplier : MonoBehaviour
{
    // -- Serialized -------------------

    [SerializeField] private SO_UIThemeConfig theme;
    [SerializeField] private UIThemeRole role = UIThemeRole.TextPrimary;

    [Tooltip("Take only RGB from the token and keep the Graphic's current alpha. Use it on nodes " +
             "that already have a hand-tuned alpha; leave it off so the token's own alpha wins.")]
    [SerializeField] private bool preserveAlpha = false;

    // -- State -------------------

    private Graphic cachedGraphic;

    private Graphic TargetGraphic =>
        cachedGraphic != null ? cachedGraphic : (cachedGraphic = GetComponent<Graphic>());

    // -- Unity -------------------

    private void OnEnable()
    {
        Apply();
#if UNITY_EDITOR
        SO_UIThemeConfig.OnThemeChanged += Apply;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SO_UIThemeConfig.OnThemeChanged -= Apply;
#endif
    }

    // -- Public API -------------------

    /// <summary>Writes the token color into the Graphic. Safe to call repeatedly.</summary>
    public void Apply()
    {
        if (theme == null || TargetGraphic == null) return;

        Color target = theme.Get(role);
        if (preserveAlpha) target.a = TargetGraphic.color.a;

        if (TargetGraphic.color == target) return;
        TargetGraphic.color = target;

#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(TargetGraphic);
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Deferred: writing to a Graphic inside OnValidate trips Unity's
        // "SendMessage cannot be called during OnValidate" guard.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Apply();
        };
    }
#endif
}
