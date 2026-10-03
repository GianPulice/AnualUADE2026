using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Per-state looping audio for the Nemesis, with crossfades and wall occlusion.
///
/// Two AudioSources are kept and swapped: on a state change the incoming loop fades in while the
/// outgoing one fades out over <see cref="crossfadeDuration"/>. Nothing is ever cut abruptly —
/// a state with no clip configured crossfades to silence rather than stopping.
///
/// Occlusion attenuates, it does not mute: a wall between the Nemesis and the player drops the
/// volume to <see cref="occludedVolumeMultiplier"/>, and that multiplier itself is eased so
/// walking past a doorway does not click.
///
/// Reactive chase music lives separately, in <see cref="NemesisChaseMusic"/>: it owns the Music
/// bus rather than this class's Nemesis bus, and reacts to NemesisEvents.OnChaseStarted/OnChaseEnded
/// instead of every per-state loop here.
///
/// Beside the loops, the Nemesis's one-shots, all through AudioManager's pool on the same bus: the
/// cues of a drop between floors (plan §15.5), and its voice — the sting when it knows which hiding
/// spot the player is in, "te perdí" when a search gives up, and the activation cue (plan §16.2 and
/// principle 7). Voices share a cooldown so it never talks over itself.
/// </summary>
public class NemesisAudio : MonoBehaviour
{
    [Serializable]
    private class StateLoop
    {
        public NemesisStateManager.ENemesisState state;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Loops")]
    [Tooltip("One entry per state. States with no entry, or with an empty clip, fade to silence.")]
    [SerializeField] private StateLoop[] stateLoops = Array.Empty<StateLoop>();

    [Tooltip("Seconds to crossfade between two state loops. Spec range: 0.3 - 0.5.")]
    [SerializeField, Range(0.05f, 2f)] private float crossfadeDuration = 0.4f;

    [Header("Routing")]
    [Tooltip("Optional. If empty it is taken from AudioManager's Nemesis bus at runtime.")]
    [SerializeField] private AudioMixerGroup outputGroup;

    [Header("3D falloff")]
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 25f;

