using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisData", menuName = "Scriptable Objects/SO_NemesisData")]
public class SO_NemesisData : ScriptableObject
{
    [SerializeField] private float investigationTimeOut;
    [SerializeField] private float searchTimeOut;
    [SerializeField] private float visionLossGracePeriod;
    [SerializeField] private float patrolWaypointWaitTime;
    [SerializeField] private float noiseUpdateCooldown;

    // Tuneable detection parameters live here and not on the sensor components so that a
    // designer changes them in one asset, and so Tier 3.3 can scale difficulty by handing the
    // sensors a runtime copy of this SO. The LayerMasks stay on the components: those are scene
    // wiring, not design values.
    //
    // Defaults match what the Nemesis prefab had before the migration (viewRange 10,
    // viewAngle 90, listenRange 10) so moving them here did not retune the enemy.

    [Header("Vision")]
    [Tooltip("Radius of the vision OverlapSphere.")]
    [SerializeField] private float viewRange = 10f;

    [Tooltip("Full width of the vision cone in degrees. Halved when tested against the " +
             "forward vector.")]
    [Range(0, 360)]
    [SerializeField] private float viewAngle = 90f;

    [Tooltip("Hard detection radius. Inside it the Nemesis notices the player no matter what: " +
             "no cone, no occlusion raycast, and it is the only thing that defeats Hidden. " +
             "Keep it well under viewRange — this is 'it is literally on top of me', not a " +
             "second vision range. NOT the same as proximityRadius below, which only drives the " +
             "HUD vignette and detects nothing. 0 disables it.")]
    [SerializeField] private float proximityDetectionRange = 3f;

    [Tooltip("viewRange is multiplied by this while the player is crouching. " +
             "1 = crouching does not help at all, 0.5 = spotted at half the distance.")]
    [SerializeField, Range(0f, 1f)] private float crouchVisionMultiplier = 0.6f;

    [Header("Hearing")]
    [Tooltip("Hard ceiling on hearing, and the radius of the broadphase OverlapSphere. How loud " +
             "the player actually is decides the real range — see Noise Range Scale.")]
    [SerializeField] private float listenRange = 10f;

    [Tooltip("Metres of hearing per metre of the player's own noise emitter radius.\n\n" +
             "This is the knob that makes moving quietly WORTH something. The player's emitter " +
             "(crouch 1 / walk 2 / run 6) used to decide only whether the OverlapSphere caught " +
             "the collider at all: once a wall was in the way the test collapsed to " +
             "'listenRange * multiplier', identical for a sprint and a crouch. Sneaking behind " +
             "cover bought you literally nothing, which is where it mattered most.\n\n" +
             "At 2.5 the three gaits become 2.5 / 5 / 15 m, capped by Listen Range. Keep Listen " +
             "Range above loudness * this for the loudest gait, or the cap flattens the top of " +
             "the scale and running stops being louder than walking.")]
    [SerializeField, Min(0.1f)] private float noiseRangeScale = 2.5f;

    [Tooltip("Whether a wall between the Nemesis and a noise attenuates it.")]
    [SerializeField] private bool wallOcclusionEnabled = true;

    [Tooltip("Effective range through a wall = listenRange * this. Spec default: 0.6.")]
    [SerializeField, Range(0f, 1f)] private float wallOcclusionMultiplier = 0.6f;

    [Tooltip("Effective range through a FLOOR = listenRange * this.\n\n" +
             "Deliberately more generous than the wall multiplier: a floor slab is the one thing " +
             "the Nemesis can never see through, so hearing is its only channel to the storey " +
             "above. Set this too low and it can never work out that you are up there; set it to " +
             "1 and it tracks you between floors as if the slab were not there.\n\n" +
             "Combined with the player's own noise radii (crouch 1 / walk 2 / run 6) this is the " +
             "knob that decides how much running upstairs costs you.")]
    [SerializeField, Range(0f, 1f)] private float floorOcclusionMultiplier = 0.75f;

    [Tooltip("Measure how far a noise is along the NavMesh instead of in a straight line.\n\n" +
             "This is what makes a player directly overhead read as the 12 metres they are on " +
             "foot rather than the 5 they are as the crow flies — so hearing them does not make " +
             "the Nemesis behave as though they were within arm's reach. Costs one " +
             "NavMesh.CalculatePath every NoiseUpdateCooldown seconds.")]
    [SerializeField] private bool hearingUsesPathDistance = true;

