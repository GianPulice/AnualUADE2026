using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Receives the <see cref="EscapeBeatMarker"/>s of the PlayableDirector on this same GameObject and
/// hands each to <see cref="EscapeSequenceDirector"/>. Timeline delivers marker notifications to
/// components on the director's own object, and the PlayableDirector of the opening cinematic
/// sits on the same GameObject as the conductor, so this small receiver is what connects them.
///
/// Nothing to configure but the director it reports to.
/// </summary>
[RequireComponent(typeof(PlayableDirector))]
public class EscapeBeatReceiver : MonoBehaviour, INotificationReceiver
{
    [SerializeField] private EscapeSequenceDirector sequence;

    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is EscapeBeatMarker marker && sequence != null)
            sequence.OnBeat(marker.Beat);
    }
}
