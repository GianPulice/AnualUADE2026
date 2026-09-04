using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Creates the master AudioMixer with the hierarchy and exposed parameters defined
/// in the Audio System Spec (Master > Music, Ambience, SFX, Player, Nemesis, UI, Voice).
///
/// Idempotent: if the asset already exists, it is not overwritten — it only makes sure the
/// child groups and exposed parameters are present and correctly named.
///
/// Run from: Tools/Audio/Create or Update Master Mixer.
/// </summary>
public static class AudioMixerSetup
{
    private const string MixerFolder = "Assets/_Project/ScriptableObjects/Audio";
    private const string MixerPath   = MixerFolder + "/MasterMixer.mixer";

    // Direct children of Master (7). Total groups = 8 counting Master.
    private static readonly string[] ChildGroups =
    {
        "Music", "Ambience", "SFX", "Player", "Nemesis", "UI", "Voice"
    };

    // Grandchildren, keyed by the name of their parent in ChildGroups.
    //
    // The four Ambience sub-groups are what let the ambient layers keep a fixed balance against
    // each other. A child group's volume is an OFFSET that sums in dB with its parent, so
    // "Sub at -12 dB" survives every write to AmbienceVolume — and AudioManager.
    // SetGameplaySfxBundle rewrites AmbienceVolume whenever the player touches the single SFX
    // slider, which is why the ratios cannot live in an exposed parameter.
    //
    // Ambience/Sub also needs a Highpass + Lowpass pair, and per-source DSP only exists at the
    // mixer-group level, so these groups are required rather than merely tidy.
    //
    // Deliberately NOT passed to ExposeVolumeParameter: their attenuation is a fixed design value
    // that is never set from code. Not exposing them sidesteps the SetGameplaySfxBundle problem
    // class entirely, and avoids RewriteExposedParameterNames, which only walks
    // masterGroup.children at depth 1 and would silently skip grandchildren.
    private static readonly (string parent, string[] children)[] SubGroups =
    {
        ("Ambience", new[] { "Bed", "Events", "Texture", "Sub" })
    };

