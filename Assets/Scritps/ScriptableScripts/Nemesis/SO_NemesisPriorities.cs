using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Nemesis's priority ladder, as an asset the designer reorders.
///
/// WHY A LIST AND NOT A GRAPH
///
/// This started as a Unity Behavior graph, and building it made the shape obvious: every branch
/// was [conditions] -> "put the Nemesis in X", hung off a single Selector. A tree whose every
/// branch is a leaf is a list drawn sideways, and it cost a package dependency, a binary-ish
/// asset that only merges through UnityYAMLMerge, a second decision layer that could disagree
/// with the C# one, and eight wrapper node classes that did nothing but call the predicates
/// below. None of that bought a shape the list cannot express.
///
/// So the authoring surface is this: a reorderable list of rungs, read top to bottom, first match
/// wins. The designer drags a rung up to make it outrank another and edits the thresholds in
/// <see cref="SO_NemesisData"/>. No recompile, no dependency, and an asset that merges like any
/// other text file.
///
/// WHAT LIVES HERE AND WHAT DOES NOT
///
/// Here: the ORDER, and which questions each rung asks. In SO_NemesisData: the NUMBERS. A rung
/// says "the belief is younger than the chase grace", never "younger than 2" — so a threshold
/// still has exactly one home and the asset the designer already tunes stays the place to tune
/// it. <see cref="ENemesisThreshold.Custom"/> is the escape hatch for a number that genuinely
/// belongs to one rung and nowhere else.
///
/// Not here: what a state DOES once entered. The ladder decides WHICH state; the FSM owns
/// entering it, running it and leaving it. Nothing in this asset can touch the agent, the
/// animator or the sensors.
/// </summary>
[CreateAssetMenu(fileName = "SO_NemesisPriorities",
                 menuName = "Scriptable Objects/SO_NemesisPriorities")]
public class SO_NemesisPriorities : ScriptableObject
{
    [Tooltip("Las reglas, de mayor a menor prioridad. Se leen de arriba hacia abajo y gana la " +
             "primera que se cumpla entera.\n\n" +
             "Arrastrá una regla hacia arriba para que le gane a las de abajo. La última " +
             "conviene que no tenga condiciones: es la red de seguridad.")]
    [SerializeField] private List<NemesisPriorityRung> rungs = new List<NemesisPriorityRung>();

    [Header("Histéresis")]
    [Tooltip("Segundos mínimos que el Nemesis se queda en un estado antes de que la escalera " +
             "pueda sacarlo de ahí.\n\n" +
             "Es lo que evita que dos reglas se lo pasen ida y vuelta cada frame: mientras la " +
             "ventana está abierta solo pueden ganar las reglas marcadas como 'Interrumpe'. Sin " +
             "esto un parpadeo del sensor —el jugador justo en el borde del rango de escucha— " +
             "alterna de estado varias veces por segundo, y en cada cambio el estado nuevo no " +
             "llega a ejecutar nada: el Nemesis se queda plantado cambiando de tarea.\n\n" +
             "0 lo desactiva. Más de ~0,5 s empieza a notarse como reacción lenta.")]
    [SerializeField, Range(0f, 1f)] private float minimumStateDwell = 0.35f;

    /// <summary>The ladder, in priority order.</summary>
    public IReadOnlyList<NemesisPriorityRung> Rungs => rungs;

    /// <summary>See <see cref="minimumStateDwell"/>.</summary>
    public float MinimumStateDwell => minimumStateDwell;

    /// <summary>
    /// Fills a brand-new asset with the shipped ladder instead of an empty list.
    ///
    /// An empty priority asset is not a neutral starting point — it is a Nemesis that never
    /// decides anything. Unity calls this once, when the asset is created from the menu.
    /// </summary>
    private void Reset() => rungs = BuildDefaultLadder();

