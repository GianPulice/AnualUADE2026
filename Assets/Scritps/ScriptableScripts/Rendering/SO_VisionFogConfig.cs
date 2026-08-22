using UnityEngine;

/// <summary>
/// Vision fog configuration preset. Assignable to the <see cref="VisionRangeController"/>
/// as the default, or used by a <c>LightZone</c> trigger to define the "feeling" of a
/// specific area of the level.
///
/// The designer does not think in terms of physical light amount — they think about what
/// atmosphere this area should have. Typical examples:
///   - Default_Dark        — dead-end corridors, fog closes in early
///   - SafeRoom            — safe room, wide vision even with little light
///   - PuzzleRoom_Lit      — room with monitors, medium-wide vision
///   - BossArena           — tense atmosphere, short vision even with torches
///   - SilentHill_Foggy    — dense Silent Hill style fog, mid grey
///
/// ── THE MODEL (v2) ──────────────────────────────────────────────────────────
/// The fog is no longer "lerp the screen towards fogColor". It is a Beer-Lambert medium
/// with two independent halves, and understanding the split is what makes the sliders
/// predictable:
///
///   result = scene * exp(-opticalDepth * extinction)  +  fogColor * inscatter
///            \_______________________________________/    \___________________/
///              EXTINCTION: eats the scene towards BLACK      IN-SCATTERING: adds colour
///
/// So "how dark does it get" (<see cref="darkness"/>, <see cref="fogDensity"/>) and "what
/// colour is that darkness, and how much of it shows" (<see cref="fogColor"/>,
/// <see cref="inscatterStrength"/>) are separate knobs. With inscatterStrength at 0 the
/// area goes to pure black whatever colour is picked; raising it a little gives "dark with
/// a hint of blue" instead of jumping straight to a flat grey wall — which is exactly what
/// the single lerp could not express.
///
/// ── ON THE TOOLTIPS ─────────────────────────────────────────────────────────
/// Written in Spanish, unlike the doc comments around them. They are the game designer's
/// interface, not the programmer's, and the rest of this feature's designer-facing text
/// (SO_VisionFogConfigEditor's buttons, warnings and preview captions) is already Spanish.
/// Field names stay English because renaming them would break every serialised .asset.
/// Each one names a value worth trying: a tooltip that only restates the field name costs a
/// designer the same hunt through the shader that having no tooltip does.
///
/// To create: Project window → right click → Create → Rendering → Vision Fog Config.
/// </summary>
[CreateAssetMenu(fileName = "SO_VisionFog_", menuName = "Rendering/Vision Fog Config")]
public class SO_VisionFogConfig : ScriptableObject
{
    /// <summary>Bumped whenever the field layout changes in a way that needs translating.</summary>
    public const int CurrentDataVersion = 2;

    // Deliberately initialised to 0, not to CurrentDataVersion. Unity runs field initialisers
    // before applying the serialised YAML, and a v1 asset has no `dataVersion` key at all — so
    // it keeps whatever the initialiser left. Starting at 0 is what makes "no key on disk" mean
    // "old asset, migrate me". Reset() stamps the current version on genuinely new assets.
    [SerializeField, HideInInspector] private int dataVersion = 0;

    // ── Range ───────────────────────────────────────────────────────────────
    [Header("Rango de visión (metros)")]
    [Tooltip("Hasta acá NO hay nada de niebla: se ve la escena limpia.\n\n" +
             "Subirlo da sensación de espacio (un hall), bajarlo a 0–1 hace que la niebla te " +
             "respire en la nuca (un pasillo).")]
    [Min(0f)] public float visionStart = 2f;

    [Tooltip("Distancia donde la niebla llega a su densidad completa. Es LA perilla de " +
             "\"hasta dónde ve el jugador\".\n\n" +
             "Referencias: 6–10 m = opresivo · 12–20 m = normal · 25+ = zona segura o exterior.\n" +
             "Si es menor o igual que visionStart el shader se apaga entero.")]
    [Min(0f)] public float visionEnd = 12f;

