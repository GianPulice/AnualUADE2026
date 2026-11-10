using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// TEMPORARY (deleted at the end of the session): looks at the bathroom key from the room behind its wall.
[InitializeOnLoad]
internal static class TmpKeyWallProbe
{
    private const string Dir = @"C:\Users\IAKI~1\AppData\Local\Temp\claude\af640412bb5abeb3\38e5ec6a-329a-44b0-93c3-75460b37371a\scratchpad\";
    private const string OutPath = Dir + "keywall.txt";
    private const string Flag = "TmpKeyWallProbe.armed";

    // Where the player stands in MANTENIMIENTO, and the yaw it faces (towards the key, -x).
    private static readonly Vector3[] Spots =
    {
        new Vector3(-15.5f, 5.05f, 22.93f), new Vector3(-17.2f, 5.05f, 25.2f), new Vector3(-18.4f, 5.05f, 22.93f),
    };

    private static int step, spot, shot;
    private static float stepAt;
    private static readonly StringBuilder buffer = new StringBuilder();
    private static ItemGlint glint;

    static TmpKeyWallProbe()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) Arm();
    }

    [MenuItem("Tools/Tmp/Key Wall Probe")]
    private static void Run()
    {
        SessionState.SetBool(Flag, true);
        File.WriteAllText(OutPath, "armed\n");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false)) Arm();
        if (change == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Flag, false))
        {
            SessionState.SetBool(Flag, false);
            EditorApplication.update -= Tick;
            File.AppendAllText(OutPath, buffer + "EXIT\n");
            buffer.Clear();
        }
    }

    private static void Arm()
    {
        Application.runInBackground = true;
        step = 0; spot = 0; shot = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Next() { step++; stepAt = Time.time; }
    private static float Elapsed => Time.time - stepAt;

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        PlayerStateManager player = PlayerRegistry.Current;
        if (Time.time > 90f && step < 99) { Done(); return; }

        switch (step)
        {
            case 0:
                if (player == null || LoadingScreen.IsLoading) return;
                Next();
                break;
            case 1:
                if (Elapsed < 2f) return;
                foreach (var view in Object.FindObjectsByType<WakeUpCinematicView>(FindObjectsInactive.Exclude))
                    view.SendMessage("Skip", SendMessageOptions.DontRequireReceiver);
                GameObject key = GameObject.Find("InventoryItemKeyValve");
                glint = key != null ? key.GetComponent<ItemGlint>() : null;
                buffer.AppendLine($"key {(key != null)} glint {(glint != null)} pull {(glint != null ? Field("_pull").GetValue(glint) : "-")}");
                Next();
                break;
            case 2: // stand at the spot, facing the key
                if (Elapsed < 2f) return;
                Vector3 at = Spots[spot];
                Vector3 to = new Vector3(-19.35f, at.y, 22.93f) - at;
                float yaw = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y;
                player.TeleportTo(at, Quaternion.Euler(0f, yaw, 0f));
                Zone2EntryTeleport.PlaceCameraBehind(yaw);
                Next();
                break;
            case 3: // new pull, then the old fixed 0.4
                if (Elapsed < 1.2f) return;
                Flash(shot % 2 == 0 ? (float?)null : 0.4f);
                Next();
                break;
            case 4: // the flash peaks ~0.24 s in
                if (Elapsed < 0.24f) return;
                Camera cam = Camera.main;
                Vector3 screen = cam.WorldToScreenPoint(new Vector3(-19.354f, 6.36f, 22.93f));
                Vector3 c = cam.transform.position;
                RaycastHit hit;
                bool blocked = Physics.Linecast(c, new Vector3(-19.354f, 6.36f, 22.93f), out hit, ~0, QueryTriggerInteraction.Ignore);
                string name = $"keywall_{spot}_{(shot % 2 == 0 ? "new" : "old")}.png";
                buffer.AppendLine($"spot {spot} {(shot % 2 == 0 ? "new pull" : "old 0.4")}: camera {c:F2} key on screen {screen:F0} line blocked by {(blocked ? hit.collider.name : "nothing")} -> {name}");
                ScreenCapture.CaptureScreenshot(Dir + name);
                shot++;
                if (shot % 2 == 1) { step = 3; stepAt = Time.time; return; }
                spot++;
                if (spot >= Spots.Length) { Done(); return; }
                step = 2; stepAt = Time.time;
                break;
        }
    }

    private static FieldInfo Field(string n) => typeof(ItemGlint).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);

    private static void Flash(float? pull)
    {
        if (glint == null) return;
        if (pull.HasValue) Field("_pull").SetValue(glint, pull.Value);
        else Field("_pull").SetValue(glint, 0.155f);
        Field("_nextFlash").SetValue(glint, Time.time);
    }

    private static void Done()
    {
        File.AppendAllText(OutPath, buffer + "DONE\n");
        buffer.Clear();
        step = 99;
        EditorApplication.isPlaying = false;
    }
}
