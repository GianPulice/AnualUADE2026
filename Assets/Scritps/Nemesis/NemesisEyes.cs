using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Nemesis's eye lights, reachable and drivable from the root.
///
/// The lights themselves sit several levels down the rig, on bones that an animation import can
/// rename or reparent. Reaching them by dragging references into an inspector means a re-import
/// silently empties the field; this collects them from its own hierarchy in Awake, the same way
/// NemesisStateManager resolves the sensors and the Animator, so a rig swap costs nothing.
///
/// SETUP: add it to the Nemesis root. It finds the lights on its own — the two Point Lights on the
/// head in the current prefab — and nothing has to be wired.
///
/// WHY IT DRIVES THEM FROM THE STATE
///
/// Because they are the only tell the Nemesis has at a distance. It walks, it stops, it turns
/// round; from thirty metres down a corridor the silhouette says almost nothing about whether it
/// has noticed you. The eyes can say it before the audio does.
///
/// The colours honour the project's rule that RED IS DANGER AND NOTHING ELSE: only the capture
/// burns red. Alerted is amber — the same alert band the gizmos and the HUD use — and calm is the
/// cool blue that means passive everywhere else in this codebase.
/// </summary>
public class NemesisEyes : MonoBehaviour
{
    [Header("Lights")]
    [Tooltip("Leave empty to collect every Light under this object at startup, which is what the " +
             "prefab expects. Fill it in only to drive a subset.")]
    [SerializeField] private List<Light> eyeLights = new List<Light>();

    [Header("Colour by state")]
    [Tooltip("Patrolling: it has no idea where you are.")]
    [SerializeField] private Color calmColor = new Color(0.541f, 0.706f, 0.831f);

    [Tooltip("Investigating and Searching: it is acting on something it sensed.")]
    [SerializeField] private Color alertColor = new Color(1f, 0.784f, 0.314f);

    [Tooltip("Chasing and Traversing: it is coming for you and knows where you are.")]
    [SerializeField] private Color huntColor = new Color(1f, 0.55f, 0.15f);

    [Tooltip("Catch only. Red is reserved for danger by the project's visual language, and a " +
             "capture is the only moment that qualifies.")]
    [SerializeField] private Color catchColor = new Color(0.8f, 0.1f, 0.1f);

    [Header("Intensity")]
    [SerializeField, Min(0f)] private float calmIntensity = 1.2f;
    [SerializeField, Min(0f)] private float huntIntensity = 2.6f;

    [Header("Beam — where it is looking")]
    [Tooltip("Alcance del haz, en metros. El prefab venía en 2, que es la razón por la que los " +
             "ojos no se leían de lejos: el cono moría antes de tocar nada.\n\n" +
             "Lo que ve el jugador NO es el haz en el aire (no hay volumétrico) sino los DOS " +
             "óvalos rojos que el cono deja sobre la pared o el piso que el Nemesis está mirando. " +
             "Ese es el indicador de hacia dónde mira, y se lee desde mucho más lejos que el " +
             "alcance del haz en sí.\n\n" +
             "OJO: esto NO cambia lo que el Nemesis ve. La detección es ViewRange en " +
             "SO_NemesisData (7 m) y es un número totalmente independiente — el haz puede llegar " +
             "a 15 m sin que el Nemesis te detecte a más de 7.")]
    [SerializeField, Min(0f)] private float beamRange = 12f;

    [Tooltip("Multiplica el alcance del haz mientras persigue. Un cono que se estira al " +
             "detectarte es legible desde lejos incluso sin mirar el color.")]
    [SerializeField, Min(0.1f)] private float huntBeamRangeScale = 1.35f;

    [Header("Glow through the fog")]
    [Tooltip("Opcional. El FogLightBypass que hace que los ojos se lean COMO UN RESPLANDOR a " +
             "través de la niebla, que es lo que te deja reconocer al Nemesis de lejos.\n\n" +
             "Vacío se busca en esta jerarquía. Si no hay ninguno, los ojos siguen funcionando " +
             "como luces normales — solo que la niebla se los come a distancia.")]
    [SerializeField] private FogLightBypass fogGlow;

    [Tooltip("Radio en metros del halo. CHICO a propósito — cerca del ancho de la cabeza.\n\n" +
             "El bypass es una ESFERA por definición (posición + radio), así que no puede mostrar " +
             "dirección: con un radio grande el Nemesis se convierte en un farol flotante y tapa " +
             "justamente el cono que sí dice hacia dónde mira. Su único trabajo acá es que los " +
             "ojos se ENCUENTREN entre la niebla, como dos puntos. Quién mira hacia dónde lo dice " +
             "el haz, no esto.")]
    [SerializeField, Min(0f)] private float glowRadius = 1.8f;

    [Tooltip("Cuánta niebla DISUELVE el halo. Prácticamente 0 a propósito.\n\n" +
             "Subirlo hace que el Nemesis camine adentro de una burbuja de aire limpio: se ve " +
             "mal, y además te REGALA información — verías nítido todo lo que lo rodea.")]
    [SerializeField, Range(0f, 1f)] private float glowClearAmount = 0.02f;

    [Tooltip("Multiplica la intensidad de la Light para el brillo del halo. Por debajo de 1 a " +
             "propósito: si el halo compite con el haz, gana el halo — es más grande y no tiene " +
             "forma — y perdés la lectura de hacia dónde mira.")]
    [SerializeField, Min(0f)] private float glowIntensityScale = 0.45f;

