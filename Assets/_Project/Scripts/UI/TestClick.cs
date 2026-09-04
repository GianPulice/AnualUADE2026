using UnityEngine;

// Debug-only helper: logs which UI GameObject the mouse is currently over.
// Not part of the shipping build — remove (or wrap in #if UNITY_EDITOR) before release.
// NOTE: throws a NullReferenceException if there is no EventSystem in the scene,
// because EventSystem.current is not null-checked.
public class TestClick : MonoBehaviour
{


    // Update is called once per frame
    void Update()
    {
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("The mouse is over: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject);
        }
    }
}
