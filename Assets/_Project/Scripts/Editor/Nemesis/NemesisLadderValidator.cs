using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using State = NemesisStateManager.ENemesisState;

/// <summary>
/// Two checks on the Nemesis's priority ladder that need neither a scene nor Play mode (plan §19.4,
/// etapa A4). Both exist because every fix to the ladder so far was verified by playing, and the
/// bugs it has are the kind a playtest reports as "it stood there twitching".
///
/// PARITY. A rung lives in two places: <see cref="SO_NemesisPriorities"/>'s asset, which is what
/// runs, and <c>BuildDefaultLadder()</c>, which is what runs with no asset and what "Restaurar la
/// escalera por defecto" writes back (plan §10). One edited without the other is a ladder that
/// changes the day somebody presses that button. This compares them rung by rung.
///
/// REPLAY. Walks the real rungs over a scripted world — "it is standing where it last saw the
/// player and the sighting comes and goes every sweep" — and looks at the states that come out. It
/// is not the game: the predicates are the script's, not the sensors'. What it does test is the
/// ladder's own logic, which is where two rungs handing the Nemesis back and forth comes from. The
/// hysteresis and the thresholds are <see cref="NemesisDecision"/>'s own (IsInsideDwell, IsHeldBack,
/// ResolveThreshold), so the replay cannot drift from the voter on those.
///
/// Three readings are not flags and the replay keeps them itself, the way the game does: the time in
/// the state, the ages of the last sense and the last sighting, and the belief's radius (a sighting
/// pins it, a noise sets it to how well it was heard, time grows it; the merging of evidence that
/// NemesisBelief also does is left out). A scenario about the route verdict hands in the raw answer
/// of each path query and gets it settled by the oracle's own rule (<see cref="SettledVerdict"/>).
///
/// A scenario that fails today and is already in the plan carries a KnownIssue: it is reported as a
/// note instead of a problem, and turns into a problem the day it passes, so the mark gets removed
/// with the fix.
///
/// Menu: Tools > Nemesis > Validate Ladder. Also part of Validate Navigation Setup.
/// </summary>
public static class NemesisLadderValidator
{
    /// <summary>The replay's frame: fifty a second, finer than the sensors' 0.1 s sweep.</summary>
    private const float Step = 0.02f;

    /// <summary>The dwell <see cref="NemesisDecision"/> falls back to with no priorities asset.</summary>
    private const float FallbackDwell = 0.35f;

    /// <summary>The radius a noise leaves the belief at unless a scenario says otherwise: a footstep
    /// heard in the open about five metres off, on the shipped numbers.</summary>
    private const float DefaultHeardRadius = 1.75f;

    [MenuItem("Tools/Nemesis/Validate Ladder")]
    private static void ValidateFromMenu()
    {
        StringBuilder report = new StringBuilder();
        int problems = Validate(report);

        if (problems == 0)
        {
            Debug.Log("[NemesisLadderValidator] All good: the asset matches BuildDefaultLadder() and " +
                      "every replay scenario behaves as expected." +
                      (report.Length > 0 ? $"\n\n{report}" : ""));
            return;
        }

        Debug.LogWarning($"[NemesisLadderValidator] {problems} problem(s):\n\n{report}");
    }

    /// <summary>
    /// Runs both checks on every priorities asset in the project (or on the code's ladder when there
    /// is none) and appends what it finds to <paramref name="report"/>.
    /// </summary>
    /// <returns>How many problems. Notes do not count.</returns>
    public static int Validate(StringBuilder report)
    {
        SO_NemesisData data = FirstAsset<SO_NemesisData>();
        List<SO_NemesisPriorities> assets = AllAssets<SO_NemesisPriorities>();
        int problems = 0;

        if (assets.Count == 0)
        {
            report.AppendLine("- Note: there is no SO_NemesisPriorities asset, so the Nemesis runs on " +
                              "BuildDefaultLadder(). Replaying that one.");
            return ReplayAll(report, "BuildDefaultLadder()", SO_NemesisPriorities.BuildDefaultLadder(),
                             FallbackDwell, data);
        }

        foreach (SO_NemesisPriorities asset in assets)
        {
            problems += ValidateParity(report, asset);
            problems += ReplayAll(report, asset.name, asset.Rungs, asset.MinimumStateDwell, data);
        }

        return problems;
    }

    // ── Parity ───────────────────────────────────────────

