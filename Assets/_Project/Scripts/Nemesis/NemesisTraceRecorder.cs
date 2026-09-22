using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Writes what the Nemesis was doing, and why, to a CSV file while in Play — so a playtest can be
/// read back afterwards instead of reconstructed from memory.
///
/// WHY IT EXISTS. The F9 HUD answers "why is it doing this" live, but a playtest note says "at some
/// point it ran on the spot" or "it chased me without the red vignette", and by then the frame that
/// explains it is gone. The bugs of the 21/09 pass (WIR-006, WIR-018, WIR-024, WIR-043) all turned
/// out to be about the same few numbers read together — which rung won, what the senses had, whether
/// the path was pending or partial, and how fast the body was really going — so those are logged
/// together, one row every <see cref="SampleInterval"/> and one extra row on every state change.
///
/// ONLY OBSERVES. It reads the facade's public surface and nothing it reads has a side effect: no
/// path queries of its own (the oracle's cache must not be warmed by a logger), no writes to the
/// agent, the FSM or the senses. Editor and development builds only; in a release build it switches
/// itself off in Awake.
///
/// SETUP: none. NemesisStateManager adds it like its other siblings. In the Editor the file goes to
/// the project's Logs/NemesisTrace folder (ignored by git); in a development build, to
/// persistentDataPath/NemesisTrace. The path is printed to the console once per session.
/// </summary>
public class NemesisTraceRecorder : MonoBehaviour
{
    [Tooltip("Apagalo para dejar de escribir el CSV. Solo corre en el editor y en builds de " +
             "desarrollo.")]
    [SerializeField] private bool record = true;

    /// <summary>Seconds between periodic rows. State changes always write a row of their own on top
    /// of these, so this only decides how finely the stretches in between are sampled.</summary>
    private const float SampleInterval = 0.25f;

    /// <summary>Rows buffered before a flush. A state change always flushes, so a crash loses at
    /// most the tail of a quiet stretch.</summary>
    private const int FlushEvery = 20;

    private NemesisStateManager stateManager;
    private StreamWriter writer;
    private readonly StringBuilder line = new StringBuilder(256);

    private float nextSampleAt;
    private int rowsSinceFlush;
    private NemesisStateManager.ENemesisState? lastState;
    private bool wasActive;

    private void Awake()
    {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        enabled = false;
#else
        stateManager = GetComponent<NemesisStateManager>();
        if (stateManager == null) enabled = false;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void LateUpdate()
    {
        if (!record || stateManager == null) return;

        bool active = stateManager.IsActive;
        if (!active)
        {
            if (wasActive) WriteRow("dormant");
            wasActive = false;
            return;
        }

        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        NemesisStateManager.ENemesisState? state = stateManager.CurrentStateKey;
        bool changed = !wasActive || state != lastState;

        wasActive = true;
        lastState = state;

        if (changed)
        {
            WriteRow("state");
            Flush();
            return;
        }

        if (Time.time < nextSampleAt) return;
        WriteRow("sample");
    }

    private void WriteRow(string kind)
    {
        if (writer == null && !TryOpen()) return;

        nextSampleAt = Time.time + SampleInterval;

        NemesisDecision decision = stateManager.Decision;
        NavMeshAgent agent = stateManager.NavAgent;
        Transform player = stateManager.PlayerTransform;
        Vector3 position = transform.position;

        bool hasBelief = stateManager.TryGetBelief(out Vector3 belief, out bool fromSight);
        bool agentReady = stateManager.IsAgentReady;

        line.Clear();
        Append(Time.time);
        Append(kind);
        Append(stateManager.CurrentStateKey?.ToString() ?? "-");
        Append(stateManager.TimeInCurrentState);
        Append(decision != null ? decision.LastRungIndex : -1);
        AppendQuoted(decision != null ? decision.LastReason : "");
        Append(stateManager.CurrentGait.ToString());
        Append(stateManager.HasVisualTarget);
        Append(stateManager.HasAudioTarget);
        Append(stateManager.IsSuspicious);
        Append(stateManager.Awareness);
        Append(stateManager.BeliefAge);
        Append(hasBelief ? (fromSight ? "sight" : "noise") : "-");
        Append(hasBelief ? Vector3.Distance(position, belief) : -1f);
        Append(player != null ? Vector3.Distance(position, player.position) : -1f);
        Append(player != null ? player.position.y - position.y : 0f);
        Append(agentReady);
        Append(agentReady && agent.pathPending);
        Append(agentReady && agent.hasPath);
        Append(agentReady ? agent.pathStatus.ToString() : "-");
        Append(agentReady ? agent.remainingDistance : -1f);
        Append(agentReady ? agent.velocity.magnitude : 0f);
        Append(stateManager.NetFlatSpeed);
        Append(agent != null ? agent.speed : 0f);
        Append(stateManager.IsUsingElevator);
        Append(stateManager.IsChaseStagnant);
        Append(stateManager.StuckRepathCount);
        Append(stateManager.StuckWarpCount);
        Append(position.x);
        Append(position.y);
        Append(position.z, last: true);

        writer.WriteLine(line.ToString());

        if (++rowsSinceFlush >= FlushEvery) Flush();
    }

    private bool TryOpen()
    {
        try
        {
#if UNITY_EDITOR
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "NemesisTrace");
#else
            string folder = Path.Combine(Application.persistentDataPath, "NemesisTrace");
#endif
            Directory.CreateDirectory(folder);

            string file = Path.Combine(folder,
                $"trace_{System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.csv");

            writer = new StreamWriter(file, false, new UTF8Encoding(false));
            writer.WriteLine("time,kind,state,time_in_state,rung,reason,gait,sees,hears,suspicious," +
                             "awareness,belief_age,belief_from,dist_belief,dist_player,player_dy," +
                             "agent_ready,path_pending,has_path,path_status,remaining,agent_speed_now," +
                             "net_flat_speed,agent_speed_cmd,using_lift,chase_stagnant,repaths,warps," +
                             "x,y,z");

            Debug.Log($"[{nameof(NemesisTraceRecorder)}] Recording the Nemesis to {file}", this);
            return true;
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[{nameof(NemesisTraceRecorder)}] Could not open the trace file ({e.Message}). " +
                             "Recording is off for this session.", this);
            record = false;
            return false;
        }
    }

    private void Flush()
    {
        rowsSinceFlush = 0;
        writer?.Flush();
    }

    private void Close()
    {
        if (writer == null) return;

        writer.Flush();
        writer.Dispose();
        writer = null;
    }

    private void OnApplicationQuit() => Close();

    private void OnDestroy() => Close();

    // ── CSV formatting (invariant culture: a comma decimal separator would break the columns) ──

    private void Append(float value, bool last = false)
    {
        if (float.IsInfinity(value)) line.Append("inf");
        else line.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        if (!last) line.Append(',');
    }

    private void Append(int value)
    {
        line.Append(value.ToString(CultureInfo.InvariantCulture));
        line.Append(',');
    }

    private void Append(bool value)
    {
        line.Append(value ? '1' : '0');
        line.Append(',');
    }

    private void Append(string value)
    {
        line.Append(value);
        line.Append(',');
    }

    private void AppendQuoted(string value)
    {
        line.Append('"');
        line.Append((value ?? "").Replace("\"", "\"\""));
        line.Append("\",");
    }
#endif
}
