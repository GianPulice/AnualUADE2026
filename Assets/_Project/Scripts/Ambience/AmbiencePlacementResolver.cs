using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Decides WHERE an ambient one-shot plays.
///
/// The audio spec asks for a valid position around the player, preferably outside their field of
/// view, at a random distance, sometimes behind a wall or on another floor. The hard part is the word
/// "valid": a naive random point 8-30 m away in a blockout frequently lands outside the building,
/// inside solid geometry, or in dead space — and a chain rattling in the void reads as a bug, not as
/// atmosphere.
///
/// Two sources of positions, tried in that order:
///
///   1. ANCHORS. AmbienceEmitter markers an LD placed on real props. Preferred, because the sound
///      then comes from the thing that would actually make it. An anchor position is authoritative
///      and is never moved.
///
///   2. VALIDATED RANDOM. A sampled direction and distance, put through three rejection tests:
///        - CheckSphere against solidMask   — is the point inside geometry?
///        - NavMesh.SamplePosition          — is the point near walkable space, i.e. inside the
///                                            building? This is by far the most effective guard
///                                            against "outside the building" in a blockout.
///        - Linecast against occluderMask   — is the straight line to it blocked?
///
/// OCCLUSION IS A FEATURE, WITH A CAP. The spec wants some sounds to come from behind a wall, and in
/// a factory most candidate points are occluded anyway. But occluded (attenuated and muffled) plus
/// distant (linear rolloff) can add up to inaudible, which would silently delete whole classes of
/// event. Hence maxOccludedFraction: clear points are preferred for the first several attempts.
///
/// WHY NOT REUSE FieldOfListening.IsOccludedByWall
/// Three reasons. It lives on the Nemesis prefab, so the ambience would lose occlusion in any scene
/// without a Nemesis — which is the blockout today and every test scene. Its obstacleMask answers a
/// different question: a grate should block the Nemesis's hearing but should NOT block a sound that
/// is meant to be coming through it. And ambience needs two masks where that component has one.
///
/// This runs roughly twice a minute and allocates nothing: CheckSphere, Linecast(out hit) and
/// NavMesh.SamplePosition(out hit) are all allocation-free, and the anchor candidate lists are
/// reused.
/// </summary>
public class AmbiencePlacementResolver : MonoBehaviour
{
    [Header("Layer masks")]
    [Tooltip("Surfaces that block the line from the listener to a candidate point.\n\n" +
             "Include walls, floors and closed doors. Do NOT include thin props a sound should be " +
             "able to come through, like grates or railings.")]
    [SerializeField] private LayerMask occluderMask;

    [Tooltip("Geometry a candidate point must not be INSIDE. The occluders plus the ground.\n\n" +
             "Do NOT include Default. In this project Default is where VISUAL_MASS and the " +
             "PATIO_CARGA_Mass_* blocks live — solid decorative volumes that surround the playable " +
             "area — so including it rejects every candidate that leaves the current room. Deciding " +
             "whether a point is outside the building is the NavMesh test's job, not this one's.\n\n" +
             "If you give AmbienceEmitter anchors colliders, put them on Ignore Raycast or make them " +
             "triggers, or the resolver will reject its own anchors.")]
    [SerializeField] private LayerMask solidMask;

    [Header("Anchors")]
    [Tooltip("Chance of trying an LD-placed anchor before falling back to validated random. " +
             "Anchors read as diegetic and always land in valid space, so they should dominate; the " +
             "random half supplies the \"it came from nowhere\" unease.\n\n" +
             "With no anchors placed, random handles everything and nothing breaks.")]
    [SerializeField, Range(0f, 1f)] private float anchorChance = 0.6f;

    [Header("Validated random")]
    [Tooltip("Attempts before giving up on a position. The last few attempts progressively relax " +
             "the constraints — see the class summary.")]
    [SerializeField, Range(1, 32)] private int maxAttempts = 12;

