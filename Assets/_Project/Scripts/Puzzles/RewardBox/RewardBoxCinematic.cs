using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// The screen side of a reward box opening: everything that has to be taken when the shot starts
/// and given back, exactly once, when it ends — the player's control, the camera, the fog's centre,
/// the camera blend and the Nemesis.
///
/// The same shape as <see cref="EscapeSequenceDirector"/>'s Begin/EndCinematic, kept small:
///   • the player is frozen only if it was free, and only that player is released;
///   • <see cref="CinematicState"/> turns look input off and shows the skip prompt;
///   • the shot is an <see cref="EscapeShotCamera"/> (any shot will do: it only goes live and locks
///     on a point), every cut is a hard cut, and the blend comes back two frames later;
///   • the fog is measured from the shot, so the box is seen through the same fog as the gameplay
///     and its light and beacon carry it (WIR-040 for why not from the player);
///   • an active Nemesis is held where it stands (<see cref="NemesisStateManager.SetExternalHold"/>,
///     the Director's staged-entrance hold) with its stuck watchdog off. One that something else is
///     already holding is left alone, so this never releases someone else's hold.
/// </summary>
public sealed class RewardBoxCinematic
{
    private PlayerStateManager lockedPlayer;
    private EscapeShotCamera liveShot;
    private VisionRangeController fog;
    private Transform fogCentre;
    private CinemachineBrain brain;
    private CinemachineBlendDefinition previousBlend;
    private readonly List<NemesisStateManager> heldNemeses = new List<NemesisStateManager>();

    public bool IsPlaying { get; private set; }

    /// <summary>Nothing else owns the screen or the player right now.</summary>
    public static bool ScreenIsFree()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        return player != null &&
               !player.IsDisabled &&
               !CinematicState.IsPlaying &&
               !PauseManager.IsGameplayInputBlocked &&
               !LoadingScreen.IsLoading;
    }

    /// <summary>Takes the screen and goes live on <paramref name="shot"/>, locked on
    /// <paramref name="lookAt"/>. False (and nothing taken) when there is no player or no shot.</summary>
    public bool Begin(EscapeShotCamera shot, Transform lookAt, SO_RewardBoxConfig config)
    {
        if (IsPlaying) return true;

        PlayerStateManager player = PlayerRegistry.Current;
        if (shot == null || player == null || config == null) return false;

        if (!player.IsDisabled)
        {
            player.IsDisabled = true;
            lockedPlayer = player;
        }

        CinematicState.Begin(config.Skippable, config.SkipPromptText);
        HoldNemeses();

        brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain != null)
        {
            previousBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        }

        shot.GoLive(null);
        shot.LockOn(lookAt);
        liveShot = shot;

        fog = Object.FindAnyObjectByType<VisionRangeController>();
        fogCentre = Camera.main != null ? Camera.main.transform : null;
        if (fog != null && fogCentre != null) fog.SetCentreOverride(fogCentre);

        IsPlaying = true;
        return true;
    }

    /// <summary>Gives everything back. <paramref name="host"/> runs the blend restore two frames
    /// later; a host that is being disabled restores it at once.</summary>
    public void End(MonoBehaviour host)
    {
        if (!IsPlaying) return;
        IsPlaying = false;

        if (liveShot != null) liveShot.Release();
        liveShot = null;

        if (fog != null && fogCentre != null) fog.ClearCentreOverride(fogCentre);
        fog = null;
        fogCentre = null;

        ReleaseNemeses();
        CinematicState.End();

        if (lockedPlayer != null) lockedPlayer.IsDisabled = false;
        lockedPlayer = null;

        // Put back after the brain has made the cut back to gameplay, or that cut would blend.
        if (brain != null)
        {
            if (host != null && host.isActiveAndEnabled) host.StartCoroutine(RestoreBlendLater(brain, previousBlend));
            else brain.DefaultBlend = previousBlend;
        }
        brain = null;
    }

    private void HoldNemeses()
    {
        heldNemeses.Clear();
        foreach (NemesisStateManager nemesis in Object.FindObjectsByType<NemesisStateManager>())
        {
            if (!nemesis.IsActive || !nemesis.IsAgentReady || nemesis.NavAgent.isStopped) continue;

            // Standing still with a path reads as wedged to the watchdog, which would repath and
            // then warp it away mid-shot (same reason as the Director's staged entrance).
            nemesis.PushStuckSuppression();
            nemesis.SetExternalHold(true);
            heldNemeses.Add(nemesis);
        }
    }

    private void ReleaseNemeses()
    {
        foreach (NemesisStateManager nemesis in heldNemeses)
        {
            if (nemesis == null) continue;
            nemesis.SetExternalHold(false);
            nemesis.PopStuckSuppression();
        }
        heldNemeses.Clear();
    }

    private static IEnumerator RestoreBlendLater(CinemachineBrain target, CinemachineBlendDefinition blend)
    {
        yield return null;
        yield return null;
        if (target != null) target.DefaultBlend = blend;
    }
}
