using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives every Selectable under this canvas the default hover and click sounds
/// (<see cref="UISelectableSound"/>), inactive children included, so a screen needs one component
/// instead of one per button. Nodes that already have a <see cref="UISelectableSound"/> keep theirs.
///
/// Selectables instantiated later (inventory rows, save slot cards) are not covered on purpose:
/// they play their own sounds from code (<see cref="UISounds"/>).
/// </summary>
public class UICanvasSounds : MonoBehaviour
{
    [Tooltip("Selectables that get the hover sound but no click, because the action they trigger " +
             "plays its own sound (e.g. the discard confirm button).")]
    [SerializeField] private Selectable[] noClick = new Selectable[0];

    private void Awake()
    {
        foreach (Selectable selectable in GetComponentsInChildren<Selectable>(true))
        {
            if (selectable.GetComponent<UISelectableSound>() != null) continue;

            UISelectableSound sound = selectable.gameObject.AddComponent<UISelectableSound>();
            if (System.Array.IndexOf(noClick, selectable) >= 0) sound.Configure(UISounds.Hover, null);
        }
    }
}
