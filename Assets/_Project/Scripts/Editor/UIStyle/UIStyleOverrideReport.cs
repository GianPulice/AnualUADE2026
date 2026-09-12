#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lists the instance overrides that scenes and prefabs hold on every prefab a profile styles. An
/// override on a colour, a font or a material wins over the prefab, so a styled prefab can still
/// show its old look in LevelUI — this is where that shows up. Run it before and after a phase.
///
/// Read straight from the YAML, not by opening the scenes: nothing gets loaded, dirtied or saved, and
/// the scene open in the editor is left alone. Placement (position, size, anchors, name, order) is
/// per instance by nature and only counted.
/// </summary>
public static class UIStyleOverrideReport
{
    private static readonly string[] PlacementProperties =
    {
        "m_Name", "m_RootOrder", "m_LocalPosition", "m_LocalRotation", "m_LocalScale", "m_LocalEulerAnglesHint",
        "m_AnchoredPosition", "m_SizeDelta", "m_AnchorMin", "m_AnchorMax", "m_Pivot",
    };

    private static readonly Regex BlockHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Multiline);
    private static readonly Regex SourcePrefab = new Regex(@"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
    private static readonly Regex Modification = new Regex(
        @"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}\s*\n\s*propertyPath: (.*)\n\s*value: (.*)\n\s*objectReference: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?");
    private static readonly Regex NameLine = new Regex(@"^  m_Name: (.*)$", RegexOptions.Multiline);
    private static readonly Regex GameObjectLine = new Regex(@"^  m_GameObject: \{fileID: (-?\d+)\}", RegexOptions.Multiline);

    private static readonly Dictionary<string, Dictionary<string, string>> nodeNames = new Dictionary<string, Dictionary<string, string>>();

    [MenuItem("Tools/UI/Style/Report Scene Overrides", priority = 2)]
    public static void Run()
    {
        nodeNames.Clear();

        Dictionary<string, string> tracked = UIStyleTools.FindAllProfiles()
            .Where(p => p.prefab != null)
            .Select(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p.prefab)))
            .Distinct()
            .ToDictionary(guid => guid, guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));

        IEnumerable<string> files = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project" })
            .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p);

        StringBuilder report = new StringBuilder();
        int instances = 0, styleOverrides = 0;

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            if (!tracked.Keys.Any(text.Contains)) continue;

            foreach (string block in PrefabInstanceBlocks(text))
            {
                Match source = SourcePrefab.Match(block);
                if (!source.Success || !tracked.TryGetValue(source.Groups[1].Value, out string prefabName)) continue;

                instances++;
                List<string> lines = new List<string>();
                int placement = 0;

                foreach (Match m in Modification.Matches(block))
                {
                    string property = m.Groups[3].Value.Trim();
                    if (PlacementProperties.Any(p => property == p || property.StartsWith(p + ".")))
                    {
                        placement++;
                        continue;
                    }

                    string value = m.Groups[5].Value != "0" ? $"→ {{fileID: {m.Groups[5].Value}, guid: {m.Groups[6].Value}}}" : m.Groups[4].Value.Trim();
                    lines.Add($"      {NodeName(m.Groups[2].Value, m.Groups[1].Value)} . {property} = {value}");
                }

                if (!block.Contains("m_AddedComponents: []")) lines.Add("      (has added components)");
                if (!block.Contains("m_AddedGameObjects: []")) lines.Add("      (has added GameObjects)");
                if (!block.Contains("m_RemovedComponents: []")) lines.Add("      (has removed components)");

                styleOverrides += lines.Count;
                report.AppendLine($"  {file} — {prefabName}: {lines.Count} override(s), {placement} placement");
                foreach (string line in lines) report.AppendLine(line);
            }
        }

        Debug.Log($"[UIStyleOverrideReport] {instances} instance(s) of styled prefabs, {styleOverrides} non-placement override(s).\n{report}");
    }

    private static IEnumerable<string> PrefabInstanceBlocks(string text)
    {
        MatchCollection headers = BlockHeader.Matches(text);
        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i].Groups[1].Value != "1001") continue;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            yield return text.Substring(headers[i].Index, end - headers[i].Index);
        }
    }

    /// <summary>The name of the GameObject behind a fileID of the prefab with that guid, or the fileID.</summary>
    private static string NodeName(string guid, string fileId)
    {
        if (!nodeNames.TryGetValue(guid, out Dictionary<string, string> names))
            nodeNames[guid] = names = ReadNodeNames(AssetDatabase.GUIDToAssetPath(guid));

        return names.TryGetValue(fileId, out string name) ? name : $"#{fileId}";
    }

    private static Dictionary<string, string> ReadNodeNames(string path)
    {
        Dictionary<string, string> names = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return names;

        string text = File.ReadAllText(path);
        MatchCollection headers = BlockHeader.Matches(text);
        Dictionary<string, string> owner = new Dictionary<string, string>();

        for (int i = 0; i < headers.Count; i++)
        {
            int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            string block = text.Substring(headers[i].Index, end - headers[i].Index);
            string id = headers[i].Groups[2].Value;

            if (headers[i].Groups[1].Value == "1")
            {
                Match name = NameLine.Match(block);
                if (name.Success) names[id] = name.Groups[1].Value.Trim();
            }
            else
            {
                Match go = GameObjectLine.Match(block);
                if (go.Success) owner[id] = go.Groups[1].Value;
            }
        }

        foreach (KeyValuePair<string, string> pair in owner)
            if (names.TryGetValue(pair.Value, out string name)) names[pair.Key] = name;

        return names;
    }
}
#endif
