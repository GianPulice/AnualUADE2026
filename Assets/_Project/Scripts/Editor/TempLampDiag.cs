using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// TEMP diagnostic — delete after use.
public static class TempLampDiag
{
    [MenuItem("Tools/Temp/Lamp Diag")]
    public static void Run()
    {
        var sb = new StringBuilder("TEMP-LAMP start\n");
        Scene scene = SceneManager.GetSceneByName("WIRED_Zona1_Blockout");
        foreach (GameObject root in scene.GetRootGameObjects()) Walk(root.transform, sb);
        sb.AppendLine("TEMP-LAMP end");
        Debug.Log(sb.ToString());
    }

    static string Lid(Object o)
    {
        if (o == null) return "null";
        try
        {
            GlobalObjectId g = GlobalObjectId.GetGlobalObjectIdSlow(o);
            return $"{g.targetObjectId}/{g.targetPrefabId}";
        }
        catch (System.Exception e) { return "err:" + e.GetType().Name; }
    }

    static void Walk(Transform t, StringBuilder sb)
    {
        bool lamp = t.name == "EscapeLamp";
        bool ceiling = t.name.StartsWith("CeilingLamp_Baked (");
        if (lamp || ceiling)
        {
            Object handle = PrefabUtility.GetPrefabInstanceHandle(t.gameObject);
            string parentInfo = "";
            if (lamp && handle != null)
            {
                var so = new SerializedObject(handle);
                var p = so.FindProperty("m_Modification.m_TransformParent");
                var proxy = p?.objectReferenceValue as Transform;
                parentInfo = proxy == null ? " proxy=NULL"
                    : $" proxy='{proxy.name}' proxyPos=({proxy.position.x:F2},{proxy.position.z:F2}) proxyLid={Lid(proxy)} proxyPI={Lid(PrefabUtility.GetPrefabInstanceHandle(proxy))} proxyParent={(proxy.parent ? proxy.parent.name : "none")}";
            }
            sb.AppendLine($"{(lamp ? "LAMP " : "CEIL ")}{t.name,-24} pos=({t.position.x:F2},{t.position.z:F2}) PI={Lid(handle)} trLid={Lid(t)} inst={t.gameObject.GetInstanceID()}{parentInfo}");
        }
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), sb);
    }
}