    private static int ValidateParity(StringBuilder report, SO_NemesisPriorities asset)
    {
        IReadOnlyList<NemesisPriorityRung> saved = asset.Rungs;
        List<NemesisPriorityRung> shipped = SO_NemesisPriorities.BuildDefaultLadder();
        int problems = 0;

        if (saved.Count != shipped.Count)
        {
            report.AppendLine($"- {asset.name}: {saved.Count} rungs in the asset, {shipped.Count} in " +
                              "BuildDefaultLadder().");
            problems++;
        }

        int shared = Mathf.Min(saved.Count, shipped.Count);
        for (int i = 0; i < shared; i++)
        {
            string difference = Difference(saved[i], shipped[i]);
            if (difference == null) continue;

            report.AppendLine($"- {asset.name}, rung {i} (\"{shipped[i].note}\"): {difference}");
            problems++;
        }

        if (problems > 0)
        {
            report.AppendLine("  A rung goes in the asset AND in BuildDefaultLadder() (plan §10). \"Restaurar " +
                              "la escalera por defecto\" in the asset's inspector rewrites the asset from the " +
                              "code; the other way round is by hand.");
        }

        return problems;
    }

    /// <summary>What differs between a rung of the asset and the same rung in code, or null.</summary>
    private static string Difference(NemesisPriorityRung saved, NemesisPriorityRung shipped)
    {
        if (saved == null) return "the asset has an empty rung here.";
        if (!saved.enabled) return "it is switched off in the asset.";
        if (saved.target != shipped.target) return $"the asset asks for {saved.target}, the code for {shipped.target}.";
        if (saved.interrupts != shipped.interrupts) return $"'interrupts' is {saved.interrupts} in the asset and {shipped.interrupts} in code.";
        if (saved.note != shipped.note) return $"the asset's note is \"{saved.note}\".";

        int savedCount = saved.conditions != null ? saved.conditions.Count : 0;
        int shippedCount = shipped.conditions != null ? shipped.conditions.Count : 0;
        if (savedCount != shippedCount) return $"{savedCount} conditions in the asset, {shippedCount} in code.";

        for (int i = 0; i < savedCount; i++)
        {
            NemesisCondition a = saved.conditions[i];
            NemesisCondition b = shipped.conditions[i];

            bool same = a.predicate == b.predicate && a.negate == b.negate &&
                        (!b.UsesState || a.state == b.state) &&
                        (!b.UsesThreshold || (a.threshold == b.threshold &&
                                              (b.threshold != ENemesisThreshold.Custom ||
                                               Mathf.Approximately(a.customSeconds, b.customSeconds))));
            if (!same) return $"condition {i} is \"{Describe(a)}\" in the asset and \"{Describe(b)}\" in code.";
        }

        return null;
    }

    private static string Describe(NemesisCondition condition)
    {
        string text = condition.predicate.ToString();

        if (condition.UsesState) text += $"({condition.state})";
        if (condition.UsesThreshold)
        {
            text += condition.threshold == ENemesisThreshold.Custom
                ? $"({Seconds(condition.customSeconds)} s)"
                : $"({condition.threshold})";
        }

        return condition.negate ? "NOT " + text : text;
    }

    // ── Replay ───────────────────────────────────────────

    /// <summary>What the senses say on one frame of a scenario. Everything is false until the
    /// script says otherwise, every frame.</summary>
    private sealed class World
    {
        private readonly bool[] flags = new bool[Enum.GetValues(typeof(ENemesisPredicate)).Length];

        public bool this[ENemesisPredicate predicate]
        {
            get => flags[(int)predicate];
            set => flags[(int)predicate] = value;
        }

        /// <summary>Seconds since it last sensed the player, when the script wants to say so itself.
        /// Left null it is counted: zero while it sees or hears them, running on otherwise.</summary>
        public float? BeliefAge;

        /// <summary>How well a noise of the player's is heard while HearsPlayer is on, in metres: the
        /// radius it leaves the belief at (NemesisBelief.NoiseRadiusFor).</summary>
        public float HeardRadius = DefaultHeardRadius;

        /// <summary>The raw answer of this frame's path query to the belief ("it does not get
        /// there"), for a scenario about the route verdict. The replay settles it as the oracle does
        /// and IsBeliefUnreachable reads the result. Left null, the script sets the predicate
        /// itself.</summary>
        public bool? RouteFails;

        public void Clear()
        {
            Array.Clear(flags, 0, flags.Length);
            BeliefAge = null;
            HeardRadius = DefaultHeardRadius;
            RouteFails = null;
        }
    }

