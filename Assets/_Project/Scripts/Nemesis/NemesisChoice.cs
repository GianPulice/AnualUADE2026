using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the Nemesis is paying attention to — its FOCUS — and the one place that decides when that
/// changes (plan §17.3 re-election, §17.4, Fase 2B part 4).
///
/// NemesisBelief KNOWS: where the player is believed to be, the latest lead (a decoy, a Director
/// pulse), the latest glimpse. This component CHOOSES which of them to follow, by asking
/// <see cref="FocusArbiter"/> every time something new comes in: the player's evidence (the belief's
/// sequence moved), a new lead (its lead sequence moved), a fresh glimpse while it is suspicious. Never
/// because a sensor is merely on — that is the per-frame re-aiming the plan replaces. The one repeat
/// is a lead that keeps sounding and is not the focus: it is asked again every
/// <see cref="LeadReofferInterval"/>, because the fire alarm never goes quiet, and a decoy that lost
/// once to a fresh belief has to be able to win once that belief has gone cold (§17.5).
///
/// WHO READS IT:
///   - The ladder, through the predicate FocusIsLead: "su atención está en una pista" sends it to
///     Investigating, above the search budget, so a decoy that wins the question pulls it out of a
///     cold search (case 31) and one that loses never moves it off its patrol (case 32).
///   - NemesisInvestigatingState walks to the focus: the lead, the glimpse, or the player's latest
///     noise.
///   - NemesisDecoyBreaker breaks the decoy that is the focus, not the last one it happened to hear.
///   - F9's "foco" row: what, how much it is worth, and the last decision with the question behind it.
///
/// LETTING GO OF A LEAD OR A GLIMPSE FALLS BACK ON THE PLAYER while there is a belief: the focus is
/// "nothing" only when it believes nothing. With "nothing" as the thing to beat, any Director pulse
/// won the question after an empty check and pulled a warm search out from under itself (review
/// 28/09). The fallback carries no commitment bonus: it was not just chosen.
///
/// WHAT IT REMEMBERS PER DECOY (§17.5): how many times it sent the Nemesis somewhere for nothing — the
/// habituation, ×LeadHabituation each — for the session (it outlives a capture, R5); and which decoys
/// it has already checked while they were still sounding, which are discarded until they stop.
///
/// It does not decide states and it does not steer: the ladder is still the one voice (plan §10).
///
/// SETUP: none. NemesisStateManager adds it next to itself, initializes it, and ticks it right after
/// the belief. Its numbers live on SO_NemesisData (Elección).
/// </summary>
[DisallowMultipleComponent]
public class NemesisChoice : MonoBehaviour
{
    /// <summary>A glimpse is offered at most this often: the corner of its eye refreshes it every
    /// frame of contact, and one question per sensor sweep is plenty.</summary>
    private const float GlimpseOfferInterval = 0.25f;

    /// <summary>A lead that keeps sounding and is not the focus is asked about again this often.
    /// </summary>
    private const float LeadReofferInterval = 1f;

    /// <summary>A lead heard this recently is still sounding: a few sweeps of the ear (0.1 s each).
    /// </summary>
    private const float LeadStillHeard = 0.5f;

    /// <summary>A lead the Nemesis is still walking to is dropped this long after it was last heard:
    /// the fire alarm across the level is a long walk, but not a forever one.</summary>
    private const float LeadFocusMemory = 30f;

    /// <summary>A glimpse stops being the focus this long after it was seen, if nothing came of it.
    /// </summary>
    private const float GlimpseFocusMemory = 8f;

    /// <summary>A decoy is a point; an anonymous noise (a Director pulse) is less sure.</summary>
    private const float DecoyRadius = 0.5f;
    private const float AnonymousLeadRadius = 1.5f;
    private const float GlimpseRadius = 1.5f;

    /// <summary>A path distance to a lead is reused this long, while neither end moved more than
    /// <see cref="PathMemoSlack"/>: two decoys trading places as the loudest would otherwise cost a
    /// path query per ear sweep.</summary>
    private const float PathMemoLifetime = 1f;
    private const float PathMemoSlack = 1f;

