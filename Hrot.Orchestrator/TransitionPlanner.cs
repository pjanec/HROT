using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using FDP.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

// ── Step abstractions ──────────────────────────────────────────────────────────

/// <summary>A single entry in a planned transition trajectory.</summary>
public abstract class ISysOpStep { }

/// <summary>
/// Instructs all cluster nodes to transition to <see cref="TargetState"/> as part of a 2PC round.
/// </summary>
public sealed class TransitionStep : ISysOpStep
{
    public ClusterState TargetState { get; }
    public TransitionStep(ClusterState target) => TargetState = target;
}

/// <summary>
/// An out-of-band operation appended after the final <see cref="TransitionStep"/>,
/// for example a replay-seek to a specific wall-clock position.
/// </summary>
public sealed class OperationStep : ISysOpStep
{
    public ClusterOpType Operation { get; }
    public string    PayloadJson { get; }
    public OperationStep(ClusterOpType operation, string payloadJson)
    {
        Operation   = operation;
        PayloadJson = payloadJson;
    }
}

// ── Planner ────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves <see cref="ClusterOpRequest"/> targets into an ordered <see cref="Queue{T}"/> of
/// <see cref="ISysOpStep"/> entries via Breadth-First Search over the cluster state machine directed graph.
/// Pure application-layer class — no DDS dependency.
/// </summary>
/// <remarks>
/// <b>Adjacency definition</b> follows CGF-1-DESIGN §4.1.  Failure-recovery edges
/// (e.g. <c>LoadingEdit → Standby</c>) are excluded from the planning graph; they are
/// automatic rollback paths triggered by node-side errors, not plannable transitions.
/// <c>ClusterState.Degraded</c> is a system-imposed state with no outgoing planning edges
/// and is therefore unreachable/invalid as a planning target.
///
/// <para>
/// BFS is delegated to <see cref="FDP.Toolkit.Orchestration.TransitionPlanner"/> using
/// the graph provided at construction time.  Use <see cref="HrotStateGraph.Build()"/>
/// to create the canonical Hrot cluster state machine graph.
/// </para>
/// </remarks>
public sealed class ClusterMasterPlanner
{
    private readonly FDP.Toolkit.Orchestration.TransitionPlanner _tkPlanner;
    private readonly ITransitionGraph _graph;

    /// <summary>
    /// Creates a planner backed by the provided transition graph.
    /// </summary>
    /// <param name="graph">
    /// Cluster state machine graph to use for BFS path-finding.
    /// Pass <see cref="HrotStateGraph.Build()"/> in production.
    /// </param>
    public ClusterMasterPlanner(ITransitionGraph graph)
    {
        _graph     = graph ?? throw new ArgumentNullException(nameof(graph));
        _tkPlanner = new FDP.Toolkit.Orchestration.TransitionPlanner(graph);
    }

    /// <summary>
    /// Returns the Cluster states directly reachable from <paramref name="current"/> via a
    /// single planning edge (i.e. one-step neighbours in the transition graph).
    /// </summary>
    /// <remarks>
    /// Used by the Orchestrator UI panel to enumerate valid next-state buttons without
    /// needing the full BFS trajectory.
    /// </remarks>
    public IReadOnlyList<ClusterState> GetReachableTargets(ClusterState current)
    {
        var neighbors = _graph.GetNeighbors((int)current);
        return neighbors.Select(i => (ClusterState)i).ToList();
    }

