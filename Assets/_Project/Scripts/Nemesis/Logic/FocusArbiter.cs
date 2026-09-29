using UnityEngine;

/// <summary>
/// What the Nemesis pays attention to (plan §17.4, Fase 2B part 4): the questions it asks when
/// something new comes in, against what it is after right now.
///
/// NemesisBelief KNOWS (the player, the leads, the glimpses); NemesisChoice asks this class what to
/// FOLLOW; the ladder still decides the state. It answers with a verdict and the question that decided
/// it, so F9 can say "cambió: paso del jugador 0.53 > cadenas 0.30" or "no cambió: mismo lugar".
///
/// THE QUESTIONS, in order; the first that answers decides (§17.4 numbering):
///   1  Anything new?           The caller only asks with new evidence.
///   2  Is it seeing the player? Switch now. Nothing competes.
///   3  In the middle of something that is not cut? (breaking the radio, opening a spot) Keep.
///   4  The same thing it is after? (the same decoy, the player again, the same kind within 3 m)
///      Update: the state keeps its sweep and its pause.
///   5  Discarded? (a broken radio, a lead it checked that is still sounding) Ignore.
///      A lead sounding where it is already searching for the player does not compete: it SUMS
///      (§17.5; the caller says when). Keep.
///   6  Can it get there? Not a veto: an unreachable or other-floor target only weighs less.
///   7  What is the new thing worth? Under the attention floor, ignored — habituation ends here.
///   8  What is the current thing worth, today? Plus the commitment bonus (decays in 3 s) against
///      things of its own rank or lower, and "almost there" (under 4 m from a lead or a glimpse; not
///      from the belief, which a search circles the whole time).
///   9  Does the new one beat it by the margin? No: keep.
///   10 Switched to this kind a moment ago? Keep (no dithering between two alternating noises).
///   11 Switch.
///
/// VALUE = base (by kind, and by decoy type) × confidence (a small radius is worth more) × freshness
/// (half-life) × the cost of getting there × habituation.
///
/// PURE: plain numbers in, a verdict out. No scene, no state manager: WIRED.Nemesis.Logic, so EditMode
/// tests can reach it.
/// </summary>
public static class FocusArbiter
{
    /// <summary>What a focus is. The order is the rank: a higher kind is not protected against by the
    /// commitment bonus of a lower one (a footstep of the player is worth hearing out even right after
    /// choosing the chains).</summary>
    public enum EKind
    {
        None,
        Lead,
        Glimpse,
        Player,
    }

    public enum EVerdict
    {
        /// <summary>Stay on the current focus, untouched.</summary>
        Keep,

        /// <summary>The same thing: refresh the focus's position and inputs, restart nothing.</summary>
        Update,

        /// <summary>Change to the candidate.</summary>
        Switch,

        /// <summary>Not worth anything: drop the candidate.</summary>
        Ignore,
    }

    /// <summary>Which question decided. For F9 and the tests.</summary>
    public enum EReason
    {
        SeesPlayer,
        Busy,
        SameThing,
        Discarded,
        SumsWithBelief,
        BelowAttention,
        NotBetterByMargin,
        TooSoon,
        Better,
    }

    /// <summary>Something to pay attention to, described in plain numbers.</summary>
    public struct Candidate
    {
        public EKind Kind;

        /// <summary>Which decoy, for a lead; 0 for anything anonymous (a Director pulse, the player).
        /// </summary>
        public int Identity;

        public Vector3 Position;

        /// <summary>Worth by kind: the player 1, a radio 0.6, the fire alarm 0.7, the chains 0.45, any
        /// other noise 0.4 (plan §17.5).</summary>
        public float BaseValue;

        /// <summary>How precise, in metres: a sighting is a point, a footstep through a wall a room.
        /// </summary>
        public float Radius;

        /// <summary>Seconds since it was sensed.</summary>
        public float Age;

        /// <summary>Metres to get there, over the NavMesh when known. Infinity: no complete route.
        /// </summary>
        public float Distance;

        public bool AcrossFloors;

        /// <summary>1 when fresh; lower for a decoy that has already sent it somewhere for nothing.
        /// </summary>
        public float Habituation;

        /// <summary>The player, seen right now (question 2).</summary>
        public bool IsSight;

