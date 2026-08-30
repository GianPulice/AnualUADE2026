using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hearing sensor of the Nemesis.
///
/// The tuneable values (range, wall occlusion) live in <see cref="SO_NemesisData"/> so a
/// designer edits them in one asset and Tier 3.3 can scale them by handing this component a
/// runtime copy of the SO through <see cref="SetData"/>. The LayerMasks stay here: those are
/// scene wiring, not design values.
/// </summary>
public class FieldOfListening : MonoBehaviour
{
    [Tooltip("Seconds between hearing sweeps. Kept on the component: it is a performance knob, " +
             "not a design value.")]
    [SerializeField] private float listenDelay = 0.1f;

    [SerializeField] private LayerMask listenMask;

    [Tooltip("Geometric line of sight. Used by IsOccludedByWall, which despite living on the " +
             "hearing sensor answers a VISION question for four other systems: whether the " +
             "capture has a wall in the way, whether a spawn point is in view of the player, " +
             "whether a stuck-escape warp would be seen, and whether the Nemesis's own audio is " +
             "muffled. Floors belong in here. Changing it changes all four.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("What actually stops SOUND. Walls, not floors.\n\n" +
             "Separate from obstacleMask because the two questions have different answers: the " +
             "Nemesis must never SEE through a slab, but it does HEAR through one — that is the " +
             "only channel it has to the storey above, and without it a player upstairs simply " +
             "does not exist to it.\n\n" +
             "Left empty it is derived from obstacleMask minus floorMask, which reproduces the " +
             "intended behaviour without anyone having to fill it in.")]
    [SerializeField] private LayerMask soundBlockerMask;

    [Tooltip("Floor and ceiling slabs. Attenuate sound by SO_NemesisData.FloorOcclusionMultiplier " +
             "instead of blocking it.\n\n" +
             "Left empty it defaults to the Ground layer.")]
    [SerializeField] private LayerMask floorMask;

    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents.")]
    [SerializeField] private SO_NemesisData nemesisData;

    private List<GameObject> listenedTargets;
    private float currentTimer = 0;
    private bool hasAudioTarget = false;
    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition;

    private float lastNoiseTime;

    // Path-distance cache. A deadline on the Time.time clock rather than a countdown, so a paused
    // game freezes it — nothing it measures can move while paused.
    private float nextPathQueryTime;
    private float cachedPathDistance;
    private bool cachedPathValid;