    [Tooltip("Chance of sampling from the rear hemisphere rather than the full circle. This is what " +
             "produces the \"was that behind me?\" reaction the spec is after.")]
    [SerializeField, Range(0f, 1f)] private float behindBias = 0.75f;

    [Tooltip("Half-angle of the cone directly ahead that is excluded even when not using the rear " +
             "hemisphere. Keeps sounds from appearing in plain sight.")]
    [SerializeField, Range(0f, 90f)] private float frontExclusionHalfAngle = 35f;

    [Tooltip("Upper bound on how often an occluded point is accepted while clear ones are still " +
             "being preferred. Raise for a more muffled, walled-in feel; lower if events are " +
             "getting lost.")]
    [SerializeField, Range(0f, 1f)] private float maxOccludedFraction = 0.4f;

    [Tooltip("How many of the early attempts prefer a clear line to the point.")]
    [SerializeField, Range(0, 32)] private int preferClearAttempts = 6;

    [Tooltip("How far PAST the blocking surface the emitter is placed when snapping to an occluder. " +
             "Past, not in front: the distance from the listener then equals the real distance to " +
             "the radiating surface, so Unity's rolloff computes the right loudness and the wall " +
             "itself becomes the source.")]
    [SerializeField, Min(0f)] private float wallPenetration = 0.3f;

    [Tooltip("Radius of the inside-geometry test. Roughly the size of the empty pocket a sound needs.")]
    [SerializeField, Min(0.05f)] private float solidCheckRadius = 0.3f;

    [Tooltip("How far from a candidate point the resolver looks for walkable NavMesh before " +
             "deciding the point is outside the building.")]
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;

    [Header("Debug")]
    [Tooltip("Draws the recently resolved positions in the Scene view. With no automated tests in " +
             "this project, this and the scheduler's log ARE the test suite.")]
    [SerializeField] private bool drawGizmos = true;

    [SerializeField, Range(1, 32)] private int gizmoHistory = 8;

    private readonly List<AmbienceEmitter> anchorCandidates = new List<AmbienceEmitter>();
    private readonly List<float> anchorWeights = new List<float>();

    private readonly List<DebugRecord> debugRecords = new List<DebugRecord>();

    // Rejection tallies, accumulated for the whole session. The scheduler appends them to its
    // placement-failure warning so the message names the filter that is actually rejecting instead
    // of listing every suspect.
    private int rejectedOnScreen;
    private int rejectedInsideGeometry;
    private int rejectedOffNavMesh;
    private int rejectedOccludedCap;

    // Identity of the last collider that failed a candidate, so the diagnostic can name the actual
    // object instead of describing a category. Four is plenty — only the first entry is reported.
    private readonly Collider[] overlapBuffer = new Collider[4];
    private string lastBlockerName = "";
    private int lastBlockerLayer = -1;

    // Parameters of the last entry attempted. Reported alongside the tallies because a zero-filled
    // entry — the Unity inspector zero-fills new array elements and skips the C# defaults — spawns
    // every candidate at the player's feet and rejects 100%, with no other visible symptom.
    private Vector2 lastEntryDistanceRange;
    private Vector2 lastEntryVerticalRange;

    private struct DebugRecord
    {
        public Vector3 Position;
        public Vector3 ListenerPosition;
        public bool Occluded;
        public bool FromAnchor;
        public SO_AmbienceEventBank.ETier Tier;
    }