    /// <summary>
    /// The ladder as shipped, in code, so it is also what runs when no asset is assigned.
    ///
    /// ONE DEFINITION, NOT TWO. <see cref="NemesisDecision"/> walks a list either way: this one
    /// when the Nemesis has no priorities asset, the asset's own when it has. There is no second
    /// hand-written ladder that could drift from what the designer sees in the inspector, which
    /// is exactly the failure the Behavior graph had — the graph and the C# fallback were two
    /// separate descriptions of the same intent.
    ///
    /// Reading it top to bottom is reading the design: a capture in progress is never
    /// re-decided; a commitment just made is honoured before anything else is even asked; the
    /// lift outranks plain sight because a visible player one floor up is the case a flat chase
    /// handles worst; and the bottom rung is unconditional so the ladder can never fall through
    /// to nothing.
    /// </summary>
    public static List<NemesisPriorityRung> BuildDefaultLadder()
    {
        return new List<NemesisPriorityRung>
        {
            // A capture is a SEQUENCE — grab, wait for the checkpoint to report back, hold the
            // grace window open so the player can get away from where they respawned — and every
            // step of it is bookkeeping the ladder cannot see. Without this rung the ladder votes
            // straight through it: the player reappears, the Nemesis hears them, sight pulls it
            // out mid-grace, it loses them, and it lands in Searching. Catch leaves on its own
            // terms only.
            Rung(NemesisStateManager.ENemesisState.Catch,
                 "una captura en curso no se vuelve a decidir",
                 interrupts: true,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Catch)),

            // The dwell floor that used to be a private const inside NemesisSearchingState, and
            // the clearest example of why the ladder beats transitions scattered across states:
            // Chasing handing over a target it can still see (because the path to it was partial)
            // and Searching handing it straight back (because it can see it) is a closed loop
            // that turns over every other frame. Buried in one state it read as a magic number;
            // as the second rung it reads as what it is.
            Rung(NemesisStateManager.ENemesisState.Searching,
                 "compromiso: la búsqueda dura al menos medio segundo",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Searching),
                 NemesisCondition.TimeInStateUnder(0.5f)),

            // Close enough to grab, and the post-capture cooldown has expired. Marked as an
            // interrupt: a Nemesis with its hands within reach must not wait out a dwell window.
            Rung(NemesisStateManager.ENemesisState.Catch,
                 "lo tiene al alcance de la mano",
                 interrupts: true,
                 NemesisCondition.Is(ENemesisPredicate.CanCatchPlayer)),

            // It believes the player is on another floor and the lift is the way there. Bounded
            // by the commit time measured from the last SENSE: the walk plus the ride is tens of
            // seconds with the player invisible behind a slab, so without a bound this would hold
            // forever on a belief that has gone cold.
            // ITS BODY IS ON THE LIFT. Nothing re-decides that.
            //
            // NemesisElevatorUser drives the Nemesis by hand for the whole crossing, and for the
            // first twenty seconds of it - the wait for the cabin - the agent is still enabled, so
            // nothing else in the system can tell that something has taken over. An interrupt
            // because this is not a sensor that can flicker, it is a fact about who owns the body.
            Rung(NemesisStateManager.ENemesisState.Traversing,
                 "esta cruzando el montacargas",
                 interrupts: true,
                 NemesisCondition.Is(ENemesisPredicate.IsUsingElevator)),

