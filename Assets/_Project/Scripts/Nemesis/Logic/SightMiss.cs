/// <summary>
/// Why the eyes do not have the player right now: the one question a playtest note like "it lost me
/// in the open" or "it stood there and did not see me" cannot answer once the frame is gone (plan
/// §19.8).
///
/// DIAGNOSTICS ONLY. FieldOfView fills it in from what its sweep already worked out — the overlap it
/// ran, the cone test of each candidate — so it costs no ray of its own, and nothing decides on it:
/// no rung, no state and no other sense reads it. NemesisTraceRecorder writes it to the CSV.
///
/// PURE: the sweep's findings in, a reason out.
/// </summary>
public static class SightMiss
{
    public enum EReason
    {
        /// <summary>It is seeing the player.</summary>
        None,

        /// <summary>There is no player to look for.</summary>
        NoTarget,

        /// <summary>The player is inside a hiding spot: normal vision does not reach in there.
        /// </summary>
        Hidden,

        /// <summary>Nothing of the player's is inside the range the sweep used. That range already
        /// has the crouch multiplier in it, so "out of range" can mean "crouched".</summary>
        OutOfRange,

        /// <summary>In range, and every sample of them is outside the vision cone.</summary>
        OutOfCone,

        /// <summary>In range and inside the cone, with geometry in between on every sample that was.
        /// </summary>
        Occluded,

        /// <summary>Caught in the outer band only: it feeds the suspicion meter and is not a
        /// sighting yet.</summary>
        Periphery,

        /// <summary>The sighting was dropped by hand rather than lost: the end of a capture
        /// (FieldOfView.ForgetLastKnownPosition).</summary>
        Forgotten,
    }

    /// <summary>
    /// The reason a sweep that had a player out in the open came back without a sighting.
    /// </summary>
    /// <param name="anyInRange">The overlap found something of theirs inside the sweep's range.</param>
    /// <param name="anyInsideCone">At least one sample of a candidate was inside the vision cone
    /// (and so had its occlusion ray cast).</param>
    /// <param name="anyInPeriphery">A candidate got through, in the outer band only.</param>
    public static EReason Classify(bool anyInRange, bool anyInsideCone, bool anyInPeriphery)
    {
        // Seen, just not enough: that says more than anything the candidates that failed could.
        if (anyInPeriphery) return EReason.Periphery;

        if (!anyInRange) return EReason.OutOfRange;

        return anyInsideCone ? EReason.Occluded : EReason.OutOfCone;
    }

    /// <summary>The short lower-case name the trace writes. "-" for None, like its other empty
    /// columns.</summary>
    public static string Token(EReason reason) => reason switch
    {
        EReason.NoTarget => "no_player",
        EReason.Hidden => "hidden",
        EReason.OutOfRange => "range",
        EReason.OutOfCone => "cone",
        EReason.Occluded => "occluded",
        EReason.Periphery => "periphery",
        EReason.Forgotten => "forgotten",
        _ => "-",
    };
}