    [Tooltip("How fast colour and intensity ease towards their target, per second. Snapping reads " +
             "as a UI element rather than as a light on a creature.")]
    [SerializeField, Min(0.1f)] private float easeSpeed = 4f;

    private Color targetColor;
    private float targetIntensity;
    private float targetRange;
    private Color currentColor;
    private float currentIntensity;
    private float currentRange;

    private void Awake()
    {
        if (eyeLights.Count == 0) eyeLights.AddRange(GetComponentsInChildren<Light>(true));

        // FogLightBypass and NOT FogLightSource, and the difference is not cosmetic:
        // VisionRangeController keeps exactly ONE FogLightSource (_playerLight, found with
        // FindAnyObjectByType), so putting one on the Nemesis would take the slot away from the
        // player's own module light. Bypass zones are a registered list of up to
        // VisionRangeController.MaxBypassZones instead, which is what a second glowing thing in
        // the world is supposed to be.
        if (fogGlow == null) fogGlow = GetComponentInChildren<FogLightBypass>(true);

        if (eyeLights.Count == 0)
        {
            Debug.LogWarning($"[{nameof(NemesisEyes)}] '{name}' found no Light under it. The eyes " +
                             "will do nothing — check that the lights are children of this object.",
                             this);
            enabled = false;
            return;
        }

        // Awake/OnDestroy and not OnEnable/OnDisable, per the project's convention for static
        // events: a static delegate outlives the GameObject's enabled state, so an enabled-scoped
        // subscription is a listener that quietly stops listening. Here that would mean the eyes
        // staying whatever colour they were when the object was last switched off.
        NemesisEvents.OnStateChanged += HandleStateChanged;

        currentColor = targetColor = calmColor;
        currentIntensity = targetIntensity = calmIntensity;
        currentRange = targetRange = beamRange;
        Apply();
    }

    private void OnDestroy() => NemesisEvents.OnStateChanged -= HandleStateChanged;

    /// <summary>
    /// Turns the eyes on or off wholesale. Called by whatever hides the Nemesis while it is
    /// dormant — a disabled Light is the one thing that makes it genuinely invisible in the dark,
    /// where switching the renderers off is not enough on its own.
    /// </summary>
    public void SetLightsEnabled(bool value)
    {
        for (int i = 0; i < eyeLights.Count; i++)
        {
            if (eyeLights[i] != null) eyeLights[i].enabled = value;
        }

        // The glow goes with them. A dormant Nemesis leaving a red halo hanging in the fog would
        // announce a monster that has not spawned yet — and it is a registered bypass zone, so it
        // would also be eating one of the sixteen slots for nothing.
        if (fogGlow != null) fogGlow.enabled = value;
    }

    private void HandleStateChanged(NemesisStateManager.ENemesisState state)
    {
        switch (state)
        {
            case NemesisStateManager.ENemesisState.Catch:
                targetColor = catchColor;
                targetIntensity = huntIntensity;
                targetRange = beamRange * huntBeamRangeScale;
                break;

            case NemesisStateManager.ENemesisState.Chasing:
            case NemesisStateManager.ENemesisState.Traversing:
                targetColor = huntColor;
                targetIntensity = huntIntensity;
                targetRange = beamRange * huntBeamRangeScale;
                break;

            case NemesisStateManager.ENemesisState.Investigating:
            case NemesisStateManager.ENemesisState.Searching:
                targetColor = alertColor;
                targetIntensity = Mathf.Lerp(calmIntensity, huntIntensity, 0.5f);
                targetRange = beamRange;
                break;

            default:
                targetColor = calmColor;
                targetIntensity = calmIntensity;
                targetRange = beamRange;
                break;
        }
    }

    private void Update()
    {
        // Unscaled: the eyes keep easing while a pause menu is up rather than freezing
        // mid-transition, which would leave them a colour that belongs to neither state.
        float step = easeSpeed * Time.unscaledDeltaTime;

        currentColor = Color.Lerp(currentColor, targetColor, step);
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, step);
        currentRange = Mathf.Lerp(currentRange, targetRange, step);

        Apply();
    }

    private void Apply()
    {
        for (int i = 0; i < eyeLights.Count; i++)
        {
            if (eyeLights[i] == null) continue;

            eyeLights[i].color = currentColor;
            eyeLights[i].intensity = currentIntensity;

            // Only Spot lights carry a direction, and these are Spots (22° cone) despite the
            // GameObjects being named "Point Light". The beam is the whole tell: what the player
            // reads from across a room is the pair of coloured pools it leaves on whatever the
            // Nemesis is facing.
            eyeLights[i].range = currentRange;
        }

        if (fogGlow == null) return;

        // overrideAppearance on, because the point of this zone is precisely that it does NOT look
        // like the area's lamps: it is the one light in the level that means "the thing that kills
        // you is over there".
        fogGlow.overrideAppearance = true;
        fogGlow.radius = glowRadius;
        fogGlow.color = currentColor;
        fogGlow.intensity = currentIntensity * glowIntensityScale;

        // Glow high, clear low — the exact case FogLightBypass's own docs call out: "a lamp seen
        // through heavy fog wants high intensity and low clear". Clearing the fog around the
        // Nemesis would hand the player a clean view of whatever room it is standing in, which is
        // the opposite of a warning.
        fogGlow.clearAmount = glowClearAmount;
    }
}