    private NemesisStateManager stateManager;
    private NemesisDecoyBreaker breaker;

    // ── The focus ────────────────────────────────────────────────────────────

    private FocusArbiter.EKind focusKind;
    private Vector3 focusPosition;
    private DecoyNoiseSource focusDecoy;
    private int focusIdentity;
    private float focusBaseValue;
    private float focusRadius;
    private float focusSensedAt;
    private float focusChosenAt;

    /// <summary>When the commitment bonus (question 8) started counting down: the moment it CHOSE the
    /// focus. Never for the fallback onto the player after letting go of something.</summary>
    private float committedAt = float.NegativeInfinity;

    private readonly float[] lastSwitchAt = { float.NegativeInfinity, float.NegativeInfinity,
                                              float.NegativeInfinity, float.NegativeInfinity };

    private int consumedBelief = int.MinValue;
    private int consumedLead = int.MinValue;
    private float leadOfferedAt = float.NegativeInfinity;
    private float glimpseOfferedAt = float.NegativeInfinity;

    private readonly Dictionary<DecoyNoiseSource, int> fruitlessVisits = new Dictionary<DecoyNoiseSource, int>();
    private readonly HashSet<DecoyNoiseSource> checkedWhileSounding = new HashSet<DecoyNoiseSource>();
    private readonly List<DecoyNoiseSource> scratch = new List<DecoyNoiseSource>();

    private struct PathMemo
    {
        public Vector3 From;
        public Vector3 To;
        public float At;
        public float Distance;
    }

    private readonly Dictionary<int, PathMemo> pathMemo = new Dictionary<int, PathMemo>();

    /// <summary>What it is following now.</summary>
    public FocusArbiter.EKind FocusKind => focusKind;

    public bool IsFocusOnLead => focusKind == FocusArbiter.EKind.Lead;

    /// <summary>Where the focus is. The player's is the belief itself, read live.</summary>
    public Vector3 FocusPosition
    {
        get
        {
            NemesisBelief belief = stateManager != null ? stateManager.Belief : null;
            if (focusKind == FocusArbiter.EKind.Player && belief != null && belief.HasBelief) return belief.Position;
            return focusPosition;
        }
    }

    /// <summary>The decoy that is the focus, or null (an anonymous lead, or no lead at all).</summary>
    public DecoyNoiseSource FocusDecoy => focusKind == FocusArbiter.EKind.Lead ? focusDecoy : null;

    /// <summary>Goes up every time the focus changes to something else (a Switch, or letting go).
    /// What a state compares to notice "it changed its mind", the way it compares the belief's
    /// sequence.</summary>
    public int SwitchSequence { get; private set; }

    /// <summary>Goes up whenever the focus moves at all: a switch, or an update of the same thing.
    /// </summary>
    public int FocusSequence { get; private set; }

    /// <summary>Seconds since the current focus was chosen.</summary>
    public float FocusAge => Time.time - focusChosenAt;

    /// <summary>What the focus is worth right now, as the arbiter sees it. For F9.</summary>
    public float FocusValue => FocusArbiter.Value(CurrentAsCandidate(), Tuning);

    /// <summary>The last decision worth reading, with the question behind it. For F9.</summary>
    public string LastDecision { get; private set; } = "—";

    public float LastDecisionAt { get; private set; } = float.NegativeInfinity;

    /// <summary>How many times this decoy has sent it somewhere for nothing. For F9.</summary>
    public int FruitlessVisitsTo(DecoyNoiseSource decoy) =>
        decoy != null && fruitlessVisits.TryGetValue(decoy, out int visits) ? visits : 0;

    private SO_NemesisData Data => stateManager != null ? stateManager.NemesisData : null;

    /// <summary>Called once by NemesisStateManager, after its references are resolved.</summary>
    public void Initialize(NemesisStateManager manager)
    {
        stateManager = manager;
        breaker = manager != null ? manager.GetComponent<NemesisDecoyBreaker>() : null;
    }