    [Header("Occlusion")]
    [SerializeField] private bool occlusionEnabled = true;

    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents. " +
             "Reuses its obstacleMask raycast instead of a second one here.")]
    [SerializeField] private FieldOfListening fieldOfListening;

    [Tooltip("Volume multiplier while a wall stands between the Nemesis and the player. " +
             "Attenuation, never a cut: 0 would silence it completely.")]
    [SerializeField, Range(0f, 1f)] private float occludedVolumeMultiplier = 0.35f;

    [Tooltip("How fast the occlusion multiplier eases towards its target, per second.")]
    [SerializeField, Min(0.1f)] private float occlusionEaseSpeed = 3f;

    [Header("Bajadas entre pisos (plan §15.5)")]
    [Tooltip("Gruñido al asomarse al borde antes de tirarse. Es la mitad audible del aviso: desde " +
             "abajo el jugador lo oye antes de verlo. Uno al azar por bajada. Vacío = se tira en " +
             "silencio (la bajada anda igual).")]
    [SerializeField] private AudioClip[] dropGrowls = Array.Empty<AudioClip>();

    [Tooltip("Golpe de manos en el borde al descolgarse (sólo en las bajadas altas).")]
    [SerializeField] private AudioClip dropHandSlam;

    [Tooltip("Impacto al aterrizar. Más fuerte que un paso de persecución: se oye desde lejos, y es " +
             "lo que le dice al jugador que ya está abajo.")]
    [SerializeField] private AudioClip dropImpact;

    [SerializeField, Range(0f, 1f)] private float dropCueVolume = 1f;

    [Tooltip("Pitch del impacto. Debajo de 1 suena más pesado, y permite usar un paso como impacto " +
             "provisorio.")]
    [SerializeField, Range(0.5f, 1.5f)] private float dropImpactPitch = 0.85f;

    [Header("Voz y avisos (plan §16.2, principio 7)")]
    [Tooltip("Aviso cuando SABE en qué escondite estás y todavía no llegó a la puerta (D1, D16, D34). " +
             "Desde adentro es lo único que distingue \"sabe\" de \"adivina\": el margen para decidir " +
             "salir antes de que llegue. Uno al azar. Vacío = sin aviso.")]
    [SerializeField] private AudioClip[] knownSpotStings = Array.Empty<AudioClip>();

    [Tooltip("Pitch del aviso. Debajo de 1 suena más grave, y permite usar una voz como aviso provisorio.")]
    [SerializeField, Range(0.5f, 1.5f)] private float knownSpotStingPitch = 0.8f;

    [Tooltip("Metros en planta desde el Nemesis a la puerta del escondite por debajo de los cuales el " +
             "aviso no suena. Ahí lo abre enseguida, y el golpe tiene que ser la música al abrir (D13), " +
             "no una voz un segundo antes.")]
    [SerializeField, Min(0f)] private float knownSpotStingMinDistance = 2f;

    [Tooltip("Voz de \"te perdí\": suena al volver a patrullar después de una búsqueda que terminó sin " +
             "encontrarte. Confirma lo que ya dice el silencio de la música (D5): dejó de buscar. Una al " +
             "azar. Vacío = sin voz.")]
    [SerializeField] private AudioClip[] lostVoices = Array.Empty<AudioClip>();

    [Tooltip("Sonido (SO_SoundData) cuando lo despierta un puzzle. No suena si lo despierta un script: " +
             "el escape trae el suyo (SO_EscapeSequenceConfig.revealSoundId). Vacío = sin cue.")]
    [SerializeField, SoundId] private string activationSoundId = "sfx_nemesis_activacion";

    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

    [Tooltip("Segundos mínimos entre dos voces (el aviso o \"te perdí\"), para que no hable encima de " +
             "sí mismo.")]
    [SerializeField, Min(0f)] private float voiceCooldown = 3f;

    // Set once the run has a result: from then on the loops only fade out.
    private bool silenced;

    // Set when the grab lands (Catch): the loops have faded out and stay out until the state
    // leaves Catch. See HandlePlayerCaptured.
    private bool grabbing;

    private NemesisStateManager stateManager;

    /// <summary>The known spot as of the last frame, to hear it change. See
    /// <see cref="TickKnownSpotSting"/>.</summary>
    private HidingSpot lastKnownSpot;

    /// <summary>A search ended empty and the hunt has not settled yet. See
    /// <see cref="HandleSearchEnded"/>.</summary>
    private bool lostVoicePending;

    private float nextVoiceAt;

    private AudioSource sourceA;
    private AudioSource sourceB;

    private AudioSource activeSource;   // fading in / already at full
    private AudioSource fadingSource;   // fading out

    private float activeTargetVolume;
    private float fadingStartVolume;
    private float crossfadeProgress = 1f;

    private float occlusionMultiplier = 1f;

    private void Awake()
    {
        stateManager = GetComponentInParent<NemesisStateManager>();

        if (fieldOfListening == null && stateManager != null) fieldOfListening = stateManager.FieldOfListening;

        NemesisEvents.OnStateChanged += HandleStateChanged;
        NemesisEvents.OnSearchEnded += HandleSearchEnded;
        NemesisEvents.OnActivated += HandleActivated;
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        GameResultManager.OnGameResult += HandleGameResult;
        GameResultManager.OnResultCleared += HandleResultCleared;

        sourceA = CreateSource("NemesisLoopA");
        sourceB = CreateSource("NemesisLoopB");
        activeSource = sourceA;
        fadingSource = sourceB;
    }

    private void Start()
    {
        // Resolved in Start and not Awake: AudioManager sets its groups up in its own Awake.
        if (outputGroup == null && AudioManager.Exists) outputGroup = AudioManager.Instance.NemesisGroup;

        sourceA.outputAudioMixerGroup = outputGroup;
        sourceB.outputAudioMixerGroup = outputGroup;

        WarnIfUnauthored();
    }

    /// <summary>
    /// Says so, once, when this component will never make a sound.
    ///
    /// It exists because NemesisStateManager now grows this component on any Nemesis that lacks
    /// one. That is the right default — but it turns "there is no audio component" into "there is
    /// an audio component with nothing in it", and the second failure is the quieter of the two:
    /// silence is also what a correctly-authored monster does between crossfades, so there is
    /// nothing to tell the two apart by ear. Authoring stateLoops is a designer task on the
    /// prefab, and this is the line that says the task is still open.
    /// </summary>
    private void WarnIfUnauthored()
    {
        foreach (StateLoop loop in stateLoops)
            if (loop != null && loop.clip != null) return;

        Debug.LogWarning($"[{nameof(NemesisAudio)}] '{name}': stateLoops has no clip in it, so the " +
                         "Nemesis will be silent in every state. Author one entry per state on the " +
                         "prefab (Patrolling, Investigating, Chasing, Searching).", this);
    }

    // Awake/OnDestroy and not OnEnable/OnDisable, per docs/CLAUDE.md: a static delegate outlives
    // the GameObject's enabled state, so scoping the subscription to it produces a listener that
    // quietly stops listening. Here that would mean missing the state change that happened while
    // this was off and coming back playing the wrong loop, because OnStateChanged only fires on
    // the transition and there is no catch-up.
    private void OnDestroy()
    {
        NemesisEvents.OnStateChanged -= HandleStateChanged;
        NemesisEvents.OnSearchEnded -= HandleSearchEnded;
        NemesisEvents.OnActivated -= HandleActivated;
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        GameResultManager.OnGameResult -= HandleGameResult;
        GameResultManager.OnResultCleared -= HandleResultCleared;
    }

    /// <summary>
    /// The Nemesis has the player in its hands: its breathing and growling loops fade out, so the
    /// grab plays without them. Catch has an authored loop (the chase's, which was already running
    /// when it got there), and left alone it would keep going under the whole shot.
    ///
    /// Only for this Nemesis's own grab (it is in Catch, which is also where the event comes from:
    /// OnCaptured raises it from inside Catch's EnterState). A capture raised from anywhere else
    /// leaves the loops alone, since nothing would bring them back. The hidden player's pull-out is
    /// Catch too, but silent here until the hands arrive: the event is the grab, not the state.
    ///
    /// What brings the loops back is the state leaving Catch (<see cref="HandleStateChanged"/>).
    /// </summary>
    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        if (stateManager == null || stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Catch) return;

        grabbing = true;
        if (!silenced) StartCrossfade(null, 0f);
    }

    /// <summary>
    /// The run is over — win, loss or game over. The crossfade runs on scaled time, and the result
    /// screen sets timeScale to 0: left alone, the current loop would freeze at full volume and
    /// play over the result screen forever. So the loops fade out on unscaled time instead (see
    /// <see cref="FadeOutForResult"/>). Lives with the level, so a Retry reloads it clean.
    /// </summary>
    private void HandleGameResult(GameResultModel result) => silenced = true;

    // A defeat the level took back (the escape replays its cinematic): the Nemesis has a voice again.
    private void HandleResultCleared() => silenced = false;

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 1f;                       // Fully 3D.
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;

        return source;
    }

    private void HandleStateChanged(NemesisStateManager.ENemesisState state)
    {
        if (state != NemesisStateManager.ENemesisState.Catch) grabbing = false;

        if (silenced) return;

        TrackLostVoice(state);

        // The grab has already faded the loops out. This event can reach here after it (the
        // telemetry reports the state a frame later) and would start Catch's loop again.
        if (grabbing) return;

        if (!TryGetLoop(state, out AudioClip clip, out float volume))
        {
            // No entry authored for this state. Silence is a legitimate authoring choice for a
            // state a designer deliberately left blank — but it is the wrong answer for
            // Traversing, which did not exist when these arrays were filled in. Every prefab in
            // the project therefore has a hole exactly there, and the Nemesis would go quiet for
            // the whole elevator ride: the one stretch of a pursuit where the player cannot see
            // it and the loop is all they have to go on.
            //
            // Traversing IS the chase continuing by other means, so it borrows the chase loop.
            if (state == NemesisStateManager.ENemesisState.Traversing)
                TryGetLoop(NemesisStateManager.ENemesisState.Chasing, out clip, out volume);
        }

        // Same clip already playing (e.g. Chasing -> Catch sharing a loop): let it run rather
        // than restarting it from sample zero, which would be audible.
        if (clip != null && activeSource.clip == clip && activeSource.isPlaying)
        {
            activeTargetVolume = volume;
            return;
        }

        StartCrossfade(clip, volume);
    }

    /// <summary>
    /// A one-shot of a drop between floors (plan §15.5): the growl when it looks down, the hands on
    /// the edge, the landing. Played by NemesisElevatorUser at the start of each phase, not by
    /// animation events: the phases are timed by code, and the placeholder drop has no clips to
    /// carry events at all.
    ///
    /// On the Nemesis bus and in 3D, like the loops; never through DetectableAudio, which is what
    /// the Nemesis HEARS, not what it does. Fire-and-forget through AudioManager's pool, so no
    /// occlusion: at the range these carry, a floor slab between is the point.
    /// </summary>
    public void PlayDropCue(EDropCue cue)
    {
        if (silenced || !AudioManager.Exists) return;

        AudioClip clip;
        switch (cue)
        {
            case EDropCue.Growl:
                clip = dropGrowls.Length > 0 ? dropGrowls[UnityEngine.Random.Range(0, dropGrowls.Length)] : null;
                break;
            case EDropCue.HandSlam: clip = dropHandSlam; break;
            case EDropCue.Impact:   clip = dropImpact;   break;
            default:                clip = null;         break;
        }

        if (clip == null) return;

        float pitch = cue == EDropCue.Impact ? dropImpactPitch : 1f;
        AudioManager.Instance.PlayNemesis(clip, transform.position, dropCueVolume, pitch, minDistance, maxDistance);
    }

    // ── Voice and warnings (plan §16.2, principle 7) ────────────────────────

    /// <summary>
    /// A search is over. An empty one arms "te perdí", which only speaks once the hunt has really
    /// settled back into patrol.
    ///
    /// NOT ON THE EVENT ITSELF. Searching → Investigating counts as the end of a search too (plan Fase
    /// 3): off to check a sigh, a decoy that outbids a stale belief (2B part 4). Voicing it there
    /// would say "lost you" and go on hunting. So an empty end only arms the line; Patrolling plays
    /// it, and Chasing or Catch in between disarms it (<see cref="TrackLostVoice"/>). The same
    /// rule the Director's revisit uses.
    ///
    /// The one ordering trap: NemesisTelemetry raises StateChanged BEFORE SearchEnded, so on the
    /// commonest ending of all — Searching straight to Patrolling — the Patrolling event has already
    /// gone by when this arrives. Already patrolling, it speaks now.
    /// </summary>
    private void HandleSearchEnded(Vector3 area, bool found)
    {
        if (found)
        {
            lostVoicePending = false;
            return;
        }

        if (stateManager != null && stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Patrolling)
        {
            lostVoicePending = false;
            PlayVoice(lostVoices, 1f);
            return;
        }

        lostVoicePending = true;
    }

    /// <summary>The state half of <see cref="HandleSearchEnded"/>: a hunt that found the player
    /// cancels the line, one that settles into patrol speaks it.</summary>
    private void TrackLostVoice(NemesisStateManager.ENemesisState state)
    {
        switch (state)
        {
            case NemesisStateManager.ENemesisState.Chasing:
            case NemesisStateManager.ENemesisState.Catch:
                lostVoicePending = false;
                break;

            case NemesisStateManager.ENemesisState.Patrolling:
                if (!lostVoicePending) break;

                lostVoicePending = false;
                PlayVoice(lostVoices, 1f);
                break;
        }
    }

    /// <summary>
    /// The Nemesis enters the game (spec §7.1). Only when a puzzle woke it: woken by a script, the
    /// script owns the moment — the escape plays its own reveal, and hearing both would be the same
    /// cue twice. An id whose SO has no clip yet plays silence, with no warning (see
    /// Nemesis-System.md › Audio): wired now, heard the day somebody drags a clip in.
    /// </summary>
    private void HandleActivated()
    {
        if (silenced || string.IsNullOrWhiteSpace(activationSoundId) || !AudioManager.Exists) return;

        NemesisController controller = stateManager != null ? stateManager.NemesisController : null;
        if (controller != null && controller.WakeOnlyFromScript) return;

        AudioManager.Instance.PlayNemesis(activationSoundId, transform.position);
    }

    /// <summary>
    /// "It knows" (plan §16.2, D1, D16): the sting when a hiding spot becomes KNOWN, heard from
    /// inside it. Without it the player cannot tell a Nemesis that knows from one that guesses, and
    /// the margin D1 promises — see it coming and bail out before it opens — only exists on paper.
    ///
    /// Read off the facade's KnownHidingSpot each frame rather than from an event: "known" has four
    /// ways in (seen climbing in, made out through the slats, on top of it, opened it) and one
    /// place that owns them, and listening to that place is all this needs.
    ///
    /// Not at the door. Known at arm's length — "lo tiene encima", "lo abrió" — it opens the spot
    /// the same second, and the beat there is the music hitting as the door opens (D13: it arrives
    /// in silence). A sting a heartbeat before would spend the surprise. A suspected spot never
    /// stings: that is the guess the sting is there to tell apart.
    /// </summary>
    private void TickKnownSpotSting()
    {
        HidingSpot known = stateManager != null ? stateManager.KnownHidingSpot : null;
        if (ReferenceEquals(known, lastKnownSpot)) return;

        lastKnownSpot = known;
        if (known == null || known.ApproachPoint == null) return;

        Vector3 toDoor = known.ApproachPoint.position - transform.position;
        toDoor.y = 0f;
        if (toDoor.sqrMagnitude < knownSpotStingMinDistance * knownSpotStingMinDistance) return;

        PlayVoice(knownSpotStings, knownSpotStingPitch);
    }

    /// <summary>One line from a bank, in 3D on the Nemesis bus, unless it spoke too recently.
    /// </summary>
    private void PlayVoice(AudioClip[] bank, float pitch)
    {
        if (silenced || bank == null || bank.Length == 0 || !AudioManager.Exists) return;
        if (Time.time < nextVoiceAt) return;

        AudioClip clip = bank[UnityEngine.Random.Range(0, bank.Length)];
        if (clip == null) return;

        nextVoiceAt = Time.time + voiceCooldown;
        AudioManager.Instance.PlayNemesis(clip, transform.position, voiceVolume, pitch, minDistance, maxDistance);
    }

    /// <summary>The loop authored for a state, if any.</summary>
    private bool TryGetLoop(NemesisStateManager.ENemesisState state, out AudioClip clip, out float volume)
    {
        clip = null;
        volume = 0f;

        foreach (StateLoop loop in stateLoops)
        {
            if (loop == null || loop.state != state) continue;

            clip = loop.clip;
            volume = loop.volume;
            return clip != null;
        }

        return false;
    }

    private void StartCrossfade(AudioClip clip, float volume)
    {
        // Swap roles: whatever was audible is now the one on its way out.
        (activeSource, fadingSource) = (fadingSource, activeSource);

        fadingStartVolume = fadingSource.volume;

        activeSource.clip = clip;
        activeSource.volume = 0f;
        activeTargetVolume = clip != null ? volume : 0f;

        if (clip != null) activeSource.Play();

        crossfadeProgress = 0f;
    }

    private void Update()
    {
        if (silenced)
        {
            FadeOutForResult();
            return;
        }

        UpdateOcclusion();
        UpdateCrossfade();
        TickKnownSpotSting();
    }

    // Unscaled: this runs under the result screen's timeScale 0, where deltaTime is always zero.
    private void FadeOutForResult()
    {
        float step = Time.unscaledDeltaTime / Mathf.Max(crossfadeDuration, 0.01f);
        FadeOut(sourceA, step);
        FadeOut(sourceB, step);
    }

    private static void FadeOut(AudioSource source, float step)
    {
        if (source == null || !source.isPlaying) return;

        source.volume = Mathf.MoveTowards(source.volume, 0f, step);
        if (source.volume > 0f) return;

        source.Stop();
        source.clip = null;
    }

    private void UpdateOcclusion()
    {
        float target = 1f;

        if (occlusionEnabled && fieldOfListening != null && PlayerRegistry.HasPlayer)
        {
            Vector3 listener = PlayerRegistry.CurrentTransform.position;
            if (fieldOfListening.IsOccludedByWall(listener, transform.position))
                target = occludedVolumeMultiplier;
        }

        // Eased, never snapped: a hard jump on crossing a doorway clicks.
        occlusionMultiplier = Mathf.MoveTowards(occlusionMultiplier, target,
                                                occlusionEaseSpeed * Time.deltaTime);
    }

    private void UpdateCrossfade()
    {
        if (crossfadeProgress < 1f)
        {
            crossfadeProgress = crossfadeDuration > 0f
                ? Mathf.Clamp01(crossfadeProgress + Time.deltaTime / crossfadeDuration)
                : 1f;

            if (crossfadeProgress >= 1f && fadingSource.isPlaying)
            {
                fadingSource.Stop();
                fadingSource.clip = null;
            }
        }

        activeSource.volume = activeTargetVolume * crossfadeProgress * occlusionMultiplier;

        if (fadingSource.isPlaying)
            fadingSource.volume = fadingStartVolume * (1f - crossfadeProgress) * occlusionMultiplier;
    }
}