        /// <summary>Broken, or already checked and still sounding the same (question 5).</summary>
        public bool Discarded;

        public static Candidate None => new Candidate { Kind = EKind.None, Habituation = 1f };
    }

    public struct Tuning
    {
        /// <summary>Radius at which confidence halves.</summary>
        public float ConfidenceRadius;

        /// <summary>Seconds in which freshness halves.</summary>
        public float FreshnessHalfLife;

        /// <summary>Metres of travel at which the cost halves the value.</summary>
        public float CostDistance;

        public float AcrossFloorsFactor;
        public float UnreachableFactor;

        /// <summary>Under this, a lead or a glimpse is not worth any attention at all (question 7).
        /// </summary>
        public float AttentionFloor;

        /// <summary>How much more the new thing has to be worth (question 9).</summary>
        public float Margin;

        /// <summary>Added to a focus just chosen, fading to nothing over CommitmentDecay seconds.
        /// </summary>
        public float CommitmentBonus;
        public float CommitmentDecay;

        /// <summary>A focus closer than NearDistance is worth NearBonus more: almost there.</summary>
        public float NearDistance;
        public float NearBonus;

        /// <summary>Two things of the same kind closer than this are the same place (question 4).
        /// </summary>
        public float SameSpotDistance;

        /// <summary>Seconds after switching to a kind during which it does not switch to another of
        /// that kind (question 10).</summary>
        public float AntiDither;

        /// <summary>The shipped numbers (plan §12). Checked against cases 25 and 28–33.</summary>
        public static Tuning Default => new Tuning
        {
            ConfidenceRadius = 4f,
            FreshnessHalfLife = 6f,
            CostDistance = 60f,
            AcrossFloorsFactor = 0.7f,
            UnreachableFactor = 0.4f,
            AttentionFloor = 0.12f,
            Margin = 0.05f,
            CommitmentBonus = 0.3f,
            CommitmentDecay = 3f,
            NearDistance = 4f,
            NearBonus = 0.15f,
            SameSpotDistance = 3f,
            AntiDither = 2f,
        };
    }

    public struct Decision
    {
        public EVerdict Verdict;
        public EReason Reason;

        /// <summary>What the candidate was worth, and the current focus with its bonuses: the two
        /// numbers F9 prints.</summary>
        public float CandidateValue;
        public float CurrentValue;
    }

    /// <summary>
    /// The questions. <paramref name="current"/> is the focus as it is now (its age and distance as of
    /// now); <paramref name="chosenAge"/> how long ago it was chosen; <paramref name="sinceSwitchToKind"/>
    /// how long since the last switch to a focus of the candidate's kind. <paramref name="busy"/>: it is
    /// in the middle of something that is not cut. <paramref name="sumsWithBelief"/>: the candidate is a
    /// lead sounding inside the zone where it believes the player is.
    /// </summary>
    public static Decision Decide(in Candidate current, float chosenAge, float sinceSwitchToKind,
                                  in Candidate candidate, bool busy, bool sumsWithBelief, in Tuning tuning)
    {
        float candidateValue = Value(candidate, tuning);

        // 2. Seeing the player: nothing competes.
        if (candidate.IsSight)
        {
            return current.Kind == EKind.Player
                ? Make(EVerdict.Update, EReason.SeesPlayer, candidateValue, 0f)
                : Make(EVerdict.Switch, EReason.SeesPlayer, candidateValue, 0f);
        }

        // 3. Mid-way through something that is not cut.
        if (busy) return Make(EVerdict.Keep, EReason.Busy, candidateValue, 0f);

        // 4. The same thing.
        if (IsSameThing(current, candidate, tuning.SameSpotDistance))
            return Make(EVerdict.Update, EReason.SameThing, candidateValue, 0f);

        // 5. Discarded.
        if (candidate.Discarded) return Make(EVerdict.Ignore, EReason.Discarded, candidateValue, 0f);

        // A lead where it believes the player is: going there serves both (§17.5).
        if (current.Kind == EKind.Player && candidate.Kind == EKind.Lead && sumsWithBelief)
            return Make(EVerdict.Keep, EReason.SumsWithBelief, candidateValue, 0f);

        // 6 and 7. What the new thing is worth. The player is never beneath notice.
        if (candidate.Kind != EKind.Player && candidateValue < tuning.AttentionFloor)
            return Make(EVerdict.Ignore, EReason.BelowAttention, candidateValue, 0f);

        // 8. What the current one is worth today.
        float currentValue = CurrentWorth(current, chosenAge, candidate.Kind, tuning);

        // 9. By the margin.
        if (candidateValue < currentValue + tuning.Margin)
            return Make(EVerdict.Keep, EReason.NotBetterByMargin, candidateValue, currentValue);

        // 10. No dithering between two of the same kind.
        if (candidate.Kind == current.Kind && sinceSwitchToKind < tuning.AntiDither)
            return Make(EVerdict.Keep, EReason.TooSoon, candidateValue, currentValue);

        // 11.
        return Make(EVerdict.Switch, EReason.Better, candidateValue, currentValue);
    }