    [Header("Navigation")]
    [Tooltip("Seconds between recalculations of the route verdict — reachable, how far, which " +
             "floor, whether the lift is on the way.\n\n" +
             "Each one is a NavMesh.CalculatePath, so this is a real cost knob. It is also a " +
             "STABILITY knob: the verdict flipping frame to frame is what made the Nemesis " +
             "oscillate between Chasing and Searching while standing under the player. Do not " +
             "drop it near zero to make it feel sharper.")]
    [SerializeField, Min(0.05f)] private float routeVerdictInterval = 0.4f;

    [Tooltip("Height difference, in metres, past which a target counts as being on another " +
             "floor. Roughly one storey; below a full storey it starts firing on ramps and " +
             "crates.")]
    [SerializeField, Min(0.5f)] private float floorHeightThreshold = 2.5f;

    [Tooltip("Seconds the Nemesis keeps walking to the freight elevator after it has stopped " +
             "seeing or hearing the player.\n\n" +
             "This is what makes the lift trip a decision instead of an accident. At 0 it turns " +
             "around the instant you break line of sight — which, since a floor slab breaks it " +
             "the moment it starts climbing, means it never gets there at all.")]
    [SerializeField, Min(0f)] private float elevatorCommitTime = 12f;

    [Tooltip("Seconds of movement the Nemesis extrapolates ahead of a remembered position when " +
             "deciding where to look.\n\n" +
             "Keep it small. The velocity it extrapolates was OBSERVED, not read off the player, " +
             "so a long lead turns a stale glimpse into a confident claim about somewhere nobody " +
             "was ever seen — and a monster that arrives where you were going reads as the game " +
             "cheating, not as the monster being sharp. 0 disables prediction entirely.")]
    [SerializeField, Range(0f, 1.5f)] private float searchLeadTime = 0.4f;

    [Tooltip("Radius, in metres, of the random scatter the Searching state falls back to when the " +
             "patrol graph cannot offer an unswept waypoint.\n\n" +
             "It used to be a hardcoded 5 inside the state, which made it invisible: nobody tuning " +
             "the search could see how tight a circle the Nemesis was actually walking. Small " +
             "values have it pacing the room it lost you in; large ones scatter it so wide the " +
             "sweep stops reading as a search at all.")]
    [SerializeField, Min(1f)] private float searchSweepRadius = 5f;

    [Header("Search — interception")]
    //
    // Searching used to walk to the nearest unswept waypoint FROM WHERE IT WAS STANDING: the last
    // known position never entered the maths, so it circled the spot it lost you at while you
    // walked away. These four turn that sweep into a cut-off — anchor on where it last sensed
    // you, project along the direction it saw you moving, and head for the waypoint it can reach
    // before you can.
    //
    // It still runs on BELIEF: the heading comes from FieldOfView.LastKnownVelocity, which is
    // measured from consecutive sightings. Change direction the moment you break line of sight
    // and the cut-off goes to the wrong place — that is the reward for juking, and it is meant
    // to be there.

    [Tooltip("How far from a detection a waypoint may sit and still be marked as 'this is where " +
             "I sensed them'. Roughly the spacing between neighbouring waypoints.")]
    [SerializeField, Min(0.5f)] private float beliefTraceRadius = 3f;

    [Tooltip("How strictly a waypoint has to be AHEAD of the player to count as a cut-off, as a " +
             "dot product against the observed heading.\n\n" +
             "1 = only dead ahead. 0 = anything not behind them. Negative values let it cut off " +
             "backwards, which is not cutting off — it is guessing. Around 0.25 gives a workable " +
             "forward arc without demanding the player run in a straight line.")]
    [SerializeField, Range(-1f, 1f)] private float interceptForwardDot = 0.25f;

    [Tooltip("How late the Nemesis is still allowed to arrive and count it as a cut-off.\n\n" +
             "1 = it must get there no later than the player would. 1.15 lets it commit to points " +
             "it reaches 15% late, which is usually still in front of a player who slows down at " +
             "a corner. Push it far past that and it starts committing to interceptions it " +
             "cannot make, which reads as following you badly rather than as cutting you off.")]
    [SerializeField, Min(1f)] private float interceptTimeMargin = 1.15f;