    /// <summary>
    /// Computes the shortest path from <paramref name="current"/> to <paramref name="target"/>
    /// using BFS over the cluster state machine graph.
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
    public IReadOnlyList<ClusterState> CalculateShortestPath(ClusterState current, ClusterState target)
    {
        try
        {
            var intPath = _tkPlanner.CalculateShortestPath((int)current, (int)target);
            return intPath.Select(i => (ClusterState)i).ToList();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] No valid cluster state machine path from {current} to {target}. " +
                $"The transition '{current}' → '{target}' is not reachable in the planning graph.");
        }
    }

    /// <summary>
    /// Plans a full trajectory for a <see cref="ClusterOpRequest"/>, returning an ordered queue
    /// of steps to execute as part of the next distributed transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Payload encoding (normative — prefer JSON object form in new code):</b>
    /// <see cref="ClusterOpRequest.PayloadJson"/> must be one of:
    /// <list type="bullet">
    ///   <item>A plain integer string (e.g. <c>"30"</c>): target Cluster state numeric value.
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
    /// When the resolved target is <see cref="ClusterState.OperatingReplay"/> AND
    /// <c>TargetWallTicks</c> is present in the JSON payload, an
    /// <see cref="OperationStep"/>(<see cref="ClusterOpType.ReplaySeek"/>) is appended after
    /// the final <see cref="TransitionStep"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="request"/> has an empty, whitespace-only, non-parseable,
    /// or structurally incomplete <see cref="ClusterOpRequest.PayloadJson"/> (missing
    /// <c>TargetState</c>).  Also propagated from <see cref="CalculateShortestPath"/> when
    /// the requested path is unreachable.  Always thrown before any DDS command is issued.
    /// </exception>
    public Queue<ISysOpStep> PlanTrajectory(ClusterState current, ClusterOpRequest request)
    {
        ClusterState targetState  = ClusterState.Idle;
        string?  seekTicksRaw = null;

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new InvalidOperationException(
                "[TransitionPlanner] TransitionState payload is empty or whitespace — " +
                "a valid target Cluster state is required (integer or JSON object with TargetState).");
        }

        if (int.TryParse(request.PayloadJson, out var rawInt))
        {
            targetState = (ClusterState)rawInt;
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

                targetState = (ClusterState)tsProp.GetInt32();

                if (doc.RootElement.TryGetProperty("TargetWallTicks", out var seekProp))
                    seekTicksRaw = seekProp.GetRawText();
            }
        }

        var path  = CalculateShortestPath(current, targetState);
        var queue = new Queue<ISysOpStep>();

        // CGF1-S0307: If the payload carries a ScenarioId, prepend a PrefetchScenario
        // step so the StorageGateway copies scenario files to all nodes before the first
        // cluster state machine transition executes.
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
            queue.Enqueue(new OperationStep(ClusterOpType.PrefetchScenario, scenarioId!));

        foreach (var state in path)
            queue.Enqueue(new TransitionStep(state));

        // Append a ReplaySeek operation when targeting RunningReplay with a seek hint.
        if (targetState == ClusterState.OperatingReplay && seekTicksRaw != null)
            queue.Enqueue(new OperationStep(ClusterOpType.ReplaySeek, seekTicksRaw));

        return queue;
    }

    /// <summary>
    /// Plans a <see cref="ClusterOpType.ManageEpisode"/> operation for an in-flight exercise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Precondition:</b> <paramref name="current"/> must be
    /// <see cref="ClusterState.OperatingLive"/>.  Any other state causes an
    /// <see cref="InvalidOperationException"/> with <c>OpStatus.InvalidState</c> semantics.
    /// </para>
    /// <para>
    /// <b>Payload JSON (required fields):</b>
    /// <code>
    /// {
    ///   "Mode": "Start" | "Stop",
    ///   "EpisodeId": "&lt;guid&gt;",
    ///   "ScenarioId": "&lt;id&gt;"   // required for Mode:Start; ignored for Mode:Stop
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Start trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.PrefetchScenario"/>, scenarioId) —
    ///         ensures episode asset files are staged on all nodes.</item>
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.ManageEpisode"/>, fullPayload) —
    ///         signals <c>ClusterMaster</c> to fan out <c>StartEpisode</c> to nodes.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Stop trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.ManageEpisode"/>, fullPayload) —
    ///         signals <c>ClusterMaster</c> to fan out <c>StopEpisode</c> to nodes.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="current"/> is not <see cref="ClusterState.OperatingLive"/>,
    /// or when the payload is missing required fields (<c>Mode</c>, <c>EpisodeId</c>, or
    /// <c>ScenarioId</c> for Start mode).
    /// </exception>
    public Queue<ISysOpStep> PlanManageEpisode(ClusterState current, ClusterOpRequest request)
    {
        if (current != ClusterState.OperatingLive)
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageEpisode requires RunningLive; current state is {current}.");

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageEpisode payload is empty or whitespace.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.PayloadJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageEpisode payload is not valid JSON: {ex.Message}", ex);
        }

        string? mode;
        string? episodeId;
        string? scenarioId;
        using (doc)
        {
            mode       = doc.RootElement.TryGetProperty("Mode",       out var m) ? m.GetString()      : null;
            episodeId    = doc.RootElement.TryGetProperty("EpisodeId",    out var s) ? s.GetString()      : null;
            scenarioId = doc.RootElement.TryGetProperty("ScenarioId", out var sc) ? sc.GetString()    : null;
        }

        if (string.IsNullOrWhiteSpace(mode))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageEpisode payload missing required 'Mode' field (Start|Stop).");
        if (string.IsNullOrWhiteSpace(episodeId))
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageEpisode payload missing required 'EpisodeId' field.");

        var queue = new Queue<ISysOpStep>();

        if (string.Equals(mode, "Start", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(scenarioId))
                throw new InvalidOperationException(
                    "[TransitionPlanner] ManageEpisode Mode:Start payload missing required 'ScenarioId' field.");

            // Prefetch episode asset files to all nodes before injection.
            queue.Enqueue(new OperationStep(ClusterOpType.PrefetchScenario, scenarioId!));
            // Fan out StartEpisode to all nodes.
            queue.Enqueue(new OperationStep(ClusterOpType.ManageEpisode, request.PayloadJson));
        }
        else if (string.Equals(mode, "Stop", StringComparison.OrdinalIgnoreCase))
        {
            queue.Enqueue(new OperationStep(ClusterOpType.ManageEpisode, request.PayloadJson));
        }
        else
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageEpisode unknown Mode '{mode}'; expected 'Start' or 'Stop'.");
        }

        return queue;
    }
}
