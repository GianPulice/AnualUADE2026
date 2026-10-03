#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// TEMPORARY - measures where the Nemesis's eyes land in the escape's eyes shot (Cam_Eyes). Starts only
// when Temp/EyesShotProbe/run.flag exists. Deleted when done.
public class TmpEyesShotProbe : MonoBehaviour
{
    private static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "EyesShotProbe"));
    private static readonly StringBuilder Log = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string flag = Path.Combine(Dir, "run.flag");
        if (!File.Exists(flag)) return;
        File.Delete(flag);

        var go = new GameObject("TmpEyesShotProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<TmpEyesShotProbe>().Run().Forget();
    }

    private static void L(string s)
    {
        Log.AppendLine($"[f{Time.frameCount} t{Time.time:F2}] {s}");
        File.WriteAllText(Path.Combine(Dir, "log.txt"), Log.ToString());
    }

    private async UniTaskVoid Run()
    {
        Application.runInBackground = true;
        try
        {
            L($"probe start, runInBackground={Application.runInBackground}");

            EscapeSequenceDirector director = null;
            for (int i = 0; i < 1800; i++)
            {
                director = FindAnyObjectByType<EscapeSequenceDirector>();
                if (director != null && PlayerRegistry.Current != null) break;
                await UniTask.Yield();
            }
            if (director == null || PlayerRegistry.Current == null) { L("NO director/player"); return; }
            await UniTask.Delay(1500);

            EscapeShotCamera eyes = FindObjectsByType<EscapeShotCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name.StartsWith("Cam_Eyes"));
            if (eyes == null) { L("NO Cam_Eyes"); return; }
            CinemachineCamera cc = eyes.GetComponent<CinemachineCamera>();
            Quaternion restRot = eyes.transform.rotation;
            Vector3 restPos = eyes.transform.position;
            L($"Cam_Eyes rest pos {restPos} rot {restRot.eulerAngles} fov {cc.Lens.FieldOfView} (vertical)");

            // Player into the reveal trigger, as a real run would have walked there.
            var stageField = typeof(EscapeSequenceDirector).GetField("stage", BindingFlags.NonPublic | BindingFlags.Instance);
            object stage = stageField.GetValue(director);
            Transform mark = (Transform)stage.GetType().GetField("playerCorridorMark").GetValue(stage);

            director.StartForTest();
            await UniTask.Delay(800);
            PlayerStateManager player = PlayerRegistry.Current;
            player.TeleportTo(mark.position, player.transform.rotation);
            L($"teleported player to corridor mark {mark.position}");

            NemesisCinematicActor actor = FindAnyObjectByType<NemesisCinematicActor>();
            CinemachineBrain brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
            Camera outCam = brain != null ? brain.OutputCamera : Camera.main;

            bool shotSeen = false, shot1 = false, shot2 = false;
            float liveSince = 0f;
            for (int i = 0; i < 3600; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);

                if (eyes.IsLive)
                {
                    if (!shotSeen) { shotSeen = true; liveSince = Time.time; L("Cam_Eyes LIVE"); }

                    Transform point = actor != null ? actor.EyesPoint : null;
                    if (point != null && outCam != null)
                    {
                        Vector3 vp = outCam.WorldToViewportPoint(point.position);

                        // Where the SCENE framing would have put it: elevation off the rest axis, degrees.
                        Vector3 local = Quaternion.Inverse(restRot) * (point.position - restPos);
                        float elev = Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
                        float azim = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

                        if ((Time.frameCount % 15) == 0)
                            L($"eyes world {point.position} | vp now ({vp.x:F3},{vp.y:F3}) | rest framing offset: up {elev:F2} deg, right {azim:F2} deg (half fov 5) | cam rot {eyes.transform.rotation.eulerAngles} | out cam fov {outCam.fieldOfView:F1}");
                    }

                    if (!shot1 && Time.time - liveSince > 0.4f)
                    {
                        shot1 = true;
                        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "eyes_shot.png"));
                        L("screenshot eyes_shot.png");
                    }
                }
                else if (shotSeen)
                {
                    L("Cam_Eyes stepped down");
                    break;
                }

                if (i == 3599) L("TIMEOUT waiting for the eyes shot");
            }
            _ = shot2;
        }
        catch (System.Exception e)
        {
            L("EXCEPTION " + e);
        }
        finally
        {
            L("done, leaving play");
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
