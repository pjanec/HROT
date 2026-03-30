using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.BDC.SSTD.Orchestration;
using FDP.Toolkit.Orchestration;

namespace Bagira.Orchestrator;

// ── Step abstractions ──────────────────────────────────────────────────────────

/// <summary>A single entry in a planned transition trajectory.</summary>
public abstract class ISysOpStep { }

/// <summary>
/// Instructs all cluster nodes to transition to <see cref="TargetState"/> as part of a 2PC round.
/// </summary>
public sealed class TransitionStep : ISysOpStep
{
    public DSMState TargetState { get; }
    public TransitionStep(DSMState target) => TargetState = target;
}

/// <summary>
/// An out-of-band operation appended after the final <see cref="TransitionStep"/>,
/// for example a replay-seek to a specific wall-clock position.
/// </summary>
public sealed class OperationStep : ISysOpStep
{
    public SysOpType Operation { get; }
    public string    PayloadJson { get; }
    public OperationStep(SysOpType operation, string payloadJson)
    {
        Operation   = operation;
        PayloadJson = payloadJson;
    }
}

// ── Planner ────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves <see cref="SysOpRequest"/> targets into an ordered <see cref="Queue{T}"/> of
/// <see cref="ISysOpStep"/> entries via Breadth-First Search over the DSM directed graph.
/// Pure application-layer class — no DDS dependency.
/// </summary>
/// <remarks>
/// <b>Adjacency definition</b> follows CGF-1-DESIGN §4.1.  Failure-recovery edges
/// (e.g. <c>LoadingEdit → Standby</c>) are excluded from the planning graph; they are
/// automatic rollback paths triggered by node-side errors, not plannable transitions.
/// <c>DSMState.Degraded</c> is a system-imposed state with no outgoing planning edges
/// and is therefore unreachable/invalid as a planning target.
///
/// <para>
/// BFS is delegated to <see cref="FDP.Toolkit.Orchestration.TransitionPlanner"/> using
/// the graph provided at construction time.  Use <see cref="BagiraStateGraph.Build()"/>
/// to create the canonical Bagira DSM graph.
/// </para>
/// </remarks>
public sealed class DrillMasterPlanner
{
    private readonly FDP.Toolkit.Orchestration.TransitionPlanner _tkPlanner;
    private readonly ITransitionGraph _graph;

    /// <summary>
    /// Creates a planner backed by the provided transition graph.
    /// </summary>
    /// <param name="graph">
    /// DSM graph to use for BFS path-finding.
    /// Pass <see cref="BagiraStateGraph.Build()"/> in production.
    /// </param>
    public DrillMasterPlanner(ITransitionGraph graph)
    {
        _graph     = graph ?? throw new ArgumentNullException(nameof(graph));
        _tkPlanner = new FDP.Toolkit.Orchestration.TransitionPlanner(graph);
    }

    /// <summary>
    /// Returns the DSM states directly reachable from <paramref name="current"/> via a
    /// single planning edge (i.e. one-step neighbours in the transition graph).
    /// </summary>
    /// <remarks>
    /// Used by the Orchestrator UI panel to enumerate valid next-state buttons without
    /// needing the full BFS trajectory.
    /// </remarks>
    public IReadOnlyList<DSMState> GetReachableTargets(DSMState current)
    {
        var neighbors = _graph.GetNeighbors((int)current);
        return neighbors.Select(i => (DSMState)i).ToList();
    }