    private void Reset()
    {
        // Matches how WIRED_Zona1_Blockout is actually laid out: real geometry on Wall (11) and
        // Ground (3), with Default (0) reserved for props, grouping objects and the VISUAL_MASS
        // decorative blocks. Default is deliberately absent from both masks — see solidMask's
        // tooltip for why including it rejects everything.
        occluderMask = LayerMask.GetMask("Wall");
        solidMask = LayerMask.GetMask("Wall", "Ground");
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a position for <paramref name="entry"/>.
    ///
    /// Returns false when no valid position could be found. The caller must then SKIP the event —
    /// never fall back to playing at the listener's position, which is exactly the 2D-in-your-ears
    /// artefact the spec rules out.
    /// </summary>
    /// <param name="listenerCamera">
    /// The camera, not the player. The AudioListener lives on the camera, and the player can look
    /// around without turning their body, so "outside the field of view" has to be measured against
    /// where they are actually looking.
    /// </param>
    /// <param name="playerFeetPosition">
    /// Ground reference for the height offset. An "on the floor below" sound is relative to the
    /// player's feet, not to their eyeline.
    /// </param>
    /// <param name="tier">Only used to colour the debug gizmo.</param>
    public bool TryResolvePosition(SO_AmbienceEventBank.Entry entry, Camera listenerCamera,
                                   Vector3 playerFeetPosition, SO_AmbienceEventBank.ETier tier,
                                   out AmbiencePlacement placement)
    {
        placement = default;

        if (entry == null || listenerCamera == null) return false;

        lastEntryDistanceRange = entry.distanceRange;
        lastEntryVerticalRange = entry.verticalRange;

        Vector3 listenerPosition = listenerCamera.transform.position;
        Vector3 listenerForward = listenerCamera.transform.forward;

        if (Random.value < anchorChance &&
            TryPickAnchor(entry, listenerPosition, listenerForward, out placement))
        {
            RecordDebug(placement, listenerPosition, tier);
            return true;
        }

        if (TryPickRandom(entry, listenerPosition, listenerForward, playerFeetPosition,
                          listenerCamera, out placement))
        {
            RecordDebug(placement, listenerPosition, tier);
            return true;
        }

        return false;
    }

    /// <summary>
    /// How many candidate points each filter has rejected this session, and what to do about the
    /// dominant one. Consumed by AmbienceEventScheduler's placement-failure warning.
    /// </summary>
    public string GetRejectionSummary()
    {
        int total = rejectedOnScreen + rejectedInsideGeometry + rejectedOffNavMesh + rejectedOccludedCap;
        if (total == 0) return "no rejections recorded — the resolver is not the problem.";

        string dominant;
        if (rejectedInsideGeometry >= rejectedOffNavMesh &&
            rejectedInsideGeometry >= rejectedOccludedCap &&
            rejectedInsideGeometry >= rejectedOnScreen)
        {
            dominant = "solidMask is catching most candidates — either it includes a layer it " +
                       "should not, or the area is too tight for the event's distanceRange. Try " +
                       "lowering distanceRange, or solidCheckRadius.";
        }
        else if (rejectedOffNavMesh >= rejectedOccludedCap && rejectedOffNavMesh >= rejectedOnScreen)
        {
            dominant = "the NavMesh test is rejecting most candidates — the area around the player " +
                       "is not covered by baked NavMesh, or navMeshSampleRadius is too small for " +
                       "how far the walkable floor sits from these positions. Rebake the NavMesh, " +
                       "raise navMeshSampleRadius, or turn off requireNavMeshNearby on these events.";
        }
        else if (rejectedOccludedCap >= rejectedOnScreen)
        {
            dominant = "the occlusion cap is rejecting most candidates — nearly everything around " +
                       "the player is behind a wall. Raise maxOccludedFraction or lower " +
                       "preferClearAttempts.";
        }
        else
        {
            dominant = "most candidates are landing in the player's view. Raise " +
                       "frontExclusionHalfAngle or behindBias.";
        }

        string blocker = lastBlockerLayer >= 0
            ? $"\n  last blocker: '{lastBlockerName}' on layer " +
              $"{lastBlockerLayer} ({LayerMask.LayerToName(lastBlockerLayer)})"
            : "";

        return $"rejections — on-screen {rejectedOnScreen}, inside-geometry {rejectedInsideGeometry}, " +
               $"off-navmesh {rejectedOffNavMesh}, occlusion-cap {rejectedOccludedCap}." +
               $"\n  solidMask = {DescribeMask(solidMask)}" +
               $"\n  occluderMask = {DescribeMask(occluderMask)}" +
               $"\n  solidCheckRadius = {solidCheckRadius}" +
               $"\n  last entry: distanceRange = {lastEntryDistanceRange}, " +
               $"verticalRange = {lastEntryVerticalRange}{DescribeDegenerateEntry()}" +
               blocker +
               $"\n  {dominant}";
    }

    /// <summary>
    /// Names the zero-filled-entry failure explicitly when the numbers show it, because the symptom
    /// looks identical to hostile geometry and the cause is nowhere near the resolver.
    /// </summary>
    private string DescribeDegenerateEntry()
    {
        bool noDistance = lastEntryDistanceRange.y <= 0f;
        bool noVertical = Mathf.Approximately(lastEntryVerticalRange.x, 0f) &&
                          Mathf.Approximately(lastEntryVerticalRange.y, 0f);

        if (!noDistance && !noVertical) return "";

        return "\n  ^^ THIS IS THE BUG: a zeroed range spawns every candidate at the player's feet, " +
               "inside the floor. Unity zero-fills new elements when you raise the Size of an array " +
               "of [Serializable] classes and skips the C# defaults. Re-save the event bank asset — " +
               "its OnValidate repairs this — or set the ranges by hand.";
    }

    /// <summary>Spells a LayerMask out by name, so a wrong value in the inspector is visible in the log.</summary>
    private static string DescribeMask(LayerMask mask)
    {
        int value = mask.value;
        if (value == 0) return "0 (Nothing — every test against it passes)";
        if (value == ~0) return "-1 (Everything)";

        string names = "";
        for (int layer = 0; layer < 32; layer++)
        {
            if ((value & (1 << layer)) == 0) continue;

            string layerName = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName)) layerName = $"<unnamed {layer}>";

            names += names.Length == 0 ? layerName : ", " + layerName;
        }