    private sealed class Scenario
    {
        public string Name;
        public State Start;

        /// <summary>How long it had been in <see cref="Start"/> already: past the dwell, so the
        /// window is not what the scenario is about unless it means to be.</summary>
        public float StartedAgo = 2f;

        public float Seconds = 4f;

        /// <summary>Seconds since the eyes last had the player when the scenario starts. 0: it was
        /// looking at them the frame before. Infinity: it has not seen anyone.</summary>
        public float LastSeenAgo;

        /// <summary>The world at a given time, in seconds from the start.</summary>
        public Action<float, World> Script;

        /// <summary>Null when the run is what it should be; otherwise what is wrong with it.</summary>
        public Func<Run, string> Check;

        /// <summary>Set while the scenario is expected to fail: which plan item fixes it.</summary>
        public string KnownIssue;
    }

    private sealed class Run
    {
        public readonly List<(float time, State was, State now, string note)> Transitions =
            new List<(float, State, State, string)>();

        public State Final;
        public float Length;

        public bool Entered(State state)
        {
            foreach (var transition in Transitions)
            {
                if (transition.now == state) return true;
            }

            return false;
        }

        /// <summary>The shortest finished visit to a state, in seconds; infinity if it never left it
        /// (or never went).</summary>
        public float ShortestVisit(State state)
        {
            float shortest = float.PositiveInfinity;

            for (int i = 0; i < Transitions.Count; i++)
            {
                if (Transitions[i].now != state) continue;
                if (i + 1 >= Transitions.Count) continue;

                shortest = Mathf.Min(shortest, Transitions[i + 1].time - Transitions[i].time);
            }

            return shortest;
        }

        public string Describe(int max = 6)
        {
            if (Transitions.Count == 0) return "no transitions";

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < Transitions.Count && i < max; i++)
            {
                var t = Transitions[i];
                text.Append($"{Seconds(t.time)} s {t.was}→{t.now} (\"{t.note}\"); ");
            }