    // ── Density ─────────────────────────────────────────────────────────────
    [Header("Densidad")]
    [Tooltip("Cuánta niebla se acumula al llegar a visionEnd. Se lee directo como cuánto de " +
             "la escena SOBREVIVE ahí:\n\n" +
             "  2.3 → queda el 10% (brumoso, todavía adivinás formas)\n" +
             "  4.6 → queda el 1% (el \"no se ve nada\" habitual)\n" +
             "  6.9 → queda el 0.1% (negro absoluto)\n\n" +
             "Mirá la previsualización de abajo: te dice el porcentaje exacto.")]
    [Range(0.25f, 12f)] public float fogDensity = 4.6f;

    [Tooltip("La FORMA de la caída entre visionStart y visionEnd (la densidad total no cambia).\n\n" +
             "  1    = rampa pareja\n" +
             "  >1   = MENOS niebla cerca y un cierre de golpe al final\n" +
             "  <1   = la niebla muerde apenas pasás visionStart\n\n" +
             "OJO: el tooltip viejo de 'densityPower' decía esto al revés. Mirá la curva de la " +
             "previsualización si dudás.")]
    [Range(0.1f, 4f)] public float fogFalloffPower = 1f;

    // ── Darkness (extinction) ───────────────────────────────────────────────
    [Header("Oscuridad (extinción)")]
    [Tooltip("Cuánto se traga la oscuridad a la escena.\n\n" +
             "  1   = en visionEnd es negro de verdad (área de oscuridad)\n" +
             "  0.7 = queda algo del entorno legible en el fondo\n" +
             "  0   = no oscurece nada (sólo quedaría el color de niebla, si tiene)")]
    [Range(0f, 1f)] public float darkness = 1f;

