using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Reads, writes and deletes the save slots on disk: one JSON file per slot in
/// <c>Application.persistentDataPath/Saves/slot_NN.json</c>. Its one job — what goes in a save and
/// when is <see cref="SaveGame"/>'s.
///
/// Writes are atomic: the JSON goes to a <c>.tmp</c> file first and only replaces the real file once
/// it is complete, so closing the game (or a crash) mid-write leaves the previous save intact instead
/// of a half-written one. A leftover <c>.tmp</c> is never read.
///
/// Reads never throw: a missing, unreadable, corrupt or newer-version file reads as "no save" (and
/// says why in the console), so the menu shows that slot as empty instead of breaking.
/// </summary>
public static class SaveFileStore
{
    private const string FolderName = "Saves";
    private const string TempSuffix = ".tmp";

    /// <summary>Slots are 1-based, like the cards. Anything outside this range is refused.</summary>
    public const int MaxSlotIndex = 99;

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

    public static bool IsValidSlot(int slotIndex) => slotIndex >= 1 && slotIndex <= MaxSlotIndex;

    public static string PathFor(int slotIndex) => Path.Combine(Folder, $"slot_{slotIndex:00}.json");

    public static bool Write(int slotIndex, SaveData data)
    {
        if (!IsValidSlot(slotIndex) || data == null)
        {
            Debug.LogError($"[{nameof(SaveFileStore)}] Write refused: slot {slotIndex}, data {(data == null ? "null" : "ok")}.");
            return false;
        }

        string path = PathFor(slotIndex);
        string tempPath = path + TempSuffix;

        try
        {
            Directory.CreateDirectory(Folder);

            data.version = SaveData.CurrentVersion;
            data.slotIndex = slotIndex;
            File.WriteAllText(tempPath, JsonUtility.ToJson(data, true), Utf8NoBom);

            ReplaceWithTemp(tempPath, path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[{nameof(SaveFileStore)}] Could not write slot {slotIndex} to '{path}': {e.Message}");
            TryDelete(tempPath);
            return false;
        }
    }

    /// <returns>true only for a complete, valid save of this slot.</returns>
    public static bool TryRead(int slotIndex, out SaveData data)
    {
        data = null;
        if (!IsValidSlot(slotIndex)) return false;

        string path = PathFor(slotIndex);
        if (!File.Exists(path)) return false;

        try
        {
            SaveData read = JsonUtility.FromJson<SaveData>(File.ReadAllText(path, Utf8NoBom));
            string problem = Validate(read, slotIndex);
            if (problem != null)
            {
                Debug.LogWarning($"[{nameof(SaveFileStore)}] Slot {slotIndex} ignored ({problem}): '{path}'.");
                return false;
            }

            data = read;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{nameof(SaveFileStore)}] Slot {slotIndex} could not be read ('{path}'): {e.Message}");
            return false;
        }
    }

    /// <returns>false only when a file was there and could not be deleted.</returns>
    public static bool Delete(int slotIndex)
    {
        if (!IsValidSlot(slotIndex)) return false;

        string path = PathFor(slotIndex);
        bool ok = TryDelete(path);
        TryDelete(path + TempSuffix);
        return ok;
    }

    private static string Validate(SaveData data, int slotIndex)
    {
        if (data == null) return "empty file";
        if (data.version < 1 || data.version > SaveData.CurrentVersion) return $"unsupported version {data.version}";
        if (data.slotIndex != slotIndex) return $"belongs to slot {data.slotIndex}";
        if (string.IsNullOrWhiteSpace(data.levelId)) return "no level";
        if (string.IsNullOrWhiteSpace(data.sceneGroupLabel)) return "no scene group";

        data.modules ??= new System.Collections.Generic.List<SavedModule>();
        data.puzzles ??= new SavedPuzzleState();
        return null;
    }

    private static void ReplaceWithTemp(string tempPath, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(tempPath, path);
            return;
        }

        try
        {
            File.Replace(tempPath, path, null);
        }
        catch (Exception e) when (e is PlatformNotSupportedException || e is IOException)
        {
            // Some file systems have no atomic replace. The temp file is complete at this point,
            // so the window where neither file is the save is a single delete + rename.
            File.Delete(path);
            File.Move(tempPath, path);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[{nameof(SaveFileStore)}] Could not delete '{path}': {e.Message}");
            return false;
        }
    }
}