            if (Transitions.Count > max) text.Append($"… {Transitions.Count} in {Seconds(Length)} s");
            return text.ToString();
        }
    }

    private static int ReplayAll(StringBuilder report, string ladderName, IReadOnlyList<NemesisPriorityRung> ladder,
                                 float dwell, SO_NemesisData data)
    {
        int problems = 0;

        foreach (Scenario scenario in Scenarios())
        {
            Run run = Replay(ladder, dwell, data, scenario);
            string wrong = scenario.Check(run);

            if (wrong == null && scenario.KnownIssue == null) continue;

            if (wrong != null && scenario.KnownIssue != null)
            {
                report.AppendLine($"- Note, known ({scenario.KnownIssue}): {ladderName}, \"{scenario.Name}\": " +
                                  $"{wrong} [{run.Describe()}]");
                continue;
            }

            if (wrong == null)
            {
                report.AppendLine($"- {ladderName}, \"{scenario.Name}\": passes now. Take its KnownIssue " +
                                  $"(\"{scenario.KnownIssue}\") off in NemesisLadderValidator.Scenarios().");
            }
            else
            {
                report.AppendLine($"- {ladderName}, \"{scenario.Name}\": {wrong} [{run.Describe()}]");
            }

            problems++;
        }

        return problems;
    }

    /// <summary>
    /// One scenario, frame by frame: the script says what the predicates are, the first rung that
    /// holds decides, and a different answer is a transition that restarts the state's clock —
    /// the three things the FSM does around <see cref="NemesisDecision"/>.
    /// </summary>
    private static Run Replay(IReadOnlyList<NemesisPriorityRung> ladder, float dwell, SO_NemesisData data,
                              Scenario scenario)
    {
        Run run = new Run();
        World world = new World();

        State current = scenario.Start;
        float timeInState = scenario.StartedAgo;
        float beliefAge = 0f;

        float sightRadius = data != null ? data.BeliefSightRadius : 0.5f;
        float growth = data != null ? data.BeliefGrowthSpeed : 4.5f;
        float settleTime = data != null ? data.RouteVerdictSettleTime : 0.75f;
        float verdictInterval = data != null ? data.RouteVerdictInterval : 0.4f;

        float sightAge = scenario.LastSeenAgo;
        float beliefRadius = float.IsInfinity(sightAge) ? DefaultHeardRadius : sightRadius + growth * sightAge;
        SettledVerdict routeFails = new SettledVerdict();

        for (float time = 0f; time < scenario.Seconds; time += Step)
        {
            world.Clear();
            scenario.Script(time, world);

            if (world.BeliefAge.HasValue) beliefAge = world.BeliefAge.Value;
            else if (world[ENemesisPredicate.SeesPlayer] || world[ENemesisPredicate.HearsPlayer]) beliefAge = 0f;
            else beliefAge += Step;

            bool sees = world[ENemesisPredicate.SeesPlayer];
            sightAge = sees ? 0f : sightAge + Step;

            if (sees) beliefRadius = sightRadius;
            else if (world[ENemesisPredicate.HearsPlayer]) beliefRadius = world.HeardRadius;
            else beliefRadius += growth * Step;

            if (world.RouteFails.HasValue)
            {
                world[ENemesisPredicate.IsBeliefUnreachable] = routeFails.Step(
                    time, world.RouteFails.Value, settleTime, Mathf.Max(settleTime, verdictInterval));
            }

            bool holding = NemesisDecision.IsInsideDwell(current, dwell, timeInState);
            State decided = current;
            string note = null;

            for (int i = 0; i < ladder.Count; i++)
            {
                NemesisPriorityRung rung = ladder[i];
                if (rung == null || !rung.enabled) continue;
                if (NemesisDecision.IsHeldBack(holding, rung, current)) continue;
                if (!Holds(rung, world, current, timeInState, beliefAge, sightAge, beliefRadius, data)) continue;

                decided = rung.target;
                note = rung.note;
                break;
            }

            if (decided != current)
            {
                run.Transitions.Add((time, current, decided, note));
                current = decided;
                timeInState = 0f;
            }
            else
            {
                timeInState += Step;
            }
        }

        run.Final = current;
        run.Length = scenario.Seconds;
        return run;
    }

    private static bool Holds(NemesisPriorityRung rung, World world, State current, float timeInState,
                              float beliefAge, float sightAge, float beliefRadius, SO_NemesisData data)
    {
        if (rung.conditions == null) return true;

        foreach (NemesisCondition condition in rung.conditions)
        {
            bool value = condition.predicate switch
            {
                ENemesisPredicate.IsInState => current == condition.state,
                ENemesisPredicate.BeliefAgeUnder => beliefAge < NemesisDecision.ResolveThreshold(condition, data),
                ENemesisPredicate.TimeInStateUnder => timeInState < NemesisDecision.ResolveThreshold(condition, data),
                ENemesisPredicate.SightAgeUnder => sightAge < NemesisDecision.ResolveThreshold(condition, data),
                ENemesisPredicate.BeliefRadiusUnder => beliefRadius < NemesisDecision.ResolveThreshold(condition, data),
                _ => world[condition.predicate],
            };

            if (value == condition.negate) return false;
        }

        return true;
    }

    // ── Scenarios ────────────────────────────────────────

    /// <summary>True for the first <paramref name="on"/> seconds of every <paramref name="period"/>.
    /// </summary>
    private static bool Pulse(float time, float period, float on) => time % period < on;

    /// <summary>Seconds for the report, with a decimal point whatever the machine's culture: the
    /// report gets pasted into notes next to the trace, which writes them that way.</summary>
    private static string Seconds(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IEnumerable<Scenario> Scenarios()
    {
        yield return new Scenario
        {
            Name = "a chase in plain view stays a chase",
            Start = State.Chasing,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.SeesPlayer] = true;
                world[ENemesisPredicate.HasBelief] = true;
            },
            Check = run => run.Transitions.Count == 0 ? null : "it left Chasing with the player in view.",
        };

        // The original reason the ladder has a dwell window, kept as a tripwire: a player at the very
        // edge of its hearing must send it walking over once, not make it change its mind per sweep.
        yield return new Scenario
        {
            Name = "hearing that comes and goes sends it to look, once",
            Start = State.Patrolling,
            LastSeenAgo = float.PositiveInfinity,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.HearsPlayer] = Pulse(time, 0.2f, 0.1f);
                world[ENemesisPredicate.HasBelief] = true;
                world[ENemesisPredicate.IsInvestigationWarm] = true;
            },
            Check = run => run.Transitions.Count == 1 && run.Final == State.Investigating
                ? null
                : "it should walk to the noise and stay on that walk.",
        };

        // Plan §19.4, T2. Standing where it last saw the player, one missed sweep used to end the
        // chase and the next one start it again. "todavía sabe dónde está" holds it (etapa B1).
        yield return new Scenario
        {
            Name = "at the last seen point, a sighting that comes and goes every sweep",
            Start = State.Chasing,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.SeesPlayer] = Pulse(time, 0.2f, 0.1f);
                world[ENemesisPredicate.HasBelief] = true;
                world[ENemesisPredicate.HasArrived] = true;
                world[ENemesisPredicate.IsSearchWarm] = true;
            },
            Check = run => run.ShortestVisit(State.Searching) >= 1f
                ? null
                : $"it bounces Chasing↔Searching: the shortest search lasted {Seconds(run.ShortestVisit(State.Searching))} s.",
        };

        // The other side of that rung: a sighting that is really gone still hands over to the
        // search, and promptly. Silent, the belief outgrows ChaseHoldRadius in about half a second.
        yield return new Scenario
        {
            Name = "at the last seen point, the player is gone and silent",
            Start = State.Chasing,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.HasBelief] = true;
                world[ENemesisPredicate.HasArrived] = true;
                world[ENemesisPredicate.IsSearchWarm] = true;
            },
            Check = run => run.Entered(State.Searching) && run.Transitions[0].time <= 1f && run.Final == State.Searching
                ? null
                : "it should hand over to the search within a second and stay there.",
        };

        // And the trap in it: footsteps keep the belief tight for as long as they last, and a chase
        // without a sighting stands at the last SEEN point. ChaseHoldMaxTime is what ends that.
        yield return new Scenario
        {
            Name = "at the last seen point, the player is gone and still heard running",
            Start = State.Chasing,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.HearsPlayer] = true;
                world[ENemesisPredicate.HasBelief] = true;
                world[ENemesisPredicate.HasArrived] = true;
                world[ENemesisPredicate.IsSearchWarm] = true;
            },
            Check = run => run.Entered(State.Searching) && run.Transitions[0].time <= 2f && run.Final == State.Searching
                ? null
                : "hearing them is holding the chase on its own: it stands at the corner instead of searching.",
        };

        // Plan §19.4, T1. One path query to the belief fails while the player is in plain view (it
        // stands for a whole RouteVerdictInterval), and both chase rungs ask for a reachable belief.
        // The verdict has to hold before the ladder reads it (etapa B2).
        yield return new Scenario
        {
            Name = "in plain view, one path query fails now and then",
            Start = State.Chasing,
            Seconds = 6f,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.SeesPlayer] = true;
                world[ENemesisPredicate.HasBelief] = true;
                world.RouteFails = Pulse(time + 1.2f, 1.6f, 0.4f);
                world[ENemesisPredicate.IsSearchWarm] = true;
            },
            Check = run => run.Transitions.Count == 0 ? null : "it left the chase over one failed path query.",
        };

        // What that must not cost (WIR-018): a player it really cannot reach, in plain view, is
        // still not chased. The first verdict is taken as it comes, so there is no chase to drop.
        yield return new Scenario
        {
            Name = "in plain view of a player it cannot reach",
            Start = State.Chasing,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.SeesPlayer] = true;
                world[ENemesisPredicate.HasBelief] = true;
                world.RouteFails = true;
                world[ENemesisPredicate.IsSearchWarm] = true;
            },
            Check = run => run.Transitions.Count == 1 && run.Final == State.Searching && run.Transitions[0].time <= 0.1f
                ? null
                : "it should stop chasing at once and stay in the search.",
        };

        // Plan §19.3, M1. Off the lift on a belief that went cold on the way: half a minute old is
        // not something to search the landing for.
        yield return new Scenario
        {
            Name = "off the lift with a belief that went cold on the way",
            Start = State.Traversing,
            StartedAgo = 20f,
            LastSeenAgo = float.PositiveInfinity,
            Script = (time, world) =>
            {
                world[ENemesisPredicate.HasBelief] = true;
                world.BeliefAge = 31f + time;
            },
            Check = run => run.Entered(State.Searching)
                ? "it searches the landing on a 31-second-old belief."
                : null,
            KnownIssue = "montacargas M1, etapa D1",
        };
    }

    // ── Assets ───────────────────────────────────────────

    private static List<T> AllAssets<T>() where T : UnityEngine.Object
    {
        List<T> found = new List<T>();

        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) found.Add(asset);
        }

        return found;
    }

    private static T FirstAsset<T>() where T : UnityEngine.Object
    {
        List<T> found = AllAssets<T>();
        return found.Count > 0 ? found[0] : null;
    }
}
