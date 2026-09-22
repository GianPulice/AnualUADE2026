using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// A marker on the Timeline's marker track: "at this moment, do this <see cref="EscapeBeat"/>".
/// Drag it along the track to retime the action, change the dropdown to swap it, delete it to
/// drop that action from the cinematic.
///
/// <see cref="EscapeSequenceDirector"/> sits on the PlayableDirector's own GameObject, so it is the
/// receiver of every marker on the root marker track.
///
/// Retroactive on purpose: when the player skips, the playhead JUMPS to the end, and a marker the
/// jump passes over must still fire — otherwise a skip would leave the corridor doors unlocked or
/// the Nemesis in the wrong place. TriggerOnce keeps a marker from firing twice when the director
/// is evaluated again after the jump.
///
/// Add one with right click ▸ Add Escape Beat on the marker track.
/// </summary>
[DisplayName("Escape Beat")]
public class EscapeBeatMarker : Marker, INotification, INotificationOptionProvider
{
    [Tooltip("The action this marker triggers.")]
    [SerializeField] private EscapeBeat beat = EscapeBeat.AlarmStart;

    public EscapeBeat Beat
    {
        get => beat;
        set => beat = value;
    }

    // INotification: the id only has to be stable, and the receiver reads the marker itself.
    public PropertyName id => new PropertyName(beat.ToString());

    // INotificationOptionProvider
    public NotificationFlags flags => NotificationFlags.Retroactive | NotificationFlags.TriggerOnce;
}