            // THE APPROACH IS A COMMITMENT, and this rung is what makes it one.
            //
            // The rung below can only ENTER this state; it cannot hold it, because its condition
            // is re-asked from wherever the Nemesis is standing right now. RouteToBeliefCrossesFloors
            // measures the path from the CURRENT position, and walking towards a landing changes
            // that path continuously - near the doors it flips outright, because a route computed
            // from a point already on the link no longer counts as crossing it.
            //
            // Each flip is a real transition, and StateManager.Update runs a transition OR
            // UpdateState, never both. So the Nemesis bounced Traversing-Searching several times a
            // second, never ran a frame of either, and never finished the walk - the violet/green
            // flicker at the landing. Raising SearchTimeOut made it obvious rather than causing
            // it: a longer search sweeps further and wanders into the flip zone more often.
            //
            // Same shape and same reasoning as "le queda presupuesto de busqueda" further down:
            // once a commitment is made it runs on its own clock instead of being re-justified
            // every frame. The two bounds are what keeps it from becoming a trap - it gives up if
            // the walk drags past ElevatorCommitTime, or if the belief it set out for goes cold.
            Rung(NemesisStateManager.ENemesisState.Traversing,
                 "ya se comprometio con el montacargas",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Traversing),
                 NemesisCondition.TimeInStateUnder(ENemesisThreshold.ElevatorCommitTime),
                 NemesisCondition.BeliefAgeUnder(ENemesisThreshold.ElevatorCommitTime)),

            Rung(NemesisStateManager.ENemesisState.Traversing,
                 "para llegar hay que tomar el montacargas",
                 interrupts: false,
                 NemesisCondition.Is(ENemesisPredicate.RouteToBeliefCrossesFloors),
                 NemesisCondition.BeliefAgeUnder(ENemesisThreshold.ElevatorCommitTime)),

            // Plainly visible. An interrupt for the same reason as the capture: seeing the player
            // is the one piece of information that should never be held behind a dwell window.
            Rung(NemesisStateManager.ENemesisState.Chasing,
                 "lo está viendo",
                 interrupts: true,
                 NemesisCondition.Is(ENemesisPredicate.SeesPlayer)),

            // Just lost sight. Measured from the last SENSE rather than from entering the state,
            // so hearing them mid-chase renews the pursuit exactly the way seeing them would —
            // which is what the old per-state counter did by resetting itself.
            Rung(NemesisStateManager.ENemesisState.Chasing,
                 "lo perdió de vista recién",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Chasing),
                 NemesisCondition.BeliefAgeUnder(ENemesisThreshold.VisionLossGracePeriod)),

            // Once in, the search runs on its own clock: it is a fixed budget of time to spend on
            // a belief, not something to re-justify every frame — and ABOVE "hears a noise" is
            // load-bearing, not cosmetic.
            //
            // NemesisSearchingState's own UpdateState already has a "a fresh noise outranks
            // everything, re-aim the cut-off" mechanism (see RetargetSearch), and it never got to
            // run: with the noise rung sitting above this one, hearing anything while searching —
            // even the same noise still going — voted the ladder into Investigating before
            // Searching.UpdateState ever executed a single frame. StateManager.Update runs a
            // transition OR UpdateState, never both, so the retarget logic was dead code and every
            // noise cut the search short — which read as "sometimes short, sometimes the full
            // budget" depending on whether the player happened to be making noise. This rung
            // outranking the noise rung is what lets Searching absorb a fresh noise itself instead
            // of the ladder yanking the Nemesis out from under it.
            Rung(NemesisStateManager.ENemesisState.Searching,
                 "le queda presupuesto de búsqueda",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Searching),
                 NemesisCondition.TimeInStateUnder(ENemesisThreshold.SearchTimeOut)),

            // A noise to walk towards — reached only when the rung above did not already claim an
            // ongoing search. Starting fresh from Patrolling or Traversing still works exactly the
            // same: IsIn(Searching) above is false, so this is the first rung that fires.
            // Caught something in the corner of its eye. Walks over to look instead of sprinting,
            // which is the entire point of splitting the cone into two bands: peeking round a
            // corner used to trip "lo esta viendo", an INTERRUPT rung, in the same frame.
            //
            // WHERE THIS SITS IS THE WHOLE DESIGN OF IT.
            //
            // Below the search budget, because a suspicion is WEAKER information than a noise and
            // the noise rung is already below it - for the reason spelled out on that rung, that
            // pulling Searching out from under itself leaves its own retarget logic as dead code.
            // Put this above the budget and that bug comes straight back, wearing a different hat.
            //
            // Above the noise rung, because seeing a shape is worth more than hearing one: it
            // carries a direction and a distance where a noise carries a rough origin.
            //
            // Not an interrupt: a suspicion is exactly the kind of thing that should have to wait
            // out the hysteresis window. If it resolves into an actual sighting, "lo esta viendo"
            // is an interrupt and wins anyway.
            Rung(NemesisStateManager.ENemesisState.Investigating,
                 "vio algo de reojo",
                 interrupts: false,
                 NemesisCondition.Is(ENemesisPredicate.IsSuspicious)),

            Rung(NemesisStateManager.ENemesisState.Investigating,
                 "escucha un ruido",
                 interrupts: false,
                 NemesisCondition.Is(ENemesisPredicate.HearsPlayer)),

            // Still on its way to a noise it has not reached. Leaves on arrival or on running out
            // of patience; a fresh noise renews it for free, because the belief age resets on
            // every detection.
            Rung(NemesisStateManager.ENemesisState.Investigating,
                 "sigue yendo hacia el último ruido",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Investigating),
                 NemesisCondition.Not(ENemesisPredicate.HasArrived),
                 NemesisCondition.BeliefAgeUnder(ENemesisThreshold.InvestigationTimeOut)),

            // Coming off a pursuit still believing something: sweep rather than file it away.
            // Two rungs and not one because a rung is an AND — splitting the old
            // "(chasing OR traversing) AND has belief" into two lines is what keeps every rung
            // readable as a single sentence.
            Rung(NemesisStateManager.ENemesisState.Searching,
                 "venía persiguiendo y todavía cree algo",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Chasing),
                 NemesisCondition.Is(ENemesisPredicate.HasBelief)),

            Rung(NemesisStateManager.ENemesisState.Searching,
                 "venía hacia el montacargas y todavía cree algo",
                 interrupts: false,
                 NemesisCondition.InState(NemesisStateManager.ENemesisState.Traversing),
                 NemesisCondition.Is(ENemesisPredicate.HasBelief)),

            // No conditions: always true. The ladder must end in something unconditional or it
            // can fall through to "stay where you are", which reads as a frozen Nemesis.
            Rung(NemesisStateManager.ENemesisState.Patrolling,
                 "nada que atender",
                 interrupts: false),
        };
    }

    private static NemesisPriorityRung Rung(NemesisStateManager.ENemesisState target, string note,
                                            bool interrupts, params NemesisCondition[] conditions)
    {
        return new NemesisPriorityRung
        {
            enabled = true,
            target = target,
            note = note,
            interrupts = interrupts,
            conditions = new List<NemesisCondition>(conditions ?? Array.Empty<NemesisCondition>()),
        };
    }
}

