using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Nemesis's eyes. One job: two points of light the player can pick out from across the level,
/// through the fog.
///
/// Not the beam, not the state, not the fog around it. Those were three separate jobs living in
/// this class and none of them was the one the eyes are actually for.
///
/// ── HOW IT WORKS ───────────────────────────────────────────────────────────
///
/// It puts a <see cref="FogBeacon"/> on each eye. A beacon is composited AFTER the fog's
/// extinction, which is the only place in the pipeline where "make it this bright" survives the
/// fog. See <see cref="FogBeacon"/> for why the previous approach — a FogLightBypass zone — could
/// not work at range: its glow is injected BEFORE the extinction, so past <c>visionEnd</c> it
/// arrives multiplied by ~0.004 and lands under the fog's own in-scattering.
///
/// ── WHAT HAPPENED TO THE SPOT LIGHTS ───────────────────────────────────────
///
/// The two red Spots on the head are kept as the ANCHORS — they are already parented to the head
/// bone and already sit where the eyes are, so they survive a rig re-import the same way they did
/// before. The Light components themselves are switched off by default
/// (<see cref="disableRealLights"/>): at the range they were driven to they lit the corridor red,
/// which competes with the very thing this is for. Turn the flag off to get them back.
/// </summary>
public class NemesisEyes : MonoBehaviour
{
    [Header("Anchors")]
    [Tooltip("Dónde están los ojos. Deja la lista vacía para tomar todas las Lights que cuelguen " +
             "de este objeto, que es lo que el prefab hace: los dos Spots de la cabeza.\n\n" +
             "Sólo se usa su POSICIÓN y su forward. Las Lights en sí se apagan (ver abajo).")]
    [SerializeField] private List<Light> eyeLights = new List<Light>();