    /// <summary>
    /// Offers whatever is new to the arbiter. Ticked by NemesisStateManager right after the belief,
    /// before the ladder, so every reader in the frame sees the same focus.
    /// </summary>
    public void Tick()
    {
        NemesisBelief belief = stateManager != null ? stateManager.Belief : null;
        if (belief == null) return;

        ForgetStoppedDecoys();

        // Forgotten (a capture: NemesisBelief.Forget): so is whatever it was following.
        if (focusKind == FocusArbiter.EKind.Lead && !belief.TryGetLead(out _, out _, out _)) Drop("olvidó la pista");
        if (focusKind == FocusArbiter.EKind.Glimpse && !belief.TryGetGlimpse(out _, out _)) Drop("olvidó el vistazo");
        if (focusKind == FocusArbiter.EKind.Player && !belief.HasBelief) Drop("olvidó al jugador");

        // A hiding spot it suspects or knows outranks any decoy: it is the player, glimpsed getting in
        // or made out through the slats. Letting go of the lead is what keeps "su atención está en una
        // pista", above "sospecha de un escondite" in the ladder, from sending it to the radio instead.
        if (focusKind == FocusArbiter.EKind.Lead &&
            (stateManager.KnownHidingSpot != null || stateManager.SuspectedHidingSpot != null))
            Drop("sospecha de un escondite");

        // The player. Their evidence comes first: it is what a lead or a glimpse has to beat.
        if (belief.HasBelief && belief.Sequence != consumedBelief)
        {
            consumedBelief = belief.Sequence;
            Offer(PlayerCandidate(belief), belief, null);
        }

        TickLead(belief);

        // A glimpse, while it does not plainly see the player and the corner of its eye has made it
        // suspicious. Below the threshold a glimpse is not something to walk to: nothing in the ladder
        // acts on it, and one frame of contact used to take the focus off a lead for nothing.
        if (!stateManager.HasVisualTarget && stateManager.IsSuspicious &&
            Time.time - glimpseOfferedAt >= GlimpseOfferInterval &&
            belief.TryGetGlimpse(out Vector3 glimpse, out float glimpseAge) && glimpseAge < GlimpseOfferInterval)
        {
            glimpseOfferedAt = Time.time;
            Offer(GlimpseCandidate(glimpse, glimpseAge), belief, null);
        }

        Expire();
    }

    /// <summary>
    /// A NEW lead (another source, another place, or the same after a silence) is asked about at once.
    /// One that keeps sounding is asked about again every LeadReofferInterval while it is not the
    /// focus: whatever made it lose — a fresh belief, being busy, a search it summed with, the
    /// anti-dither — may have passed. The lead that IS the focus keeps its age honest instead: it is
    /// as fresh as the last time it was heard.
    /// </summary>
    private void TickLead(NemesisBelief belief)
    {
        bool isNew = belief.LeadSequence != consumedLead;
        consumedLead = belief.LeadSequence;

        if (!belief.TryGetLead(out Vector3 leadPosition, out float leadAge, out DecoyNoiseSource decoy)) return;

        bool isFocus = IsFocusLead(decoy, leadPosition);
        if (isFocus) focusSensedAt = Mathf.Max(focusSensedAt, Time.time - leadAge);

        bool askAgain = !isFocus && leadAge < LeadStillHeard && Time.time - leadOfferedAt >= LeadReofferInterval;
        if (!isNew && !askAgain) return;

        leadOfferedAt = Time.time;
        Offer(LeadCandidate(leadPosition, leadAge, decoy), belief, decoy);
    }

    /// <summary>
    /// Investigating reports that it looked at the lead or the glimpse it was sent to and found
    /// nothing. A decoy gets one more fruitless visit (habituation) and, while it keeps sounding, is
    /// not worth another look; the focus goes.
    /// </summary>
    public void MarkFocusChecked()
    {
        if (focusKind == FocusArbiter.EKind.Lead && focusDecoy != null)
        {
            fruitlessVisits[focusDecoy] = FruitlessVisitsTo(focusDecoy) + 1;
            if (focusDecoy.IsEmitting) checkedWhileSounding.Add(focusDecoy);
        }

        if (focusKind == FocusArbiter.EKind.Lead || focusKind == FocusArbiter.EKind.Glimpse)
            Drop("revisó: nada");
    }