    // WARNING — never put a limiter, compressor or Duck Volume on Master.
    // The ambience system runs a 17 Hz and a 32 Hz drone on Ambience/Sub. An inaudible 17 Hz sine
    // is still a large PEAK signal, so any dynamics processor on Master will duck the entire mix
    // at the sub's LFO rate: the whole game breathes once every 20-60 seconds and the cause is
    // almost impossible to find by ear. If loudness compliance ever demands a limiter, restructure
    // to Master > { Gameplay, Sub } and put it on Gameplay.

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
                Debug.LogError("[AudioMixerSetup] Could not create the AudioMixer (the internal API changed).");
                return;
            }
            Debug.Log($"[AudioMixerSetup] AudioMixer created at {MixerPath}.");
        }
        else
        {
            mixerObj = existing;
            Debug.Log($"[AudioMixerSetup] Reusing existing AudioMixer: {MixerPath}.");
        }

        var mixerType = mixerObj.GetType();
        var masterGroup = mixerType.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?.GetValue(mixerObj);
        if (masterGroup == null)
        {
            Debug.LogError("[AudioMixerSetup] Could not get masterGroup from the controller.");
            return;
        }

        // 1) Ensure the children exist by name
        foreach (var name in ChildGroups)
        {
            EnsureChildGroup(mixerObj, masterGroup, name);
        }

        // 1b) Ensure the grandchildren exist. EnsureChildGroup already takes an arbitrary parent,
        //     so this needs no additional reflection.
        foreach (var (parentName, children) in SubGroups)
        {
            var parent = FindChildGroupByName(mixerObj, masterGroup, parentName);
            if (parent == null)
            {
                Debug.LogError($"[AudioMixerSetup] Parent group '{parentName}' not found; " +
                               $"its sub-groups were skipped.");
                continue;
            }

            foreach (var childName in children)
                EnsureChildGroup(mixerObj, parent, childName);
        }

        // 2) Exposed parameters for each group + Master
        ExposeVolumeParameter(mixerObj, masterGroup, "MasterVolume");
        foreach (var name in ChildGroups)
        {
            var child = FindChildGroupByName(mixerObj, masterGroup, name);
            if (child != null)
                ExposeVolumeParameter(mixerObj, child, name + "Volume");
        }

        // 3) Force the rename of the params (the internal API does not expose Rename:
        //    when exposing, Unity assigns a default name like "MyExposedParam N").
        //    We rewrite the exposedParameters array with the canonical names.
        RewriteExposedParameterNames(mixerObj, masterGroup);

        EditorUtility.SetDirty(mixerObj);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AudioMixerSetup] Mixer ready with 8 groups and exposed params: MasterVolume, " +
                  "MusicVolume, AmbienceVolume, SFXVolume, PlayerVolume, NemesisVolume, UIVolume, " +
                  "VoiceVolume.\n" +
                  "Plus 4 un-exposed Ambience sub-groups: Bed, Events, Texture, Sub.\n" +
                  "MANUAL STEPS REMAINING (the internal API cannot set these):\n" +
                  "  1. Set the sub-group faders: Bed 0 dB, Events -2 dB, Texture -3 dB, Sub -12 dB.\n" +
                  "  2. On Ambience/Sub add a Highpass (cutoff 12 Hz) and a Lowpass (cutoff 120 Hz).\n" +
                  "  3. Save the mixer asset.");
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
        // Programmatic creation of AudioMixers lives in UnityEditor.Audio.AudioMixerController
        // (an internal class). There is no official public API.
        var asm = typeof(AudioMixer).Assembly; // UnityEngine
        // The type lives in the UnityEditor assembly.
        var editorAsm = typeof(EditorWindow).Assembly;
        var controllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
        if (controllerType == null)
        {
            Debug.LogError("[AudioMixerSetup] Could not find UnityEditor.Audio.AudioMixerController.");
            return null;
        }

        // Look up the static creation method.
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

        // Fallback: create the instance with CreateInstance and save it.
        var instance = ScriptableObject.CreateInstance(controllerType);
        if (instance == null) return null;

        // Call SetView/Initialize if it exists (some versions need it).
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
            Debug.LogError($"[AudioMixerSetup] Could not create group '{name}': the CreateNewGroup API is missing.");
            return;
        }

        // AddChildToParent(newGroup, parent)
        var addMethod = mixerType.GetMethod("AddChildToParent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        addMethod?.Invoke(mixer, new[] { newGroup, parentGroup });

        // Note: AddGroupToCurrentView is omitted — it fails if the mixer does not have an
        // initialized view yet. The groups stay accessible anyway; when the mixer is opened in
        // the editor they show up and can be dragged into the view by hand if needed.

        // The controller already marks the new group as its own sub-asset inside CreateNewGroup,
        // so AddObjectToAsset must NOT be called again (that throws a UnityException).
    }

    private static void ExposeVolumeParameter(Object mixer, object group, string exposedName)
    {
        var mixerType = mixer.GetType();
        var groupType = group.GetType();
        var editorAsm = typeof(EditorWindow).Assembly;

        // The group's volume parameter is group.GetGUIDForVolume() (returns a GUID).
        var guidMethod = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (guidMethod == null)
        {
            Debug.LogError($"[AudioMixerSetup] GetGUIDForVolume not found for '{exposedName}'.");
            return;
        }
        var guid = guidMethod.Invoke(group, null);

        // ContainsExposedParameter(GUID) — for idempotency.
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
            // For groups: AudioGroupParameterPath(AudioMixerGroupController group, GUID parameter).
            var groupPathType = editorAsm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            if (groupPathType == null)
            {
                Debug.LogError($"[AudioMixerSetup] AudioGroupParameterPath type not found.");
                return;
            }

            var ctor = groupPathType.GetConstructor(new[] { groupType, guid.GetType() });
            if (ctor == null)
            {
                Debug.LogError($"[AudioMixerSetup] AudioGroupParameterPath constructor not found.");
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
                // Rename to our convention (by default Unity uses the path's internal name).
                var renameMethod = mixerType.GetMethod("RenameExposedParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                renameMethod?.Invoke(mixer, new object[] { guid, exposedName });
                return;
            }

            Debug.LogError($"[AudioMixerSetup] AddExposedParameter not found for '{exposedName}'.");
        }
        else
        {
            // If it already exists, rename to our convention (in case it came with another name).
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