    [Header("Look")]
    [Tooltip("El color de los ojos. Rojo peligro por la regla del proyecto: rojo = amenaza y " +
             "nada más.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    [SerializeField] private Color eyeColor = new Color(1f, 0.12f, 0.08f);

    [Tooltip("Brillo en pantalla. Es absoluto: la niebla no lo toca, así que el mismo número se " +
             "ve igual a 3 m que a 40 m y con cualquier preset. Arriba de 1 empieza a florecer " +
             "con el Bloom, que es lo que lo hace leer como una luz.")]
    [SerializeField, Min(0f)] private float intensity = 3.5f;

    [Tooltip("Tamaño real del ojo en metros. Sólo manda de cerca.")]
    [SerializeField, Min(0.001f)] private float worldRadius = 0.045f;

    [Tooltip("Radio mínimo en píxeles: el piso que impide que a 30 m el ojo caiga en sub-píxel " +
             "y titile. 3-4 px es un punto nítido.")]
    [SerializeField, Min(0f)] private float minPixelRadius = 3.5f;

    [Header("Cuándo se ven")]
    [Tooltip("Apertura total en grados dentro de la cual se ven. Fuera de eso el Nemesis te está " +
             "dando la espalda y no hay ojos que mirar.")]
    [SerializeField, Range(1f, 360f)] private float facingAngle = 150f;

    [Tooltip("Distancia por debajo de la cual se apagan, en metros. 0 = siempre prendidos.")]
    [SerializeField, Min(0f)] private float nearFadeStart = 0f;

    [Tooltip("Distancia a partir de la cual están a brillo completo. Entre ésta y la de arriba " +
             "hacen un fundido. Se ignora si no es mayor.")]
    [SerializeField, Min(0f)] private float nearFadeEnd = 0f;

    [Header("Legacy")]
    [Tooltip("Apaga las Light reales de la cabeza y deja sólo los puntos.\n\n" +
             "Estaban puestas como el indicador de hacia dónde mira: dos conos rojos que " +
             "pintaban óvalos en la pared. Es una feature distinta de ésta y compite con ella. " +
             "Destildalo para recuperarlas tal como estaban en el prefab.")]
    [SerializeField] private bool disableRealLights = true;

    private readonly List<FogBeacon> _beacons = new List<FogBeacon>();
    private Transform _center;

    // The flare (see SetFlare): one halo per eye, enabled only while it is up, plus the last values
    // it was given so a retune in Play Mode can re-apply them.
    private readonly List<FogBeacon> _halos = new List<FogBeacon>();
    private bool _lightsOn = true;
    private float _flareAmount;
    private float _flarePower = 1f;
    private float _flareCoreRadius;
    private float _flareHaloIntensity;
    private float _flareHaloRadius;

    // Pixel sizes are authored for a 1080p frame and scaled to the real one.
    private const float ReferenceHeight = 1080f;

    /// <summary>
    /// Makes the eyes flare, and takes the flare back. For a moment that wants them to be seen far
    /// more than they normally are (the escape's eyes shot), without touching the values the
    /// designer tuned: at <paramref name="amount"/> 0 the eyes are exactly as authored.
    ///
    /// This is the eyes' own bloom, and it has to be: the gameplay camera does not render URP's
    /// post-processing, so there is no Bloom volume to push. The fog pass draws each beacon as a
    /// gaussian, so a bigger radius at a high brightness reads as a hot core, and a second, much
    /// wider and fainter gaussian per eye (the halo) reads as the glow bleeding round it. The
    /// halo also hides the hard edge the core's disc would otherwise show.
    /// </summary>
    /// <param name="amount">0..1: how far into the flare.</param>
    /// <param name="power">Brightness multiplier of the core at amount 1.</param>
    /// <param name="coreRadiusPixels">On-screen radius of the core at amount 1, in pixels at 1080p.
    /// Above the fog's <c>beaconMaxPixels</c> to take effect at close range.</param>
    /// <param name="haloIntensity">Brightness of the halo at its centre, at amount 1.</param>
    /// <param name="haloRadiusPixels">On-screen radius of the halo at amount 1, in pixels at 1080p.</param>
    public void SetFlare(float amount, float power, float coreRadiusPixels,
                         float haloIntensity, float haloRadiusPixels)
    {
        _flareAmount = Mathf.Clamp01(amount);
        _flarePower = Mathf.Max(1f, power);
        _flareCoreRadius = Mathf.Max(0f, coreRadiusPixels);
        _flareHaloIntensity = Mathf.Max(0f, haloIntensity);
        _flareHaloRadius = Mathf.Max(0f, haloRadiusPixels);

        ApplyFlare();
    }

    private void ApplyFlare()
    {
        float screen = Screen.height / ReferenceHeight;

        for (int i = 0; i < _beacons.Count; i++)
        {
            FogBeacon beacon = _beacons[i];
            if (beacon == null) continue;

            beacon.IntensityScale = Mathf.Lerp(1f, _flarePower, _flareAmount);
            beacon.minPixelRadius = Mathf.Max(
                minPixelRadius, Mathf.Lerp(minPixelRadius, _flareCoreRadius * screen, _flareAmount));
        }

        bool haloUp = _lightsOn && _flareAmount > 0.001f;
        for (int i = 0; i < _halos.Count; i++)
        {
            FogBeacon halo = _halos[i];
            if (halo == null) continue;

            halo.intensity = _flareHaloIntensity;
            halo.IntensityScale = _flareAmount;

            // Grows with the flare rather than appearing at full size on its first frame.
            halo.minPixelRadius = Mathf.Max(1f, _flareHaloRadius * screen * Mathf.Lerp(0.35f, 1f, _flareAmount));
            halo.enabled = haloUp && _flareHaloIntensity > 0.0001f && _flareHaloRadius > 0.5f;
        }
    }

    private FogBeacon CreateHalo(Light anchor)
    {
        GameObject go = new GameObject("EyeHalo");
        go.transform.SetParent(anchor.transform, false);

        FogBeacon halo = go.AddComponent<FogBeacon>();
        halo.enabled = false;
        ConfigureHalo(halo);
        return halo;
    }

    // The halo follows the eye's own look: same colour, same cone (a halo round eyes that are
    // turned away would give the monster's back away), same near fade.
    private void ConfigureHalo(FogBeacon halo)
    {
        halo.color          = eyeColor;
        halo.worldRadius    = worldRadius;
        halo.limitByFacing  = true;
        halo.facingAngle    = facingAngle;
        halo.nearFadeStart  = nearFadeStart;
        halo.nearFadeEnd    = nearFadeEnd;
    }

    /// <summary>
    /// The point half way between the eyes, riding on the head: for a camera to be trained on "the
    /// eyes" as one point, whatever the model's height or pose. Made the first time it is asked for
    /// and kept in the same parent as the anchors, so it moves with them. Null when there are none.
    /// </summary>
    public Transform Center
    {
        get
        {
            if (_center != null) return _center;

            Transform parent = null;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < eyeLights.Count; i++)
            {
                if (eyeLights[i] == null) continue;

                if (parent == null) parent = eyeLights[i].transform.parent;
                sum += eyeLights[i].transform.position;
                count++;
            }
            if (count == 0) return null;

            _center = new GameObject("EyesCenter").transform;
            _center.SetParent(parent != null ? parent : transform, false);
            _center.position = sum / count;
            return _center;
        }
    }

    private void Awake()
    {
        // Collected rather than dragged in, same reason as before: the lights hang off bones that
        // an animation re-import can rename or reparent, which silently empties an inspector
        // reference.
        if (eyeLights.Count == 0) eyeLights.AddRange(GetComponentsInChildren<Light>(true));

        if (eyeLights.Count == 0)
        {
            Debug.LogWarning($"[{nameof(NemesisEyes)}] '{name}' found no Light under it to use as " +
                             "an eye anchor. There will be no eyes — check that the lights are " +
                             "children of this object.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < eyeLights.Count; i++)
        {
            Light light = eyeLights[i];
            if (light == null) continue;

            if (disableRealLights) light.enabled = false;

            // Get-or-add, then always configure: with one source of truth an authored beacon and a
            // created one behave the same, instead of the look depending on whether someone had
            // added the component by hand.
            if (!light.TryGetComponent(out FogBeacon beacon))
                beacon = light.gameObject.AddComponent<FogBeacon>();

            beacon.color          = eyeColor;
            beacon.intensity      = intensity;
            beacon.worldRadius    = worldRadius;
            beacon.minPixelRadius = minPixelRadius;
            beacon.limitByFacing  = true;
            beacon.facingAngle    = facingAngle;
            beacon.nearFadeStart  = nearFadeStart;
            beacon.nearFadeEnd    = nearFadeEnd;

            _beacons.Add(beacon);
            _halos.Add(CreateHalo(light));
        }
    }

    /// <summary>
    /// Turns the eyes on or off wholesale. Called by <see cref="NemesisLifecycle"/> while the
    /// Nemesis is dormant — a dormant monster leaving two red dots hanging in the fog would
    /// announce something that has not spawned yet.
    /// </summary>
    public void SetLightsEnabled(bool value)
    {
        _lightsOn = value;

        for (int i = 0; i < _beacons.Count; i++)
        {
            if (_beacons[i] != null) _beacons[i].enabled = value;
        }

        // The halos ride on the eyes: dormant or shut, they are down too.
        ApplyFlare();

        if (disableRealLights) return;

        for (int i = 0; i < eyeLights.Count; i++)
        {
            if (eyeLights[i] != null) eyeLights[i].enabled = value;
        }
    }

#if UNITY_EDITOR
    // Retuning in Play Mode without leaving it: the beacons are created in Awake, so an inspector
    // edit would otherwise not reach them until the next entry.
    private void OnValidate()
    {
        if (!Application.isPlaying) return;

        for (int i = 0; i < _beacons.Count; i++)
        {
            FogBeacon beacon = _beacons[i];
            if (beacon == null) continue;

            beacon.color          = eyeColor;
            beacon.intensity      = intensity;
            beacon.worldRadius    = worldRadius;
            beacon.facingAngle    = facingAngle;
            beacon.nearFadeStart  = nearFadeStart;
            beacon.nearFadeEnd    = nearFadeEnd;
        }

        for (int i = 0; i < _halos.Count; i++)
        {
            if (_halos[i] != null) ConfigureHalo(_halos[i]);
        }

        // Sets the pixel radius of the cores (and the halos) back from the authored value.
        ApplyFlare();
    }
#endif
}