    // ── The question ─────────────────────────────────────────────────────────

    private FocusArbiter.Tuning Tuning
    {
        get
        {
            FocusArbiter.Tuning tuning = FocusArbiter.Tuning.Default;
            SO_NemesisData data = Data;
            if (data == null) return tuning;

            tuning.AttentionFloor = data.FocusAttentionFloor;
            tuning.Margin = data.FocusMargin;
            tuning.CommitmentBonus = data.FocusCommitmentBonus;
            tuning.CommitmentDecay = data.FocusCommitmentDecay;
            tuning.FreshnessHalfLife = data.FocusFreshnessHalfLife;
            tuning.CostDistance = data.FocusCostDistance;
            tuning.AntiDither = data.FocusAntiDither;
            return tuning;
        }
    }

    /// <param name="decoy">The decoy behind a lead candidate, carried by hand: the candidate is plain
    /// data (WIRED.Nemesis.Logic cannot see DecoyNoiseSource), and looking it up again by id misses a
    /// decoy that stopped within the sweep.</param>
    private void Offer(in FocusArbiter.Candidate candidate, NemesisBelief belief, DecoyNoiseSource decoy)
    {
        FocusArbiter.Candidate current = CurrentAsCandidate();
        DecoyNoiseSource currentDecoy = FocusDecoy;
        float sinceSwitch = Time.time - lastSwitchAt[(int)candidate.Kind];

        FocusArbiter.Decision decision = FocusArbiter.Decide(current, Time.time - committedAt, sinceSwitch, candidate,
                                                             IsBusyFor(candidate.Kind),
                                                             SumsWithBelief(candidate, belief), Tuning);

        switch (decision.Verdict)
        {
            case FocusArbiter.EVerdict.Switch:
                Take(candidate, decoy);
                Note($"cambió: {Describe(candidate, decoy)} {decision.CandidateValue:0.00} > " +
                     $"{Describe(current, currentDecoy)} {decision.CurrentValue:0.00}", decision.Reason);
                break;

            case FocusArbiter.EVerdict.Update:
                Refresh(candidate, decoy);
                break;

            case FocusArbiter.EVerdict.Keep:
                // The stream of the player's own evidence while it is already on them is not news.
                if (candidate.Kind == FocusArbiter.EKind.Player && current.Kind == FocusArbiter.EKind.Player) break;
                Note($"siguió con {Describe(current, currentDecoy)}: {Describe(candidate, decoy)} " +
                     $"{decision.CandidateValue:0.00} vs {decision.CurrentValue:0.00}", decision.Reason);
                break;

            case FocusArbiter.EVerdict.Ignore:
                Note($"ignoró {Describe(candidate, decoy)} ({decision.CandidateValue:0.00})", decision.Reason);
                break;
        }
    }

    /// <summary>
    /// Question 3, per what is asking. Against anything: the two things its hands are busy with —
    /// breaking the radio, opening a hiding spot at its door. Against a lead or a glimpse, also going
    /// to a hiding spot it suspects or knows: that is the player, and a decoy does not take it off them.
    /// </summary>
    private bool IsBusyFor(FocusArbiter.EKind kind)
    {
        if (breaker != null && breaker.IsBreaking) return true;

        NemesisSearchingState searching = stateManager.SearchingState;
        if (stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Searching &&
            searching != null && searching.IsCheckingSpot)
            return true;

        return kind != FocusArbiter.EKind.Player &&
               (stateManager.KnownHidingSpot != null || stateManager.SuspectedHidingSpot != null);
    }

