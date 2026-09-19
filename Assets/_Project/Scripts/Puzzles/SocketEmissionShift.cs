using System.Collections;
using UnityEngine;

/// <summary>
/// Recolours the glowing zone of a socket's station when its item goes in: the authored emission
/// (red on the energy core, white on the mechanic core and on the pressure regulator's side gauges —
/// not its front panel) fades to green, "module powered" — see Materials-System.md, section on reserved colours.
///
/// Only the emissive ZONE changes because the targets are URP/Lit slots with an _EmissionMap: the
/// map masks <c>_EmissionColor</c> to its glowing spots, so rewriting the colour leaves the rest of
/// the part as it was. Point it at those slots only — a slot without a map would glow all over.
///
/// Written through a <see cref="MaterialPropertyBlock"/>, slot by slot and reading the slot's block
/// back first (same as <see cref="ItemProximityHighlight"/>), so shared materials stay shared.
///
/// Put it on the socket's root, next to <see cref="SocketInteractable"/>. It listens to
/// <see cref="SocketInteractable.Inserted"/> (animate) and
/// <see cref="SocketInteractable.InsertedStateSynced"/> (snap, after a load or checkpoint).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SocketInteractable))]
public class SocketEmissionShift : MonoBehaviour
{
    /// <summary>One emissive slot: a Renderer plus its material index.</summary>
    [System.Serializable]
    public struct Target
    {
        [Tooltip("Renderer of the station part that has the glowing zone.")]
        public Renderer renderer;

        [Tooltip("Material slot index within that Renderer. 0 if the part has one material.")]
        public int materialIndex;
    }

    [Tooltip("Station slots whose emissive zone changes colour. Their material must be URP/Lit with " +
             "Emission on AND an Emission Map (the map is what keeps the change to the zone).")]
    [SerializeField] private Target[] targets = new Target[0];

    [Tooltip("Emission colour once the item is in. HDR — keep it above 1 so the zone still reads " +
             "in the PS1 filter.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color insertedColor = new Color(0.25f, 2.4f, 0.55f, 1f);

    [Tooltip("Seconds of the fade from the authored colour to the inserted one.")]
    [SerializeField, Min(0f)] private float fadeDuration = 1.2f;

    [Tooltip("Quick dropouts before the fade, like the zone losing power and coming back. 0 = none.")]
    [SerializeField, Range(0, 6)] private int flickerCount = 2;

    [Tooltip("Seconds each flicker lasts (half off, half on).")]
    [SerializeField, Min(0.01f)] private float flickerDuration = 0.12f;

    private static readonly int EmitColorId = Shader.PropertyToID("_EmissionColor");

    private SocketInteractable _socket;
    private ItemProximityHighlight _highlight;
    private MaterialPropertyBlock _block;
    private Color[] _authored;
    private Coroutine _routine;

    private void Awake()
    {
        _socket    = GetComponent<SocketInteractable>();
        _highlight = GetComponent<ItemProximityHighlight>();
        _block     = new MaterialPropertyBlock();

        _authored = new Color[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            Material material = GetMaterial(targets[i]);
            _authored[i] = material != null && material.HasProperty(EmitColorId)
                ? material.GetColor(EmitColorId)
                : Color.black;
        }
    }

    private void OnEnable()
    {
        _socket.Inserted            += HandleInserted;
        _socket.InsertedStateSynced += HandleStateSynced;
    }

    private void OnDisable()
    {
        _socket.Inserted            -= HandleInserted;
        _socket.InsertedStateSynced -= HandleStateSynced;
    }

    private void HandleInserted()
    {
        // The socket is finished now: drop the highlight at once instead of fading it over the
        // flicker, so the colour change reads on its own.
        if (_highlight != null) _highlight.SnapToFar();

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShiftRoutine());
    }

    private void HandleStateSynced(bool inserted)
    {
        // Empty: the authored colour is already showing (and writing it now could land on top of a
        // lit highlight). Inserted during the sync delay: the animation owns it.
        if (!inserted || _routine != null) return;
        if (_highlight != null) _highlight.SnapToFar();
        ApplyAll(1f, true);
    }

    private IEnumerator ShiftRoutine()
    {
        float half = flickerDuration * 0.5f;
        for (int i = 0; i < flickerCount; i++)
        {
            ApplyAll(0f, false);
            yield return new WaitForSeconds(half);
            ApplyAll(0f, true);
            yield return new WaitForSeconds(half);
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            ApplyAll(t * t * (3f - 2f * t), true);
            yield return null;
        }

        ApplyAll(1f, true);
        _routine = null;
    }

    /// <summary>
    /// <paramref name="t"/> 0 = authored colour, 1 = <see cref="insertedColor"/>; unlit forces
    /// black (a flicker dropout).
    /// </summary>
    private void ApplyAll(float t, bool lit)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target.renderer == null) continue;
            if (target.materialIndex < 0 || target.materialIndex >= target.renderer.sharedMaterials.Length) continue;

            Color color = lit ? Color.Lerp(_authored[i], insertedColor, t) : Color.black;

            target.renderer.GetPropertyBlock(_block, target.materialIndex);
            _block.SetColor(EmitColorId, color);
            target.renderer.SetPropertyBlock(_block, target.materialIndex);
        }
    }

    private static Material GetMaterial(Target target)
    {
        if (target.renderer == null) return null;
        Material[] materials = target.renderer.sharedMaterials;
        return target.materialIndex >= 0 && target.materialIndex < materials.Length
            ? materials[target.materialIndex]
            : null;
    }
}