    /// <summary>
    /// Computes the shortest path from <paramref name="current"/> to <paramref name="target"/>
    /// using BFS over the DSM graph.
    /// </summary>
    /// <returns>
    /// The list of states to visit (excluding <paramref name="current"/>), in traversal order.
    /// An empty list when <paramref name="current"/> == <paramref name="target"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no path exists (e.g. <paramref name="current"/> or <paramref name="target"/>
    /// is <c>Degraded</c>, or the graph is otherwise disconnected for the given pair).
    /// The message includes both state names.
    /// </exception>
    public IReadOnlyList<DSMState> CalculateShortestPath(DSMState current, DSMState target)
    {
        try
        {
            var intPath = _tkPlanner.CalculateShortestPath((int)current, (int)target);
            return intPath.Select(i => (DSMState)i).ToList();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] No valid DSM path from {current} to {target}. " +
                $"The transition '{current}' → '{target}' is not reachable in the planning graph.");
        }
    }

    /// <summary>
    /// Plans a full trajectory for a <see cref="SysOpRequest"/>, returning an ordered queue
    /// of steps to execute as part of the next distributed transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Payload encoding (normative — prefer JSON object form in new code):</b>
    /// <see cref="SysOpRequest.PayloadJson"/> must be one of:
    /// <list type="bullet">
    ///   <item>A plain integer string (e.g. <c>"30"</c>): target DSM state numeric value.
    ///         Supported for backward compatibility; new code should use the JSON object form.</item>
    ///   <item>A JSON object with at least <c>TargetState</c> (int) and optionally
    ///         <c>TargetWallTicks</c> (long) — the <b>preferred</b> encoding:
    ///         <code>{ "TargetState": 30, "TargetWallTicks": 999000 }</code></item>
    /// </list>
    /// An empty, whitespace-only, non-parseable JSON string, or a JSON object that
    /// lacks <c>TargetState</c> is a caller error and will cause an
    /// <see cref="InvalidOperationException"/> to be thrown.
    /// </para>
    /// <para>
    /// When the resolved target is <see cref="DSMState.RunningReplay"/> AND
    /// <c>TargetWallTicks</c> is present in the JSON payload, an
    /// <see cref="OperationStep"/>(<see cref="SysOpType.ReplaySeek"/>) is appended after
    /// the final <see cref="TransitionStep"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="request"/> has an empty, whitespace-only, non-parseable,
    /// or structurally incomplete <see cref="SysOpRequest.PayloadJson"/> (missing
    /// <c>TargetState</c>).  Also propagated from <see cref="CalculateShortestPath"/> when
    /// the requested path is unreachable.  Always thrown before any DDS command is issued.
    /// </exception>
    public Queue<ISysOpStep> PlanTrajectory(DSMState current, SysOpRequest request)
    {
        DSMState targetState  = DSMState.Standby;
        string?  seekTicksRaw = null;

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new InvalidOperationException(
                "[TransitionPlanner] TransitionState payload is empty or whitespace — " +
                "a valid target DSM state is required (integer or JSON object with TargetState).");
        }

        if (int.TryParse(request.PayloadJson, out var rawInt))
        {
            targetState = (DSMState)rawInt;
        }
        else
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(request.PayloadJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"[TransitionPlanner] TransitionState payload is not valid JSON: {ex.Message}", ex);
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("TargetState", out var tsProp))
                    throw new InvalidOperationException(
                        "[TransitionPlanner] TransitionState JSON payload does not contain " +
                        "required 'TargetState' property.");

                targetState = (DSMState)tsProp.GetInt32();

                if (doc.RootElement.TryGetProperty("TargetWallTicks", out var seekProp))
                    seekTicksRaw = seekProp.GetRawText();
            }
        }

        var path  = CalculateShortestPath(current, targetState);
        var queue = new Queue<ISysOpStep>();

        // CGF1-S0307: If the payload carries a ScenarioId, prepend a PrefetchScenario
        // step so the StorageGateway copies scenario files to all nodes before the first
        // DSM transition executes.
        string? scenarioId = null;
        if (!string.IsNullOrWhiteSpace(request.PayloadJson) && !int.TryParse(request.PayloadJson, out _))
        {
            // The payload was already validated as JSON in the TargetState block above,
            // so this parse cannot throw JsonException — no try/catch needed here.
            using var scenarioDoc = JsonDocument.Parse(request.PayloadJson);
            if (scenarioDoc.RootElement.TryGetProperty("ScenarioId", out var sidProp))
                scenarioId = sidProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(scenarioId))
            queue.Enqueue(new OperationStep(SysOpType.PrefetchScenario, scenarioId!));

        foreach (var state in path)
            queue.Enqueue(new TransitionStep(state));

        // Append a ReplaySeek operation when targeting RunningReplay with a seek hint.
        if (targetState == DSMState.RunningReplay && seekTicksRaw != null)
            queue.Enqueue(new OperationStep(SysOpType.ReplaySeek, seekTicksRaw));

        return queue;
    }

    /// <summary>
    /// Plans a <see cref="SysOpType.ManageStory"/> operation for an in-flight drill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Precondition:</b> <paramref name="current"/> must be
    /// <see cref="DSMState.RunningLive"/>.  Any other state causes an
    /// <see cref="InvalidOperationException"/> with <c>OpStatus.InvalidState</c> semantics.
    /// </para>
    /// <para>
    /// <b>Payload JSON (required fields):</b>
    /// <code>
    /// {
    ///   "Mode": "Start" | "Stop",
    ///   "StoryId": "&lt;guid&gt;",
    ///   "ScenarioId": "&lt;id&gt;"   // required for Mode:Start; ignored for Mode:Stop
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Start trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="SysOpType.PrefetchScenario"/>, scenarioId) —
    ///         ensures story asset files are staged on all nodes.</item>
    ///   <item><see cref="OperationStep"/>(<see cref="SysOpType.ManageStory"/>, fullPayload) —
    ///         signals <c>DrillMaster</c> to fan out <c>StartStory</c> to nodes.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Stop trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="SysOpType.ManageStory"/>, fullPayload) —
    ///         signals <c>DrillMaster</c> to fan out <c>StopStory</c> to nodes.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="current"/> is not <see cref="DSMState.RunningLive"/>,
    /// or when the payload is missing required fields (<c>Mode</c>, <c>StoryId</c>, or
    /// <c>ScenarioId</c> for Start mode).
    /// </exception>
    public Queue<ISysOpStep> PlanManageStory(DSMState current, SysOpRequest request)
    {
        if (current != DSMState.RunningLive)
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageStory requires RunningLive; current state is {current}.");

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageStory payload is empty or whitespace.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.PayloadJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageStory payload is not valid JSON: {ex.Message}", ex);
        }

        string? mode;
        string? storyId;
        string? scenarioId;
        using (doc)
        {
            mode       = doc.RootElement.TryGetProperty("Mode",       out var m) ? m.GetString()      : null;
            storyId    = doc.RootElement.TryGetProperty("StoryId",    out var s) ? s.GetString()      : null;
            scenarioId = doc.RootElement.TryGetProperty("ScenarioId", out var sc) ? sc.GetString()    : null;
        }

        if (string.IsNullOrWhiteSpace(mode))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageStory payload missing required 'Mode' field (Start|Stop).");
        if (string.IsNullOrWhiteSpace(storyId))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageStory payload missing required 'StoryId' field.");

        var queue = new Queue<ISysOpStep>();

        if (string.Equals(mode, "Start", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(scenarioId))
                throw new InvalidOperationException(
                    "[TransitionPlanner] ManageStory Mode:Start payload missing required 'ScenarioId' field.");

            // Prefetch story asset files to all nodes before injection.
            queue.Enqueue(new OperationStep(SysOpType.PrefetchScenario, scenarioId!));
            // Fan out StartStory to all nodes.
            queue.Enqueue(new OperationStep(SysOpType.ManageStory, request.PayloadJson));
        }
        else if (string.Equals(mode, "Stop", StringComparison.OrdinalIgnoreCase))
        {
            queue.Enqueue(new OperationStep(SysOpType.ManageStory, request.PayloadJson));
        }
        else
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageStory unknown Mode '{mode}'; expected 'Start' or 'Stop'.");
        }

        return queue;
    }
}