        return $"{value} ({names})";
    }

    // ── Anchors ──────────────────────────────────────────────────────────────

    private bool TryPickAnchor(SO_AmbienceEventBank.Entry entry, Vector3 listenerPosition,
                               Vector3 listenerForward, out AmbiencePlacement placement)
    {
        placement = default;

        anchorCandidates.Clear();
        anchorWeights.Clear();

        float totalWeight = 0f;
        Vector3 forwardFlat = Flatten(listenerForward);

        IReadOnlyList<AmbienceEmitter> all = AmbienceEmitter.Registered;

        for (int i = 0; i < all.Count; i++)
        {
            AmbienceEmitter anchor = all[i];

            if (anchor == null || !anchor.isActiveAndEnabled) continue;
            if (!anchor.IsReady) continue;
            if (!anchor.Accepts(entry.tags)) continue;

            Vector3 toAnchor = anchor.transform.position - listenerPosition;
            float distance = toAnchor.magnitude;

            if (distance < entry.distanceRange.x || distance > entry.distanceRange.y) continue;

            // Anchors respect the front exclusion cone too, so a sound never pops out of a prop the
            // player is staring straight at.
            if (distance > 0.001f)
            {
                float angle = Vector3.Angle(forwardFlat, Flatten(toAnchor));
                if (angle < frontExclusionHalfAngle) continue;
            }

            anchorCandidates.Add(anchor);
            anchorWeights.Add(anchor.Weight);
            totalWeight += anchor.Weight;
        }

        if (anchorCandidates.Count == 0 || totalWeight <= 0f) return false;

        float roll = Random.value * totalWeight;
        int chosen = anchorCandidates.Count - 1;

        for (int i = 0; i < anchorWeights.Count; i++)
        {
            roll -= anchorWeights[i];
            if (roll > 0f) continue;
            chosen = i;
            break;
        }

        AmbienceEmitter picked = anchorCandidates[chosen];
        picked.MarkUsed();

        Vector3 position = picked.transform.position;

        // An anchor position is authoritative — never snapped. But it can still be behind a wall, and
        // in that case it should be muffled like anything else.
        bool occluded = Physics.Linecast(listenerPosition, position, occluderMask,
                                         QueryTriggerInteraction.Ignore);

        placement = new AmbiencePlacement(position, occluded, picked);
        return true;
    }

    // ── Validated random ─────────────────────────────────────────────────────

    private bool TryPickRandom(SO_AmbienceEventBank.Entry entry, Vector3 listenerPosition,
                               Vector3 listenerForward, Vector3 playerFeetPosition,
                               Camera listenerCamera, out AmbiencePlacement placement)
    {
        placement = default;

        // The last few attempts drop the optional constraints rather than failing outright: a player
        // standing in a corner with the only open space in front of them should still hear the
        // building.
        int relaxFrom = Mathf.Max(1, maxAttempts - 3);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool relaxed = attempt >= relaxFrom;

            Vector3 direction = SampleDirection(listenerForward, relaxed);

            // distanceRange is HORIZONTAL distance; verticalRange is applied as an absolute height
            // relative to the player's feet, so the true 3D distance is slightly larger.
            // Once relaxed, pull the distance towards the near end of the band. A corridor only has
            // open space along its own axis, so a point 30 m away in a random rear direction is
            // almost always inside a wall — while a point at the minimum distance usually is not.
            // This is what keeps tight geometry (PASILLO_*, VESTUARIOS, ESCALERA_01) from starving.
            float farLimit = relaxed
                ? Mathf.Lerp(entry.distanceRange.x, entry.distanceRange.y, 0.35f)
                : entry.distanceRange.y;

            float horizontalDistance = Random.Range(entry.distanceRange.x, farLimit);
            float height = Random.Range(entry.verticalRange.x, entry.verticalRange.y);

            Vector3 candidate = listenerPosition + direction * horizontalDistance;
            candidate.y = playerFeetPosition.y + height;

            // A real frustum test on top of the angular bias — the angular test is flat, the frustum
            // accounts for aspect ratio and pitch. Skipped once relaxed.
            if (!relaxed && IsOnScreen(listenerCamera, candidate))
            {
                rejectedOnScreen++;
                continue;
            }

            // OverlapSphereNonAlloc rather than CheckSphere: same cost and same answer, but it hands
            // back WHICH collider blocked, which is the difference between a diagnostic that names
            // the object and one that lists suspects.
            int overlaps = Physics.OverlapSphereNonAlloc(candidate, solidCheckRadius, overlapBuffer,
                                                         solidMask, QueryTriggerInteraction.Ignore);
            if (overlaps > 0)
            {
                rejectedInsideGeometry++;

                if (overlapBuffer[0] != null)
                {
                    lastBlockerName = overlapBuffer[0].name;
                    lastBlockerLayer = overlapBuffer[0].gameObject.layer;
                }

                continue;
            }

            // Sampled at the PLAYER'S floor level, not at the candidate's height. The question this
            // test asks is "is this XZ position inside the building?", and the vertical offset must
            // not participate in it: the NavMesh lies on the floor, SamplePosition measures 3D
            // distance, so testing the raised point would reject everything above
            // navMeshSampleRadius — which is exactly the ceiling and other-floor placements the
            // design asks for.
            Vector3 navProbe = new Vector3(candidate.x, playerFeetPosition.y, candidate.z);

            if (entry.requireNavMeshNearby && !relaxed &&
                !NavMesh.SamplePosition(navProbe, out _, navMeshSampleRadius, NavMesh.AllAreas))
            {
                rejectedOffNavMesh++;
                continue;
            }

            bool occluded = Physics.Linecast(listenerPosition, candidate, out RaycastHit hit,
                                             occluderMask, QueryTriggerInteraction.Ignore);

            if (occluded)
            {
                // While still preferring clear points, only take an occluded one if the roll allows.
                if (attempt < preferClearAttempts && Random.value > maxOccludedFraction)
                {
                    rejectedOccludedCap++;
                    continue;
                }

                if (entry.allowOccluderSnap)
                    candidate = hit.point + direction * wallPenetration;
            }

            placement = new AmbiencePlacement(candidate, occluded, null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A horizontal unit vector, biased away from where the player is looking.
    /// </summary>
    private Vector3 SampleDirection(Vector3 listenerForward, bool relaxed)
    {
        Vector3 forwardFlat = Flatten(listenerForward);
        float baseYaw = Mathf.Atan2(forwardFlat.x, forwardFlat.z) * Mathf.Rad2Deg;

        float yawOffset;

        if (!relaxed && Random.value < behindBias)
        {
            // Rear hemisphere.
            yawOffset = 180f + Random.Range(-90f, 90f);
        }
        else if (!relaxed)
        {
            // Full circle minus the cone straight ahead.
            yawOffset = Random.Range(frontExclusionHalfAngle, 360f - frontExclusionHalfAngle);
        }
        else
        {
            yawOffset = Random.Range(0f, 360f);
        }

        float yaw = (baseYaw + yawOffset) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
    }

    private static Vector3 Flatten(Vector3 v)
    {
        Vector3 flat = new Vector3(v.x, 0f, v.z);

        // Looking straight up or down leaves nothing to flatten; any yaw is as good as another.
        return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
    }

    private static bool IsOnScreen(Camera camera, Vector3 worldPosition)
    {
        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);

        return viewport.z > 0f &&
               viewport.x >= 0f && viewport.x <= 1f &&
               viewport.y >= 0f && viewport.y <= 1f;
    }

    // ── Debug ────────────────────────────────────────────────────────────────

    private void RecordDebug(AmbiencePlacement placement, Vector3 listenerPosition,
                             SO_AmbienceEventBank.ETier tier)
    {
        if (!drawGizmos) return;

        debugRecords.Add(new DebugRecord
        {
            Position = placement.Position,
            ListenerPosition = listenerPosition,
            Occluded = placement.Occluded,
            FromAnchor = placement.Anchor != null,
            Tier = tier
        });

        while (debugRecords.Count > gizmoHistory)
            debugRecords.RemoveAt(0);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos || debugRecords.Count == 0) return;

        for (int i = 0; i < debugRecords.Count; i++)
        {
            DebugRecord record = debugRecords[i];

            // Newest records are drawn at full strength, older ones fade out.
            float freshness = (i + 1f) / debugRecords.Count;

            Color color = TierColor(record.Tier);
            color.a = 0.25f + 0.75f * freshness;
            Gizmos.color = color;

            Gizmos.DrawWireSphere(record.Position, record.FromAnchor ? 0.7f : 0.5f);

            if (record.Occluded)
            {
                // Occluded events get a second, larger ring so a muffled one is identifiable at a
                // glance without reading the log.
                Gizmos.DrawWireSphere(record.Position, 1.1f);
            }

            Gizmos.DrawLine(record.ListenerPosition, record.Position);
        }
    }

    private static Color TierColor(SO_AmbienceEventBank.ETier tier)
    {
        switch (tier)
        {
            case SO_AmbienceEventBank.ETier.Rare:     return new Color(1f, 0.25f, 0.2f);
            case SO_AmbienceEventBank.ETier.Uncommon: return new Color(1f, 0.75f, 0.2f);
            case SO_AmbienceEventBank.ETier.Common:
            default:                                  return new Color(0.4f, 0.9f, 1f);
        }
    }
#endif
}

/// <summary>Where an ambient one-shot resolved to, and how it got there.</summary>
public readonly struct AmbiencePlacement
{
    public readonly Vector3 Position;

    /// <summary>True when a surface stands between the listener and the emitter. Drives the muffling.</summary>
    public readonly bool Occluded;

    /// <summary>The anchor this came from, or null when it was placed by validated random.</summary>
    public readonly AmbienceEmitter Anchor;

    public AmbiencePlacement(Vector3 position, bool occluded, AmbienceEmitter anchor)
    {
        Position = position;
        Occluded = occluded;
        Anchor = anchor;
    }
}
