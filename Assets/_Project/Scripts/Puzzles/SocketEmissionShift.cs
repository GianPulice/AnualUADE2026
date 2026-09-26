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
///
/// With <see cref="dimOutsideSafeZone"/> on, the zones are also capped while the player is outside
/// the safe zone (<see cref="NemesisSafeZones"/>, the Hub). An HDR emission far above 1 — the
/// pressure regulator's gauges glow at 181, and 1367 once green — survives the vision fog and
/// reads from across the level; the cap keeps that glow inside the Hub.
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

    [Header("Outside the safe zone")]
    [Tooltip("Cap these zones while the player is outside the safe zone (the Hub, see SafeZoneMarker). " +
             "Turn it on when the zones glow far above 1: that glow survives the vision fog and " +
             "reads from across the level. Off = always the full colour.")]
    [SerializeField] private bool dimOutsideSafeZone;

    [Tooltip("Brightest a zone may glow while the player is outside the safe zone: the HDR intensity " +
             "of its strongest channel. The other sockets glow at about 2.4. 0 = dark.")]
    [SerializeField, Min(0f)] private float outsideSafeZoneIntensity = 2.4f;

    [Tooltip("Seconds of the fade when the player leaves the safe zone, and back when they enter it.")]
    [SerializeField, Min(0f)] private float safeZoneFadeDuration = 0.8f;

    private static readonly int EmitColorId = Shader.PropertyToID("_EmissionColor");

    // How often the player's position is tested against the safe-zone volumes.
    private const float SafeZoneCheckInterval = 0.2f;

    private SocketInteractable _socket;
    private ItemProximityHighlight _highlight;
    private MaterialPropertyBlock _block;
    private Color[] _authored;
    private Coroutine _routine;

    // What ApplyAll last showed: 0 = authored colour, 1 = insertedColor; unlit = a flicker dropout.
    private float _shiftT;
    private bool _lit = true;

    // 1 = full colour (player inside the safe zone), 0 = capped. Fades between the two.
    private float _zoneWeight = 1f;
    private float _zoneTarget = 1f;
    private float _nextZoneCheck;
    private bool _zoneKnown;

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

    private void Update()
    {
        if (!dimOutsideSafeZone) return;

        if (Time.time >= _nextZoneCheck)
        {
            _nextZoneCheck = Time.time + SafeZoneCheckInterval;

            // No player yet (loading): nothing to measure, keep what shows.
            Transform player = PlayerRegistry.CurrentTransform;
            if (player == null) return;

            _zoneTarget = IsInsideSafeZone(player.position) ? 1f : 0f;

            // First reading snaps: a level that starts with the player outside must not open on a
            // fade out of the full glow.
            if (!_zoneKnown)
            {
                _zoneKnown = true;
                _zoneWeight = _zoneTarget;
                if (_routine == null && _zoneWeight < 1f) WriteAll();
                return;
            }
        }

        if (!_zoneKnown || _zoneWeight == _zoneTarget) return;

        _zoneWeight = safeZoneFadeDuration > 0f
            ? Mathf.MoveTowards(_zoneWeight, _zoneTarget, Time.deltaTime / safeZoneFadeDuration)
            : _zoneTarget;

        // A running shift writes every step on its own and picks up the new weight there.
        if (_routine == null) WriteAll();
    }

    // A scene without safe-zone volumes (a test scene) counts as inside: nothing to dim against.
    private static bool IsInsideSafeZone(Vector3 point) =>
        float.IsPositiveInfinity(NemesisSafeZones.FlatDistance(point)) || NemesisSafeZones.Contains(point);

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
        _shiftT = t;
        _lit = lit;
        WriteAll();
    }

    private void WriteAll()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target.renderer == null) continue;
            if (target.materialIndex < 0 || target.materialIndex >= target.renderer.sharedMaterials.Length) continue;

            Color color = _lit ? CapOutsideSafeZone(Color.Lerp(_authored[i], insertedColor, _shiftT)) : Color.black;

            target.renderer.GetPropertyBlock(_block, target.materialIndex);
            _block.SetColor(EmitColorId, color);
            target.renderer.SetPropertyBlock(_block, target.materialIndex);
        }
    }

    /// <summary>
    /// <paramref name="color"/> as shown for the player's position: unchanged inside the safe zone,
    /// scaled down to <see cref="outsideSafeZoneIntensity"/> outside it (hue kept), faded between.
    /// </summary>
    private Color CapOutsideSafeZone(Color color)
    {
        if (!dimOutsideSafeZone || _zoneWeight >= 1f) return color;

        float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        Color capped = color;
        if (peak > outsideSafeZoneIntensity)
        {
            capped = color * (outsideSafeZoneIntensity / peak);
            capped.a = color.a;
        }

        return Color.Lerp(capped, color, _zoneWeight);
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