    /// <summary>
    /// A lead sounding where it is already SEARCHING for the player (§17.5, case 33): the belief's
    /// radius, capped at the widest sweep, plus LeadSumsMargin, on the same floor. Going there serves
    /// both, and the sweep covers it. Only while searching: a patrol near an old belief is not covering
    /// anything, and a lead there is worth the walk on its own value.
    /// </summary>
    private bool SumsWithBelief(in FocusArbiter.Candidate candidate, NemesisBelief belief)
    {
        if (candidate.Kind != FocusArbiter.EKind.Lead || belief == null || !belief.HasBelief) return false;
        if (stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Searching) return false;

        SO_NemesisData data = Data;
        float floor = data != null ? data.FloorHeightThreshold : 2.5f;
        if (Mathf.Abs(candidate.Position.y - belief.Position.y) > floor) return false;

        float maxRadius = data != null ? data.RoomSweepRadius : 8f;
        float margin = data != null ? data.LeadSumsMargin : 3f;
        float zone = Mathf.Min(belief.Radius, maxRadius) + margin;

        Vector3 offset = candidate.Position - belief.Position;
        offset.y = 0f;
        return offset.sqrMagnitude <= zone * zone;
    }

    // ── Candidates ───────────────────────────────────────────────────────────

    private FocusArbiter.Candidate PlayerCandidate(NemesisBelief belief)
    {
        return new FocusArbiter.Candidate
        {
            Kind = FocusArbiter.EKind.Player,
            Position = belief.Position,
            BaseValue = 1f,
            Radius = belief.EvidenceRadius,
            Age = belief.Age,
            Distance = FlatDistanceTo(belief.Position),
            AcrossFloors = IsAcrossFloors(belief.Position),
            Habituation = 1f,
            IsSight = stateManager.HasVisualTarget && belief.IsAnchoredBySight,
        };
    }

    private FocusArbiter.Candidate LeadCandidate(Vector3 position, float age, DecoyNoiseSource decoy)
    {
        int identity = decoy != null ? decoy.Id : 0;

        return new FocusArbiter.Candidate
        {
            Kind = FocusArbiter.EKind.Lead,
            Identity = identity,
            Position = position,
            BaseValue = LeadBaseValue(decoy),
            Radius = decoy != null ? DecoyRadius : AnonymousLeadRadius,
            Age = age,
            Distance = PathDistanceTo(identity, position),
            AcrossFloors = IsAcrossFloors(position),
            Habituation = HabituationOf(decoy),
            Discarded = decoy != null && checkedWhileSounding.Contains(decoy),
        };
    }

    private FocusArbiter.Candidate GlimpseCandidate(Vector3 position, float age)
    {
        SO_NemesisData data = Data;
        return new FocusArbiter.Candidate
        {
            Kind = FocusArbiter.EKind.Glimpse,
            Position = position,
            BaseValue = data != null ? data.GlimpseValue : 0.5f,
            Radius = GlimpseRadius,
            Age = age,
            Distance = FlatDistanceTo(position),
            AcrossFloors = IsAcrossFloors(position),
            Habituation = 1f,
        };
    }

    /// <summary>The focus as the arbiter needs it: its age and distance as of now — a lead measured
    /// over the NavMesh, like the leads it is compared against.</summary>
    private FocusArbiter.Candidate CurrentAsCandidate()
    {
        if (focusKind == FocusArbiter.EKind.None) return FocusArbiter.Candidate.None;

        if (focusKind == FocusArbiter.EKind.Player)
        {
            NemesisBelief belief = stateManager.Belief;
            if (belief == null || !belief.HasBelief) return FocusArbiter.Candidate.None;
            FocusArbiter.Candidate player = PlayerCandidate(belief);
            player.IsSight = false;
            return player;
        }

        bool isLead = focusKind == FocusArbiter.EKind.Lead;

        return new FocusArbiter.Candidate
        {
            Kind = focusKind,
            Identity = focusIdentity,
            Position = focusPosition,
            BaseValue = focusBaseValue,
            Radius = focusRadius,
            Age = Time.time - focusSensedAt,
            Distance = isLead ? PathDistanceTo(focusIdentity, focusPosition) : FlatDistanceTo(focusPosition),
            AcrossFloors = IsAcrossFloors(focusPosition),
            Habituation = isLead ? HabituationOf(focusDecoy) : 1f,
        };
    }