    [Tooltip("How fast the Nemesis ASSUMES the player is moving when working out whether it can " +
             "cut them off. Not read off the player — that would be omniscience.\n\n" +
             "Set it to the player's sprint speed: assuming the worst case makes it cut wide and " +
             "commit only to interceptions that hold up even if you run flat out. Assume too " +
             "little and it cuts behind you every time.")]
    [SerializeField, Min(0.5f)] private float assumedPlayerSpeed = 4.5f;

    [Tooltip("Seconds over which a sighting or a noise stops steering the patrol.\n\n" +
             "At 0 seconds old the player bias applies at full RoutePlayerBiasStrength; by this " +
             "many seconds it is gone and the roll falls back to the route weights you authored. " +
             "It is what stops the Nemesis orbiting the room it lost you in for the rest of the " +
             "run.\n\n" +
             "Only has any effect while BiasUsesLastKnownPosition is on — with it off the bias " +
             "reads the player's live position, which is never stale.")]
    [SerializeField, Min(1f)] private float beliefMemoryTime = 45f;

    [Tooltip("Maximum seconds the Nemesis waits at a landing for the freight elevator to free up " +
             "or finish a trip.\n\n" +
             "This is the safety net for 'the player is riding the lift right now': once it runs " +
             "out, the Nemesis abandons the link and paths whatever other way it can, instead of " +
             "standing at the doors forever. Keep it comfortably above one full ride, or it gives " +
             "up on trips that were about to work.")]
    [SerializeField, Min(1f)] private float elevatorWaitTimeout = 20f;

    [Tooltip("Seconds the Nemesis ignores a freight elevator after giving up on it.\n\n" +
             "Without it, abandoning the link and re-evaluating it are the same frame: the agent " +
             "is still standing on the link, so the next Update starts the whole wait over, times " +
             "out again, and the Nemesis spends the rest of the run cycling at the landing " +
             "without ever moving. The stuck watchdog cannot save it either — a traversal " +
             "suppresses the watchdog by design.\n\n" +
             "Keep it long enough for the agent to actually walk away and commit to another " +
             "route, or it steps off the link and immediately steps back on.")]
    [SerializeField, Min(0f)] private float elevatorAbandonCooldown = 10f;

    [Header("Stuck detection")]
    [Tooltip("How long the Nemesis has to make no progress before it counts as stuck and warps " +
             "itself out.")]
    [SerializeField, Min(0.5f)] private float stuckCheckInterval = 3f;

    [Tooltip("Distance it has to cover within stuckCheckInterval to count as making progress.")]
    [SerializeField, Min(0.05f)] private float stuckMinDistance = 0.5f;

    [Tooltip("Waypoints closer than this to the player are not eligible when repositioning after " +
             "a capture, so the Nemesis does not warp on top of the player it just respawned.")]
    [SerializeField, Min(0f)] private float repositionMinPlayerDistance = 15f;

    [Header("Player feedback")]
    [Tooltip("Distance at which the proximity vignette starts to show. Independent of the " +
             "vision range: tension has to rise even if the Nemesis has never seen you. " +
             "A bit larger than the FieldOfView's viewRange (10 in the prefab) works well.")]
    [SerializeField] private float proximityRadius = 12f;