    [Tooltip("Deja que un canal de color sobreviva más que los otros. Blanco = neutro, todos " +
             "igual.\n\n" +
             "Teñirlo apenas de rojo hace que lo cálido se siga leyendo mientras los azules " +
             "mueren primero — lee como luz de sodio en la oscuridad. Es un efecto sutil: " +
             "empezá con (1, 0.9, 0.8), no con rojo puro.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color extinctionTint = Color.white;

    // ── Fog colour (in-scattering) ──────────────────────────────────────────
    [Header("Color de la niebla (in-scattering)")]
    [Tooltip("El color que la niebla SUMA.\n\n" +
             "Ya no es \"el color en el que se convierte la pantalla\": cuánto se ve lo decide " +
             "inscatterStrength, acá abajo. Un área de pura oscuridad deja esto en negro con " +
             "strength 0.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color fogColor = Color.black;

    [Tooltip("Multiplica a fogColor. Existe porque el color picker casi no tiene precisión por " +
             "debajo de 0.05, que es justo el rango que necesita un área oscura.\n\n" +
             "Elegí el TONO con fogColor y la fuerza con este número.")]
    [Range(0f, 4f)] public float fogColorIntensity = 1f;

    [Tooltip("Cuánto color de niebla se inyecta. Ésta es LA perilla contra el \"apenas me " +
             "muevo del negro se pone gris\".\n\n" +
             "  0        = oscuridad pura, no importa qué color esté puesto arriba\n" +
             "  0.03–0.1 = oscuridad con un dejo del color (lo que casi siempre querés)\n" +
             "  1        = niebla estilo Silent Hill, el color tapa la escena")]
    [Range(0f, 1f)] public float inscatterStrength = 0f;

    // ── Light preservation ──────────────────────────────────────────────────
    [Header("Preservación de luces (lo brillante atraviesa)")]
    [Tooltip("Cuánto disuelven la niebla los píxeles que ya son brillantes — es lo que hace que " +
             "una lámpara lejana guíe al jugador.\n\n" +
             "  0    = la niebla tapa todo, no importa el brillo\n" +
             "  0.5–1.5 = las luces se insinúan\n" +
             "  3–4  = las luces perforan fuerte (áreas donde sólo se ven las luces)\n\n" +
             "Los cuatro campos de abajo son los que evitan que esto se vaya de mano.")]
    [Range(0f, 4f)] public float lightPreservation = 0f;

    [Tooltip("Qué tan brillante tiene que ser un píxel para contar como \"una luz\".\n\n" +
             "Es lo que impide que una pared apenas iluminada atraviese igual que una lámpara. " +
             "Si ves demasiado a través de la niebla, SUBÍ ESTE PRIMERO.\n\n" +
             "0.5 acá equivale a luminancia 1.0 en el buffer HDR.")]
    [Range(0f, 1f)] public float lightThreshold = 0.4f;

    [Tooltip("El ancho del borde suave por encima del umbral.\n\n" +
             "  0.05 = corte duro entre \"es luz\" y \"no es luz\" (se ve el recorte)\n" +
             "  0.25 = transición natural\n" +
             "  0.6+ = muy gradual, casi sin umbral")]
    [Range(0.01f, 1f)] public float lightKnee = 0.25f;

    [Tooltip("El techo del efecto: cuánta niebla puede sacar una luz como MÁXIMO.\n\n" +
             "Por debajo de 1 ninguna luz limpia del todo, así que una lámpara lejana queda " +
             "brumosa en vez de leerse como un agujero recortado en la oscuridad.\n" +
             "0.45–0.6 es un buen punto de partida. 1 = puede limpiar el 100%.")]
    [Range(0f, 1f)] public float maxLightPreservation = 0.6f;

    [Tooltip("Qué tan rápido se apaga la preservación con la distancia al jugador.\n\n" +
             "  0    = una lámpara a 60 m atraviesa igual que una a 3 m (era el bug viejo)\n" +
             "  0.05 = a la mitad del efecto a ~14 m\n" +
             "  0.15 = sólo atraviesan las luces cercanas")]
    [Range(0f, 0.5f)] public float lightDistanceFalloff = 0.05f;

    // ── Blur ────────────────────────────────────────────────────────────────
    [Header("Blur")]
    [Tooltip("Desenfoque óptico a medida que la niebla espesa — lejos no sólo se oscurece, " +
             "también se pierde nitidez.\n\n" +
             "Valores típicos: 0.004–0.015. Es el radio a densidad plena; el shader lo escala " +
             "solo según cuánta niebla hay en cada píxel.\n\n" +
             "Necesita 'Enable Blur' prendido en el material (VisionFog.mat).")]
    [Range(0f, 0.05f)] public float blurStrength = 0f;

    // ── Player module lights ────────────────────────────────────────────────
    // Not a flashlight: these are the module LEDs stuck to the character's body (head, leg),
    // so the opening is a sphere around the player rather than a cone he aims.
    [Header("Luces del módulo del player")]
    [Tooltip("Radio en metros donde las luces del cuerpo del jugador disuelven la niebla. " +
             "0 = apagado.\n\n" +
             "No es una linterna: son los LEDs pegados al cuerpo, así que abre una ESFERA " +
             "alrededor del jugador, no un cono que apunta.\n\n" +
             "Un FogLightSource en la escena puede pisar este valor con el range de su Light real.")]
    [Range(0f, 30f)] public float playerLightRange = 8f;

    [Tooltip("Cuánta niebla disuelven las luces del módulo en el centro del radio.\n\n" +
             "  1    = limpia del todo\n" +
             "  0.85 = queda algo de niebla adentro (más creíble en un área oscura)\n\n" +
             "Antes se llamaba playerLightIntensity y no tenía tope.")]
    [Range(0f, 1f)] public float playerLightClear = 0.85f;

    [Tooltip("La forma de la caída del radio.\n\n" +
             "  1 = lineal, el borde se nota\n" +
             "  2 = cuadrática (lo que hacía la versión vieja, fijo)\n" +
             "  4+ = una apertura chica y focal, con mucho degradé")]
    [Range(0.5f, 8f)] public float playerLightFalloff = 2f;

    [Tooltip("Color de las luces del módulo. Lo usan tanto el tinte como la inyección de acá abajo.\n\n" +
             "OJO: desde el fix de color space esto se convierte a lineal, así que un color muy " +
             "saturado pega bastante más fuerte que antes. Si venías de la versión vieja y lo " +
             "querés igual, subí los canales bajos (un (1, 0.06, 0.06) viejo equivale a " +
             "(1, 0.27, 0.27) ahora).")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color playerLightColor = new Color(1f, 0.85f, 0.6f);

    [Tooltip("Cuánto MULTIPLICA el color a la escena dentro del radio.\n\n" +
             "Un color saturado además de teñir oscurece (el rojo baja la luminancia a ~34%), y " +
             "eso es lo que mantenía oscuro el entorno adentro del agujero en la versión vieja. " +
             "Ahora es opcional: con la extinción bien seteada podés bajarlo a 0 y queda más " +
             "predecible.")]
    [Range(0f, 1f)] public float playerLightTint = 1f;

    [Tooltip("Cuánta luz de color SUMA el módulo dentro de la niebla — el halo.\n\n" +
             "Es lo que hace que la zona iluminada se lea como una lámpara en la oscuridad y no " +
             "como un agujero recortado. 0 reproduce la versión vieja; probá 0.3–1.")]
    [Range(0f, 4f)] public float playerLightInjection = 0f;

    // ── World light sources ─────────────────────────────────────────────────
    [Header("Focos del mundo (componentes FogLightBypass)")]
    [Tooltip("La forma de la caída de TODAS las zonas bypass del área. 2 = cuadrática, que es lo " +
             "que hacía la versión vieja fijo.")]
    [Range(0.5f, 8f)] public float bypassFalloff = 2f;

    [Tooltip("El color que usan las zonas bypass que no traen el suyo propio.\n\n" +
             "La idea es que todas las lámparas de un área se vean iguales y se retoquen juntas " +
             "desde acá. Poné el override en el componente FogLightBypass sólo cuando una " +
             "lámpara tenga que diferenciarse.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color bypassDefaultColor = new Color(1f, 0.85f, 0.6f);

    [Tooltip("Cuánta luz inyecta en la niebla una zona bypass por defecto — o sea, cuánto BRILLA.\n\n" +
             "0 = sólo limpia niebla, sin halo.")]
    [Range(0f, 8f)] public float bypassDefaultIntensity = 1f;

    [Tooltip("Cuánta niebla disuelve una zona bypass por defecto — o sea, cuánto ACLARA.\n\n" +
             "Está separado de la intensidad a propósito: una lámpara vista a través de niebla " +
             "espesa brilla mucho (intensity alto) sin que la geometría de alrededor se vuelva " +
             "nítida (clear bajo). Ése es el look que casi siempre querés.")]
    [Range(0f, 1f)] public float bypassDefaultClear = 1f;

    // ── Transition ──────────────────────────────────────────────────────────
    [Header("Transición al activarse")]
    [Tooltip("Segundos que tarda la niebla en pasar del preset anterior a éste, cuando el " +
             "jugador entra a la LightZone.\n\n" +
             "  0     = cambio instantáneo (para sustos o cortes)\n" +
             "  1–3   = se nota pero no molesta\n" +
             "  8–15  = el jugador no registra el cambio, sólo la sensación")]
    [Min(0f)] public float transitionDuration = 1f;

    // ── GD PENDING §3.7 ────────────────────────────────────────────────────────
    // Which objects show a silhouette through the fog.
    // Default = None until it is agreed with GD when it applies (playtesting).
    [Header("Siluetas en la niebla — pendiente de GD §3.7")]
    [Tooltip("TODAVÍA NO IMPLEMENTADO en el shader — está pendiente de definir con GD (§3.7).\n\n" +
             "  None    = ningún objeto se insinúa a través de la niebla\n" +
             "  Items   = silueta sólo en items agarrables\n" +
             "  Puzzles = silueta sólo en interactuables\n" +
             "  All     = las dos")]
    public SilhouetteMode silhouetteMode = SilhouetteMode.None;

    // ── Legacy v1 fields ────────────────────────────────────────────────────
    // Kept, with their original serialised names, purely so Unity can still read them out of
    // the four existing .asset files. Migrate() translates them and they are never read again.
    // Removing them would make the migration silently no-op on the assets it exists for.
    [SerializeField, HideInInspector] private float densityPower = 1f;
    [SerializeField, HideInInspector] private float playerLightIntensity = 1f;

    // ── Migration ───────────────────────────────────────────────────────────

    /// <summary>
    /// Translates a v1 asset in place. Idempotent: it stamps <see cref="dataVersion"/> so it
    /// runs once, and re-running it by hand on an already-migrated asset only rewrites values
    /// that the v1 fields still describe.
    /// </summary>
    [ContextMenu("Migrate v1 values → v2")]
    public void Migrate()
    {
        // densityPower did exactly the job fogFalloffPower does now — the maths is the same
        // pow() on the normalised ramp. Only the documentation was inverted. So the number
        // carries over as-is and the look is preserved.
        fogFalloffPower = densityPower > 0.001f ? densityPower : 1f;

        // v1 had no density concept: it lerped fully to fogColor at visionEnd. 4.6 is the
        // exponent that leaves 1% of the scene there, which is the closest honest equivalent.
        fogDensity = 4.6f;
        darkness = 1f;
        extinctionTint = Color.white;

        // The v1 lerp meant a non-black fogColor WAS the screen at full density, so any preset
        // with real colour in it was authoring visible fog and should keep showing it. A black
        // fogColor was authoring darkness, which is now inscatterStrength 0.
        //
        // The colour value itself is carried over unchanged rather than brightened to match how
        // it used to look. v1 pushed it through Shader.SetGlobalColor, which does no gamma
        // conversion, so in this Linear project every fog colour was rendering roughly 2-3x
        // brighter than authored. Preserving that would preserve the bug this migration exists
        // to fix — the presets are meant to come out darker, and correct.
        inscatterStrength = fogColor.maxColorComponent > 0.01f ? 1f : 0f;
        fogColorIntensity = 1f;

        // v1 clamped nothing, so presets carry values tuned against a formula that saturated
        // instantly on the HDR buffer. The number is kept but the new threshold/knee/ceiling
        // are what actually make it behave — expect to retune these three per preset.
        lightPreservation = Mathf.Clamp(lightPreservation, 0f, 4f);
        lightThreshold = 0.4f;
        lightKnee = 0.25f;
        maxLightPreservation = 0.6f;
        lightDistanceFalloff = 0.05f;

        // v1's playerLightIntensity was an unbounded multiplier fed into a saturate(), so
        // anything at or above 1 meant "fully clear".
        playerLightClear = Mathf.Clamp01(playerLightIntensity);
        playerLightFalloff = 2f;   // the curve v1 hard-coded
        playerLightTint = 1f;      // v1 always applied the full multiply
        playerLightInjection = 0f; // v1 had no additive term

        bypassFalloff = 2f;        // v1 hard-coded quadratic
        bypassDefaultColor = new Color(1f, 0.85f, 0.6f);
        bypassDefaultIntensity = 0f; // v1 bypass zones cleared fog but emitted no light
        bypassDefaultClear = 1f;

        dataVersion = CurrentDataVersion;
    }

    /// <summary>Unity calls this when the asset is first created, which is the one moment we
    /// know the values are new defaults rather than v1 data worth translating.</summary>
    private void Reset()
    {
        dataVersion = CurrentDataVersion;
    }

    /// <summary>
    /// Runs in the player too, not only the editor. A v1 asset that nobody happened to open in
    /// the Inspector before the build would otherwise ship with the new fields left on their
    /// initialisers — the fog would be quietly wrong in the build and correct in the editor,
    /// which is the worst version of this bug to have to find.
    /// </summary>
    private void OnEnable()
    {
        if (dataVersion < CurrentDataVersion) Migrate();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (dataVersion >= CurrentDataVersion) return;

        Migrate();

        // Deferred: OnValidate can run during asset import, and marking an object dirty from
        // inside that pass is what produces the "SetDirty called during import" warnings.
        // This is the half that makes the migration stick on disk rather than only in memory.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            UnityEditor.EditorUtility.SetDirty(this);
        };
    }
#endif
}

public enum SilhouetteMode
{
    None,
    Items,
    Puzzles,
    All
}
