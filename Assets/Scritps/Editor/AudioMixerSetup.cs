using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Crea el AudioMixer maestro con la jerarquia y los exposed parameters definidos
/// en el Audio System Spec (Master > Music, Ambience, SFX, Player, Nemesis, UI, Voice).
///
/// Idempotente: si el asset ya existe, no lo sobreescribe — solo asegura que los
/// grupos hijos y los exposed parameters esten presentes y bien nombrados.
///
/// Ejecutar desde: Tools/Audio/Create or Update Master Mixer.
/// </summary>
public static class AudioMixerSetup
{
    private const string MixerFolder = "Assets/ScriptableObjects/Audio";
    private const string MixerPath   = MixerFolder + "/MasterMixer.mixer";

    // Hijos directos de Master (7). Total grupos = 8 contando Master.
    private static readonly string[] ChildGroups =
    {
        "Music", "Ambience", "SFX", "Player", "Nemesis", "UI", "Voice"
    };

    [MenuItem("Tools/Audio/Create or Update Master Mixer")]
    public static void CreateOrUpdateMasterMixer()
    {
        EnsureFolder(MixerFolder);

        var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        Object mixerObj;

        if (existing == null)
        {
            mixerObj = CreateMixerAsset(MixerPath);
            if (mixerObj == null)
            {
                Debug.LogError("[AudioMixerSetup] No se pudo crear el AudioMixer (la API interna cambio).");
                return;
            }
            Debug.Log($"[AudioMixerSetup] AudioMixer creado en {MixerPath}.");
        }
        else
        {
            mixerObj = existing;
            Debug.Log($"[AudioMixerSetup] AudioMixer existente reutilizado: {MixerPath}.");
        }

        var mixerType = mixerObj.GetType();
        var masterGroup = mixerType.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?.GetValue(mixerObj);
        if (masterGroup == null)
        {
            Debug.LogError("[AudioMixerSetup] No se pudo obtener masterGroup del controller.");
            return;
        }

        // 1) Asegurar hijos por nombre
        foreach (var name in ChildGroups)
        {
            EnsureChildGroup(mixerObj, masterGroup, name);
        }

        // 2) Exposed parameters para cada grupo + Master
        ExposeVolumeParameter(mixerObj, masterGroup, "MasterVolume");
        foreach (var name in ChildGroups)
        {
            var child = FindChildGroupByName(mixerObj, masterGroup, name);
            if (child != null)
                ExposeVolumeParameter(mixerObj, child, name + "Volume");
        }

        // 3) Forzar el rename de los params (la API interna no expone Rename:
        //    al exponer, Unity asigna nombre por default tipo "MyExposedParam N").
        //    Reescribimos el array exposedParameters con los nombres canonicos.
        RewriteExposedParameterNames(mixerObj, masterGroup);

        EditorUtility.SetDirty(mixerObj);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AudioMixerSetup] Mixer listo con 8 grupos y exposed params: MasterVolume, MusicVolume, AmbienceVolume, SFXVolume, PlayerVolume, NemesisVolume, UIVolume, VoiceVolume.");
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parts = folder.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static Object CreateMixerAsset(string path)
    {
        // La creacion programatica de AudioMixers vive en UnityEditor.Audio.AudioMixerController
        // (clase internal). La API publica oficial no existe.
        var asm = typeof(AudioMixer).Assembly; // UnityEngine
        // El tipo vive en el assembly UnityEditor.
        var editorAsm = typeof(EditorWindow).Assembly;
        var controllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
        if (controllerType == null)
        {
            Debug.LogError("[AudioMixerSetup] No se pudo encontrar UnityEditor.Audio.AudioMixerController.");
            return null;
        }

        // Buscamos el metodo estatico de creacion.
        var createMethod = controllerType.GetMethod(
            "CreateMixerControllerAtPath",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);

        if (createMethod != null)
        {
            return createMethod.Invoke(null, new object[] { path }) as Object;
        }

        // Fallback: crear instancia con CreateInstance y guardar.
        var instance = ScriptableObject.CreateInstance(controllerType);
        if (instance == null) return null;

        // Llamar a SetView/Initialize si existe (algunas versiones lo necesitan).
        var initMethod = controllerType.GetMethod("ClearEventHandlers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        initMethod?.Invoke(instance, null);

        AssetDatabase.CreateAsset(instance, path);
        return instance;
    }

    private static void RewriteExposedParameterNames(Object mixer, object masterGroup)
    {
        var mixerType = mixer.GetType();
        var groupType = masterGroup.GetType();
        var guidMethod = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (guidMethod == null) return;

        var guidToName = new System.Collections.Generic.Dictionary<object, string>();
        guidToName[guidMethod.Invoke(masterGroup, null)] = "MasterVolume";

        var childrenProp = groupType.GetProperty("children", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var children = childrenProp?.GetValue(masterGroup) as System.Collections.IEnumerable;
        if (children != null)
        {
            foreach (var c in children)
            {
                var n = (string)c.GetType().GetProperty("name").GetValue(c);
                guidToName[guidMethod.Invoke(c, null)] = n + "Volume";
            }
        }

        var exposedProp = mixerType.GetProperty("exposedParameters", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var arr = exposedProp?.GetValue(mixer) as System.Array;
        if (arr == null || arr.Length == 0) return;

        var elemType = arr.GetType().GetElementType();
        var newArr = System.Array.CreateInstance(elemType, arr.Length);
        var nameField = elemType.GetField("name");
        var guidField = elemType.GetField("guid");
        for (int i = 0; i < arr.Length; i++)
        {
            var item = arr.GetValue(i);
            var g = guidField.GetValue(item);
            if (guidToName.TryGetValue(g, out var target))
                nameField.SetValue(item, target);
            newArr.SetValue(item, i);
        }
        exposedProp.SetValue(mixer, newArr);
    }

    private static object FindChildGroupByName(Object mixer, object parentGroup, string name)
    {
        var groupType = parentGroup.GetType();
        var childrenProp = groupType.GetProperty("children", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (childrenProp == null) return null;
        var children = childrenProp.GetValue(parentGroup) as System.Collections.IEnumerable;
        if (children == null) return null;
        foreach (var child in children)
        {
            var n = (string)child.GetType().GetProperty("name").GetValue(child);
            if (n == name) return child;
        }
        return null;
    }

    private static void EnsureChildGroup(Object mixer, object parentGroup, string name)
    {
        if (FindChildGroupByName(mixer, parentGroup, name) != null) return;

        var mixerType = mixer.GetType();
        // CreateNewGroup(name) — internal en AudioMixerController.
        var createMethod = mixerType.GetMethod("CreateNewGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string), typeof(bool) }, null);
        object newGroup = null;
        if (createMethod != null)
        {
            newGroup = createMethod.Invoke(mixer, new object[] { name, false });
        }
        else
        {
            createMethod = mixerType.GetMethod("CreateNewGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            if (createMethod != null)
                newGroup = createMethod.Invoke(mixer, new object[] { name });
        }

        if (newGroup == null)
        {
            Debug.LogError($"[AudioMixerSetup] No se pudo crear grupo '{name}': API CreateNewGroup ausente.");
            return;
        }

        // AddChildToParent(newGroup, parent)
        var addMethod = mixerType.GetMethod("AddChildToParent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        addMethod?.Invoke(mixer, new[] { newGroup, parentGroup });

        // Nota: omitido AddGroupToCurrentView — falla si el mixer todavia no tiene una
        // view inicializada. Los grupos quedan accesibles igual; al abrir el mixer en el
        // editor aparecen y se pueden arrastrar a la vista a mano si hace falta.

        // El controller ya marca al nuevo group como sub-asset propio dentro de CreateNewGroup,
        // asi que NO hay que volver a llamar AddObjectToAsset (eso lanza UnityException).
    }

    private static void ExposeVolumeParameter(Object mixer, object group, string exposedName)
    {
        var mixerType = mixer.GetType();
        var groupType = group.GetType();
        var editorAsm = typeof(EditorWindow).Assembly;

        // El parametro de volumen del grupo es group.GetGUIDForVolume() (devuelve GUID).
        var guidMethod = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (guidMethod == null)
        {
            Debug.LogError($"[AudioMixerSetup] GetGUIDForVolume no encontrado para '{exposedName}'.");
            return;
        }
        var guid = guidMethod.Invoke(group, null);

        // ContainsExposedParameter(GUID) — para idempotencia.
        var containsMethod = mixerType.GetMethod("ContainsExposedParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        bool already = false;
        if (containsMethod != null)
        {
            try { already = (bool)containsMethod.Invoke(mixer, new[] { guid }); }
            catch { already = false; }
        }

        if (!already)
        {
            // Unity 6: AddExposedParameter(AudioParameterPath path).
            // Para grupos: AudioGroupParameterPath(AudioMixerGroupController group, GUID parameter).
            var groupPathType = editorAsm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            if (groupPathType == null)
            {
                Debug.LogError($"[AudioMixerSetup] AudioGroupParameterPath type no encontrado.");
                return;
            }

            var ctor = groupPathType.GetConstructor(new[] { groupType, guid.GetType() });
            if (ctor == null)
            {
                Debug.LogError($"[AudioMixerSetup] AudioGroupParameterPath constructor no encontrado.");
                return;
            }
            var pathInstance = ctor.Invoke(new[] { group, guid });

            var addExposedMethod = mixerType.GetMethod("AddExposedParameter",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { groupPathType.BaseType ?? groupPathType }, null);

            if (addExposedMethod == null)
            {
                addExposedMethod = mixerType.GetMethod("AddExposedParameter",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { groupPathType }, null);
            }

            if (addExposedMethod != null)
            {
                addExposedMethod.Invoke(mixer, new[] { pathInstance });
                // Renombrar a nuestra convencion (Unity por default usa el nombre interno del path).
                var renameMethod = mixerType.GetMethod("RenameExposedParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                renameMethod?.Invoke(mixer, new object[] { guid, exposedName });
                return;
            }

            Debug.LogError($"[AudioMixerSetup] AddExposedParameter no encontrado para '{exposedName}'.");
        }
        else
        {
            // Si ya existe, renombrar a nuestra convencion (por si vino con otro nombre).
            var exposedParamsProp = mixerType.GetProperty("exposedParameters", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var exposedArr = exposedParamsProp?.GetValue(mixer) as System.Collections.IEnumerable;
            if (exposedArr != null)
            {
                foreach (var p in exposedArr)
                {
                    var pType = p.GetType();
                    var pGuid = pType.GetField("guid")?.GetValue(p);
                    if (pGuid != null && pGuid.Equals(guid))
                    {
                        var renameMethod = mixerType.GetMethod("RenameExposedParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        renameMethod?.Invoke(mixer, new object[] { guid, exposedName });
                        break;
                    }
                }
            }
        }
    }
}