    private float HabituationOf(DecoyNoiseSource decoy)
    {
        SO_NemesisData data = Data;
        float habituationStep = data != null ? data.LeadHabituation : 0.6f;
        return Mathf.Pow(habituationStep, FruitlessVisitsTo(decoy));
    }

    private float LeadBaseValue(DecoyNoiseSource decoy)
    {
        SO_NemesisData data = Data;
        if (data == null) return 0.4f;
        if (decoy == null) return data.LeadValueOther;

        switch (decoy.Kind)
        {
            case DecoyNoiseSource.EKind.Radio: return data.LeadValueRadio;
            case DecoyNoiseSource.EKind.FireAlarm: return data.LeadValueFireAlarm;
            case DecoyNoiseSource.EKind.Chains: return data.LeadValueChains;
            default: return data.LeadValueOther;
        }
    }

    /// <summary>Whether the lead the belief carries right now is the one that is the focus: the same
    /// decoy, or — for an anonymous focus — an anonymous noise at the same place.</summary>
    private bool IsFocusLead(DecoyNoiseSource decoy, Vector3 position)
    {
        if (focusKind != FocusArbiter.EKind.Lead) return false;
        if (focusIdentity != 0) return decoy != null && decoy.Id == focusIdentity;
        if (decoy != null) return false;

        float same = Tuning.SameSpotDistance;
        return (position - focusPosition).sqrMagnitude < same * same;
    }

    // ── Changing the focus ───────────────────────────────────────────────────

    private void Take(in FocusArbiter.Candidate candidate, DecoyNoiseSource decoy)
    {
        focusKind = candidate.Kind;
        focusIdentity = candidate.Identity;
        focusDecoy = candidate.Kind == FocusArbiter.EKind.Lead ? (decoy != null ? decoy : FindDecoy(candidate.Identity)) : null;
        focusChosenAt = Time.time;
        committedAt = Time.time;
        lastSwitchAt[(int)candidate.Kind] = Time.time;
        SwitchSequence++;
        Refresh(candidate, decoy);
    }

    private void Refresh(in FocusArbiter.Candidate candidate, DecoyNoiseSource decoy)
    {
        // An anonymous noise that turns out to be a decoy standing there (question 4 calls them the
        // same place): from now on it is that decoy — the one to break, to habituate to, to discard.
        if (candidate.Kind == FocusArbiter.EKind.Lead && focusIdentity == 0 && candidate.Identity != 0)
        {
            focusIdentity = candidate.Identity;
            focusDecoy = decoy != null ? decoy : FindDecoy(candidate.Identity);
        }

        focusPosition = candidate.Position;
        focusBaseValue = candidate.BaseValue;
        focusRadius = candidate.Radius;
        focusSensedAt = Time.time - Mathf.Max(0f, candidate.Age);
        FocusSequence++;
    }

    /// <summary>
    /// Lets go of the focus. A lead or a glimpse falls back on the player while there is a belief —
    /// see the class summary — without the commitment bonus of a fresh choice; otherwise, nothing.
    /// </summary>
    private void Drop(string why)
    {
        if (focusKind == FocusArbiter.EKind.None) return;

        NemesisBelief belief = stateManager != null ? stateManager.Belief : null;
        bool backToPlayer = focusKind != FocusArbiter.EKind.Player && belief != null && belief.HasBelief;

        Note($"soltó {DescribeFocus()}: {why}{(backToPlayer ? " → vos" : "")}", null);

        focusDecoy = null;
        focusIdentity = 0;

        if (backToPlayer)
        {
            focusKind = FocusArbiter.EKind.Player;
            focusChosenAt = Time.time;
            committedAt = float.NegativeInfinity;
        }
        else
        {
            focusKind = FocusArbiter.EKind.None;
        }

        SwitchSequence++;
        FocusSequence++;
    }

