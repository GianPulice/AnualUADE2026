using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The last beat of the escape: the gate falls shut in the Nemesis's face, in a burst of dust. One
/// job: the fall and its impact — the gate's drop (<see cref="PuzzleGate.SlamShutAsync"/>), the dust
/// thrown off the floor, the sound. WHEN it falls, and what the camera does with the hit, is
/// <see cref="EscapeSequenceDirector"/>'s.
///
/// Sits on the dust object at the foot of the gate. The Particle Systems under it ARE the dust
/// (sized to the gate: a sheet of dust rolling out to each side, and a spray of chips): they play
/// once, on the impact. Tune them on the children like any Particle System.
/// </summary>
public class EscapeGateSlam : MonoBehaviour
{
    [Tooltip("El portón que cae (el del final del escape).")]
    [SerializeField] private PuzzleGate gate;

    [Tooltip("Los sistemas de partículas del polvo. Vacío = todos los que haya debajo de este objeto.")]
    [SerializeField] private ParticleSystem[] dust = Array.Empty<ParticleSystem>();

    /// <summary>The gate hit the floor (the dust is up).</summary>
    public bool HasSlammed { get; private set; }

    /// <summary>Drops the gate and, when it hits, raises the dust and plays the sound. Completes on
    /// the impact frame.</summary>
    public async UniTask SlamAsync(float seconds, string impactSoundId, CancellationToken token)
    {
        if (gate != null) await gate.SlamShutAsync(seconds, token);
        Impact(impactSoundId);
    }

    /// <summary>Shut at once, dust and sound included: what a skip needs.</summary>
    public void SlamNow(string impactSoundId)
    {
        // Zero seconds: SlamShutAsync closes on its first line and never waits.
        if (gate != null) gate.SlamShutAsync(0f, this.GetCancellationTokenOnDestroy()).Forget();
        Impact(impactSoundId);
    }

    /// <summary>Back to before the slam, dust gone (the test key replays the escape).</summary>
    public void ResetDust()
    {
        HasSlammed = false;
        foreach (ParticleSystem ps in Systems())
            if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Impact(string soundId)
    {
        HasSlammed = true;

        // One at a time, not withChildren: the list may hold a parent and its own children.
        foreach (ParticleSystem ps in Systems())
        {
            if (ps == null) continue;
            ps.Clear(false);
            ps.Play(false);
        }

        if (!string.IsNullOrWhiteSpace(soundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(soundId, transform.position);
    }

    private ParticleSystem[] Systems() =>
        dust != null && dust.Length > 0 ? dust : GetComponentsInChildren<ParticleSystem>(true);
}