    /// <summary>
    /// What something is worth right now: base × confidence × freshness × cost × habituation.
    /// </summary>
    public static float Value(in Candidate candidate, in Tuning tuning)
    {
        if (candidate.Kind == EKind.None) return 0f;

        float confidence = 1f / (1f + Mathf.Max(0f, candidate.Radius) / Mathf.Max(0.01f, tuning.ConfidenceRadius));
        float freshness = Mathf.Pow(0.5f, Mathf.Max(0f, candidate.Age) / Mathf.Max(0.01f, tuning.FreshnessHalfLife));

        float cost = float.IsPositiveInfinity(candidate.Distance)
            ? tuning.UnreachableFactor
            : 1f / (1f + Mathf.Max(0f, candidate.Distance) / Mathf.Max(0.01f, tuning.CostDistance));
        if (candidate.AcrossFloors) cost *= tuning.AcrossFloorsFactor;

        return Mathf.Max(0f, candidate.BaseValue) * confidence * freshness * cost * Mathf.Clamp01(candidate.Habituation);
    }

    /// <summary>
    /// Question 8: the current focus's worth, plus the commitment bonus against a candidate of its own
    /// rank or lower, plus "almost there".
    /// </summary>
    public static float CurrentWorth(in Candidate current, float chosenAge, EKind against, in Tuning tuning)
    {
        if (current.Kind == EKind.None) return 0f;

        float worth = Value(current, tuning);

        if (against <= current.Kind && tuning.CommitmentDecay > 0f)
            worth += tuning.CommitmentBonus * Mathf.Clamp01(1f - chosenAge / tuning.CommitmentDecay);

        // "Almost there" is for something it walks TO. The player's focus is the belief, which a search
        // circles at a few metres the whole time: counting that as almost there made a cold search
        // unbeatable by any decoy (case 31).
        if (current.Kind != EKind.Player && current.Distance < tuning.NearDistance) worth += tuning.NearBonus;

        return worth;
    }

    /// <summary>Question 4. The player is always the player; a decoy is itself, and two different
    /// decoys are two things however close (each is broken, habituated to and discarded on its own);
    /// anything else of the same kind within <paramref name="sameSpotDistance"/> is the same place —
    /// an anonymous noise next to a decoy included, which is usually the decoy heard before it was
    /// told apart.</summary>
    public static bool IsSameThing(in Candidate current, in Candidate candidate, float sameSpotDistance)
    {
        if (current.Kind == EKind.None || current.Kind != candidate.Kind) return false;
        if (candidate.Kind == EKind.Player) return true;
        if (candidate.Identity != 0 && current.Identity != 0) return candidate.Identity == current.Identity;

        return (candidate.Position - current.Position).sqrMagnitude < sameSpotDistance * sameSpotDistance;
    }

    /// <summary>The §17.4 question behind a reason, for F9.</summary>
    public static int QuestionOf(EReason reason)
    {
        switch (reason)
        {
            case EReason.SeesPlayer: return 2;
            case EReason.Busy: return 3;
            case EReason.SameThing: return 4;
            case EReason.Discarded: return 5;
            case EReason.SumsWithBelief: return 5;
            case EReason.BelowAttention: return 7;
            case EReason.NotBetterByMargin: return 9;
            case EReason.TooSoon: return 10;
            default: return 11;
        }
    }

    private static Decision Make(EVerdict verdict, EReason reason, float candidateValue, float currentValue) =>
        new Decision { Verdict = verdict, Reason = reason, CandidateValue = candidateValue, CurrentValue = currentValue };
}