/// <summary>
/// One rung: a state, and the questions that all have to answer yes for it to win.
///
/// The conditions are an AND and only an AND. An OR is two rungs, one under the other, which
/// costs a line and buys a list where every entry reads as a single sentence — as opposed to a
/// boolean expression the designer has to parse.
/// </summary>
[Serializable]
public class NemesisPriorityRung
{
    [Tooltip("Destildala para apagar esta regla sin borrarla. Útil para probar qué pasa sin ella.")]
    public bool enabled = true;

    [Tooltip("El estado que pide esta regla si se cumple.")]
    public NemesisStateManager.ENemesisState target;

    [Tooltip("Puede ganar aunque la ventana de histéresis (Minimum State Dwell) esté abierta.\n\n" +
             "Reservalo para lo que nunca debe esperar: verlo y poder agarrarlo. Marcarlas todas " +
             "equivale a apagar la histéresis.")]
    public bool interrupts;

    [Tooltip("Para vos y para el HUD de debug: cuando esta regla gana, este texto es lo que " +
             "aparece como motivo.")]
    public string note;

    [Tooltip("Todas tienen que cumplirse. Una lista vacía es 'siempre': eso es lo que hace que " +
             "la última regla sea la red de seguridad.")]
    public List<NemesisCondition> conditions = new List<NemesisCondition>();
}

/// <summary>
/// One question about the world, answered by a predicate on <see cref="NemesisDecision"/>.
///
/// A struct with a field per predicate shape rather than a class hierarchy: Unity serialises
/// polymorphic lists only through SerializeReference, which stores a type name in the asset and
/// breaks the entry when the class is renamed or moved. A designer's ladder should survive a
/// refactor, so the cost is a couple of fields that only some predicates read — and the custom
/// inspector hides the ones that do not apply.
/// </summary>
[Serializable]
public struct NemesisCondition
{
    [Tooltip("La pregunta.")]
    public ENemesisPredicate predicate;

    [Tooltip("Invierte la respuesta: se cumple cuando la pregunta da NO.")]
    public bool negate;

    [Tooltip("Solo para 'Is In State': contra qué estado se compara.")]
    public NemesisStateManager.ENemesisState state;

    [Tooltip("Solo para las preguntas de tiempo: de dónde sale el número de segundos. Casi " +
             "siempre querés un campo de SO_NemesisData, no 'Custom' — así el umbral vive en un " +
             "solo lugar.")]
    public ENemesisThreshold threshold;

    [Tooltip("Segundos, solo cuando el umbral es 'Custom'.")]
    public float customSeconds;

    public static NemesisCondition Is(ENemesisPredicate predicate) =>
        new NemesisCondition { predicate = predicate };

    public static NemesisCondition Not(ENemesisPredicate predicate) =>
        new NemesisCondition { predicate = predicate, negate = true };

    public static NemesisCondition InState(NemesisStateManager.ENemesisState state) =>
        new NemesisCondition { predicate = ENemesisPredicate.IsInState, state = state };