    [Header("Patrol routes (Tier 3.1)")]
    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of walking the active " +
             "route in the opposite direction for that cycle.")]
    [SerializeField, Range(0f, 1f)] private float routeReverseChance = 0.15f;

    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of skipping the very " +
             "next waypoint on the first hop of that cycle (advances two waypoints instead of " +
             "one). Rolled independently from routeReverseChance, so both can land together.")]
    [SerializeField, Range(0f, 1f)] private float routeSkipWaypointChance = 0.15f;

    [Header("Patrol routes — player bias")]
    [Tooltip("How often, in seconds, the Nemesis re-picks its route without having left " +
             "Patrolling. Without this the roll only runs once, on entering the state, and a long " +
             "patrol feels dead: the bias was computed three minutes ago.\n\n" +
             "0 disables periodic replanning (the old behaviour).")]
    [SerializeField, Min(0f)] private float routeReplanInterval = 25f;

    [Tooltip("How many times more likely a zone becomes when the player is on top of it. " +
             "1 = no bias, the roll uses only the inspector weight.\n\n" +
             "It stays a roll: never 'always the nearest zone', just more tickets in the draw. " +
             "Distance is measured over the NavMesh and not in a straight line, so a zone one " +
             "floor up counts as far even when it is 4 metres away.")]
    [SerializeField, Min(1f)] private float routePlayerBiasStrength = 3f;

    [Tooltip("Metres of path beyond which the player bias no longer applies. Smaller = the " +
             "Nemesis only prioritises when it is genuinely close.")]
    [SerializeField, Min(1f)] private float routePlayerBiasFalloff = 40f;

    [Tooltip("Bias against the last known position (what the Nemesis saw or heard) instead of the " +
             "player's real position.\n\n" +
             "On is the fair option and what Mr. X does: it chases the memory, not the truth. Off " +
             "makes it omniscient and stops the patrol feeling like a patrol — it starts feeling " +
             "remote-controlled.")]
    [SerializeField] private bool biasUsesLastKnownPosition = true;

    [Header("Patrol routes — cúmulos (clusters)")]
    [Tooltip("Patrol by ZONE instead of by waypoint: the Nemesis picks a cúmulo of nearby " +
             "waypoints, sweeps it, and only then moves on to another one — preferring a cúmulo " +
             "next door, so it migrates through the level instead of jumping across it.\n\n" +
             "Off restores the old behaviour: one waypoint picked at a time out of the whole " +
             "merged set, with Cross Route Transfer Chance rolled on every arrival. That is what " +
             "made the patrol read as teleporting — two consecutive waypoints of a route in this " +
             "level can be thirty metres apart.\n\n" +
             "With this on, Cross Route Transfer Chance is ignored: a cúmulo is spatial, so it " +
             "already mixes whatever routes cover that corner of the level.")]
    [SerializeField] private bool clusterPatrolEnabled = true;

    [Tooltip("Metres. How far from a cúmulo's centre a waypoint may sit and still belong to it — " +
             "i.e. how big a 'zone' is.\n\n" +
             "Tune it against the level, not against a feeling: it should be about the size of a " +
             "room or a stretch of corridor. Too small and every waypoint becomes its own cúmulo, " +
             "which is the old behaviour with extra steps (the graph warns when that happens). " +
             "Too large and one cúmulo swallows the floor, and the Nemesis never appears to leave.")]
    [SerializeField, Min(1f)] private float clusterRadius = 12f;

    [Tooltip("Ceiling on how many waypoints one cúmulo may hold, so a densely marked room does " +
             "not absorb everything within the radius.")]
    [SerializeField, Range(2, 12)] private int maxClusterSize = 5;

    [Tooltip("Fewest waypoints of a cúmulo the Nemesis visits before moving on. A cúmulo with " +
             "fewer members than this is simply swept whole.")]
    [SerializeField, Min(1)] private int clusterMinWaypoints = 3;

    [Tooltip("Most waypoints of a cúmulo the Nemesis visits before moving on. Rolled between the " +
             "minimum and this on each cúmulo, so it does not spend the same amount of time in " +
             "every zone.")]
    [SerializeField, Min(1)] private int clusterMaxWaypoints = 6;

    [Tooltip("How many times more likely the NEXT cúmulo is when it is right next door.\n\n" +
             "This is the knob that turns the patrol into a walk through the level instead of a " +
             "series of jumps: at 1 the next zone is drawn from anywhere on the island with no " +
             "preference at all. It only applies when finishing a cúmulo — entering Patrolling " +
             "fresh (after a chase, say) is deliberately free to relocate anywhere.")]
    [SerializeField, Min(1f)] private float clusterNeighbourBias = 4f;

    [Tooltip("Metres of path beyond which a cúmulo stops counting as 'next door'. Measured over " +
             "the NavMesh, so a zone one floor up is as far as the walk to the lift makes it.")]
    [SerializeField, Min(1f)] private float clusterNeighbourFalloff = 25f;

    [Header("Patrol routes — cross-route transfer")]
    [Tooltip("Chance, on each waypoint arrival, of jumping to a waypoint on ANOTHER unlocked " +
             "route instead of following the current route in order.\n\n" +
             "This is what lets it change floor without waiting for the route roll to hand it the " +
             "upper one: if another route has a reachable waypoint on level 1, it can borrow it " +
             "and adopt that route from there. 0 locks it inside its own route, as before.\n\n" +
             "IGNORED while Cluster Patrol Enabled is on — see that field.")]
    [SerializeField, Range(0f, 1f)] private float crossRouteTransferChance = 0.3f;

    [Tooltip("How many candidate waypoints are evaluated with real path distance on each pick. " +
             "The rest are discarded by a straight-line prefilter, which is free.\n\n" +
             "Each candidate costs two path queries. 8 is generous for a level this size; raising " +
             "it is only needed with a great many waypoints packed close together.")]
    [SerializeField, Range(2, 24)] private int waypointBiasSampleCount = 8;

    [Header("Capture")]
    [Tooltip("Real horizontal distance at which the Nemesis can grab the player.\n\n" +
             "Checked in addition to the agent's path because remainingDistance lies when the " +
             "path is partial: with the player sealed off behind a wall, the agent reaches the " +
             "closest point it can and remainingDistance drops to zero. Without this check that " +
             "fires the capture through the wall.")]
    [SerializeField, Min(0.5f)] private float catchMaxReach = 2f;

    [Tooltip("Maximum height difference allowed for a grab. Prevents capture between floors when " +
             "the player is directly above or below the Nemesis.")]
    [SerializeField, Min(0.5f)] private float catchMaxVerticalOffset = 1.5f;

    [Tooltip("Require a clear line of sight (no wall in between) to grab. Uses the " +
             "FieldOfListening's obstacleMask, the same one that already filters sound.")]
    [SerializeField] private bool catchRequiresLineOfSight = true;

    [Header("Extreme proximity detection")]
    [Tooltip("Whether hard proximity detection (proximityDetectionRange) also respects walls.\n\n" +
             "Off, the Nemesis detects you through a thin wall just by standing on the other side, " +
             "which is how it ends up grabbing you without ever having seen you. On, it still " +
             "ignores the vision cone and still defeats Hidden — it only asks that there be no " +
             "geometry in between.")]
    [SerializeField] private bool proximityDetectionRespectsWalls = true;

    [Header("Proximity vignette (HUD)")]
    [Tooltip("Measure HUD proximity over the NavMesh instead of in a straight line.\n\n" +
             "This is the fix for 'the threat UI shows up when it is on another floor': in a " +
             "straight line the Nemesis one floor below is 4 metres away and lights the vignette " +
             "up to maximum, when it is really half a storey of walking away.")]
    [SerializeField] private bool proximityUsesPathDistance = true;

    [Tooltip("How often that path distance is recomputed. It does not need to be per frame: the " +
             "vignette interpolates between measurements.")]
    [SerializeField, Min(0.05f)] private float proximityRecalcInterval = 0.2f;

    public float InvestigationTimeOut { get => investigationTimeOut; set => investigationTimeOut = value; }
    public float SearchTimeOut { get => searchTimeOut; set => searchTimeOut = value; }
    public float VisionLossGracePeriod { get => visionLossGracePeriod; set => visionLossGracePeriod = value; }
    public float PatrolWaypointWaitTime { get => patrolWaypointWaitTime; set => patrolWaypointWaitTime = value; }
    public float NoiseUpdateCooldown { get => noiseUpdateCooldown; set => noiseUpdateCooldown = value; }
    public float ViewRange { get => viewRange; set => viewRange = value; }
    public float ViewAngle { get => viewAngle; set => viewAngle = value; }
    public float ProximityDetectionRange { get => proximityDetectionRange; set => proximityDetectionRange = value; }
    public float CrouchVisionMultiplier { get => crouchVisionMultiplier; set => crouchVisionMultiplier = value; }
    public float ListenRange { get => listenRange; set => listenRange = value; }
    public float NoiseRangeScale { get => noiseRangeScale; set => noiseRangeScale = value; }
    public bool WallOcclusionEnabled { get => wallOcclusionEnabled; set => wallOcclusionEnabled = value; }
    public float WallOcclusionMultiplier { get => wallOcclusionMultiplier; set => wallOcclusionMultiplier = value; }
    public float FloorOcclusionMultiplier { get => floorOcclusionMultiplier; set => floorOcclusionMultiplier = value; }
    public float RouteVerdictInterval { get => routeVerdictInterval; set => routeVerdictInterval = value; }
    public float FloorHeightThreshold { get => floorHeightThreshold; set => floorHeightThreshold = value; }
    public float ElevatorCommitTime { get => elevatorCommitTime; set => elevatorCommitTime = value; }
    public float SearchLeadTime { get => searchLeadTime; set => searchLeadTime = value; }
    public float SearchSweepRadius { get => searchSweepRadius; set => searchSweepRadius = value; }
    public float BeliefTraceRadius { get => beliefTraceRadius; set => beliefTraceRadius = value; }
    public float InterceptForwardDot { get => interceptForwardDot; set => interceptForwardDot = value; }
    public float InterceptTimeMargin { get => interceptTimeMargin; set => interceptTimeMargin = value; }
    public float AssumedPlayerSpeed { get => assumedPlayerSpeed; set => assumedPlayerSpeed = value; }
    public float BeliefMemoryTime { get => beliefMemoryTime; set => beliefMemoryTime = value; }
    public float ElevatorWaitTimeout { get => elevatorWaitTimeout; set => elevatorWaitTimeout = value; }
    public float ElevatorAbandonCooldown { get => elevatorAbandonCooldown; set => elevatorAbandonCooldown = value; }
    public bool HearingUsesPathDistance { get => hearingUsesPathDistance; set => hearingUsesPathDistance = value; }
    public float StuckCheckInterval { get => stuckCheckInterval; set => stuckCheckInterval = value; }
    public float StuckMinDistance { get => stuckMinDistance; set => stuckMinDistance = value; }
    public float RepositionMinPlayerDistance { get => repositionMinPlayerDistance; set => repositionMinPlayerDistance = value; }
    public float ProximityRadius { get => proximityRadius; set => proximityRadius = value; }
    public float RouteReverseChance { get => routeReverseChance; set => routeReverseChance = value; }
    public float RouteSkipWaypointChance { get => routeSkipWaypointChance; set => routeSkipWaypointChance = value; }
    public float RouteReplanInterval { get => routeReplanInterval; set => routeReplanInterval = value; }
    public float RoutePlayerBiasStrength { get => routePlayerBiasStrength; set => routePlayerBiasStrength = value; }
    public float RoutePlayerBiasFalloff { get => routePlayerBiasFalloff; set => routePlayerBiasFalloff = value; }
    public bool BiasUsesLastKnownPosition { get => biasUsesLastKnownPosition; set => biasUsesLastKnownPosition = value; }
    public bool ClusterPatrolEnabled { get => clusterPatrolEnabled; set => clusterPatrolEnabled = value; }
    public float ClusterRadius { get => clusterRadius; set => clusterRadius = value; }
    public int MaxClusterSize { get => maxClusterSize; set => maxClusterSize = value; }
    public int ClusterMinWaypoints { get => clusterMinWaypoints; set => clusterMinWaypoints = value; }

    /// <summary>Clamped against the minimum rather than trusted: the two are independent fields
    /// and a max typed below the min would make the roll's range empty.</summary>
    public int ClusterMaxWaypoints
    {
        get => Mathf.Max(clusterMinWaypoints, clusterMaxWaypoints);
        set => clusterMaxWaypoints = value;
    }

    public float ClusterNeighbourBias { get => clusterNeighbourBias; set => clusterNeighbourBias = value; }
    public float ClusterNeighbourFalloff { get => clusterNeighbourFalloff; set => clusterNeighbourFalloff = value; }
    public float CrossRouteTransferChance { get => crossRouteTransferChance; set => crossRouteTransferChance = value; }
    public int WaypointBiasSampleCount { get => waypointBiasSampleCount; set => waypointBiasSampleCount = value; }
    public float CatchMaxReach { get => catchMaxReach; set => catchMaxReach = value; }
    public float CatchMaxVerticalOffset { get => catchMaxVerticalOffset; set => catchMaxVerticalOffset = value; }
    public bool CatchRequiresLineOfSight { get => catchRequiresLineOfSight; set => catchRequiresLineOfSight = value; }
    public bool ProximityDetectionRespectsWalls { get => proximityDetectionRespectsWalls; set => proximityDetectionRespectsWalls = value; }
    public bool ProximityUsesPathDistance { get => proximityUsesPathDistance; set => proximityUsesPathDistance = value; }
    public float ProximityRecalcInterval { get => proximityRecalcInterval; set => proximityRecalcInterval = value; }
}
