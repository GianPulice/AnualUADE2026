using UnityEngine;

public class TestClick : MonoBehaviour
{
   

    // Update is called once per frame
    void Update()
    {
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("El mouse está sobre: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject);
        }
    }
}