    public static NemesisCondition BeliefAgeUnder(ENemesisThreshold threshold) =>
        new NemesisCondition { predicate = ENemesisPredicate.BeliefAgeUnder, threshold = threshold };

    public static NemesisCondition TimeInStateUnder(ENemesisThreshold threshold) =>
        new NemesisCondition { predicate = ENemesisPredicate.TimeInStateUnder, threshold = threshold };

    public static NemesisCondition TimeInStateUnder(float seconds) =>
        new NemesisCondition
        {
            predicate = ENemesisPredicate.TimeInStateUnder,
            threshold = ENemesisThreshold.Custom,
            customSeconds = seconds,
        };

    /// <summary>Whether this predicate reads <see cref="state"/>. Used by the inspector to hide
    /// the field where it means nothing.</summary>
    public bool UsesState => predicate == ENemesisPredicate.IsInState;

    /// <summary>Whether this predicate reads <see cref="threshold"/>.</summary>
    public bool UsesThreshold => predicate == ENemesisPredicate.BeliefAgeUnder ||
                                 predicate == ENemesisPredicate.TimeInStateUnder;
}

/// <summary>
/// The questions a rung may ask. Each one maps to exactly one side-effect-free property on
/// <see cref="NemesisDecision"/>, so adding a question here means adding it there and nowhere
/// else — the sensors, the states and this asset cannot end up with three different ideas of what
/// "sees the player" means.
/// </summary>
public enum ENemesisPredicate
{
    /// <summary>A sensor has the player right now, through the cone or by extreme proximity.
    /// </summary>
    SeesPlayer,

    /// <summary>A noise is audible right now.</summary>
    HearsPlayer,

    /// <summary>It remembers a position at all. A belief is a memory: this stays true long after
    /// the memory has gone stale, which is what the age questions are for.</summary>
    HasBelief,

    /// <summary>Close enough, level enough and unobstructed enough to grab, with the
    /// post-capture cooldown expired.</summary>
    CanCatchPlayer,

    /// <summary>Getting to the belief means taking the freight elevator. Goes through the
    /// throttled route oracle — see NemesisDecision for why that matters.</summary>
    RouteToBeliefCrossesFloors,

    /// <summary>The agent has reached the end of its current path.</summary>
    HasArrived,

    /// <summary>The FSM is in the state named by the condition.</summary>
    IsInState,

    /// <summary>Seconds since either sensor last caught the player, under the threshold. How
    /// stale the information is.</summary>
    BeliefAgeUnder,

    /// <summary>Seconds in the current state, under the threshold. How long a commitment has
    /// been held. A different question from the belief age, and mixing them up is how a search
    /// budget ends up refreshing every time the player steps on gravel.</summary>
    TimeInStateUnder,

    // APPEND NEW PREDICATES HERE, NEVER IN THE MIDDLE.
    //
    // NemesisCondition is a plain struct and Unity serialises an enum field as its INTEGER value,
    // so SO_NemesisPriorities.asset stores every authored rung as "predicate: 6", not as
    // "predicate: IsInState". Inserting a member anywhere above this line renumbers everything
    // below it and silently rewrites the designer's whole ladder into a different one - the rung
    // that asked whether the Nemesis was in a given state starts asking whether it has arrived,
    // and nothing errors. The struct's own doc comment explains why it avoids SerializeReference
    // for exactly this kind of survivability; ordering is the other half of that promise.

    /// <summary>Something is in the PERIPHERAL band of the vision cone and the suspicion meter has
    /// climbed past the designer's threshold, but it has not resolved into a sighting. Goes false
    /// as soon as SeesPlayer goes true - the two never hold together.</summary>
    IsSuspicious,

    /// <summary>NemesisElevatorUser has a crossing in flight: waiting for the cabin, boarding,
    /// riding or stepping off. Nothing should re-decide the state while this holds.</summary>
    IsUsingElevator,
}

/// <summary>
/// Where a rung's number comes from.
///
/// Naming a field of <see cref="SO_NemesisData"/> rather than typing a float is the point: the
/// ladder stays readable as "while the chase grace lasts" and the number keeps one home. Custom
/// exists for a value that genuinely belongs to a single rung — the half-second search dwell is
/// the only one shipped that way.
/// </summary>
public enum ENemesisThreshold
{
    VisionLossGracePeriod,
    InvestigationTimeOut,
    SearchTimeOut,
    ElevatorCommitTime,
    BeliefMemoryTime,
    Custom,
}
