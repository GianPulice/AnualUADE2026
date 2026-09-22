using UnityEngine;

// Debug-only helper: logs which UI GameObject the mouse is currently over. The class stays so the
// (disabled) object in LevelUI does not come up as a missing script, but nothing runs outside the
// editor — delete the object and this file once nobody needs the log.
public class TestClick : MonoBehaviour
{
#if UNITY_EDITOR
    // NOTE: throws a NullReferenceException if there is no EventSystem in the scene,
    // because EventSystem.current is not null-checked.
    private void Update()
    {
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("The mouse is over: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject);
        }
    }
#endif
}