    public bool HasAudioTarget { get => hasAudioTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    /// <summary>Whether <see cref="LastKnownPosition"/> means anything yet. Same reason as
    /// <see cref="FieldOfView.HasLastKnownPosition"/>: Vector3.zero is a valid level coordinate
    /// and cannot stand in for "I have not heard anything yet".</summary>
    public bool HasLastKnownPosition { get => hasLastKnownPosition; }

    /// <summary>Seconds since the last noise was heard, or infinity if none ever was. Same
    /// purpose as <see cref="FieldOfView.TimeSinceLastSighting"/>: how much the memory is still
    /// worth, as opposed to whether it exists.</summary>
    public float TimeSinceLastNoise =>
        hasLastKnownPosition ? Time.time - lastNoiseTime : float.PositiveInfinity;

    private void Awake()
    {
        listenedTargets = new List<GameObject>();

        ResolveAcousticMasks();

        if (nemesisData != null) return;

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
            Debug.LogError($"[{nameof(FieldOfListening)}] No SO_NemesisData assigned and none " +
                           $"found in the parents — the Nemesis will not hear anything.", this);
    }

    /// <summary>
    /// Fills in the two acoustic masks when the scene left them empty.
    ///
    /// Derived rather than reported missing, because these fields were added to a component that
    /// already ships on prefabs, and an empty LayerMask is not a neutral default here — it is the
    /// most dangerous possible value. A soundBlockerMask of Nothing means no geometry ever
    /// attenuates anything, so the Nemesis would hear the player through the entire level. That
    /// regression would land on every existing Nemesis the moment this file compiled, and it
    /// fails silently: nothing errors, the monster simply becomes omniscient.
    ///
    /// The derivation reproduces exactly the behaviour that was there before — obstacleMask
    /// blocked sound, floors included — minus the floors, which is the whole point of the change.
    /// Anything set explicitly in the inspector wins.
    /// </summary>
    private void ResolveAcousticMasks()
    {
        if (floorMask == 0)
        {
            int ground = LayerMask.GetMask("Ground");

            // A project with no Ground layer is not an error here: it means floors are on Default
            // along with everything else, and no mask can tell them apart. Sound then attenuates
            // through them like a wall, which is where this started.
            if (ground != 0) floorMask = ground;
        }

        if (soundBlockerMask == 0) soundBlockerMask = obstacleMask & ~floorMask;
    }

    /// <summary>
    /// Drops the memory of the last noise. See <see cref="FieldOfView.ForgetLastKnownPosition"/>
    /// for why a capture is the only thing entitled to call this.
    /// </summary>
    public void ForgetLastKnownPosition()
    {
        hasAudioTarget = false;
        hasLastKnownPosition = false;
    }

    /// <summary>
    /// Swaps the data asset at runtime. Tier 3.3 uses it to push a scaled copy of the SO without
    /// touching the original asset.
    /// </summary>
    public void SetData(SO_NemesisData data)
    {
        if (data == null) return;
        nemesisData = data;
    }

    // Update is called once per frame
    void Update()
    {
        // Same guard as NemesisStateManager: this Update is its own, so without it the
        // Nemesis kept hearing (and reacting) with the game paused.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (currentTimer < listenDelay) currentTimer += Time.deltaTime;
        else
        {
            currentTimer = 0;
            ListenTargets();
        }
    }
    private void ListenTargets()
    {
        if (nemesisData == null) return;

        float listenRange = nemesisData.ListenRange;

        listenedTargets.Clear();
        Collider[] targetsInListenRadius = Physics.OverlapSphere(transform.position, listenRange, listenMask);
        for (int i = 0; i < targetsInListenRadius.Length; i++)
        {
            GameObject target = targetsInListenRadius[i].gameObject;
            if (listenedTargets.Contains(target)) continue;

            // The OverlapSphere is only a broadphase now — how loud the emitter actually is
            // decides the real range, and that is read off the collider it just returned.
            float loudness = GetEmitterRadius(targetsInListenRadius[i]);
            if (!CanHear(target.transform.position, listenRange, loudness)) continue;

            listenedTargets.Add(target);
        }
        if(listenedTargets.Count > 0)
        {
            hasAudioTarget = true;
            lastKnownPosition = listenedTargets[0].transform.position;
            hasLastKnownPosition = true;
            lastNoiseTime = Time.time;
        }
        else hasAudioTarget = false;
    }

    /// <summary>
    /// Whether a noise at this point is audible from here.
    ///
    /// Neither a wall nor a floor blocks sound outright — both attenuate it, and the model for
    /// that is a shrunken effective range. What differs is by how much, and that difference is
    /// the point of the whole change: a wall is a detour the Nemesis could walk around, so it
    /// muffles hard; a slab is the one surface vision can never cross, so hearing has to stay
    /// usable through it or the storey above may as well not exist.
    ///
    /// They multiply when both apply. A player crouching upstairs and behind a wall should be
    /// close to inaudible, not merely as muffled as either one alone.
    ///
    /// HOW LOUD THE PLAYER IS DECIDES THE RANGE. This is the part that used to be missing, and
    /// its absence is why moving quietly bought nothing where it mattered. The emitter radius
    /// (crouch 1 / walk 2 / run 6) only ever decided whether the broadphase OverlapSphere caught
    /// the collider; the moment any geometry was in the way the test collapsed to
    /// "listenRange * multiplier", which has no loudness term in it at all. Behind a wall a
    /// sprint and a crouch were audible at exactly the same distance.
    ///
    /// listenRange survives as a CEILING rather than as the range itself, so a designer can cap
    /// how far the Nemesis can ever hear without having to reason about the emitter.
    /// </summary>
    /// <param name="loudness">The noise emitter's own radius, in metres.</param>
    private bool CanHear(Vector3 source, float listenRange, float loudness)
    {
        float effectiveRange = Mathf.Min(listenRange, loudness * nemesisData.NoiseRangeScale);

        if (nemesisData.WallOcclusionEnabled)
        {
            if (IsBlockedBy(source, soundBlockerMask)) effectiveRange *= nemesisData.WallOcclusionMultiplier;
            if (IsBlockedBy(source, floorMask))        effectiveRange *= nemesisData.FloorOcclusionMultiplier;
        }

        return MeasuredDistanceTo(source) <= effectiveRange;
    }

    /// <summary>
    /// How loud a noise source is, as the radius of its emitter in world units.
    ///
    /// Read off the collider the OverlapSphere already returned rather than by reaching into the
    /// player: the sensor stays a sensor, and anything that wants to be heard only has to carry a
    /// trigger sized to how loud it is.
    ///
    /// lossyScale matters because the emitter rides a scaled hierarchy — a SphereCollider of
    /// radius 6 under a parent scaled 0.5 is a 3-metre noise, and reading the raw radius would
    /// make every gait louder than the design says.
    ///
    /// The bounds fallback keeps a non-sphere emitter audible instead of silently inaudible,
    /// which is the failure mode worth avoiding: a designer who swaps the collider shape should
    /// get roughly the right behaviour, not a Nemesis that has gone deaf for no visible reason.
    /// </summary>
    private static float GetEmitterRadius(Collider col)
    {
        if (col is SphereCollider sphere)
        {
            Vector3 scale = sphere.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return sphere.radius * maxScale;
        }

        return col.bounds.extents.magnitude;
    }

    /// <summary>
    /// How far the noise really is: along the NavMesh when it can be measured, in a straight line
    /// otherwise.
    ///
    /// The distinction is the entire reason a player one floor up used to be treated as though
    /// they were beside the Nemesis — five metres of slab is five metres in a straight line and
    /// twelve on foot, and every decision downstream of hearing was reading the first number.
    ///
    /// Throttled on NoiseUpdateCooldown, a field that has existed on SO_NemesisData since it was
    /// written with no reader anywhere in the project. This is what it was for. One cached figure
    /// rather than one per target is enough: the only thing that emits noise is the player.
    ///
    /// Falls back to the straight line when no path exists. Sound is not navigation — an
    /// unreachable player is still audible — and the states that act on a noise already have
    /// their own timeouts for a destination they cannot get to.
    /// </summary>
    private float MeasuredDistanceTo(Vector3 source)
    {
        float straightLine = Vector3.Distance(transform.position, source);
        if (!nemesisData.HearingUsesPathDistance) return straightLine;

        if (Time.time >= nextPathQueryTime)
        {
            nextPathQueryTime = Time.time + Mathf.Max(0.05f, nemesisData.NoiseUpdateCooldown);
            cachedPathValid = NemesisNav.TryGetPathDistance(transform.position, source,
                                                            out cachedPathDistance);
        }

        return cachedPathValid ? cachedPathDistance : straightLine;
    }

    /// <summary>Whether geometry on the given mask stands between this sensor and the point. An
    /// empty mask matches nothing, which is how a project with no Ground layer ends up with no
    /// floor attenuation rather than with an exception.</summary>
    private bool IsBlockedBy(Vector3 targetPosition, LayerMask mask)
    {
        if (mask == 0) return false;

        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        return Physics.Raycast(transform.position, toTarget / distance, distance, mask);
    }

    /// <summary>
    /// True if a wall stands between this sensor and the given point.
    /// Public so the Nemesis audio (Tier 2.6) can attenuate its loops through the same raycast
    /// instead of reimplementing it.
    /// </summary>
    public bool IsOccludedByWall(Vector3 targetPosition) =>
        IsOccludedByWall(transform.position, targetPosition);

    /// <summary>
    /// Same test from an arbitrary origin. Used by the stuck detection to ask "can the player
    /// see this waypoint?", which reuses the obstacleMask already wired on this component
    /// instead of adding a second one to the state manager.
    /// </summary>
    public bool IsOccludedByWall(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        return Physics.Raycast(origin, toTarget / distance, distance, obstacleMask);
    }
}