    /// <summary>A lead or a glimpse that has gone stale without the Nemesis getting anywhere with it.
    /// The player's focus never expires on its own: its value fades, and anything new beats it. A lead
    /// ages from the last time it was HEARD (TickLead keeps that honest), not from when it was chosen.
    /// </summary>
    private void Expire()
    {
        float age = Time.time - focusSensedAt;

        if (focusKind == FocusArbiter.EKind.Lead)
        {
            bool stillSounding = focusDecoy != null && focusDecoy.IsEmitting;
            if (!stillSounding && age > LeadFocusMemory) Drop("la pista se enfrió");
        }
        else if (focusKind == FocusArbiter.EKind.Glimpse && age > GlimpseFocusMemory)
        {
            Drop("el vistazo se enfrió");
        }
    }

    /// <summary>A checked decoy is worth looking at again once it stops and sounds anew.</summary>
    private void ForgetStoppedDecoys()
    {
        if (checkedWhileSounding.Count == 0) return;

        scratch.Clear();
        foreach (DecoyNoiseSource decoy in checkedWhileSounding)
        {
            if (decoy == null || !decoy.IsEmitting) scratch.Add(decoy);
        }
        foreach (DecoyNoiseSource decoy in scratch) checkedWhileSounding.Remove(decoy);
    }

    private static DecoyNoiseSource FindDecoy(int identity)
    {
        if (identity == 0) return null;

        IReadOnlyList<DecoyNoiseSource> decoys = DecoyNoiseSource.Active;
        for (int i = 0; i < decoys.Count; i++)
        {
            if (decoys[i] != null && decoys[i].Id == identity) return decoys[i];
        }
        return null;
    }

    // ── Distances ────────────────────────────────────────────────────────────

    private float FlatDistanceTo(Vector3 point)
    {
        Vector3 offset = point - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    /// <summary>Over the NavMesh, remembered per lead for a moment (PathMemoLifetime). Infinity when
    /// there is no complete route (the arbiter weighs it down, it does not veto it).</summary>
    private float PathDistanceTo(int identity, Vector3 point)
    {
        Vector3 from = transform.position;
        float slack = PathMemoSlack * PathMemoSlack;

        if (pathMemo.TryGetValue(identity, out PathMemo memo) && Time.time - memo.At < PathMemoLifetime &&
            (memo.From - from).sqrMagnitude < slack && (memo.To - point).sqrMagnitude < slack)
            return memo.Distance;

        float distance = NemesisNav.TryGetPathDistance(from, point, out float measured) ? measured : float.PositiveInfinity;
        pathMemo[identity] = new PathMemo { From = from, To = point, At = Time.time, Distance = distance };
        return distance;
    }

    private bool IsAcrossFloors(Vector3 point)
    {
        SO_NemesisData data = Data;
        float floor = data != null ? data.FloorHeightThreshold : 2.5f;
        return Mathf.Abs(point.y - transform.position.y) > floor;
    }

    // ── F9 ───────────────────────────────────────────────────────────────────

    private void Note(string text, FocusArbiter.EReason? reason)
    {
        LastDecision = reason.HasValue ? $"{text} [{FocusArbiter.QuestionOf(reason.Value)}]" : text;
        LastDecisionAt = Time.time;
    }

    /// <summary>What the focus is, in the HUD's words.</summary>
    public string DescribeFocus() => Describe(CurrentAsCandidate(), focusDecoy);

    private static string Describe(in FocusArbiter.Candidate candidate, DecoyNoiseSource decoy)
    {
        switch (candidate.Kind)
        {
            case FocusArbiter.EKind.Player: return "vos";
            case FocusArbiter.EKind.Glimpse: return "vistazo";
            case FocusArbiter.EKind.Lead:
                if (decoy == null) return "ruido";
                switch (decoy.Kind)
                {
                    case DecoyNoiseSource.EKind.Radio: return "radio";
                    case DecoyNoiseSource.EKind.FireAlarm: return "alarma";
                    case DecoyNoiseSource.EKind.Chains: return "cadenas";
                    default: return "señuelo";
                }
            default: return "nada";
        }
    }
}
